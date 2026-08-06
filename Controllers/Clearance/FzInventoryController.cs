using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Clearance;

public record FzDepositOption(int ShipmentId, string BlAwbNo, string? DepositRefNo);
public record FzBalanceLine(int ShipmentLineItemId, string ModelProduct, decimal Deposited, decimal Withdrawn, decimal Balance);

public record FzInventoryItemRow(
    int ShipmentLineItemId, string Destination, string BusinessUnit, string Category, string ModelProduct,
    string BlAwbNo, DateOnly? DateOfDeposit, string? DepositRefNo,
    decimal QtyDeposited, decimal QtyWithdrawn, decimal CurrentStock,
    int InventoryDays, decimal PercentWithdrawn);

[ApiController]
[Authorize(Roles = AppRoles.ClearanceViewers)]
[Route("api/fz-inventory")]
public class FzInventoryController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public FzInventoryController(ShippingPortalDbContext db) => _db = db;

    // Every Route 2 deposit that has completed the deposit step (has a
    // Containers Received at FZ date) and still has some balance left —
    // used both for the BL picker in Route 3 and the full inventory table.
    private async Task<List<FzInventoryRow>> GetOpenDepositsAsync()
    {
        var deposits = await _db.ClearanceRoute2Details
            .Where(r => r.ContainersReceivedAtFzDate != null)
            .Include(r => r.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(r => r.Clearance).ThenInclude(c => c!.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Division)
            .ToListAsync();

        var withdrawnByLineItem = await _db.ClearanceRoute3Withdrawals
            .GroupBy(w => w.DepositShipmentLineItemId)
            .Select(g => new { LineItemId = g.Key, Total = g.Sum(w => w.Qty) })
            .ToDictionaryAsync(x => x.LineItemId, x => x.Total);

        var result = new List<FzInventoryRow>();
        foreach (var deposit in deposits)
        {
            var shipment = deposit.Clearance!.Shipment!;
            var lineItems = await _db.ShipmentLineItems
                .Where(li => li.ShipmentId == shipment.Id)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
                .ToListAsync();

            var totalQty = lineItems.Sum(li => li.QtyInBl);
            var totalWithdrawn = lineItems.Sum(li => withdrawnByLineItem.GetValueOrDefault(li.Id, 0m));
            var balance = totalQty - totalWithdrawn;

            if (balance <= 0) continue;

            var categories = lineItems
                .Select(li => li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "")
                .Where(c => c != "")
                .Distinct()
                .Take(2)
                .ToList();

            result.Add(new FzInventoryRow(
                shipment.Id, shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.BlAwbNo, deposit.DepositRefNo,
                deposit.ContainersReceivedAtFzDate, shipment.PurchaseOrder?.Division?.Name, categories,
                totalQty, totalWithdrawn, balance));
        }

        return result.OrderBy(r => r.BlAwbNo).ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FzInventoryRow>>> GetInventory()
        => Ok(await GetOpenDepositsAsync());

    [HttpGet("options")]
    public async Task<ActionResult<IEnumerable<FzDepositOption>>> GetDepositOptions()
    {
        var open = await GetOpenDepositsAsync();
        return Ok(open.Select(r => new FzDepositOption(r.ShipmentId, r.BlAwbNo, r.DepositRefNo)).ToList());
    }

    [HttpGet("{depositShipmentId:int}/balance")]
    public async Task<ActionResult<IEnumerable<FzBalanceLine>>> GetBalance(int depositShipmentId)
    {
        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == depositShipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .ToListAsync();

        var withdrawnByLineItem = await _db.ClearanceRoute3Withdrawals
            .Where(w => lineItems.Select(li => li.Id).Contains(w.DepositShipmentLineItemId))
            .GroupBy(w => w.DepositShipmentLineItemId)
            .Select(g => new { LineItemId = g.Key, Total = g.Sum(w => w.Qty) })
            .ToDictionaryAsync(x => x.LineItemId, x => x.Total);

        var result = lineItems.Select(li =>
        {
            var withdrawn = withdrawnByLineItem.GetValueOrDefault(li.Id, 0m);
            return new FzBalanceLine(li.Id, li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "", li.QtyInBl, withdrawn, li.QtyInBl - withdrawn);
        }).Where(l => l.Balance > 0).ToList();

        return Ok(result);
    }
}
