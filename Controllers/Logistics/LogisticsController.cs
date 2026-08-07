using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Logistics;

public record LogisticsItemRow(
    string SourceType, int SourceLineItemId,
    string BusinessUnit, string Consignee, string Category, string ModelProduct, string BlAwbNo,
    DateOnly? PlannedCompletionDate, DateOnly? ActualCompletionDate,
    decimal Qty, string Unit, string ClearanceRoute, string? FzDestination,
    decimal AllocatedQty, decimal RemainingQty);

public record AllocationRequest(string SourceType, int SourceLineItemId, int WarehouseId, decimal Qty, string? ContactName, string? ContactPhone);
public record AllocationResponse(int Id, int WarehouseId, string WarehouseName, decimal Qty, string? ContactName, string? ContactPhone, string? DeliveryCity);

[ApiController]
[Authorize(Roles = AppRoles.LogisticsViewers)]
[Route("api/logistics")]
public class LogisticsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ClearanceScheduleService _schedule;
    public LogisticsController(ShippingPortalDbContext db, ClearanceScheduleService schedule)
    {
        _db = db;
        _schedule = schedule;
    }

    private async Task<Dictionary<(string, int), decimal>> GetAllocatedTotalsAsync()
    {
        var allocations = await _db.WarehouseAllocations.ToListAsync();
        var result = new Dictionary<(string, int), decimal>();
        foreach (var a in allocations)
        {
            if (a.ShipmentLineItemId.HasValue)
            {
                var key = ("Port", a.ShipmentLineItemId.Value);
                result[key] = result.GetValueOrDefault(key) + a.Qty;
            }
            else if (a.WithdrawalLineItemId.HasValue)
            {
                var key = ("FZWithdrawal", a.WithdrawalLineItemId.Value);
                result[key] = result.GetValueOrDefault(key) + a.Qty;
            }
        }
        return result;
    }

    [HttpGet("items")]
    public async Task<ActionResult<IEnumerable<LogisticsItemRow>>> GetItems()
    {
        var allocatedTotals = await GetAllocatedTotalsAsync();
        var result = new List<LogisticsItemRow>();

        // --- Route 1 (Clear at Port) ---
        var portClearances = await _db.Clearances
            .Where(c => c.Route == ClearanceRouteType.Route1ClearAtPort)
            .Include(c => c.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(c => c.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .ToListAsync();

        foreach (var clearance in portClearances)
        {
            var shipment = clearance.Shipment!;
            var schedule = await _schedule.GetScheduleAsync(shipment.Id);

            var lineItems = await _db.ShipmentLineItems
                .Where(li => li.ShipmentId == shipment.Id)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
                .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
                .ToListAsync();

            foreach (var li in lineItems)
            {
                var allocated = allocatedTotals.GetValueOrDefault(("Port", li.Id));
                result.Add(new LogisticsItemRow(
                    "Port", li.Id,
                    shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.PurchaseOrder?.Consignee?.Name ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                    shipment.BlAwbNo, schedule.EstimatedCompletionDate, clearance.ClearanceCompleteDate,
                    li.QtyInBl, li.PurchaseOrderLineItem?.UnitOfMeasure?.Code ?? "", "Clear at Port", null,
                    allocated, li.QtyInBl - allocated));
            }
        }

        // --- FZ Withdrawals ---
        var withdrawals = await _db.Withdrawals
            .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .ToListAsync();

        foreach (var withdrawal in withdrawals)
        {
            var depositShipment = withdrawal.DepositShipment!;
            var depositRoute2 = await _db.ClearanceRoute2Details
                .Include(r => r.Destination)
                .Include(r => r.Clearance)
                .FirstOrDefaultAsync(r => r.Clearance!.ShipmentId == depositShipment.Id);

            var withdrawalLineItems = await _db.WithdrawalLineItems
                .Where(x => x.WithdrawalId == withdrawal.Id)
                .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
                .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
                .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
                .ToListAsync();

            foreach (var wli in withdrawalLineItems)
            {
                var li = wli.DepositShipmentLineItem!;
                var allocated = allocatedTotals.GetValueOrDefault(("FZWithdrawal", wli.Id));
                result.Add(new LogisticsItemRow(
                    "FZWithdrawal", wli.Id,
                    depositShipment.PurchaseOrder?.BusinessUnit?.Name ?? "", depositShipment.PurchaseOrder?.Consignee?.Name ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "",
                    depositShipment.BlAwbNo, null, withdrawal.ClearanceActualCompletedDate,
                    wli.Qty, li.PurchaseOrderLineItem?.UnitOfMeasure?.Code ?? "", "Clear from FZ", depositRoute2?.Destination?.Name,
                    allocated, wli.Qty - allocated));
            }
        }

        return Ok(result);
    }

    [HttpGet("allocations")]
    public async Task<ActionResult<IEnumerable<AllocationResponse>>> GetAllocations([FromQuery] string sourceType, [FromQuery] int sourceLineItemId)
    {
        var query = _db.WarehouseAllocations.Include(a => a.Warehouse).ThenInclude(w => w!.City).AsQueryable();
        query = sourceType == "Port"
            ? query.Where(a => a.ShipmentLineItemId == sourceLineItemId)
            :
