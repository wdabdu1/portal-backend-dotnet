using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;
using ClearanceEntity = ShippingPortal.Api.Models.Clearance.Clearance;

namespace ShippingPortal.Api.Controllers.Clearance;

public record ClearanceShipmentSummary(
    int ShipmentId, string BlAwbNo, string BusinessUnit, string Category, DateOnly? Eta,
    int FclCount, string? DeclarationNo, string Product, decimal Qty, string TrafficLight, string RouteStatus);

public record ClearanceGeneralInfoRequest(
    DateOnly? CopyOfBlReceivedDate, DateOnly? OriginalShipmentSetReceivedDate, string? LcNo,
    string? DeclarationNo, string? Notes, DateOnly? ClearanceCompleteDate);

public record ClearanceRouteRequest(int Route); // 0=NotSelected,1=Route1,2=Route2,3=Route3

public record ClearanceDetailResponse(
    int ShipmentId, string BlAwbNo, string PoNumber, DateOnly? CopyOfBlReceivedDate,
    DateOnly? OriginalShipmentSetReceivedDate, string? LcNo, string? DeclarationNo, string? Notes,
    int Route, DateOnly? ClearanceCompleteDate);

[ApiController]
[Authorize]
[Route("api/clearance")]
public class ClearanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceController(ShippingPortalDbContext db) => _db = db;

    // Selection screen: only Confirmed shipments (nothing to clear on a Draft),
    // sorted by ETA ascending — soonest-arriving first, per the requirement.
    [HttpGet("shipments")]
    public async Task<ActionResult<IEnumerable<ClearanceShipmentSummary>>> GetShipmentsForClearance([FromQuery] string? search)
    {
        // Total target = General (applies to every shipment) + the specific
        // route's total once selected. Before a route is chosen, fall back
        // to Route 1's total as a conservative default so the light isn't
        // artificially green with no target at all.
        var slaByDivision = await _db.ClearanceSlaSettings
            .Where(s => s.IsActive)
            .GroupBy(s => s.Division)
            .Select(g => new { Division = g.Key, Total = g.Sum(s => s.TargetDays) })
            .ToDictionaryAsync(x => x.Division, x => x.Total);

        var generalDays = slaByDivision.GetValueOrDefault(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, 0);

        var query = _db.Shipments
            .Where(s => s.Status == ShipmentStatus.Confirmed)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => EF.Functions.Like(s.BlAwbNo, $"%{search}%"));
        }

        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        var clearances = await _db.Clearances
            .Where(c => shipmentIds.Contains(c.ShipmentId))
            .ToDictionaryAsync(c => c.ShipmentId);

        var results = shipments.Select(s =>
        {
            clearances.TryGetValue(s.Id, out var clearance);
            var declarationNo = clearance?.DeclarationNo;

            if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrEmpty(declarationNo)
                && !s.BlAwbNo.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !declarationNo.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var firstLine = s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem;
            var totalQty = s.LineItems.Sum(li => li.QtyInBl);

            var trafficLight = ComputeTrafficLight(s.Eta, clearance?.ClearanceCompleteDate, defaultTargetDays);
            var routeStatus = clearance is null || clearance.Route == 0 ? "Not Started" : clearance.Route.ToString();

            return new ClearanceShipmentSummary(
                s.Id, s.BlAwbNo, s.PurchaseOrder?.BusinessUnit?.Name ?? "", firstLine?.ProductCategory?.Name ?? "",
                s.Eta, s.Fcl20Count + s.Fcl40Count, declarationNo, firstLine?.ModelProduct?.Name ?? "", totalQty,
                trafficLight, routeStatus);
        })
        .Where(x => x is not null)
        .OrderBy(x => x!.Eta ?? DateOnly.MaxValue)
        .ToList();

        return Ok(results);
    }

    [HttpGet("{shipmentId:int}/detail")]
    public async Task<ActionResult<ClearanceDetailResponse>> GetDetail(int shipmentId)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);

        return new ClearanceDetailResponse(
            shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber,
            clearance?.CopyOfBlReceivedDate, clearance?.OriginalShipmentSetReceivedDate, clearance?.LcNo,
            clearance?.DeclarationNo, clearance?.Notes, (int)(clearance?.Route ?? 0), clearance?.ClearanceCompleteDate);
    }

    [HttpPut("{shipmentId:int}/general-info")]
    public async Task<IActionResult> UpsertGeneralInfo(int shipmentId, ClearanceGeneralInfoRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) { clearance = new ClearanceEntity { ShipmentId = shipmentId }; _db.Clearances.Add(clearance); }

        clearance.CopyOfBlReceivedDate = req.CopyOfBlReceivedDate;
        clearance.OriginalShipmentSetReceivedDate = req.OriginalShipmentSetReceivedDate;
        clearance.LcNo = req.LcNo;
        clearance.DeclarationNo = req.DeclarationNo;
        clearance.Notes = req.Notes;
        clearance.ClearanceCompleteDate = req.ClearanceCompleteDate;
        clearance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(clearance);
    }

    [HttpPut("{shipmentId:int}/route")]
    public async Task<IActionResult> SetRoute(int shipmentId, ClearanceRouteRequest req)
    {
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) { clearance = new ClearanceEntity { ShipmentId = shipmentId }; _db.Clearances.Add(clearance); }

        clearance.Route = (ShippingPortal.Api.Models.Clearance.ClearanceRouteType)req.Route;
        clearance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(clearance);
    }

    // Green: on track. Amber: past 70% of the target window. Red: past the
    // target entirely. Grey: no ETA to measure against. Once clearance is
    // marked complete, always Green regardless of how long it took —
    // "needs attention" no longer applies to a finished shipment.
    private static string ComputeTrafficLight(DateOnly? eta, DateOnly? clearanceCompleteDate, int targetDays)
    {
        if (clearanceCompleteDate.HasValue) return "Green";
        if (!eta.HasValue) return "Grey";

        var daysSinceEta = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - eta.Value.DayNumber;
        if (daysSinceEta > targetDays) return "Red";
        if (daysSinceEta > targetDays * 0.7) return "Amber";
        return "Green";
    }
}
