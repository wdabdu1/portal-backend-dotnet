using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ClearanceEntity = ShippingPortal.Api.Models.Clearance.Clearance;
namespace ShippingPortal.Api.Controllers.Clearance;

public record ClearanceShipmentSummary(
    int ShipmentId, string BlAwbNo, string BusinessUnit, string Category, DateOnly? Eta,
    int FclCount, string? DeclarationNo, string Product, decimal Qty, string Unit, string TrafficLight, string RouteStatus,
    string ShippingLine, decimal SlaPercent, bool IsCompleted, bool EtaHasArrived, int? DemurrageFreeDaysRemaining);

public record ClearanceGeneralInfoRequest(
    DateOnly? CopyOfBlReceivedDate, DateOnly? OriginalShipmentSetReceivedDate, string? LcNo,
    string? DeclarationNo, string? Notes, DateOnly? ClearanceCompleteDate,
    string? ImFormNo, DateOnly? ImFormDate, DateOnly? ShipmentEta,
    DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo);

public record ClearanceRouteRequest(int Route); // 0=NotSelected,1=Route1,2=Route2,3=Route3

public record ClearanceDetailResponse(
    int ShipmentId, string BlAwbNo, string PoNumber, DateOnly? Eta, DateOnly? CopyOfBlReceivedDate,
    DateOnly? OriginalShipmentSetReceivedDate, string? LcNo, string? DeclarationNo, string? Notes,
    int Route, DateOnly? ClearanceCompleteDate, string? ImFormNo, DateOnly? ImFormDate,
    string Consignee, string Category, int FclCount,
    DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo,
    string? LastOffshoreInvoiceNo, string? LastOffshoreCompanyName);

public record ClearanceScheduleResponse(DateOnly? EstimatedCompletionDate, List<ShippingPortal.Api.Services.ScheduleItem> Items);

[ApiController]
[Authorize]
[Route("api/clearance")]
public class ClearanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.SectionLockService _sectionLock;
    public ClearanceController(ShippingPortalDbContext db, ShippingPortal.Api.Services.SectionLockService sectionLock)
    {
        _db = db;
        _sectionLock = sectionLock;
    }

