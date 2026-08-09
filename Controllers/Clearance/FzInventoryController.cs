using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Clearance;

public record FzDepositOption(int ShipmentId, string BlAwbNo, string? DepositRefNo);
public record FzBalanceLine(int ShipmentLineItemId, string ModelProduct, decimal Deposited, decimal Withdrawn, decimal UnderClearance, decimal Available, decimal ThisWithdrawalQty);

public record FzOpenDeposit(
    int ShipmentId, string BusinessUnit, string BlAwbNo, string? DepositRefNo, DateOnly? DateOfDeposit,
    string? Division, decimal TotalQty, decimal TotalWithdrawn, decimal Balance);

// Withdrawn = only from COMPLETED withdrawals (Clearance Completed &
// Cargo Withdrawn date set). UnderClearance = reserved by in-progress
// withdrawals — doesn't reduce CurrentStock yet, but does reduce what's
// Available to allocate to a NEW withdrawal, preventing double-booking.
public record FzInventoryItemRow(
    int ShipmentLineItemId, string Destination, string BusinessUnit, string Category, string ModelProduct,
    string BlAwbNo, DateOnly? DateOfDeposit, string? DepositRefNo,
    decimal QtyDeposited, decimal QtyWithdrawn, decimal QtyUnderClearance, decimal CurrentStock, decimal AvailableStock,
    int InventoryDays, decimal PercentWithdrawn);

[ApiController]
[Authorize(Roles = AppRoles.ClearanceViewers)]
[Route("api/fz-inventory")]
public class FzInventoryController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public FzInventoryController(ShippingPortalDbContext db) => _db = db;

    private async Task<(Dictionary<int, decimal> Completed, Dictionary<int, decimal> UnderClearance)> GetWithdrawalTotalsByLineItemAsync()
    {
        var allLines = await _db.WithdrawalLineItems
            .Include(w => w.Withdrawal)
            .ToListAsync();

        var completed = allLines
            .Where(w => w.Withdrawal!.ClearanceActualCompletedDate != null)
            .GroupBy(w => w.DepositShipmentLineItemId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.Qty));

        var underClearance = allLines
            .Where(w => w.Withdrawal!.ClearanceActualCompletedDate == null)
            .GroupBy(w => w.DepositShipmentLineItemId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.Qty));

        return (completed, underClearance);
    }

    private async Task<List<FzOpenDeposit>> GetOpenDepositsAsync()
    {
        var deposits = await _db.ClearanceRoute2Details
            .Where(r => r.ContainersReceivedAtFzDate != null)
            .Include(r => r.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(r => r.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Division)
            .ToListAsync();

        var (completed, underClearance) = await GetWithdrawalTotalsByLineItemAsync();

        var result = new List<FzOpenDeposit>();
        foreach (var deposit in deposits)
        {
            var shipment = deposit.Clearance!.Shipment!;
            var lineItems = await _db.ShipmentLineItems.Where(li => li.ShipmentId == shipment.Id).ToListAsync();

            var totalQty = lineItems.Sum(li => li.QtyInBl);
            var totalWithdrawn = lineItems.Sum(li => completed.GetValueOrDefault(li.Id, 0m));
            var totalUnderClearance = lineItems.Sum(li => underClearance.GetValueOrDefault(li.Id, 0m));
            var available = totalQty - totalWithdrawn - totalUnderClearance;

            if (available <= 0) continue;

            result.Add(new FzOpenDeposit(
                shipment.Id, shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.BlAwbNo, deposit.DepositRefNo,
                deposit.ContainersReceivedAtFzDate, shipment.PurchaseOrder?.Division?.Name,
                totalQty, totalWithdrawn, available));
        }

        return result.OrderBy(r => r.BlAwbNo).ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FzInventoryItemRow>>> GetInventory()
    {
        var deposits = await _db.ClearanceRoute2Details
            .Where(r => r.ContainersReceivedAtFzDate != null)
            .Include(r => r.Destination)
            .Include(r => r.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .ToListAsync();

        var (completed, underClearance) = await GetWithdrawalTotalsByLineItemAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = new List<FzInventoryItemRow>();
        foreach (var deposit in deposits)
        {
            var shipment = deposit.Clearance!.Shipment!;
            var lineItems = await _db.ShipmentLineItems
                .Where(li => li.ShipmentId == shipment.Id)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
                .ToListAsync();

            foreach (var li in lineItems)
            {
                var withdrawn = completed.GetValueOrDefault(li.Id, 0m);
                var reserved = underClearance.GetValueOrDefault(li.Id, 0m);
                var currentStock = li.QtyInBl - withdrawn;
                var inventoryDays = deposit.ContainersReceivedAtFzDate.HasValue
                    ? today.DayNumber - deposit.ContainersReceivedAtFzDate.Value.DayNumber
                    : 0;

                result.Add(new FzInventoryItemRow(
                    li.Id, deposit.Destination?.Name ?? "", shipment.PurchaseOrder?.BusinessUnit?.Name ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                    shipment.BlAwbNo, deposit.ContainersReceivedAtFzDate, deposit.DepositRefNo,
                    li.QtyInBl, withdrawn, reserved, currentStock, currentStock - reserved,
                    inventoryDays, li.QtyInBl == 0 ? 0 : (withdrawn / li.QtyInBl) * 100));
            }
        }

        return Ok(result);
    }

    [HttpGet("options")]
    public async Task<ActionResult<IEnumerable<FzDepositOption>>> GetDepositOptions()
    {
        var open = await GetOpenDepositsAsync();
        return Ok(open.Select(r => new FzDepositOption(r.ShipmentId, r.BlAwbNo, r.DepositRefNo)).ToList());
    }
}
