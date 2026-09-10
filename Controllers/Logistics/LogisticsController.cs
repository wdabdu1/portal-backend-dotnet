using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Logistics;

public record LogisticsItemRow(
    string SourceType, int SourceLineItemId,
    string BusinessUnit, string Consignee, string Category, string ModelProduct, string BlAwbNo,
    DateOnly? ArrivalDate,
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

        // Confidentiality gate settings (Phase 1.5 of the Logistics
        // redesign) — a Route 1 shipment doesn't appear at all until it's
        // within this many days of its arrival. In-memory default if the
        // seed row is somehow missing; never persisted from here.
        var visibilitySettings = await _db.LogisticsVisibilitySettings.FirstOrDefaultAsync()
            ?? new LogisticsVisibilitySettings();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // --- Route 1 (Clear at Port) ---
        var portClearances = await _db.Clearances
            .Where(c => c.Route == ClearanceRouteType.Route1ClearAtPort)
            .Include(c => c.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(c => c.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .ToListAsync();

        var portShipmentIds = portClearances.Select(c => c.ShipmentId).ToList();
        var portClearanceIds = portClearances.Select(c => c.Id).ToList();

        // Batched instead of one query per shipment inside the loop below.
        var portLineItemsByShipment = (await _db.ShipmentLineItems
            .Where(li => portShipmentIds.Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .ToListAsync())
            .GroupBy(li => li.ShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var estimatedCompletionByShipment = await _schedule.GetEstimatedCompletionDatesAsync(portShipmentIds);

        // Arrival = Actual Arrival Date (Clearance's Delivery Order
        // section) if entered, else vessel ETA — this is what the
        // arrival-lead-time gate below is measured against, not the
        // clearance completion dates (which are a separate concept and
        // stay shown alongside it).
        var deliveryOrderByClearanceId = await _db.ClearanceDeliveryOrders
            .Where(d => portClearanceIds.Contains(d.ClearanceId))
            .ToDictionaryAsync(d => d.ClearanceId, d => d);

        foreach (var clearance in portClearances)
        {
            var shipment = clearance.Shipment!;
            var deliveryOrder = deliveryOrderByClearanceId.GetValueOrDefault(clearance.Id);
            var arrivalDate = deliveryOrder?.ActualArrivalDate ?? shipment.Eta;

            // Confidentiality gate: skip the whole shipment if its arrival
            // is further out than the configured lead time. Fails OPEN
            // (still shown) when no arrival date is known yet at all,
            // rather than hiding it indefinitely — an assumption, flag to
            // the user if that's not the desired behavior.
            if (arrivalDate.HasValue
                && (arrivalDate.Value.ToDateTime(TimeOnly.MinValue) - today.ToDateTime(TimeOnly.MinValue)).TotalDays > visibilitySettings.ArrivalLeadTimeDays)
                continue;

            var estimatedCompletion = estimatedCompletionByShipment.GetValueOrDefault(shipment.Id);
            var lineItems = portLineItemsByShipment.GetValueOrDefault(shipment.Id, new List<ShipmentLineItem>());

            foreach (var li in lineItems)
            {
                var allocated = allocatedTotals.GetValueOrDefault(("Port", li.Id));
                result.Add(new LogisticsItemRow(
                    "Port", li.Id,
                    shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.PurchaseOrder?.Consignee?.Name ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", "",
                    shipment.BlAwbNo, arrivalDate,
                    estimatedCompletion, clearance.ClearanceCompleteDate,
                    li.QtyInBl, li.PurchaseOrderLineItem?.UnitOfMeasure?.Code ?? "", "Clear at Port", null,
                    allocated, li.QtyInBl - allocated));
            }
        }

        // --- FZ Withdrawals ---
        var withdrawals = await _db.Withdrawals
            .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(w => w.DepositShipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .ToListAsync();

        var depositShipmentIds = withdrawals.Select(w => w.DepositShipmentId).ToList();
        var withdrawalIds = withdrawals.Select(w => w.Id).ToList();

        // Batched — was one query per withdrawal.
        var route2ByShipmentId = await _db.ClearanceRoute2Details
            .Include(r => r.Destination)
            .Include(r => r.Clearance)
            .Where(r => depositShipmentIds.Contains(r.Clearance!.ShipmentId))
            .ToDictionaryAsync(r => r.Clearance!.ShipmentId, r => r);

        var withdrawalLineItemsByWithdrawal = (await _db.WithdrawalLineItems
            .Where(x => withdrawalIds.Contains(x.WithdrawalId))
            .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(x => x.DepositShipmentLineItem).ThenInclude(li => li!.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .ToListAsync())
            .GroupBy(x => x.WithdrawalId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var withdrawal in withdrawals)
        {
            var depositShipment = withdrawal.DepositShipment!;
            var depositRoute2 = route2ByShipmentId.GetValueOrDefault(depositShipment.Id);
            var withdrawalLineItems = withdrawalLineItemsByWithdrawal.GetValueOrDefault(withdrawal.Id, new List<WithdrawalLineItem>());

            foreach (var wli in withdrawalLineItems)
            {
                var li = wli.DepositShipmentLineItem!;
                var allocated = allocatedTotals.GetValueOrDefault(("FZWithdrawal", wli.Id));
                // No arrival-lead-time gate applied here yet (still open —
                // Route 3/FZ withdrawals have no vessel-arrival concept at
                // all; per the agreed design their reveal trigger is
                // "about to be cleared" instead, which needs its own
                // logic, not built yet).
                result.Add(new LogisticsItemRow(
                    "FZWithdrawal", wli.Id,
                    depositShipment.PurchaseOrder?.BusinessUnit?.Name ?? "", depositShipment.PurchaseOrder?.Consignee?.Name ?? "",
                    li.PurchaseOrderLineItem?.ProductCategory?.Name ?? "", "",
                    depositShipment.BlAwbNo, null, null, withdrawal.ClearanceActualCompletedDate,
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
            : query.Where(a => a.WithdrawalLineItemId == sourceLineItemId);

        var allocations = await query.ToListAsync();
        return Ok(allocations.Select(a => new AllocationResponse(
            a.Id, a.WarehouseId, a.Warehouse!.Name, a.Qty, a.ContactName, a.ContactPhone, a.Warehouse.City?.Name)));
    }

    [HttpPost("allocate")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> Allocate(AllocationRequest req)
    {
        if (req.Qty <= 0) return BadRequest(new { message = "Quantity must be greater than zero." });

        decimal totalQty;
        if (req.SourceType == "Port")
        {
            var li = await _db.ShipmentLineItems.FindAsync(req.SourceLineItemId);
            if (li is null) return NotFound(new { message = "Source line item not found." });
            totalQty = li.QtyInBl;
        }
        else if (req.SourceType == "FZWithdrawal")
        {
            var wli = await _db.WithdrawalLineItems.FindAsync(req.SourceLineItemId);
            if (wli is null) return NotFound(new { message = "Source line item not found." });
            totalQty = wli.Qty;
        }
        else
        {
            return BadRequest(new { message = "Invalid source type." });
        }

        var allocatedTotals = await GetAllocatedTotalsAsync();
        var alreadyAllocated = allocatedTotals.GetValueOrDefault((req.SourceType, req.SourceLineItemId));
        if (req.Qty > totalQty - alreadyAllocated)
        {
            return BadRequest(new { message = $"Requested quantity ({req.Qty}) exceeds remaining unallocated quantity ({totalQty - alreadyAllocated})." });
        }

        var allocation = new WarehouseAllocation
        {
            ShipmentLineItemId = req.SourceType == "Port" ? req.SourceLineItemId : null,
            WithdrawalLineItemId = req.SourceType == "FZWithdrawal" ? req.SourceLineItemId : null,
            WarehouseId = req.WarehouseId,
            Qty = req.Qty,
            ContactName = req.ContactName,
            ContactPhone = req.ContactPhone
        };
        _db.WarehouseAllocations.Add(allocation);
        await _db.SaveChangesAsync();
        return Ok(new { id = allocation.Id });
    }

    [HttpDelete("allocations/{id:int}")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> DeleteAllocation(int id)
    {
        var allocation = await _db.WarehouseAllocations.FindAsync(id);
        if (allocation is null) return NotFound();

        _db.WarehouseAllocations.Remove(allocation);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
