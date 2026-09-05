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
public record ClearanceStepGap(string GroupItem, int? ActualDaysTaken, decimal? TargetDays, decimal? Gap, string Category);

// Rolled up across BOTH the pre-arrival Document Chain (informational)
// and the post-arrival Step Gaps (the reconciling total) — the "who's
// actually accountable" view a long step-by-step table doesn't make
// obvious at a glance. TotalActualDays/TotalTargetDays only sum the
// steps that have actually happened / have a fixed target, so an
// in-progress shipment doesn't understate a category just because a
// later step hasn't landed yet.
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
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.CorpFinance + "," + AppRoles.IpSupervisor)]
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
            var detail = await BuildSingleAsync(id, holidaySet);
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
            documentChainSteps.Add(new ClearanceStepGap(name, actualDays, targetDays, gap, category));
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
            var gap = (actualDays.HasValue && target.HasValue) ? actualDays.Value - target.Value : null;
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

        var foundCurrentStep = false;
        foreach (var i in schedule.Items)
        {
            if (i.GroupItem == "Customs Lab" && !customsLabRequired && !i.ActualDaysTaken.HasValue)
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

            if (i.ActualDaysTaken.HasValue)
            {
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, i.ActualDaysTaken, i.TargetDays, i.ActualDaysTaken.Value - i.TargetDays, category));
            }
            else if (!foundCurrentStep)
            {
                foundCurrentStep = true;
                var wholeDays = (int)Math.Ceiling(i.TargetDays);
                var stepStart = ClearanceScheduleService.SubtractBusinessDays(i.TargetDate, wholeDays, holidaySet);
                var elapsedSoFar = ClearanceScheduleService.BusinessDaysBetween(stepStart, today, holidaySet);
                stepGaps.Add(new ClearanceStepGap($"{i.GroupItem} (ongoing)", elapsedSoFar, i.TargetDays, elapsedSoFar - i.TargetDays, category));
            }
            else
            {
                stepGaps.Add(new ClearanceStepGap(i.GroupItem, null, i.TargetDays, null, category));
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

        var firstItem = shipment.LineItems.FirstOrDefault();
        var totalQty = shipment.LineItems.Sum(li => li.QtyInBl);

        // Free days for the top summary — whichever charge type actually
        // has a configured tariff for this shipment.
        int? summaryFreeDays = demurrage.DemurrageFreeDays20 ?? demurrage.DemurrageFreeDays40 ?? (demurrage.StorageFreeDays > 0 ? demurrage.StorageFreeDays : null);

        var demurrageBreakdown = demurrage.DemurrageBreakdown20.Count > 0 ? demurrage.DemurrageBreakdown20 : demurrage.DemurrageBreakdown40;
        double? demFreeDays = demurrage.DemurrageFreeDays20 ?? demurrage.DemurrageFreeDays40;
        double? demChargeableDays = demurrage.DemurrageChargeableDays20 ?? demurrage.DemurrageChargeableDays40;

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
            Warnings: demurrage.Warnings);
    }
}
