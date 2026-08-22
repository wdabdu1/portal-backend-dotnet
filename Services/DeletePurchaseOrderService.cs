using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;

namespace ShippingPortal.Api.Services;

// Deletes one Purchase Order and everything genuinely owned by it —
// for correcting an early mistake: a wrongly-created PO, or a wrong
// Shipment linked to one. Deliberately narrow in scope: refuses
// outright if any of the PO's shipments already has a Clearance
// record, since that means the PO has moved past the "early mistake"
// stage this tool is meant for, and into territory (FZ deposits,
// Withdrawals) this tool doesn't attempt to safely unwind.
public class DeletePurchaseOrderService
{
    private readonly ShippingPortalDbContext _db;
    public DeletePurchaseOrderService(ShippingPortalDbContext db) => _db = db;

    public record DeleteResult(bool Success, string Message, string? PoNumber = null);

    public async Task<DeleteResult> DeleteAsync(string poNumber)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNumber == poNumber);
        if (po is null) return new DeleteResult(false, $"PO '{poNumber}' not found.");

        var shipmentIds = await _db.Shipments.Where(s => s.PurchaseOrderId == po.Id).Select(s => s.Id).ToListAsync();
        var shipLineIds = await _db.ShipmentLineItems.Where(sl => shipmentIds.Contains(sl.ShipmentId)).Select(sl => sl.Id).ToListAsync();

        if (shipmentIds.Count > 0)
        {
            var clearedShipment = await _db.Shipments.Where(s => shipmentIds.Contains(s.Id) && _db.Clearances.Any(c => c.ShipmentId == s.Id))
                .Select(s => s.BlAwbNo).FirstOrDefaultAsync();
            if (clearedShipment is not null)
                return new DeleteResult(false, $"Cannot delete — Shipment '{clearedShipment}' already has a Clearance record. This tool only handles early-stage mistakes, before Clearance has started.");
        }

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        string InClause(IEnumerable<int> ids) => string.Join(",", ids);

        using (var offCmd = conn.CreateCommand()) { offCmd.CommandText = "SET FOREIGN_KEY_CHECKS = 0;"; await offCmd.ExecuteNonQueryAsync(); }

        try
        {
            if (shipLineIds.Count > 0)
            {
                var lineIdsCsv = InClause(shipLineIds);
                await ExecAsync($"DELETE FROM `TruckLoadItems` WHERE `WarehouseAllocationId` IN (SELECT `Id` FROM `WarehouseAllocations` WHERE `ShipmentLineItemId` IN ({lineIdsCsv}));");
                await ExecAsync($"DELETE FROM `WarehouseAllocations` WHERE `ShipmentLineItemId` IN ({lineIdsCsv});");
                await ExecAsync($"DELETE FROM `LastOffshoreItemDetails` WHERE `ShipmentLineItemId` IN ({lineIdsCsv});");
                await ExecAsync($"DELETE FROM `TransferPricingEntries` WHERE `ShipmentLineItemId` IN ({lineIdsCsv});");
            }

            if (shipmentIds.Count > 0)
            {
                var shipIdsCsv = InClause(shipmentIds);
                await ExecAsync($"DELETE FROM `ShipmentForwarders` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentAcds` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentDraftDocuments` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentSsmos` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentMots` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentSupplierFullSets` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentBankings` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentSupplierPaymentRecords` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentPaymentDues` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentCollectionRecords` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentOffshoreErpInfos` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `LastOffshoreDetails` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `ShipmentLineItems` WHERE `ShipmentId` IN ({shipIdsCsv});");
                await ExecAsync($"DELETE FROM `Shipments` WHERE `Id` IN ({shipIdsCsv});");
            }

            await ExecAsync($"DELETE FROM `PurchaseOrderOffshorePartners` WHERE `PurchaseOrderId` = {po.Id};");
            await ExecAsync($"DELETE FROM `PurchaseOrderLineItems` WHERE `PurchaseOrderId` = {po.Id};");
            await ExecAsync($"DELETE FROM `PurchaseOrders` WHERE `Id` = {po.Id};");
        }
        finally
        {
            using var onCmd = conn.CreateCommand();
            onCmd.CommandText = "SET FOREIGN_KEY_CHECKS = 1;";
            await onCmd.ExecuteNonQueryAsync();
        }

        return new DeleteResult(true, $"PO '{poNumber}' and all its dependent records (across {shipmentIds.Count} shipment(s)) were deleted.", poNumber);
    }
}