[HttpGet("pre-clearance-readiness")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<ShippingPortal.Api.Services.ShipmentReadiness>>> GetPreClearanceReadiness(
        [FromServices] ShippingPortal.Api.Services.PreClearanceReadinessService readinessService,
        [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        var query = _db.Shipments.Where(s => s.Status == ShipmentStatus.Confirmed).AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipmentIds = await query.Select(s => s.Id).ToListAsync();

        // This report exists to prompt action on shipments still
        // exposed to demurrage/storage risk — once a route has genuinely
        // completed (cleared at port, or deposited into FZ), it's done
        // and no longer belongs here. That completion check only needs
        // a few cheap dictionary lookups, so it now runs BEFORE the full
        // per-shipment SLA/schedule calculation below rather than after
        // — otherwise this dashboard keeps recalculating every shipment
        // ever confirmed, including ones fully delivered months or years
        // ago, and only throws that work away afterward. As the
        // database grows, that makes the page steadily slower for no
        // benefit, since a delivered shipment's readiness was never
        // going to be shown anyway.
        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var route1Completions = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route2Completions = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route3Completions = await _db.ClearanceRoute3Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);

        var activeShipmentIds = shipmentIds.Where(id =>
        {
            if (!clearances.TryGetValue(id, out var clearance)) return true;
            DateOnly? completion = clearance.Route switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => route1Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => route2Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz => route3Completions.GetValueOrDefault(clearance.Id),
                _ => null
            };
            return !completion.HasValue;
        }).ToList();

        // Only shipments still genuinely in play (pending / unclosed /
        // uncleared) go through the expensive SLA calculation.
        var stillActive = await readinessService.CalculateAsync(activeShipmentIds);

        // Distill each shipment down to the ONE step that's currently
        // active — the whole point of this report is a quick scan, not
        // a full history. Walks Document Chain -> Vessel Arrival -> DO
        // Received first; once that's done, the exact same DoReceivedDate
        // field already shows Clearance's own "Delivery Order" step as
        // complete too, so the walk continues seamlessly into the real
        // clearance schedule (Cost Estimate -> Certificate Entry ->
        // route-specific steps) with no special-casing needed. MOT/SSMO
        // run in parallel and don't occupy a position in this sequence,
        // but are checked separately below since an overdue one can
        // silently delay everything that follows.
        var scheduleService = HttpContext.RequestServices.GetRequiredService<ShippingPortal.Api.Services.ClearanceScheduleService>();
        var demurrageService = HttpContext.RequestServices.GetRequiredService<ShippingPortal.Api.Services.DemurrageStorageService>();
        var highlights = new List<ShippingPortal.Api.Services.ShipmentHighlight>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Fixed, original total SLA allowance per route — never
        // re-projected — for the cumulative-lateness check below.
        var slaRowsForLateness = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();
        var slaByDivisionForLateness = slaRowsForLateness.GroupBy(s => s.Division).ToDictionary(g => g.Key, g => g.Sum(s => s.TargetDays));
        var generalDaysForLateness = slaByDivisionForLateness.GetValueOrDefault(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, 0);
        var holidaySetForLateness = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var deliveryOrdersForLateness = await _db.ClearanceDeliveryOrders.Where(d => clearanceIds.Contains(d.ClearanceId)).ToDictionaryAsync(d => d.ClearanceId);

        var sobDates = await _db.Shipments.Where(s => activeShipmentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SobActualDate);
        var marineInsurance = await _db.ShipmentForwarders.Where(f => activeShipmentIds.Contains(f.ShipmentId)).ToDictionaryAsync(f => f.ShipmentId, f => f.MarineInsurance);

        foreach (var r in stillActive)
        {
            ShippingPortal.Api.Services.ReadinessItem? current = null;
            string? currentTrackLabel = null;

            foreach (var track in r.Tracks)
            {
                if (track.Track == "MOT Approval" || track.Track == "SSMO Approval") continue;
                var incomplete = track.Items.FirstOrDefault(i => !i.ActualDate.HasValue);
                if (incomplete is not null) { current = incomplete; currentTrackLabel = track.Track; break; }
            }

            string currentStepName;
            DateOnly? currentStepTarget;
            string currentStepStatus;
            string currentStepLight;

            if (current is not null)
            {
                currentStepName = current.GroupItem;
                currentStepTarget = current.ShouldBeDoneBy;
                currentStepStatus = current.Status;
                currentStepLight = current.Light;
            }
            else
            {
                var schedule = await scheduleService.GetScheduleAsync(r.ShipmentId);

                // Same false-buffer issue as Demurrage Analysis: the
                // schedule engine always includes Customs Lab as a fixed
                // step regardless of whether it's actually required —
                // treating it as the current bottleneck when it's
                // genuinely just skipped manufactures a "Green, on
                // track" status that isn't real.
                bool customsLabRequiredForHealth = true;
                if (clearances.TryGetValue(r.ShipmentId, out var clearanceForHealth))
                {
                    customsLabRequiredForHealth = clearanceForHealth.Route switch
                    {
                        ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort =>
                            (await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceForHealth.Id))?.CustomsLabRequired ?? false,
                        ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz =>
                            (await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceForHealth.Id))?.CustomsLabRequired ?? false,
                        _ => true
                    };
                }

                var incompleteItem = schedule.Items.FirstOrDefault(i =>
                    !i.ActualDate.HasValue && !(i.GroupItem == "Customs Lab" && !customsLabRequiredForHealth));
                if (incompleteItem is not null)
                {
                    currentStepName = incompleteItem.GroupItem;
                    currentStepTarget = incompleteItem.TargetDate;
                    currentStepStatus = incompleteItem.Status;
                    currentStepLight = incompleteItem.Light;
                }
                else
                {
                    currentStepName = "All steps complete";
                    currentStepTarget = null;
                    currentStepStatus = "Awaiting Truck & Containers";
                    currentStepLight = "Green";
                }
            }

            string? motSsmoAlertLevel = null;
            string? motSsmoAlertMessage = null;
            var motTrack = r.Tracks.FirstOrDefault(t => t.Track == "MOT Approval")?.Items.FirstOrDefault();
            var ssmoTrack = r.Tracks.FirstOrDefault(t => t.Track == "SSMO Approval")?.Items.FirstOrDefault();
            foreach (var (label, item) in new[] { ("MOT", motTrack), ("SSMO", ssmoTrack) })
            {
                // NotApplicable covers SSMO/COC not being required, or
                // required but already available — nothing left to do,
                // so it must never raise an alert even though no
                // approval date was ever going to be entered for it.
                if (item is null || item.ActualDate.HasValue || item.NotApplicable) continue;
                var daysToDeadline = item.ShouldBeDoneBy.DayNumber - today.DayNumber;
                var level = daysToDeadline <= 3 ? "Red" : "Yellow";
                if (motSsmoAlertLevel != "Red") motSsmoAlertLevel = level;
                motSsmoAlertMessage = motSsmoAlertMessage is null ? $"{label} not yet done" : $"{motSsmoAlertMessage}, {label} not yet done";
            }

            // --- Cumulative lateness: total elapsed vs. the original, fixed total allowance ---
            bool isCumulativelyLate = false;
            int? daysOverAllowance = null;
            decimal currentHitSdg = 0;
            decimal projectedHitSdg = 0;
            DateOnly? zeroChargeDeadline = null;

            if (clearances.TryGetValue(r.ShipmentId, out var clearanceForLateness))
            {
                var routeDivisionForLateness = clearanceForLateness.Route switch
                {
                    ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1,
                    ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2,
                    ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3,
                    _ => (string?)null
                };

                if (routeDivisionForLateness is not null)
                {
                    var routeDaysForLateness = slaByDivisionForLateness.GetValueOrDefault(routeDivisionForLateness, 0);
                    var totalAllowedDays = routeDivisionForLateness == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3
                        ? routeDaysForLateness : generalDaysForLateness + routeDaysForLateness;

                    var arrivalForLateness = deliveryOrdersForLateness.GetValueOrDefault(clearanceForLateness.Id)?.ActualArrivalDate;
                    var anchorForLateness = arrivalForLateness ?? r.Eta;

                    if (anchorForLateness.HasValue)
                    {
                        var elapsedDays = ShippingPortal.Api.Services.ClearanceScheduleService.BusinessDaysBetween(anchorForLateness.Value, today, holidaySetForLateness);
                        var over = elapsedDays - (int)Math.Ceiling(totalAllowedDays);
                        if (over > 0) { isCumulativelyLate = true; daysOverAllowance = over; }
                    }
                }

                if (clearanceForLateness.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort
                    || clearanceForLateness.Route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit)
                {
                    var currentResult = await demurrageService.CalculateAsync(r.ShipmentId, asOfToday: true);
                    currentHitSdg = currentResult.TotalStorageDemurrageSdg;

                    var projectedResult = await demurrageService.CalculateAsync(r.ShipmentId, asOfToday: false);
                    projectedHitSdg = projectedResult.TotalStorageDemurrageSdg;

                    if (currentResult.AnchorDate.HasValue)
                    {
                        var freeDaysOptions = new List<int> { currentResult.StorageFreeDays };
                        if (currentResult.DemurrageFreeDays20.HasValue) freeDaysOptions.Add(currentResult.DemurrageFreeDays20.Value);
                        if (currentResult.DemurrageFreeDays40.HasValue) freeDaysOptions.Add(currentResult.DemurrageFreeDays40.Value);
                        if (freeDaysOptions.Count > 0) zeroChargeDeadline = currentResult.AnchorDate.Value.AddDays(freeDaysOptions.Min());
                    }
                }
            }

            // --- Non-insured cargo risk ---
            string? insuranceAlertLevel = null;
            int? daysUninsuredPastReference = null;
            var isInsured = marineInsurance.GetValueOrDefault(r.ShipmentId, false);
            if (!isInsured)
            {
                var referenceDate = sobDates.GetValueOrDefault(r.ShipmentId) ?? r.Eta;
                if (referenceDate.HasValue && today >= referenceDate.Value)
                {
                    var daysPast = today.DayNumber - referenceDate.Value.DayNumber;
                    insuranceAlertLevel = daysPast > 3 ? "Red" : "Yellow";
                    daysUninsuredPastReference = daysPast;
                }
            }

            highlights.Add(new ShippingPortal.Api.Services.ShipmentHighlight(
                r.ShipmentId, r.BlAwbNo, r.BusinessUnit, r.Category, r.Eta, r.Fcl20Count, r.Fcl40Count,
                currentStepName, currentStepTarget, currentStepStatus, currentStepLight,
                motSsmoAlertLevel, motSsmoAlertMessage,
                isCumulativelyLate, daysOverAllowance, currentHitSdg, projectedHitSdg, zeroChargeDeadline,
                insuranceAlertLevel, daysUninsuredPastReference));
        }

        return Ok(highlights);
    }

    // Selection screen: only Confirmed shipments (nothing to clear on a Draft),
    // sorted by ETA ascending — soonest-arriving first, per the requirement.
    [HttpGet("shipments")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<ClearanceShipmentSummary>>> GetShipmentsForClearance([FromQuery] string? search, [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        // Per-step target days — used for the weighted progress calc
        // below (days of completed steps ÷ days of all applicable
        // steps), not just a division-level total.
        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();
        var slaByDivision = slaRows.GroupBy(s => s.Division).ToDictionary(g => g.Key, g => g.Sum(s => s.TargetDays));
        var generalDays = slaByDivision.GetValueOrDefault(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, 0);

        var query = _db.Shipments
            .Where(s => s.Status == ShipmentStatus.Confirmed && !s.IsDirectSales)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.ShippingLine)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        // Search (BL/AWB or Declaration No.) is applied later, in
        // memory, once Declaration No. has actually been resolved from
        // Certificate Entry — a search-time SQL filter here could only
        // ever check BlAwbNo (Declaration isn't a column on Shipment),
        // which would silently exclude every declaration-only match
        // before the real check even runs.
        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        var clearances = await _db.Clearances
            .Where(c => shipmentIds.Contains(c.ShipmentId))
            .ToDictionaryAsync(c => c.ShipmentId);

        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var certificateEntries = await _db.ClearanceCertificateEntries
            .Where(e => clearanceIds.Contains(e.ClearanceId))
            .ToDictionaryAsync(e => e.ClearanceId);

        // "Done" for the list means the shipment's OWN route has reached
        // Truck & Containers completion — not the rarely-used generic
        // Clearance.ClearanceCompleteDate field.
        var route1Completions = await _db.ClearanceRoute1Details
            .Where(r => clearanceIds.Contains(r.ClearanceId))
            .ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route2Completions = await _db.ClearanceRoute2Details
            .Where(r => clearanceIds.Contains(r.ClearanceId))
            .ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);
        var route3Completions = await _db.ClearanceRoute3Details
            .Where(r => clearanceIds.Contains(r.ClearanceId))
            .ToDictionaryAsync(r => r.ClearanceId, r => r.ClearanceActualCompletedDate);

        var deliveryOrders = await _db.ClearanceDeliveryOrders
            .Where(d => clearanceIds.Contains(d.ClearanceId))
            .ToDictionaryAsync(d => d.ClearanceId, d => d.ActualArrivalDate);

        // Shipping Line demurrage free-days — batched once for every
        // (Line, TariffGroup) combo this page actually needs, rather
        // than a per-row lookup.
        var demurrageTariffs = await _db.ShippingLineDemurrageTariffs.ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<ClearanceShipmentSummary>();

        foreach (var s in shipments)
        {
            clearances.TryGetValue(s.Id, out var clearance);
            string? declarationNo = null;
            if (clearance is not null && certificateEntries.TryGetValue(clearance.Id, out var certEntry))
            {
                declarationNo = certEntry.ScudaDeclarationNo;
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var blMatches = s.BlAwbNo.Contains(search, StringComparison.OrdinalIgnoreCase);
                var declarationMatches = !string.IsNullOrEmpty(declarationNo) && declarationNo.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!blMatches && !declarationMatches) continue;
            }

            var firstLine = s.LineItems.FirstOrDefault()?.PurchaseOrderLineItem;
            var totalQty = s.LineItems.Sum(li => li.QtyInBl);

            // No fallback to Route 1 anymore — a shipment with no route
            // chosen yet is measured only against the General steps,
            // which is the only work that's genuinely applicable so far.
            string? routeDivision = clearance?.Route switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1,
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2,
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3,
                _ => null
            };
            var routeDays = routeDivision is null ? 0 : slaByDivision.GetValueOrDefault(routeDivision, 0);
            var targetDays = routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3
                ? routeDays
                : generalDays + routeDays;

            DateOnly? actualCompletedDate = (clearance is null || routeDivision is null) ? null : routeDivision switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1 => route1Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2 => route2Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3 => route3Completions.GetValueOrDefault(clearance.Id),
                _ => null
            };

            // --- Weighted progress: days of completed steps ÷ days of every applicable step ---
            decimal slaPercent = 0;
            if (clearance is not null)
            {
                var actualDatesKey = routeDivision ?? ShippingPortal.Api.Models.Clearance.ClearanceDivision.General;
                var actualDates = await BuildActualDatesAsync(clearance.Id, actualDatesKey);
                var applicableRows = slaRows.Where(r =>
                    routeDivision is null ? r.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.General :
                    routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3 ? r.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3 :
                    r.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.General || r.Division == routeDivision
                ).ToList();

                var totalStepDays = applicableRows.Sum(r => r.TargetDays);
                var completedStepDays = applicableRows
                    .Where(r => actualDates.TryGetValue((r.Division, r.GroupItem), out var d) && d.HasValue)
                    .Sum(r => r.TargetDays);
                slaPercent = totalStepDays == 0 ? 0 : Math.Min(100m, (completedStepDays / totalStepDays) * 100m);
            }

            var trafficLight = ComputeTrafficLight(s.Eta, actualCompletedDate, targetDays);
            var routeStatus = clearance is null || clearance.Route == 0 ? "Not Started" : clearance.Route.ToString();
            var etaHasArrived = s.Eta.HasValue && s.Eta.Value < today;

            // --- Live Shipping Line demurrage free-days remaining ---
            int? demurrageFreeDaysRemaining = null;
            var tariffGroupId = firstLine?.ProductCategory?.TariffGroupId;
            if (tariffGroupId.HasValue && s.ShippingLineId != 0)
            {
                var containerSize = s.Fcl40Count > 0 ? "40" : "20";
                var tariff = demurrageTariffs.FirstOrDefault(t =>
                    t.ShippingLineId == s.ShippingLineId && t.TariffGroupId == tariffGroupId && t.ContainerSize == containerSize);
                if (tariff is not null)
                {
                    var deliveryOrder = clearance is not null ? deliveryOrders.GetValueOrDefault(clearance.Id) : null;
                    var anchor = deliveryOrder ?? s.Eta;
                    if (anchor.HasValue)
                    {
                        var daysSinceAnchor = Math.Max(0, today.DayNumber - anchor.Value.DayNumber);
                        demurrageFreeDaysRemaining = tariff.FreeDays - daysSinceAnchor;
                    }
                }
            }

            results.Add(new ClearanceShipmentSummary(
                s.Id, s.BlAwbNo, s.PurchaseOrder?.BusinessUnit?.Name ?? "", firstLine?.ProductCategory?.Name ?? "",
                s.Eta, s.Fcl20Count + s.Fcl40Count, declarationNo, firstLine?.ModelProduct?.Name ?? "", totalQty, firstLine?.UnitOfMeasure?.Code ?? "",
                trafficLight, routeStatus, s.ShippingLine?.Name ?? "", slaPercent, actualCompletedDate.HasValue,
                etaHasArrived, demurrageFreeDaysRemaining));
        }

        var ordered = results.OrderBy(x => x.Eta ?? DateOnly.MaxValue).ToList();
        return Ok(ordered);
    }

    [HttpGet("{shipmentId:int}/detail")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<ClearanceDetailResponse>> GetDetail(int shipmentId)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);

        var firstCategory = shipment.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "";
        var fclCount = shipment.Fcl20Count + shipment.Fcl40Count;

        var lastOffshore = await _db.PurchaseOrderOffshorePartners
            .Where(p => p.PurchaseOrderId == shipment.PurchaseOrderId)
            .Include(p => p.BusinessPartner)
            .OrderByDescending(p => p.SequenceOrder)
            .FirstOrDefaultAsync();
        var lastOffshoreDetail = await _db.LastOffshoreDetails.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);

        return new ClearanceDetailResponse(
            shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Eta,
            clearance?.CopyOfBlReceivedDate, clearance?.OriginalShipmentSetReceivedDate, clearance?.LcNo,
            clearance?.DeclarationNo, clearance?.Notes, (int)(clearance?.Route ?? 0), clearance?.ClearanceCompleteDate,
            clearance?.ImFormNo, clearance?.ImFormDate,
            shipment.PurchaseOrder.Consignee?.Name ?? "", firstCategory, fclCount,
            clearance?.WithdrawalRequestDate, clearance?.WithdrawalRequestRefNo,
            lastOffshoreDetail?.InvoiceNo, lastOffshore?.BusinessPartner?.Name);
    }

    // Sequential cascading schedule: each step's target date is calculated
    // from the PREVIOUS step's actual completion date if known, otherwise
    // its own target date — so a delay in one step pushes every step after
    // it. Traffic light per step reflects real performance: completed
    // early/on-time/late, or pending on-track/delayed.
    [HttpGet("{shipmentId:int}/sla-schedule")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<ClearanceScheduleResponse>> GetSlaSchedule(int shipmentId, [FromServices] ShippingPortal.Api.Services.ClearanceScheduleService scheduleService)
    {
        var result = await scheduleService.GetScheduleAsync(shipmentId);
        return Ok(new ClearanceScheduleResponse(result.EstimatedCompletionDate, result.Items));
    }

    [HttpGet("{shipmentId:int}/demurrage-storage")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<ShippingPortal.Api.Services.DemurrageStorageResult>> GetDemurrageStorage(
        int shipmentId, [FromServices] ShippingPortal.Api.Services.DemurrageStorageService service)
    {
        var result = await service.CalculateAsync(shipmentId);
        return Ok(result);
    }
    [HttpGet("{shipmentId:int}/print-estimate")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<IActionResult> PrintEstimate(
        int shipmentId, [FromServices] ShippingPortal.Api.Services.ClearanceEstimatePdfService pdfService)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.UnitOfMeasure)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var estimateLines = clearance is null
            ? new List<ShippingPortal.Api.Models.Clearance.ClearanceEstimateLineItem>()
            : await _db.ClearanceEstimateLineItems.Where(e => e.ClearanceId == clearance.Id).Include(e => e.ChargeType).ToListAsync();

        var category = shipment.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "";

        var data = new ShippingPortal.Api.Services.ClearanceEstimatePrintData(
            shipment.PurchaseOrder?.BusinessUnit?.Name ?? "",
            shipment.BlAwbNo,
            shipment.PurchaseOrder?.Consignee?.Name ?? "",
            category,
            shipment.LineItems.Select(li => new ShippingPortal.Api.Services.EstimateItemLine(
                li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "", li.QtyInBl, li.PurchaseOrderLineItem?.UnitOfMeasure?.Code)).ToList(),
            estimateLines.Select(e => new ShippingPortal.Api.Services.EstimateChargeLine(
                e.ChargeType?.Name ?? "", e.ValueSdg)).ToList());

        var pdfBytes = pdfService.Generate(data);
        return File(pdfBytes, "application/pdf", $"Clearance Estimate - {shipment.BlAwbNo}.pdf");
    }

    private async Task<Dictionary<(string, string), DateOnly?>> BuildActualDatesAsync(int clearanceId, string routeDivision)
    {
        var result = new Dictionary<(string, string), DateOnly?>();

        if (routeDivision != ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3)
        {
            var deliveryOrder = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, "Delivery Order")] = deliveryOrder?.DoReceivedDate;

            var costEstimate = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, "Clearance Cost Estimate")] = costEstimate?.AmountSettledDate;

            var certEntry = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ShippingPortal.Api.Models.Clearance.ClearanceDivision.General, "Customs Certificate Entry")] = certEntry?.CertificateEntryDate;
        }

        if (routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1)
        {
            var r1 = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(routeDivision, "Containers Move Process")] = r1?.BillSettlementDate;
            result[(routeDivision, "SSMO File Process")] = r1?.SsmoFeesSettlementDate;
            result[(routeDivision, "Customs Examination (Form 48)")] = r1?.CustExamCompletedDate;
            result[(routeDivision, "Customs Lab")] = r1?.LabResultIssuanceDate;
            result[(routeDivision, "SSMO Examination")] = r1?.SsmoCertIssuanceDate;
            result[(routeDivision, "Customs Evaluation")] = r1?.ReleaseExitPassDate;
            result[(routeDivision, "SPC Bill")] = r1?.SpcBillSettlementDate;
            result[(routeDivision, "Truck & Containers")] = r1?.ClearanceActualCompletedDate;
        }
        else if (routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2)
        {
            var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(routeDivision, "FZ Deposit Request")] = r2?.RequestApprovalDate;
            result[(routeDivision, "Customs Inspection")] = r2?.InspectionDate;
            result[(routeDivision, "SPC Bill")] = r2?.SpcBillSettlementDate;
            result[(routeDivision, "Truck & Containers")] = r2?.ClearanceActualCompletedDate;
        }
        else if (routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3)
        {
            var r3 = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(routeDivision, "Customs Certificate Entry")] = r3?.CertificateEntryDate;
            result[(routeDivision, "SSMO File Process")] = r3?.SsmoFeesSettlementDate;
            result[(routeDivision, "Customs Examination (Form 48)")] = r3?.CustExamCompletedDate;
            result[(routeDivision, "Customs Lab")] = r3?.LabResultIssuanceDate;
            result[(routeDivision, "SSMO Examination")] = r3?.SsmoCertIssuanceDate;
            result[(routeDivision, "Customs Evaluation")] = r3?.ReleaseExitPassDate;
            result[(routeDivision, "Truck & Containers")] = r3?.ClearanceActualCompletedDate;
        }

        return result;
    }

    private static DateOnly AddBusinessDays(DateOnly start, int days, HashSet<DateOnly> holidays)
    {
        var date = start;
        var added = 0;
        while (added < days)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday) continue;
            if (holidays.Contains(date)) continue;
            added++;
        }
        return date;
    }

    // Counts business days between two dates (positive = 'to' is later),
    // skipping Fri/Sat and CLR holidays — used so the "early/late" gap shown
    // to users matches the same calendar used to compute the target itself.
    private static int BusinessDaysBetween(DateOnly from, DateOnly to, HashSet<DateOnly> holidays)
    {
        if (from == to) return 0;
        var forward = to > from;
        var start = forward ? from : to;
        var end = forward ? to : from;
        var count = 0;
        var date = start;
        while (date < end)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday) continue;
            if (holidays.Contains(date)) continue;
            count++;
        }
        return forward ? count : -count;
    }
    [HttpPost("{shipmentId:int}/complete")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> CompleteClearance(int shipmentId)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) return NotFound(new { message = "Clearance record not found." });

        // Deliberately not reusing General Info's own lock/save endpoint —
        // that section is normally already locked by this point in the
        // flow. Also deliberately one-way: once set, this can't be
        // re-triggered, since re-pressing would silently overwrite the
        // real completion date with "today" and corrupt SLA history.
        if (clearance.ClearanceCompleteDate.HasValue)
        {
            return Conflict(new { message = "This clearance is already marked complete." });
        }

        clearance.ClearanceCompleteDate = DateOnly.FromDateTime(DateTime.UtcNow);
        clearance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{shipmentId:int}/general-info")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> UpsertGeneralInfo(int shipmentId, ClearanceGeneralInfoRequest req)
    {
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Clearance", shipmentId, "generalInfo");
        if (lockDenied is not null) return lockDenied;
        // Direct Sales shipments never get a Clearance record — treat
        // them the same as a nonexistent shipment here.
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId && !s.IsDirectSales)) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) { clearance = new ClearanceEntity { ShipmentId = shipmentId }; _db.Clearances.Add(clearance); }

        clearance.CopyOfBlReceivedDate = req.CopyOfBlReceivedDate;
        clearance.OriginalShipmentSetReceivedDate = req.OriginalShipmentSetReceivedDate;
        clearance.LcNo = req.LcNo;
        clearance.DeclarationNo = req.DeclarationNo;
        clearance.Notes = req.Notes;
        clearance.ClearanceCompleteDate = req.ClearanceCompleteDate;
        clearance.ImFormNo = req.ImFormNo;
        clearance.ImFormDate = req.ImFormDate;
        clearance.WithdrawalRequestDate = req.WithdrawalRequestDate;
        clearance.WithdrawalRequestRefNo = req.WithdrawalRequestRefNo;
        clearance.UpdatedAt = DateTime.UtcNow;

        // Same cell as the Shipment's own ETA — editable from either place.
        if (req.ShipmentEta.HasValue)
        {
            var shipment = await _db.Shipments.FindAsync(shipmentId);
            if (shipment is not null) shipment.Eta = req.ShipmentEta;
        }

        await _db.SaveChangesAsync();
        return Ok(clearance);
    }

    [HttpPut("{shipmentId:int}/route")]
    [Authorize(Roles = AppRoles.ClearanceEditors)]
    public async Task<IActionResult> SetRoute(int shipmentId, ClearanceRouteRequest req)
    {
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Clearance", shipmentId, "route");
        if (lockDenied is not null) return lockDenied;
        // Direct Sales shipments never get a Clearance record — treat
        // them the same as a nonexistent shipment here.
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId && !s.IsDirectSales)) return NotFound();

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        if (clearance is null) { clearance = new ClearanceEntity { ShipmentId = shipmentId }; _db.Clearances.Add(clearance); }

        clearance.Route = (ShippingPortal.Api.Models.Clearance.ClearanceRouteType)req.Route;
        clearance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(clearance);
    }

    // Green: on track. : past 70% of the target window. Red: past the
    // target entirely. Grey: no ETA to measure against. Once clearance is
    // marked complete, always Green regardless of how long it took —
    // "needs attention" no longer applies to a finished shipment.
    private static string ComputeTrafficLight(DateOnly? eta, DateOnly? clearanceCompleteDate, decimal targetDays)
    {
        if (clearanceCompleteDate.HasValue) return "Green";
        if (!eta.HasValue) return "Grey";

        var daysSinceEta = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - eta.Value.DayNumber;
        if (daysSinceEta > targetDays) return "Red";
        if (daysSinceEta > targetDays * 0.7m) return "Amber";
        return "Green";
    }

    // Not capped at 100 — a figure over 100% correctly signals overdue,
    // consistent with the Red traffic light. Bar width is capped
    // separately on the frontend so it doesn't visually overflow.
    private static decimal ComputeSlaPercent(DateOnly? eta, DateOnly? clearanceCompleteDate, decimal targetDays)
    {
        if (clearanceCompleteDate.HasValue) return 100m;
        if (!eta.HasValue || targetDays <= 0) return 0m;
        var daysSinceEta = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - eta.Value.DayNumber;
        return Math.Max(0m, (daysSinceEta / targetDays) * 100m);
    }
}
