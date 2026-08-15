using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

public record ShipmentWithHitOption(int ShipmentId, string BlAwbNo);

public record ClearanceStepGap(string GroupItem, int? ActualDaysTaken, decimal TargetDays, decimal? Gap);

public record DemurrageAnalysisResult(
    bool IsSingleShipment, int ShipmentCount,
    // Summary — only populated for single-shipment mode
    string? BusinessUnit, string? Consignee, string? Category, string? ModelProduct, decimal? Qty,
    string? BlAwbNo, int? Fcl20Count, int? Fcl40Count, string? ShippingLine, int? SummaryFreeDays,
    // General Info — averaged across shipments in group mode
    double TotalCalendarDays, double WeekendDays, double HolidayDays,
    DateOnly? Eta, DateOnly? OriginalDocReceived,
    List<ClearanceStepGap> StepGaps,
    // Charges — tier breakdown only meaningful for a single shipment
    double StorageFreeDays, double StorageChargeableDays, List<TierBreakdownLine> StorageBreakdown, decimal StorageCostSdg,
    double? DemurrageFreeDays, double? DemurrageChargeableDays, List<TierBreakdownLine> DemurrageBreakdown, decimal DemurrageCostSdg,
    decimal TotalSdg,
    List<string> Warnings);

// Every figure here is anchored on shipments that actually had a real
// Demurrage or Storage payment recorded — this is a "what happened and
// why" analysis tool, not a forecast. Single-shipment mode shows full
// detail (dates, tier-by-tier breakdown); filtering to a group instead
// averages every numeric figure across the matching set, dropping the
// per-shipment summary and tier breakdown, which don't average
// meaningfully.
[ApiController]
[Route("api/dashboards/demurrage-analysis")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.CorpFinance)]
public class DemurrageAnalysisController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly DemurrageStorageService _demurrageService;
    private readonly ClearanceScheduleService _scheduleService;

    public DemurrageAnalysisController(ShippingPortalDbContext db, DemurrageStorageService demurrageService, ClearanceScheduleService scheduleService)
    {
        _db = db;
        _demurrageService = demurrageService;
        _scheduleService = scheduleService;
    }

    private async Task<List<int>> GetHitShipmentIdsAsync(
        BuAccessService buAccess, DateOnly? etaFrom, DateOnly? etaTo, int? businessUnitId, int? consigneeId, int? shippingLineId)
    {
        var query = _db.Shipments.Where(s => s.Status != ShipmentStatus.Cancelled).AsQueryable();
        if (etaFrom.HasValue) query = query.Where(s => s.Eta >= etaFrom);
        if (etaTo.HasValue) query = query.Where(s => s.Eta <= etaTo);
        if (businessUnitId.HasValue) query = query.Where(s => s.PurchaseOrder!.BusinessUnitId == businessUnitId);
        if (consigneeId.HasValue) query = query.Where(s => s.PurchaseOrder!.ConsigneeId == consigneeId);
        if (shippingLineId.HasValue) query = query.Where(s => s.ShippingLineId == shippingLineId);

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var candidateIds = await query.Select(s => s.Id).ToListAsync();
        var clearanceByShipment = await _db.Clearances.Where(c => candidateIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId, c => c.Id);
        var clearanceIds = clearanceByShipment.Values.ToList();
        var hitClearanceIds = await _db.ClearanceActualCharges
            .Where(c => clearanceIds.Contains(c.ClearanceId) && ((c.ActualDemurragePaidSdg ?? 0) > 0 || (c.ActualStoragePaidSdg ?? 0) > 0))
            .Select(c => c.ClearanceId)
            .ToListAsync();

        return clearanceByShipment.Where(kv => hitClearanceIds.Contains(kv.Value)).Select(kv => kv.Key).ToList();
    }

    [HttpGet("shipments-with-hits")]
    public async Task<ActionResult<IEnumerable<ShipmentWithHitOption>>> GetShipmentsWithHits(
        [FromServices] BuAccessService buAccess,
        [FromQuery] DateOnly? etaFrom, [FromQuery] DateOnly? etaTo,
        [FromQuery] int? businessUnitId, [FromQuery] int? consigneeId, [FromQuery] int? shippingLineId)
    {
        var ids = await GetHitShipmentIdsAsync(buAccess, etaFrom, etaTo, businessUnitId, consigneeId, shippingLineId);
        var options = await _db.Shipments.Where(s => ids.Contains(s.Id))
            .OrderBy(s => s.BlAwbNo)
            .Select(s => new ShipmentWithHitOption(s.Id, s.BlAwbNo))
            .ToListAsync();
        return Ok(options);
    }

    [HttpGet]
    public async Task<ActionResult<DemurrageAnalysisResult>> Get(
        [FromServices] BuAccessService buAccess,
        [FromQuery] int? shipmentId,
        [FromQuery] DateOnly? etaFrom, [FromQuery] DateOnly? etaTo,
        [FromQuery] int? businessUnitId, [FromQuery] int? consigneeId, [FromQuery] int? shippingLineId)
    {
        List<int> targetIds;
        if (shipmentId.HasValue)
        {
            targetIds = new List<int> { shipmentId.Value };
        }
        else
        {
            targetIds = await GetHitShipmentIdsAsync(buAccess, etaFrom, etaTo, businessUnitId, consigneeId, shippingLineId);
        }

        if (targetIds.Count == 0)
            return Ok(new DemurrageAnalysisResult(
                shipmentId.HasValue, 0, null, null, null, null, null, null, null, null, null, null,
                0, 0, 0, null, null, new(), 0, 0, new(), 0, null, null, new(), 0, 0, new()));

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();

        var perShipment = new List<DemurrageAnalysisResult>();
        var warnings = new List<string>();

        foreach (var id in targetIds)
        {
            var detail = await BuildSingleAsync(id, holidaySet);
            if (detail is not null) { perShipment.Add(detail); warnings.AddRange(detail.Warnings); }
        }

        if (perShipment.Count == 0)
            return Ok(new DemurrageAnalysisResult(
                shipmentId.HasValue, 0, null, null, null, null, null, null, null, null, null, null,
                0, 0, 0, null, null, new(), 0, 0, new(), 0, null, null, new(), 0, 0, new()));

        if (shipmentId.HasValue)
            return Ok(perShipment[0]);

        // --- Group mode: average every numeric figure across the set ---
        double Avg(Func<DemurrageAnalysisResult, double> selector) => perShipment.Average(selector);
        decimal AvgDec(Func<DemurrageAnalysisResult, decimal> selector) => perShipment.Average(selector);

        var allStepNames = perShipment.SelectMany(p => p.StepGaps.Select(g => g.GroupItem)).Distinct().ToList();
        var avgStepGaps = allStepNames.Select(name =>
        {
            var matching = perShipment.SelectMany(p => p.StepGaps).Where(g => g.GroupItem == name).ToList();
            var withActual = matching.Where(g => g.ActualDaysTaken.HasValue).ToList();
            var avgActual = withActual.Count > 0 ? (int?)Math.Round(withActual.Average(g => g.ActualDaysTaken!.Value)) : null;
            var avgTarget = matching.Count > 0 ? matching.Average(g => g.TargetDays) : 0;
            return new ClearanceStepGap(name, avgActual, avgTarget, avgActual.HasValue ? avgActual.Value - avgTarget : null);
        }).ToList();

        var demurrageFreeAvg = perShipment.Where(p => p.DemurrageFreeDays.HasValue).Select(p => p.DemurrageFreeDays!.Value).ToList();
        var demurrageChargeableAvg = perShipment.Where(p => p.DemurrageChargeableDays.HasValue).Select(p => p.DemurrageChargeableDays!.Value).ToList();

        return Ok(new DemurrageAnalysisResult(
            false, perShipment.Count,
            null, null, null, null, null, null, null, null, null, null,
            Avg(p => p.TotalCalendarDays), Avg(p => p.WeekendDays), Avg(p => p.HolidayDays),
            null, null, avgStepGaps,
            Avg(p => p.StorageFreeDays), Avg(p => p.StorageChargeableDays), new(), AvgDec(p => p.StorageCostSdg),
            demurrageFreeAvg.Count > 0 ? demurrageFreeAvg.Average() : null,
            demurrageChargeableAvg.Count > 0 ? demurrageChargeableAvg.Average() : null,
            new(), AvgDec(p => p.DemurrageCostSdg), AvgDec(p => p.TotalSdg),
            warnings.Distinct().ToList()));
    }

    private async Task<DemurrageAnalysisResult?> BuildSingleAsync(int shipmentId, HashSet<DateOnly> holidaySet)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.ShippingLine)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null || !shipment.Eta.HasValue) return null;

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var demurrage = await _demurrageService.CalculateAsync(shipmentId);
        var schedule = await _scheduleService.GetScheduleAsync(shipmentId);

        var eta = shipment.Eta.Value;
        var containerReturn = demurrage.DemurrageEndDate ?? eta;
        var totalCalendarDays = Math.Max(0, containerReturn.DayNumber - eta.DayNumber);

        var weekendDays = 0;
        var holidayDays = 0;
        for (var d = eta; d < containerReturn; d = d.AddDays(1))
        {
            var next = d.AddDays(1);
            if (next.DayOfWeek == DayOfWeek.Friday || next.DayOfWeek == DayOfWeek.Saturday) weekendDays++;
            else if (holidaySet.Contains(next)) holidayDays++;
        }

        // The schedule engine's own first step starts counting from
        // Actual Vessel Arrival (or ETA if not yet arrived) — not from
        // ETA itself. Any real delay between ETA and actual arrival is
        // genuine elapsed time that would otherwise silently vanish
        // from the subtotal, breaking the reconciliation against
        // ETA -> Container Return. There's no fixed SLA target for
        // vessel arrival timing itself — the vessel is simply expected
        // on ETA — so target days is 0 and the full gap is the delay.
        var actualArrival = clearance is not null
            ? (await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id))?.ActualArrivalDate
            : null;
        var vesselArrivalDays = actualArrival.HasValue
            ? ShippingPortal.Api.Services.ClearanceScheduleService.BusinessDaysBetween(eta, actualArrival.Value, holidaySet)
            : (int?)null;

        var stepGaps = new List<ClearanceStepGap>();
        if (vesselArrivalDays.HasValue)
        {
            stepGaps.Add(new ClearanceStepGap("ETA → Vessel Arrival", vesselArrivalDays.Value, 0, vesselArrivalDays.Value));
        }
        stepGaps.AddRange(schedule.Items.Select(i => new ClearanceStepGap(
            i.GroupItem, i.ActualDaysTaken, i.TargetDays,
            i.ActualDaysTaken.HasValue ? i.ActualDaysTaken.Value - i.TargetDays : null)));

        var firstItem = shipment.LineItems.FirstOrDefault();
        var totalQty = shipment.LineItems.Sum(li => li.QtyInBl);

        // Free days for the top summary — whichever charge type actually
        // has a configured tariff for this shipment.
        int? summaryFreeDays = demurrage.DemurrageFreeDays20 ?? demurrage.DemurrageFreeDays40 ?? (demurrage.StorageFreeDays > 0 ? demurrage.StorageFreeDays : null);

        var demurrageBreakdown = demurrage.DemurrageBreakdown20.Count > 0 ? demurrage.DemurrageBreakdown20 : demurrage.DemurrageBreakdown40;
        double? demFreeDays = demurrage.DemurrageFreeDays20 ?? demurrage.DemurrageFreeDays40;
        double? demChargeableDays = demurrage.DemurrageChargeableDays20 ?? demurrage.DemurrageChargeableDays40;

        return new DemurrageAnalysisResult(
            true, 1,
            shipment.PurchaseOrder?.BusinessUnit?.Name, shipment.PurchaseOrder?.Consignee?.Name,
            firstItem?.PurchaseOrderLineItem?.ProductCategory?.Name, firstItem?.PurchaseOrderLineItem?.ModelProduct?.Name, totalQty,
            shipment.BlAwbNo, shipment.Fcl20Count, shipment.Fcl40Count, shipment.ShippingLine?.Name, summaryFreeDays,
            totalCalendarDays, weekendDays, holidayDays,
            eta, clearance?.OriginalShipmentSetReceivedDate, stepGaps,
            demurrage.StorageFreeDays, demurrage.StorageChargeableDays, demurrage.StorageBreakdown, demurrage.StorageCostSdg,
            demFreeDays, demChargeableDays, demurrageBreakdown, demurrage.DemurrageCostSdg, demurrage.TotalStorageDemurrageSdg,
            demurrage.Warnings);
    }
}
