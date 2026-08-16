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

// TargetDays is null for MOT/SSMO Certificate Delay rows — there's no
// fixed SLA to compare against, just a one-directional "did this block
// things, and by how much" figure that's always >= 0.
public record ClearanceStepGap(string GroupItem, int? ActualDaysTaken, decimal? TargetDays, decimal? Gap);

public record DemurrageAnalysisResult(
    bool IsSingleShipment, int ShipmentCount,
    // Summary — only populated for single-shipment mode
    string? BusinessUnit, string? Consignee, string? Category, string? ModelProduct, decimal? Qty,
    string? BlAwbNo, int? Fcl20Count, int? Fcl40Count, string? ShippingLine, int? SummaryFreeDays,
    // General Info — averaged across shipments in group mode
    double TotalCalendarDays, double WeekendDays, double HolidayDays,
    DateOnly? Eta, DateOnly? OriginalDocReceived,
    // Diagnostic only — never folded into any subtotal. Positive means
    // the vessel arrived late vs. ETA, negative means early. In group
    // mode this is a genuine average (can be negative), unlike every
    // other day-count figure which only ever adds.
    double? VesselArrivalOffsetDays,
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
        var charges = await _db.ClearanceActualCharges.Where(c => clearanceIds.Contains(c.ClearanceId)).ToListAsync();

        // This dashboard analyzes genuinely incurred hits after the
        // fact — an "actual paid" figure alone isn't enough if the
        // event it's paid against hasn't actually happened yet
        // (e.g. entered early, or a data mistake). Demurrage only
        // really stops at Containers Returned; Storage only really
        // stops at Truck Port Entry. A shipment belongs here only once
        // the relevant one has actually happened.
        var route1Returns = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => new { r.ContainersReturnedDate, r.TruckPortEntryPermitDate });
        var route2Returns = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => new { r.ContainersReturnedDate, r.TruckPortEntryPermitDate });

        var hitClearanceIds = new List<int>();
        foreach (var c in charges)
        {
            var demurrageHit = (c.ActualDemurragePaidSdg ?? 0) > 0;
            var storageHit = (c.ActualStoragePaidSdg ?? 0) > 0;
            if (!demurrageHit && !storageHit) continue;

            DateOnly? containersReturned = route1Returns.TryGetValue(c.ClearanceId, out var r1) ? r1.ContainersReturnedDate
                : route2Returns.TryGetValue(c.ClearanceId, out var r2) ? r2.ContainersReturnedDate : null;
            DateOnly? truckPortEntry = route1Returns.TryGetValue(c.ClearanceId, out var r1b) ? r1b.TruckPortEntryPermitDate
                : route2Returns.TryGetValue(c.ClearanceId, out var r2b) ? r2b.TruckPortEntryPermitDate : null;

            var demurrageReady = demurrageHit && containersReturned.HasValue;
            var storageReady = storageHit && truckPortEntry.HasValue;
            if (demurrageReady || storageReady) hitClearanceIds.Add(c.ClearanceId);
        }

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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eta = shipment.Eta.Value;
        var containerReturn = demurrage.DemurrageEndDate ?? eta;

        var actualArrival = clearance is not null
            ? (await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id))?.ActualArrivalDate
            : null;

        // Reconciliation basis is Actual Arrival -> Container Return,
        // not ETA -> Container Return. Vessel timing is a pure shift of
        // the whole window, not a performance credit/debit, so it's
        // reported separately below and deliberately excluded here.
        var spanStart = actualArrival ?? eta;
        var totalCalendarDays = Math.Max(0, containerReturn.DayNumber - spanStart.DayNumber);

        var weekendDays = 0;
        var holidayDays = 0;
        for (var d = spanStart; d < containerReturn; d = d.AddDays(1))
        {
            var next = d.AddDays(1);
            if (next.DayOfWeek == DayOfWeek.Friday || next.DayOfWeek == DayOfWeek.Saturday) weekendDays++;
            else if (holidaySet.Contains(next)) holidayDays++;
        }

        // Diagnostic only, never folded into any total — early arrival
        // doesn't buy the clearance team anything (the sequential steps
        // below already measure their own performance from whatever the
        // real anchor was), and late arrival isn't the clearance team's
        // fault either. This just answers "was the vessel early or late,
        // and could that explain why MOT/SSMO ran out of runway."
        double? vesselArrivalOffsetDays = actualArrival.HasValue
            ? (actualArrival.Value > eta
                ? ClearanceScheduleService.BusinessDaysBetween(eta, actualArrival.Value, holidaySet)
                : (actualArrival.Value < eta ? -ClearanceScheduleService.BusinessDaysBetween(actualArrival.Value, eta, holidaySet) : 0))
            : null;

        var stepGaps = new List<ClearanceStepGap>();

        // The schedule engine always includes Customs Lab as a fixed
        // step, with no awareness of whether it's actually required for
        // this shipment — treating it as "incomplete" when it's
        // genuinely just skipped would falsely surface it as the
        // current bottleneck, manufacturing a buffer that isn't real.
        var customsLabRequired = clearance?.Route switch
        {
            ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort =>
                (await _db.ClearanceRoute1Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id))?.CustomsLabRequired ?? false,
            ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz =>
                (await _db.ClearanceRoute3Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id))?.CustomsLabRequired ?? false,
            _ => true
        };

        var foundCurrentStep = false;
        foreach (var i in schedule.Items)
        {
            if (i.GroupItem == "Customs Lab" && !customsLabRequired && !i.ActualDaysTaken.HasValue)
            {
                continue;
            }

            // MOT is checked at the moment of vessel arrival, before
            // "Delivery Order" — a showstopper that either blocked
            // things (only ever adds) or didn't (shows 0), never a
            // fixed SLA to gap against.
            if (i.GroupItem == "Delivery Order" && actualArrival.HasValue)
            {
                var motDelay = await ComputeMotDelayDaysAsync(shipmentId, eta, actualArrival.Value, holidaySet, today);
                stepGaps.Add(new ClearanceStepGap("MOT Certificate Delays", motDelay, null, motDelay));
            }

            if (i.ActualDaysTaken.HasValue)
            {
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, i.ActualDaysTaken, i.TargetDays, i.ActualDaysTaken.Value - i.TargetDays));
            }
            else if (!foundCurrentStep)
            {
                foundCurrentStep = true;
                var wholeDays = (int)Math.Ceiling(i.TargetDays);
                var stepStart = ClearanceScheduleService.SubtractBusinessDays(i.TargetDate, wholeDays, holidaySet);
                var elapsedSoFar = ClearanceScheduleService.BusinessDaysBetween(stepStart, today, holidaySet);
                stepGaps.Add(new ClearanceStepGap($"{i.GroupItem} (ongoing)", elapsedSoFar, i.TargetDays, elapsedSoFar - i.TargetDays));
            }
            else
            {
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, null, i.TargetDays, null));
            }

            // SSMO COC is checked right before its own File Process step —
            // same one-directional, no-fixed-SLA treatment as MOT.
            if (i.GroupItem == "Containers Move Process")
            {
                var chainPoint = i.ActualDate ?? i.TargetDate;
                var ssmoDelay = await ComputeSsmoDelayDaysAsync(shipmentId, chainPoint, holidaySet, today);
                stepGaps.Add(new ClearanceStepGap("SSMO Certificate Delays", ssmoDelay, null, ssmoDelay));
            }
        }

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
