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

// TargetDays is null for MOT/SSMO Certificate Delay rows and for OS Doc
// Dispatch / Container Return — there's no fixed SLA to compare against
// for those, just a one-directional "how many days did this actually
// take" figure. Category is the accountable party — see
// ProcessStepCategories.ByStep (shared with the Process Performance
// dashboard, extended there with the entries this dashboard needs).
// Comment is the matching Clearance accordion group's Comments field
// (e.g. "SSMO Examination" here pulls from ClearanceRoute1/3Details
// .SsmoExaminationComments) — single-shipment mode only. Group mode
// (AverageStepGaps) never sets it: free text can't be meaningfully
// averaged across multiple shipments, so it stays null there and the
// frontend only renders the column when IsSingleShipment.
public record ClearanceStepGap(string GroupItem, int? ActualDaysTaken, decimal? TargetDays, decimal? Gap, string Category, string? Comment = null);

// Rolled up across BOTH the pre-arrival Document Chain (informational)
// and the post-arrival Step Gaps (the reconciling total) — the "who's
// actually accountable" view a long step-by-step table doesn't make
// obvious at a glance. TotalActualDays/TotalTargetDays only sum the
// steps that have actually happened / have a fixed target, so an
// in-progress shipment doesn't understate a category just because a
// later step hasn't landed yet.
// ---- Timeline & charge clocks (single-shipment mode) ----
// One line per container size actually on the shipment (or one "All containers" line for storage).
public record ClockSizeLine(string Label, int ContainerCount, int FreeDays, int ChargeableDays);

// A charge clock: demurrage runs from arrival until the containers are returned, storage runs from
// arrival until the trucks enter the port. EndIsActual = false means the end date is still a projection.
public record ChargeClock(
    string Name, DateOnly? StartDate, DateOnly? EndDate, bool EndIsActual, string EndLabel,
    int ElapsedDays, DateOnly? FreeEndsOn, List<ClockSizeLine> Lines, decimal CostSdg);

// One segment of the calendar-day timeline: from the previous recorded date to this step's recorded date.
// FreeDaysUsed + ChargeableDays = CalendarDays; CumulativeDays is the running day count at the end of the segment.
public record AttributionRow(string Step, DateOnly From, DateOnly To, int CalendarDays, int CumulativeDays, int FreeDaysUsed, int ChargeableDays, string? Note);

public record ClockAttribution(string Clock, int FreeDays, int TotalChargeableDays, List<AttributionRow> Rows);

public record DemurrageTimeline(
    DateOnly? ArrivalDate, bool ArrivalIsActual, DateOnly? ClearanceCompletedDate, DateOnly? TruckPortEntryDate, DateOnly? ContainersReturnedDate,
    ChargeClock Demurrage, ChargeClock Storage, List<ClockAttribution> Attribution, List<string> DataGaps);

public record CategoryGapRollup(string Category, int TotalActualDays, decimal TotalTargetDays, decimal TotalGapDays);

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
    // Reconciling: Subtotal + Weekends + Holidays should equal
    // TotalCalendarDays (Actual Arrival -> Container Return).
    List<ClearanceStepGap> StepGaps,
    // Informational only: the pre-arrival chain from Final Draft
    // Received through Original Shipment Set Received. Happens before
    // Actual Arrival, so it is NOT part of the StepGaps reconciliation
    // above — shown separately so Supplier/Internal/Bank slippage
    // upstream of vessel arrival is visible without corrupting the
    // total.
    List<ClearanceStepGap> DocumentChainSteps,
    // Combines DocumentChainSteps + StepGaps by accountable party.
    List<CategoryGapRollup> CategoryRollups,
    // Charges — tier breakdown only meaningful for a single shipment.
    // This remains the "how would the tiers calculate it today" figure;
    // it is deliberately NOT shown as a third headline total alongside
    // Forecast/Actual Paid below (would just be a third competing
    // number with no separate purpose from either of the others).
    double StorageFreeDays, double StorageChargeableDays, List<TierBreakdownLine> StorageBreakdown, decimal StorageCostSdg,
    double? DemurrageFreeDays, double? DemurrageChargeableDays, List<TierBreakdownLine> DemurrageBreakdown, decimal DemurrageCostSdg,
    decimal TotalSdg,
    // Forecast: frozen the moment Truck & Containers' own Actual
    // Completion Date was first saved. Actual Paid: manually entered
    // once the real invoice/settlement is known. Savings is simply
    // Forecast - Actual (positive = came in under forecast). Null
    // until the relevant data exists (never null-coalesced to 0, so
    // "no data yet" reads as "—" rather than a misleading 0).
    decimal? ForecastDemurrageSdg, decimal? ForecastStorageSdg, decimal? ForecastTotalSdg,
    decimal? ActualDemurragePaidSdg, decimal? ActualStoragePaidSdg, decimal? ActualTotalPaidSdg,
    decimal? SavingsSdg,
    List<string> Warnings,
    // Single-shipment mode: the Logistics-owned wait AFTER clearance - port shipment (SPC Bill
    // settlement -> whole shipment on trucks) and one line per FZ withdrawal (Release & Exit Pass ->
    // whole withdrawal on trucks). Deliberately NOT part of StepGaps / the reconciliation above
    // (demurrage stops at Container Return), shown as its own card together with the logged delay
    // reasons. Empty list = nothing to show yet.
    List<ClearanceStepGap>? LogisticsSteps = null,
    List<ShippingPortal.Api.Controllers.Logistics.DelayEntryDto>? LogisticsDelays = null,
    // Single-shipment mode: arrival / cleared / port-entry / return dates, both charge clocks, where the
    // chargeable days came from, and dates missing from the record.
    DemurrageTimeline? Timeline = null);

