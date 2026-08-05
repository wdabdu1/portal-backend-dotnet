using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ShipmentLineItemDetail(string ProductCategory, string ModelProduct, decimal QtyInBl, string? UnitOfMeasure);

public record ErpColumnDetail(string CompanyName, int SequenceOrder, bool IsLast, object? Data);

public record ShipmentFullDetailResponse(
    int Id, string BlAwbNo, string PoNumber, string Status,
    string BusinessUnit, string? Division, string? Supplier, string Consignee, string Category,
    string? VesselName, int Fcl20Count, int Fcl40Count, DateOnly? Etd, DateOnly? Eta, DateOnly? SobActualDate,
    List<ShipmentLineItemDetail> LineItems,
    object? Forwarder, object? Acd, object? DraftDocuments, object? Ssmo, object? Mot,
    object? SupplierFullSet, object? Banking,
    List<ErpColumnDetail> ErpInfo, string? LastOffshoreInvoiceNo);
[ApiController]
[Authorize(Roles = AppRoles.ShipmentDetailsViewers)]
[Route("api/shipments/{id:int}/full-details")]
public class ShipmentDetailsFullController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShipmentDetailsFullController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ShipmentFullDetailResponse>> Get(int id, [FromServices] BuAccessService buAccess)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Division)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (shipment is null) return NotFound();
        if (!buAccess.SeesAllBus(User) && !buAccess.CanSeeBusinessUnit(User, shipment.PurchaseOrder!.BusinessUnitId)) return Forbid();

        var isClearance = User.IsInRole(AppRoles.ClrUsr) || User.IsInRole(AppRoles.ClrSupervisor);

        // --- Forwarder — hidden entirely from Clearance (per updated scope) ---
        object? forwarder = null;
        if (!isClearance)
        {
            var fwd = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == id);
            if (fwd is not null)
            {
                var forwarderName = fwd.ForwarderId.HasValue ? (await _db.Forwarders.FindAsync(fwd.ForwarderId))?.Name : null;
                var currencyCode = fwd.CurrencyId.HasValue ? (await _db.Currencies.FindAsync(fwd.CurrencyId))?.Code : null;
                forwarder = new
                {
                    Forwarder = forwarderName,
                    fwd.ActualShippingCost,
                    Currency = currencyCode,
                    fwd.ActualShippingCostUsd,
                    fwd.AmountSaved,
                    fwd.MarineInsurance
                };
            }
        }

        // --- ACD — Clearance sees only Date + Reference No. ---
        var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == id);
        object? acdDto = acd is null ? null : (isClearance
            ? new { acd.ProcessDate, acd.RefNumber }
            : new { acd.ProcessDate, acd.CostUsd, acd.CostSettledDate, acd.RefNumber });

        // --- Draft Documents — hidden entirely from Clearance ---
        object? draftDto = null;
        if (!isClearance)
        {
            var draftDocs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == id);
            draftDto = draftDocs is null ? null : new { draftDocs.InitialDraftReceivedDate, draftDocs.FinalDraftReceivedDate, draftDocs.FinalDraftConfirmedDate };
        }

        // --- SSMO — Clearance sees only Date + Reference No. ---
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == id);
        object? ssmoDto = ssmo is null ? null : (isClearance
            ? new { ssmo.ApplicationDate, ssmo.RefNumber }
            : new { ssmo.ApplicationDate, ssmo.Cost, ssmo.CostSettledDate, ssmo.RefNumber });

        // --- MOT — Clearance sees only Date + Reference No. ---
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == id);
        object? motDto = mot is null ? null : (isClearance
            ? new { mot.ProcessDate, mot.RefNumber }
            : new
            {
                mot.ProcessDate, mot.Cost, mot.CostSettledDate, mot.RefNumber,
                mot.OffshoreApprovedPiNumber, mot.OffshoreApprovedPiDate
            });

        // --- Supplier Full Set — hidden entirely from Clearance ---
        object? supplierFullSet = null;
        if (!isClearance)
        {
            var fs = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == id);
            if (fs is not null)
            {
                var courierName = fs.FsDispatchedViaId.HasValue ? (await _db.Couriers.FindAsync(fs.FsDispatchedViaId))?.Name : null;
                supplierFullSet = new
                {
                    fs.SupplierInvoiceNo, fs.SupplierInvoiceDate, fs.FsDispatchDate,
                    DispatchedVia = courierName, fs.FsTrackingNumber, fs.FsReceivedDate
                };
            }
        }

        // --- Banking — hidden entirely from Clearance ---
        object? banking = null;
        if (!isClearance)
        {
            var bank = await _db.ShipmentBankings.FirstOrDefaultAsync(x => x.ShipmentId == id);
            if (bank is not null)
            {
                var senderBankName = bank.SenderBankId.HasValue ? (await _db.SenderBanks.FindAsync(bank.SenderBankId))?.Name : null;
                var receiverBankName = bank.ReceivingBankId.HasValue ? (await _db.ReceiverBanks.FindAsync(bank.ReceivingBankId))?.Name : null;
                var courierName = bank.OsDocDispatchedViaId.HasValue ? (await _db.Couriers.FindAsync(bank.OsDocDispatchedViaId))?.Name : null;
                var currencyCode = bank.CollectionCurrencyId.HasValue ? (await _db.Currencies.FindAsync(bank.CollectionCurrencyId))?.Code : null;
                var tenorDays = bank.TenorId.HasValue ? (await _db.Tenors.FindAsync(bank.TenorId))?.Days : (int?)null;

                banking = new
                {
                    SenderBank = senderBankName, bank.OsDocDispatchDate, DispatchedVia = courierName, bank.OsDocTrackingNumber,
                    bank.SenderBankCharges, ReceivingBank = receiverBankName, bank.NecessaryGoodType, bank.CollectionRefNo,
                    bank.CollectionValue, Currency = currencyCode, TenorDays = tenorDays, bank.ReceiverBankCharges
                };
            }
        }

        // --- Offshore chain: full ERP Info hidden from Clearance; they only
        // get the last offshore's Invoice No. as a single flat field. ---
        var offshorePartners = await _db.PurchaseOrderOffshorePartners
            .Where(op => op.PurchaseOrderId == shipment.PurchaseOrderId)
            .Include(op => op.BusinessPartner)
            .OrderBy(op => op.SequenceOrder)
            .ToListAsync();

        var erpRows = await _db.ShipmentOffshoreErpInfos.Where(e => e.ShipmentId == id).ToListAsync();
        var maxSequence = offshorePartners.Count > 0 ? offshorePartners.Max(o => o.SequenceOrder) : 0;

        List<ErpColumnDetail> erpColumns = new();
        string? lastOffshoreInvoiceNo = null;

        if (isClearance)
        {
            var lastRow = erpRows.FirstOrDefault(e =>
                offshorePartners.FirstOrDefault(op => op.Id == e.PurchaseOrderOffshorePartnerId)?.SequenceOrder == maxSequence);
            lastOffshoreInvoiceNo = lastRow?.InvoiceNo;
        }
        else
        {
            erpColumns = offshorePartners
                .Select(op =>
                {
                    var row = erpRows.FirstOrDefault(e => e.PurchaseOrderOffshorePartnerId == op.Id);
                    var isLast = op.SequenceOrder == maxSequence;
                    object? data = row is null ? null : (isLast || op.SequenceOrder == 1
                        ? new { row.PrNo, row.PoNo, row.Sa, row.BillReg, row.Grn, row.InvoiceNo }
                        : new { row.InspectionNo, row.Grn, row.InvoiceNo, row.Remarks });
                    return new ErpColumnDetail(op.BusinessPartner?.Name ?? "", op.SequenceOrder, isLast, data);
                })
                .ToList();
        }

        var lineItems = shipment.LineItems.Select(li => new ShipmentLineItemDetail(
            li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "",
            li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
            li.QtyInBl,
            li.PurchaseOrderLineItem?.UnitOfMeasure?.Code
        )).ToList();

        var category = lineItems.FirstOrDefault()?.ProductCategory ?? "";

        return new ShipmentFullDetailResponse(
            shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Status.ToString(),
            shipment.PurchaseOrder.BusinessUnit!.Name, shipment.PurchaseOrder.Division?.Name,
            isClearance ? null : shipment.PurchaseOrder.Supplier?.Name,
            shipment.PurchaseOrder.Consignee?.Name ?? "", category, shipment.VesselName,
            shipment.Fcl20Count, shipment.Fcl40Count, shipment.Etd, shipment.Eta, shipment.SobActualDate,
    }
}