// Every figure here is anchored on shipments that actually had a real
// Demurrage or Storage payment recorded — this is a "what happened and
// why" analysis tool, not a forecast. Single-shipment mode shows full
// detail (dates, tier-by-tier breakdown); filtering to a group instead
// averages every numeric figure across the matching set, dropping the
// per-shipment summary and tier breakdown, which don't average
// meaningfully.
[ApiController]
[Route("api/dashboards/demurrage-analysis")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.CorpFinance + "," + AppRoles.IpSupervisor)]
public class DemurrageAnalysisController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly DemurrageStorageService _demurrageService;
    private readonly ClearanceScheduleService _scheduleService;
    private readonly LogisticsDispatchService _dispatchService;

    public DemurrageAnalysisController(ShippingPortalDbContext db, DemurrageStorageService demurrageService, ClearanceScheduleService scheduleService, LogisticsDispatchService dispatchService)
    {
        _db = db;
        _demurrageService = demurrageService;
        _scheduleService = scheduleService;
        _dispatchService = dispatchService;
    }

    private static DemurrageAnalysisResult EmptyResult(bool isSingleShipment) => new(
        isSingleShipment, 0, null, null, null, null, null, null, null, null, null, null,
        0, 0, 0, null, null, null, new(), new(), new(),
        0, 0, new(), 0, null, null, new(), 0, 0,
        null, null, null, null, null, null, null,
        new());

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
            return Ok(EmptyResult(shipmentId.HasValue));

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();

        var perShipment = new List<DemurrageAnalysisResult>();
        var warnings = new List<string>();

        foreach (var id in targetIds)
        {
            var detail = await BuildSingleAsync(id, holidaySet, includeLogistics: shipmentId.HasValue);
            if (detail is not null) { perShipment.Add(detail); warnings.AddRange(detail.Warnings); }
        }

        if (perShipment.Count == 0)
            return Ok(EmptyResult(shipmentId.HasValue));

        if (shipmentId.HasValue)
            return Ok(perShipment[0]);

        // --- Group mode: average every numeric figure across the set ---
        double Avg(Func<DemurrageAnalysisResult, double> selector) => perShipment.Average(selector);
        decimal AvgDec(Func<DemurrageAnalysisResult, decimal> selector) => perShipment.Average(selector);
        decimal? AvgDecNullable(Func<DemurrageAnalysisResult, decimal?> selector)
        {
            var vals = perShipment.Where(p => selector(p).HasValue).Select(p => selector(p)!.Value).ToList();
            return vals.Count > 0 ? vals.Average() : null;
        }

        var avgStepGaps = AverageStepGaps(perShipment.Select(p => p.StepGaps));
        var avgDocChainSteps = AverageStepGaps(perShipment.Select(p => p.DocumentChainSteps));
        var categoryRollups = BuildCategoryRollups(avgDocChainSteps.Concat(avgStepGaps));

        var demurrageFreeAvg = perShipment.Where(p => p.DemurrageFreeDays.HasValue).Select(p => p.DemurrageFreeDays!.Value).ToList();
        var demurrageChargeableAvg = perShipment.Where(p => p.DemurrageChargeableDays.HasValue).Select(p => p.DemurrageChargeableDays!.Value).ToList();
        var vesselOffsetsPresent = perShipment.Where(p => p.VesselArrivalOffsetDays.HasValue).Select(p => p.VesselArrivalOffsetDays!.Value).ToList();

        return Ok(new DemurrageAnalysisResult(
            IsSingleShipment: false, ShipmentCount: perShipment.Count,
            BusinessUnit: null, Consignee: null, Category: null, ModelProduct: null, Qty: null,
            BlAwbNo: null, Fcl20Count: null, Fcl40Count: null, ShippingLine: null, SummaryFreeDays: null,
            TotalCalendarDays: Avg(p => p.TotalCalendarDays), WeekendDays: Avg(p => p.WeekendDays), HolidayDays: Avg(p => p.HolidayDays),
            Eta: null, OriginalDocReceived: null,
            VesselArrivalOffsetDays: vesselOffsetsPresent.Count > 0 ? vesselOffsetsPresent.Average() : null,
            StepGaps: avgStepGaps,
            DocumentChainSteps: avgDocChainSteps,
            CategoryRollups: categoryRollups,
            StorageFreeDays: Avg(p => p.StorageFreeDays), StorageChargeableDays: Avg(p => p.StorageChargeableDays), StorageBreakdown: new(), StorageCostSdg: AvgDec(p => p.StorageCostSdg),
            DemurrageFreeDays: demurrageFreeAvg.Count > 0 ? demurrageFreeAvg.Average() : null,
            DemurrageChargeableDays: demurrageChargeableAvg.Count > 0 ? demurrageChargeableAvg.Average() : null,
            DemurrageBreakdown: new(), DemurrageCostSdg: AvgDec(p => p.DemurrageCostSdg), TotalSdg: AvgDec(p => p.TotalSdg),
            ForecastDemurrageSdg: AvgDecNullable(p => p.ForecastDemurrageSdg), ForecastStorageSdg: AvgDecNullable(p => p.ForecastStorageSdg), ForecastTotalSdg: AvgDecNullable(p => p.ForecastTotalSdg),
            ActualDemurragePaidSdg: AvgDecNullable(p => p.ActualDemurragePaidSdg), ActualStoragePaidSdg: AvgDecNullable(p => p.ActualStoragePaidSdg), ActualTotalPaidSdg: AvgDecNullable(p => p.ActualTotalPaidSdg),
            SavingsSdg: AvgDecNullable(p => p.SavingsSdg),
            Warnings: warnings.Distinct().ToList()));
    }

    // Shared by both single-shipment building and group-mode averaging
    // (there, called on the already-averaged step lists) — groups by
    // GroupItem name, averaging ActualDaysTaken/TargetDays only over the
    // shipments where each is actually present.
    private static List<ClearanceStepGap> AverageStepGaps(IEnumerable<List<ClearanceStepGap>> lists)
    {
        var flat = lists.SelectMany(l => l).ToList();
        var names = flat.Select(g => g.GroupItem).Distinct().ToList();
        return names.Select(name =>
        {
            var matching = flat.Where(g => g.GroupItem == name).ToList();
            var withActual = matching.Where(g => g.ActualDaysTaken.HasValue).ToList();
            var avgActual = withActual.Count > 0 ? (int?)Math.Round(withActual.Average(g => g.ActualDaysTaken!.Value)) : null;
            var targetsPresent = matching.Where(g => g.TargetDays.HasValue).ToList();
            decimal? avgTarget = targetsPresent.Count > 0 ? targetsPresent.Average(g => g.TargetDays!.Value) : null;
            return new ClearanceStepGap(name, avgActual, avgTarget, avgActual.HasValue && avgTarget.HasValue ? avgActual.Value - avgTarget.Value : avgActual, matching[0].Category);
        }).ToList();
    }

    // Only sums steps that actually have a value — an in-progress
    // shipment's later, not-yet-reached steps don't drag a category's
    // total down to look artificially good.
    private static List<CategoryGapRollup> BuildCategoryRollups(IEnumerable<ClearanceStepGap> steps)
    {
        return steps
            .GroupBy(g => g.Category)
            .Select(grp =>
            {
                var totalActual = grp.Where(g => g.ActualDaysTaken.HasValue).Sum(g => g.ActualDaysTaken!.Value);
                var totalTarget = grp.Where(g => g.TargetDays.HasValue).Sum(g => g.TargetDays!.Value);
                return new CategoryGapRollup(grp.Key, totalActual, totalTarget, totalActual - totalTarget);
            })
            .OrderByDescending(r => r.TotalGapDays)
            .ToList();
    }

    // MOT is a genuine prerequisite (see ClearanceScheduleService) —
    // this mirrors that exact same "on-schedule MOT costs nothing"
    // logic, but reports the delay as its own explicit figure rather
    // than folding it invisibly into the anchor.
    private async Task<int> ComputeMotDelayDaysAsync(int shipmentId, DateOnly eta, DateOnly actualArrival, HashSet<DateOnly> holidaySet, DateOnly today)
    {
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId);
        var motTargetDays = await _db.ClearanceSlaSettings
            .Where(s => s.IsActive && s.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.PreClearanceMot)
            .Select(s => (decimal?)s.TargetDays).FirstOrDefaultAsync() ?? 0;

        var motTarget = ClearanceScheduleService.SubtractBusinessDays(eta, (int)Math.Ceiling(motTargetDays), holidaySet);
        var motEffective = mot?.ApprovalDate ?? (today > motTarget ? today : motTarget);

        return motEffective > actualArrival ? ClearanceScheduleService.BusinessDaysBetween(actualArrival, motEffective, holidaySet) : 0;
    }

    // Same one-directional treatment for SSMO COC, checked against
    // whatever point the cascade had reached right before SSMO File
    // Process would otherwise have started.
    private async Task<int> ComputeSsmoDelayDaysAsync(int shipmentId, DateOnly chainPoint, HashSet<DateOnly> holidaySet, DateOnly today)
    {
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId);
        if (ssmo?.CocRequired != true || ssmo.CocAvailable == true) return 0;

        var cocReady = ssmo.ApprovalDate ?? today;
        return cocReady > chainPoint ? ClearanceScheduleService.BusinessDaysBetween(chainPoint, cocReady, holidaySet) : 0;
    }

    private async Task<DemurrageAnalysisResult?> BuildSingleAsync(int shipmentId, HashSet<DateOnly> holidaySet, bool includeLogistics = false)
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

        // --- Pre-Arrival Document Chain (informational — not part of the reconciling StepGaps below) ---
        var docsSla = await _db.ClearanceSlaSettings
            .Where(s => s.IsActive && s.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.PreClearanceDocs)
            .ToListAsync();
        decimal? DocsTarget(string groupItem) => docsSla.FirstOrDefault(s => s.GroupItem == groupItem)?.TargetDays;

        var draftDoc = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);
        var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId);
        var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(b => b.ShipmentId == shipmentId);

        var documentChainSteps = new List<ClearanceStepGap>();
        DateOnly? chainActual = null;

        void AddDocStep(string name, DateOnly? actualEnd, decimal? targetDays)
        {
            int? actualDays = (chainActual.HasValue && actualEnd.HasValue)
                ? ClearanceScheduleService.BusinessDaysBetween(chainActual.Value, actualEnd.Value, holidaySet)
                : (int?)null;
            decimal? gap = (actualDays.HasValue && targetDays.HasValue) ? actualDays.Value - targetDays.Value : null;
            var category = ProcessStepCategories.ByStep.GetValueOrDefault(name, "Internal");
            documentChainSteps.Add(new ClearanceStepGap(ProcessStepCategories.DisplayName(name), actualDays, targetDays, gap, category));
            if (actualEnd.HasValue) chainActual = actualEnd;
        }

        AddDocStep("Final Draft Received", draftDoc?.FinalDraftReceivedDate, DocsTarget("Final Draft Received"));
        AddDocStep("Final Draft Confirmed", draftDoc?.FinalDraftConfirmedDate, DocsTarget("Final Draft Confirmed"));
        AddDocStep("FS Received", fullSet?.FsReceivedDate, DocsTarget("FS Received"));
        // OS Doc Dispatch — the processing team's own turnaround getting
        // documents to the bank once the Full Set is in hand. No SLA row
        // of its own (deliberately, to avoid disturbing the Pipeline
        // Health readiness cascade's document-chain sequencing) — a
        // one-directional actual-days figure, same treatment as the MOT/
        // SSMO Certificate Delay rows further down.
        AddDocStep("OS Doc Dispatch", banking?.OsDocDispatchDate, null);

        // Original Shipment Set Received is measured specifically from
        // OS Doc Dispatch, not the running chain above — matching
        // Process Performance's own treatment of this exact step.
        {
            var actualDays = (banking?.OsDocDispatchDate.HasValue == true && clearance?.OriginalShipmentSetReceivedDate.HasValue == true)
                ? (int?)ClearanceScheduleService.BusinessDaysBetween(banking!.OsDocDispatchDate!.Value, clearance!.OriginalShipmentSetReceivedDate!.Value, holidaySet)
                : null;
            var target = DocsTarget("Original Shipment Set Received");
            decimal? gap = (actualDays.HasValue && target.HasValue) ? actualDays.Value - target.Value : null;
            documentChainSteps.Add(new ClearanceStepGap("Original Shipment Set Received", actualDays, target, gap,
                ProcessStepCategories.ByStep.GetValueOrDefault("Original Shipment Set Received", "Internal")));
        }

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

        // --- Per-step Comments, single-shipment mode only ---
        // Keyed by the same GroupItem strings ClearanceScheduleService
        // uses, sourced from whichever route's accordion group actually
        // backs that step (mirrors the Clearance list's own DO/Customs
        // Examination/SSMO Examination/Truck & Containers columns, plus
        // the two General-division groups — Delivery Order, Clearance
        // Cost Estimate — and Customs Certificate Entry, which is a
        // shared table for Route 1/2 but its own field on Route 3).
        var stepComments = new Dictionary<string, string?>();
        if (clearance is not null)
        {
            var deliveryOrderRow = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id);
            stepComments["Delivery Order"] = deliveryOrderRow?.Comments;

            var costEstimateRow = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(c => c.ClearanceId == clearance.Id);
            stepComments["Clearance Cost Estimate"] = costEstimateRow?.Comments;

            if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort
                || clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit)
            {
                var certEntryRow = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(e => e.ClearanceId == clearance.Id);
                stepComments["Customs Certificate Entry"] = certEntryRow?.Comments;
            }

            if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort)
            {
                var r1c = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id);
                stepComments["Containers Move Process"] = r1c?.ContainersMoveProcessComments;
                stepComments["SSMO File Process"] = r1c?.SsmoFileProcessComments;
                stepComments["Customs Examination (Form 48)"] = r1c?.CustomsExaminationComments;
                stepComments["Customs Lab"] = r1c?.CustomsLabComments;
                stepComments["SSMO Examination"] = r1c?.SsmoExaminationComments;
                stepComments["Customs Evaluation"] = r1c?.CustomsEvaluationComments;
                stepComments["SPC Bill"] = r1c?.SpcBillComments;
                stepComments["Truck & Containers"] = r1c?.TruckContainersComments;
            }
            else if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit)
            {
                var r2c = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id);
                stepComments["FZ Deposit Request"] = r2c?.FzDepositRequestComments;
                stepComments["Customs Inspection"] = r2c?.CustomsInspectionComments;
                stepComments["SPC Bill"] = r2c?.SpcBillComments;
                stepComments["Truck & Containers"] = r2c?.TruckContainersComments;
            }
            else if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz)
            {
                var r3c = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id);
                stepComments["Customs Certificate Entry"] = r3c?.CertificateEntryComments;
                stepComments["SSMO File Process"] = r3c?.SsmoFileProcessComments;
                stepComments["Customs Examination (Form 48)"] = r3c?.CustomsExaminationComments;
                stepComments["Customs Lab"] = r3c?.CustomsLabComments;
                stepComments["SSMO Examination"] = r3c?.SsmoExaminationComments;
                stepComments["Customs Evaluation"] = r3c?.CustomsEvaluationComments;
                stepComments["Truck & Containers"] = r3c?.TruckContainersComments;
            }
        }

        // Strips the " (ongoing)" suffix used for the currently-in-progress
        // step so its comment still resolves to the same underlying group.
        string? CommentFor(string groupItem)
        {
            const string suffix = " (ongoing)";
            var key = groupItem.EndsWith(suffix) ? groupItem[..^suffix.Length] : groupItem;
            return stepComments.GetValueOrDefault(key);
        }

        // Each step is measured from the LAST RECORDED date before it (not from "today" or the engine's own
        // chain), so one blank date can never poison the steps after it: the days of an unrecorded step are
        // simply counted in the next recorded one, and the table still reconciles to arrival -> container return.
        var scheduleItems = schedule.Items;
        var lastActualIndex = scheduleItems.FindLastIndex(x => x.ActualDate.HasValue);
        var lastKnownDate = schedule.AnchorDate ?? spanStart;
        var foundCurrentStep = false;
        for (var idx = 0; idx < scheduleItems.Count; idx++)
        {
            var i = scheduleItems[idx];
            if (i.GroupItem == "Customs Lab" && !customsLabRequired && !i.ActualDate.HasValue)
            {
                continue;
            }

            var category = ProcessStepCategories.ByStep.GetValueOrDefault(i.GroupItem, "Internal");

            // MOT is checked at the moment of vessel arrival, before
            // "Delivery Order" — a showstopper that either blocked
            // things (only ever adds) or didn't (shows 0), never a
            // fixed SLA to gap against.
            if (i.GroupItem == "Delivery Order" && actualArrival.HasValue)
            {
                var motDelay = await ComputeMotDelayDaysAsync(shipmentId, eta, actualArrival.Value, holidaySet, today);
                stepGaps.Add(new ClearanceStepGap("MOT Certificate Delays", motDelay, null, motDelay, ProcessStepCategories.ByStep.GetValueOrDefault("MOT Certificate Delays", "Government")));
            }

            if (i.ActualDate.HasValue)
            {
                var actualDate = i.ActualDate.Value;
                var stepDays = actualDate > lastKnownDate ? ClearanceScheduleService.BusinessDaysBetween(lastKnownDate, actualDate, holidaySet) : 0;
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, stepDays, i.TargetDays, stepDays - i.TargetDays, category, CommentFor(i.GroupItem)));
                if (actualDate > lastKnownDate) lastKnownDate = actualDate;
            }
            else if (idx < lastActualIndex)
            {
                // A later step already has its date, so this one is finished in reality - its date was just never entered.
                stepGaps.Add(new ClearanceStepGap($"{i.GroupItem} (date not recorded)", null, i.TargetDays, null, category, CommentFor(i.GroupItem)));
            }
            else if (!foundCurrentStep)
            {
                foundCurrentStep = true;
                var elapsedSoFar = Math.Max(0, ClearanceScheduleService.BusinessDaysBetween(lastKnownDate, today, holidaySet));
                stepGaps.Add(new ClearanceStepGap($"{i.GroupItem} (ongoing)", elapsedSoFar, i.TargetDays, elapsedSoFar - i.TargetDays, category, CommentFor(i.GroupItem)));
            }
            else
            {
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, null, i.TargetDays, null, category, CommentFor(i.GroupItem)));
            }

            // SSMO COC is checked right before its own File Process step —
            // same one-directional, no-fixed-SLA treatment as MOT.
            if (i.GroupItem == "Containers Move Process")
            {
                var chainPoint = i.ActualDate ?? i.TargetDate;
                var ssmoDelay = await ComputeSsmoDelayDaysAsync(shipmentId, chainPoint, holidaySet, today);
                stepGaps.Add(new ClearanceStepGap("SSMO Certificate Delays", ssmoDelay, null, ssmoDelay, ProcessStepCategories.ByStep.GetValueOrDefault("SSMO Certificate Delays", "Government")));
            }
        }

        // Container Return — closes the gap that used to break the
        // reconciliation: Truck & Containers' own actual completion
        // (ClearanceActualCompletedDate) is when the clearance/trucking
        // team consider the job done, but demurrage doesn't actually
        // stop until the container is physically handed back
        // (ContainersReturnedDate). Those two dates can differ, and the
        // days between them were previously uncounted by any step —
        // this is that missing step. Only meaningful for Route 1/Route 2
        // (Route 3 withdrawals don't have a container-return event of
        // their own — the container was already returned under the
        // original Route 2 deposit).
        if (clearance is not null)
        {
            DateOnly? completedDate = null;
            DateOnly? returnedDate = null;
            if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort)
            {
                var r1 = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id);
                completedDate = r1?.ClearanceActualCompletedDate;
                returnedDate = r1?.ContainersReturnedDate;
            }
            else if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit)
            {
                var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(r => r.ClearanceId == clearance.Id);
                completedDate = r2?.ClearanceActualCompletedDate;
                returnedDate = r2?.ContainersReturnedDate;
            }

            if (clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort
                || clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit)
            {
                var category = ProcessStepCategories.ByStep.GetValueOrDefault("Container Return", "Internal");
                if (completedDate.HasValue && returnedDate.HasValue)
                {
                    var days = Math.Max(0, ClearanceScheduleService.BusinessDaysBetween(completedDate.Value, returnedDate.Value, holidaySet));
                    stepGaps.Add(new ClearanceStepGap("Container Return", days, null, days, category));
                }
                else
                {
                    stepGaps.Add(new ClearanceStepGap("Container Return", null, null, null, category));
                }
            }
        }

        // Logistics Dispatch: own card, never added into the reconciling step table. Single-shipment view only.
        List<ClearanceStepGap>? logisticsSteps = null;
        List<ShippingPortal.Api.Controllers.Logistics.DelayEntryDto>? logisticsDelays = null;
        if (includeLogistics)
        {
            var dispatchTarget = await _dispatchService.GetTargetDaysAsync();
            var dispatchInfos = (await _dispatchService.GetAllAsync(new List<int> { shipmentId }, includeProjectedRelease: true))
                .GetValueOrDefault(shipmentId) ?? new List<DispatchInfo>();
            var logisticsCategory = ProcessStepCategories.ByStep.GetValueOrDefault("Logistics Dispatch", "Logistics");

            logisticsSteps = new List<ClearanceStepGap>();
            foreach (var dp in dispatchInfos.Where(x => x.ReleaseDate.HasValue).OrderBy(x => x.ReleaseDate))
            {
                var scope = dp.WithdrawalId.HasValue
                    ? $"Logistics Dispatch - Withdrawal {(string.IsNullOrWhiteSpace(dp.WithdrawalRef) ? "#" + dp.WithdrawalId : dp.WithdrawalRef)}"
                    : "Logistics Dispatch";
                if (dp.ReleaseIsForecast)
                {
                    // SPC Bill not settled yet: the clock has not started - show the target only.
                    logisticsSteps.Add(new ClearanceStepGap(scope + " (waiting for SPC Bill settlement)", null, dispatchTarget, null, logisticsCategory));
                    continue;
                }
                var endDate = dp.FullyDispatchedDate ?? today;
                var dispatchDays = Math.Max(0, ClearanceScheduleService.BusinessDaysBetween(dp.ReleaseDate!.Value, endDate, holidaySet));
                var stepLabel = dp.FullyDispatchedDate.HasValue ? scope : scope + " (ongoing)";
                logisticsSteps.Add(new ClearanceStepGap(stepLabel, dispatchDays, dispatchTarget, dispatchDays - dispatchTarget, logisticsCategory));
            }

            var delayRows = await _db.LogisticsDelayLogs.Include(d => d.Reason)
                .Where(d => d.ShipmentId == shipmentId)
                .OrderBy(d => d.FromDate).ThenBy(d => d.Id)
                .ToListAsync();
            logisticsDelays = delayRows.Select(d => new ShippingPortal.Api.Controllers.Logistics.DelayEntryDto(
                d.Id, d.ShipmentId, d.WithdrawalId, d.ReasonId, d.Reason?.Name ?? "",
                d.FromDate, d.ToDate,
                LogisticsDispatchService.InclusiveBusinessDays(d.FromDate, d.ToDate ?? today, holidaySet),
                !d.ToDate.HasValue, d.Note, d.CreatedBy, d.CreatedAt)).ToList();
        }

        // Timeline & charge clocks (single-shipment view only).
        DemurrageTimeline? timeline = null;
        if (includeLogistics && clearance is not null && demurrage.Applicable && demurrage.AnchorDate.HasValue)
        {
            timeline = BuildTimeline(shipment, clearance, demurrage, schedule, actualArrival.HasValue,
                customsLabRequired,
                clearance.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort
                    ? (await _db.ClearanceRoute1Details.Where(r => r.ClearanceId == clearance.Id)
                        .Select(r => new RouteDates(r.ClearanceActualCompletedDate, r.TruckPortEntryPermitDate, r.ContainersReturnedDate)).FirstOrDefaultAsync())
                    : (await _db.ClearanceRoute2Details.Where(r => r.ClearanceId == clearance.Id)
                        .Select(r => new RouteDates(r.ClearanceActualCompletedDate, r.TruckPortEntryPermitDate, r.ContainersReturnedDate)).FirstOrDefaultAsync()));
        }

        var firstItem = shipment.LineItems.FirstOrDefault();
        var totalQty = shipment.LineItems.Sum(li => li.QtyInBl);

        // Free days / chargeable days for the summary: use the container size actually on this shipment
        // (a 40ft-only shipment must show the 40ft tariff even when a 20ft tariff is also configured).
        var useSize20 = shipment.Fcl20Count > 0 || shipment.Fcl40Count == 0;
        int? demFreeInt = useSize20
            ? (demurrage.DemurrageFreeDays20 ?? demurrage.DemurrageFreeDays40)
            : (demurrage.DemurrageFreeDays40 ?? demurrage.DemurrageFreeDays20);
        int? demChargeableInt = useSize20
            ? (demurrage.DemurrageChargeableDays20 ?? demurrage.DemurrageChargeableDays40)
            : (demurrage.DemurrageChargeableDays40 ?? demurrage.DemurrageChargeableDays20);
        int? summaryFreeDays = demFreeInt ?? (demurrage.StorageFreeDays > 0 ? demurrage.StorageFreeDays : null);

        var demurrageBreakdown = (useSize20 ? demurrage.DemurrageBreakdown20 : demurrage.DemurrageBreakdown40).Count > 0
            ? (useSize20 ? demurrage.DemurrageBreakdown20 : demurrage.DemurrageBreakdown40)
            : (demurrage.DemurrageBreakdown20.Count > 0 ? demurrage.DemurrageBreakdown20 : demurrage.DemurrageBreakdown40);
        double? demFreeDays = demFreeInt;
        double? demChargeableDays = demChargeableInt;

        // Forecast (frozen at Truck & Containers completion) vs. Actual
        // Paid (manually entered once known) vs. Savings. Null — not 0
        // — until the underlying data exists, so "no data yet" reads
        // honestly instead of as a misleadingly good/bad number.
        var charges = clearance is not null
            ? await _db.ClearanceActualCharges.FirstOrDefaultAsync(c => c.ClearanceId == clearance.Id)
            : null;
        decimal? forecastTotal = (charges?.ForecastDemurrageSdg.HasValue == true || charges?.ForecastStorageSdg.HasValue == true)
            ? (charges?.ForecastDemurrageSdg ?? 0) + (charges?.ForecastStorageSdg ?? 0)
            : null;
        decimal? actualPaidTotal = (charges?.ActualDemurragePaidSdg.HasValue == true || charges?.ActualStoragePaidSdg.HasValue == true)
            ? (charges?.ActualDemurragePaidSdg ?? 0) + (charges?.ActualStoragePaidSdg ?? 0)
            : null;
        decimal? savings = (forecastTotal.HasValue && actualPaidTotal.HasValue) ? forecastTotal - actualPaidTotal : null;

        var categoryRollups = BuildCategoryRollups(documentChainSteps.Concat(stepGaps));

        return new DemurrageAnalysisResult(
            IsSingleShipment: true, ShipmentCount: 1,
            BusinessUnit: shipment.PurchaseOrder?.BusinessUnit?.Name, Consignee: shipment.PurchaseOrder?.Consignee?.Name,
            Category: firstItem?.PurchaseOrderLineItem?.ProductCategory?.Name, ModelProduct: firstItem?.PurchaseOrderLineItem?.ModelProduct?.Name, Qty: totalQty,
            BlAwbNo: shipment.BlAwbNo, Fcl20Count: shipment.Fcl20Count, Fcl40Count: shipment.Fcl40Count, ShippingLine: shipment.ShippingLine?.Name, SummaryFreeDays: summaryFreeDays,
            TotalCalendarDays: totalCalendarDays, WeekendDays: weekendDays, HolidayDays: holidayDays,
            Eta: eta, OriginalDocReceived: clearance?.OriginalShipmentSetReceivedDate,
            VesselArrivalOffsetDays: vesselArrivalOffsetDays,
            StepGaps: stepGaps,
            DocumentChainSteps: documentChainSteps,
            CategoryRollups: categoryRollups,
            StorageFreeDays: demurrage.StorageFreeDays, StorageChargeableDays: demurrage.StorageChargeableDays, StorageBreakdown: demurrage.StorageBreakdown, StorageCostSdg: demurrage.StorageCostSdg,
            DemurrageFreeDays: demFreeDays, DemurrageChargeableDays: demChargeableDays, DemurrageBreakdown: demurrageBreakdown, DemurrageCostSdg: demurrage.DemurrageCostSdg, TotalSdg: demurrage.TotalStorageDemurrageSdg,
            ForecastDemurrageSdg: charges?.ForecastDemurrageSdg, ForecastStorageSdg: charges?.ForecastStorageSdg, ForecastTotalSdg: forecastTotal,
            ActualDemurragePaidSdg: charges?.ActualDemurragePaidSdg, ActualStoragePaidSdg: charges?.ActualStoragePaidSdg, ActualTotalPaidSdg: actualPaidTotal,
            SavingsSdg: savings,
            Warnings: demurrage.Warnings,
            LogisticsSteps: logisticsSteps, LogisticsDelays: logisticsDelays, Timeline: timeline);
    }

    private record RouteDates(DateOnly? Completed, DateOnly? TruckPortEntry, DateOnly? ContainersReturned);

    // Where each Clearance step's date lives, so a missing date can name the exact field the officer has to fill in.
    private static readonly Dictionary<string, string> StepDateFields = new()
    {
        ["Delivery Order"] = "DO Received Date",
        ["Clearance Cost Estimate"] = "Amount Settled Date",
        ["Customs Certificate Entry"] = "Certificate Entry Date",
        ["Containers Move Process"] = "Bill Settlement Date",
        ["SSMO File Process"] = "SSMO Fees Settlement Date",
        ["Customs Examination (Form 48)"] = "Exam Completed Date",
        ["Customs Lab"] = "Lab Result Issuance Date",
        ["SSMO Examination"] = "SSMO Cert Issuance Date",
        ["Customs Evaluation"] = "Release & Exit Pass Date",
        ["SPC Bill"] = "SPC Bill Settlement Date",
        ["FZ Deposit Request"] = "Request Approval Date",
        ["Customs Inspection"] = "Inspection Date",
        ["Truck & Containers"] = "Actual Completed Date"
    };

    private static DemurrageTimeline BuildTimeline(
        ShippingPortal.Api.Models.Shipments.Shipment shipment,
        ShippingPortal.Api.Models.Clearance.Clearance clearance,
        DemurrageStorageResult demurrage, ClearanceScheduleResult schedule,
        bool arrivalIsActual, bool customsLabRequired, RouteDates? route)
    {
        var start = demurrage.AnchorDate!.Value;
        var gaps = new List<string>();

        // --- Recorded step dates, in order, as calendar-day segments from true arrival ---
        var items = schedule.Items;
        var lastActualIndex = items.FindLastIndex(x => x.ActualDate.HasValue);
        var segments = new List<(string Step, DateOnly From, DateOnly To, string? Note)>();
        var prev = start;
        var unrecorded = new List<string>();
        for (var idx = 0; idx < items.Count; idx++)
        {
            var it = items[idx];
            if (it.GroupItem == "Customs Lab" && !customsLabRequired && !it.ActualDate.HasValue) continue;
            if (!it.ActualDate.HasValue)
            {
                if (idx < lastActualIndex)
                {
                    unrecorded.Add(it.GroupItem);
                    var field = StepDateFields.GetValueOrDefault(it.GroupItem, "date");
                    gaps.Add($"{it.GroupItem}: '{field}' is blank although later steps are done. Its days are counted inside the next recorded step.");
                }
                continue;
            }
            var date = it.ActualDate.Value;
            if (date < prev)
            {
                gaps.Add($"{it.GroupItem}: its date ({date:dd MMM yyyy}) is earlier than the previous recorded step ({prev:dd MMM yyyy}) - check the dates.");
                date = prev;
            }
            string? note = unrecorded.Count > 0 ? "includes unrecorded: " + string.Join(", ", unrecorded) : null;
            segments.Add((it.GroupItem, prev, date, note));
            prev = date;
            unrecorded.Clear();
        }
        var lastRecorded = prev;

        if (!arrivalIsActual)
            gaps.Add("Actual Arrival Date is not entered, so the ETA is used as the arrival date. Every charge day below is counted from it.");
        if (route?.Completed.HasValue == true && route.ContainersReturned is null)
            gaps.Add("Truck & Containers is complete but 'Containers Returned' is blank - the demurrage clock is still running (end date is a projection).");
        if (route?.Completed.HasValue == true && route.TruckPortEntry is null)
            gaps.Add("Truck & Containers is complete but 'Truck Port Entry Permit' is blank - the storage clock is still running (end date is a projection).");

        // Walks the segments for one clock: free days are used up by the earliest days, everything after is chargeable.
        ClockAttribution BuildAttribution(string clock, int freeDays, DateOnly end, bool endIsActual, string endLabel)
        {
            var rows = new List<AttributionRow>();
            var running = 0;
            var totalChargeable = 0;

            void AddRow(string step, DateOnly from, DateOnly to, string? note)
            {
                var days = to.DayNumber - from.DayNumber;
                var free = Math.Max(0, Math.Min(days, freeDays - running));
                var chargeable = days - free;
                running += days;
                totalChargeable += chargeable;
                rows.Add(new AttributionRow(step, from, to, days, running, free, chargeable, note));
            }

            foreach (var seg in segments)
            {
                if (seg.From >= end) break;
                // A step that finished the same day as the previous one adds nothing to the timeline (unless it carries a note).
                if (seg.To == seg.From && seg.Note is null) continue;
                AddRow(seg.Step, seg.From, seg.To > end ? end : seg.To, seg.Note);
            }
            var covered = lastRecorded < end ? lastRecorded : end;
            if (covered < end)
                AddRow(endIsActual ? endLabel : $"{endLabel} (projected - not yet recorded)", covered, end, null);

            return new ClockAttribution(clock, freeDays, totalChargeable, rows);
        }

        // --- Clocks ---
        var demEnd = demurrage.DemurrageEndDate ?? start;
        var stoEnd = demurrage.StorageEndDate ?? start;

        var demLines = new List<ClockSizeLine>();
        if (shipment.Fcl20Count > 0 && demurrage.DemurrageFreeDays20.HasValue)
            demLines.Add(new ClockSizeLine("20ft", shipment.Fcl20Count, demurrage.DemurrageFreeDays20.Value, demurrage.DemurrageChargeableDays20 ?? 0));
        if (shipment.Fcl40Count > 0 && demurrage.DemurrageFreeDays40.HasValue)
            demLines.Add(new ClockSizeLine("40ft", shipment.Fcl40Count, demurrage.DemurrageFreeDays40.Value, demurrage.DemurrageChargeableDays40 ?? 0));

        var stoLines = new List<ClockSizeLine>
        {
            new("All containers", shipment.Fcl20Count + shipment.Fcl40Count, demurrage.StorageFreeDays, demurrage.StorageChargeableDays)
        };

        var demClock = new ChargeClock("Demurrage (shipping line)", start, demEnd, demurrage.DemurrageEndIsActual, "Containers Returned",
            Math.Max(0, demEnd.DayNumber - start.DayNumber),
            demLines.Count > 0 ? start.AddDays(demLines.Max(l => l.FreeDays)) : null, demLines, demurrage.DemurrageCostSdg);
        var stoClock = new ChargeClock("Storage (port / SPC)", start, stoEnd, demurrage.StorageEndIsActual, "Truck Port Entry",
            Math.Max(0, stoEnd.DayNumber - start.DayNumber),
            start.AddDays(demurrage.StorageFreeDays), stoLines, demurrage.StorageCostSdg);

        var attribution = new List<ClockAttribution>();
        foreach (var l in demLines)
            attribution.Add(BuildAttribution($"Demurrage - {l.Label}", l.FreeDays, demEnd, demurrage.DemurrageEndIsActual, "Container Return"));
        attribution.Add(BuildAttribution("Storage", demurrage.StorageFreeDays, stoEnd, demurrage.StorageEndIsActual, "Truck Port Entry"));

        return new DemurrageTimeline(
            start, arrivalIsActual, route?.Completed, route?.TruckPortEntry, route?.ContainersReturned,
            demClock, stoClock, attribution, gaps);
    }
}
