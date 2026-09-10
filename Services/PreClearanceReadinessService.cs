using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

public record ReadinessItem(string GroupItem, DateOnly ShouldBeDoneBy, DateOnly? ActualDate, string Status, string Light, bool NotApplicable = false);
public record TrackResult(string Track, List<ReadinessItem> Items);
public record ShipmentReadiness(
    int ShipmentId, string BlAwbNo, string BusinessUnit, string Category, int Fcl20Count, int Fcl40Count,
    DateOnly? Etd, DateOnly? Eta, string Classification, List<TrackResult> Tracks);

// One-line "where is this shipment right now" summary — the same
// current-step distillation Pipeline Health uses (Document Chain ->
// Vessel Arrival -> DO Received, then seamlessly into Clearance's own
// schedule), reused wherever a compact SLA Status is wanted (the
// Shipment/Additional lists) without pulling in Pipeline Health's other
// alerting (MOT/SSMO, demurrage cost, insurance risk) that a plain
// status column has no room for.
public record ShipmentCurrentStep(string StepName, string Status, string Light, DateOnly? TargetDate);

// Pipeline Health's actual display shape — one current step per
// shipment (Document Chain -> Vessel Arrival -> DO Received, then
// seamlessly into Clearance's own schedule) rather than the full
// multi-track history above, plus a separate background alert for an
// overdue MOT/SSMO even though they don't occupy a position in the
// main sequence.
public record ShipmentHighlight(
    int ShipmentId, string BlAwbNo, string BusinessUnit, string Category, DateOnly? Eta,
    int Fcl20Count, int Fcl40Count,
    string CurrentStepName, DateOnly? CurrentStepTargetDate, string CurrentStepStatus, string CurrentStepLight,
    string? MotSsmoAlertLevel, string? MotSsmoAlertMessage,
    // Cumulative lateness: total business days actually elapsed since
    // anchor vs. the shipment's original, fixed total SLA allowance —
    // independent of the current step's own local status, which resets
    // with every step and can look "on track" even while the shipment
    // overall has drifted badly behind. Live cost is the real,
    // already-accrued demurrage+storage exposure as of today, not a
    // future projection — the magnitude that actually matters for
    // prioritizing action across shipments of very different sizes.
    bool IsCumulativelyLate, int? DaysOverAllowance, decimal CurrentDemurrageStorageHitSdg,
    // At current pace, if nothing changes — genuinely different from
    // CurrentDemurrageStorageHitSdg, which only counts what's already
    // accrued as of today. A shipment can still be within its free
    // days right now while heading toward a real hit by the time it
    // actually finishes; this is what catches that in advance.
    decimal ProjectedDemurrageStorageHitSdg,
    // The real, concrete deadline — anchor + the smallest free-days
    // window across whichever charge types apply. A supervisor can act
    // on a date far more directly than a derived dollar figure.
    DateOnly? ZeroChargeDeadline,
    // Non-insured cargo risk. Reference date is SOB if it's been
    // confirmed (a real event), else ETA. 0-3 days past that date is
    // Yellow (just crossed, still recoverable); beyond that is Red.
    // Not shown at all if insured, or before the reference date.
    string? InsuranceAlertLevel, int? DaysUninsuredPastReference);

// Shipment Pipeline Health — the full pre-clearance journey, entirely
// separate from the forward clearance cascade itself. The Document
// Chain is measured from BOTH ends: backward from ETA (catches things
// as the deadline nears) and forward from ETD (catches a stalled step
// immediately, even on a long-transit shipment where ETA is still far
// off) — whichever gives the earlier "should be done by" date wins,
// since that's the one that will bite first. DO Received is its own
// track, gated by vessel arrival rather than chained to the documents.
public class PreClearanceReadinessService
{
    private readonly ShippingPortalDbContext _db;
    private readonly ClearanceScheduleService _scheduleService;
    public PreClearanceReadinessService(ShippingPortalDbContext db, ClearanceScheduleService scheduleService)
    {
        _db = db;
        _scheduleService = scheduleService;
    }

    // Batched readiness (Document Chain / MOT / SSMO / Vessel / DO) plus,
    // only for shipments already past all of that, a per-shipment call
    // into the real Clearance schedule (Cost Estimate -> Certificate
    // Entry -> route steps) — the same "cheap for early-stage shipments,
    // one extra query only once they're further along" shape the
    // Pipeline Health endpoint already uses. Confirmed shipments only —
    // a Draft shipment has no clearance workflow to report on yet.
    public async Task<Dictionary<int, ShipmentCurrentStep>> GetCurrentStepsAsync(List<int> confirmedShipmentIds)
    {
        var result = new Dictionary<int, ShipmentCurrentStep>();
        if (confirmedShipmentIds.Count == 0) return result;

        var readiness = await CalculateAsync(confirmedShipmentIds);

        foreach (var r in readiness)
        {
            ReadinessItem? current = null;
            foreach (var track in r.Tracks)
            {
                if (track.Track == "MOT Approval" || track.Track == "SSMO Approval") continue;
                var incomplete = track.Items.FirstOrDefault(i => !i.ActualDate.HasValue);
                if (incomplete is not null) { current = incomplete; break; }
            }

            if (current is not null)
            {
                result[r.ShipmentId] = new ShipmentCurrentStep(current.GroupItem, current.Status, current.Light, current.ShouldBeDoneBy);
                continue;
            }

            // Document Chain / Vessel Arrival / DO Received are all done
            // (or there was no ETA yet to measure them against) — carry on
            // into the real clearance schedule for this one shipment.
            var schedule = await _scheduleService.GetScheduleAsync(r.ShipmentId);
            var incompleteItem = schedule.Items.FirstOrDefault(i => !i.ActualDate.HasValue);

            result[r.ShipmentId] = incompleteItem is not null
                ? new ShipmentCurrentStep(incompleteItem.GroupItem, incompleteItem.Status, incompleteItem.Light, incompleteItem.TargetDate)
                : (schedule.Items.Count > 0
                    ? new ShipmentCurrentStep("All steps complete", "Awaiting Truck & Containers", "Green", null)
                    : new ShipmentCurrentStep("Not yet due", "Awaiting ETA / route selection", "Green", null));
        }

        return result;
    }

    public async Task<List<ShipmentReadiness>> CalculateAsync(List<int> shipmentIds)
    {
        var result = new List<ShipmentReadiness>();
        if (shipmentIds.Count == 0) return result;

        var shipments = await _db.Shipments
            .Where(s => shipmentIds.Contains(s.Id))
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .ToListAsync();

        var draftDocs = await _db.ShipmentDraftDocuments.Where(d => shipmentIds.Contains(d.ShipmentId)).ToDictionaryAsync(d => d.ShipmentId);
        var fullSets = await _db.ShipmentSupplierFullSets.Where(f => shipmentIds.Contains(f.ShipmentId)).ToDictionaryAsync(f => f.ShipmentId);
        var mots = await _db.ShipmentMots.Where(m => shipmentIds.Contains(m.ShipmentId)).ToDictionaryAsync(m => m.ShipmentId);
        var ssmos = await _db.ShipmentSsmos.Where(s => shipmentIds.Contains(s.ShipmentId)).ToDictionaryAsync(s => s.ShipmentId);

        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var deliveryOrders = await _db.ClearanceDeliveryOrders.Where(d => clearanceIds.Contains(d.ClearanceId)).ToDictionaryAsync(d => d.ClearanceId);

        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();
        var docsRows = slaRows.Where(s => s.Division == ClearanceDivision.PreClearanceDocs).OrderBy(s => s.SequenceOrder).ToList();
        var motDays = slaRows.FirstOrDefault(s => s.Division == ClearanceDivision.PreClearanceMot)?.TargetDays ?? 0;
        var ssmoDays = slaRows.FirstOrDefault(s => s.Division == ClearanceDivision.PreClearanceSsmo)?.TargetDays ?? 0;
        var doDays = slaRows.FirstOrDefault(s => s.Division == ClearanceDivision.PreClearanceDo)?.TargetDays ?? 0;
        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var shipment in shipments)
        {
            var category = shipment.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "";
            var businessUnit = shipment.PurchaseOrder?.BusinessUnit?.Name ?? "";

            if (!shipment.Eta.HasValue)
            {
                result.Add(new ShipmentReadiness(shipment.Id, shipment.BlAwbNo, businessUnit, category,
                    shipment.Fcl20Count, shipment.Fcl40Count, shipment.Etd, null, "Green", new List<TrackResult>()));
                continue;
            }
            var eta = shipment.Eta.Value;
            var etd = shipment.Etd;

            draftDocs.TryGetValue(shipment.Id, out var dd);
            fullSets.TryGetValue(shipment.Id, out var fs);
            clearances.TryGetValue(shipment.Id, out var clearance);
            var deliveryOrder = clearance is not null ? deliveryOrders.GetValueOrDefault(clearance.Id) : null;

            DateOnly? ActualFor(string groupItem) => groupItem switch
            {
                "Final Draft Received" => dd?.FinalDraftReceivedDate,
                "Final Draft Confirmed" => dd?.FinalDraftConfirmedDate,
                "FS Received" => fs?.FsReceivedDate,
                "Original Shipment Set Received" => clearance?.OriginalShipmentSetReceivedDate,
                _ => null
            };

            // --- Backward from ETA (existing direction) ---
            var etaTargets = new List<DateOnly>();
            var cascadeBack = eta;
            for (var i = docsRows.Count - 1; i >= 0; i--)
            {
                var row = docsRows[i];
                var target = SubtractBusinessDays(cascadeBack, (int)Math.Ceiling(row.TargetDays), holidaySet);
                etaTargets.Insert(0, target);
                var actual = ActualFor(row.GroupItem);
                cascadeBack = actual ?? target;
            }

            // --- Forward from ETD (new — catches a stalled step immediately) ---
            var etdTargets = new List<DateOnly>();
            var cascadeForward = etd ?? eta;
            foreach (var row in docsRows)
            {
                var target = AddBusinessDays(cascadeForward, (int)Math.Ceiling(row.TargetDaysEtd), holidaySet);
                var actual = ActualFor(row.GroupItem);
                // Live push: if this step is still pending and already
                // overdue against its own forward target, the NEXT step's
                // clock starts from today, not the stale target — same
                // "unstuck items push everything after them" principle
                // as the main SLA cascade.
                var effectiveDate = actual ?? (today > target ? today : target);
                etdTargets.Add(target);
                cascadeForward = effectiveDate;
            }

            var docItems = new List<ReadinessItem>();
            for (var i = 0; i < docsRows.Count; i++)
            {
                var shouldBeDoneBy = etaTargets[i] < etdTargets[i] ? etaTargets[i] : etdTargets[i];
                var actual = ActualFor(docsRows[i].GroupItem);
                docItems.Add(BuildItem(docsRows[i].GroupItem, shouldBeDoneBy, actual, today, holidaySet));
            }

            // Last document-chain step's live-projected date — used below
            // to decide whether the whole chain is still on pace to beat
            // vessel arrival, or has already slipped past it.
            var lastDocTarget = docItems.Count > 0 ? docItems[^1].ShouldBeDoneBy : eta;
            var lastDocActual = docItems.Count > 0 ? docItems[^1].ActualDate : null;
            var lastDocProjected = lastDocActual ?? (today > lastDocTarget ? today : lastDocTarget);

            // --- MOT / SSMO — independent, backward from ETA ---
            mots.TryGetValue(shipment.Id, out var mot);
            ssmos.TryGetValue(shipment.Id, out var ssmo);
            var motShouldBe = SubtractBusinessDays(eta, (int)Math.Ceiling(motDays), holidaySet);
            var ssmoShouldBe = SubtractBusinessDays(eta, (int)Math.Ceiling(ssmoDays), holidaySet);
            var motItem = BuildItem("MOT Approval", motShouldBe, mot?.ApprovalDate, today, holidaySet);

            // SSMO/COC only ever needs tracking when COC is required AND
            // not already in hand — the same condition the Update
            // Shipment page uses to disable the whole application block
            // (see ssmoForm.cocRequired / cocAvailable there). When COC
            // isn't required, or is required but already available,
            // there is no action left to take, so this must never
            // surface as an overdue/pending alert even though no
            // approval date was ever going to be entered. Left
            // unanswered (CocRequired still null) still flags as
            // before, since that itself is an open item to resolve.
            var cocRequired = ssmo?.CocRequired;
            var cocAvailable = ssmo?.CocAvailable;
            var ssmoNotNeeded = cocRequired == false || (cocRequired == true && cocAvailable == true);
            var ssmoItem = ssmoNotNeeded
                ? new ReadinessItem("SSMO Approval", ssmoShouldBe, ssmo?.ApprovalDate,
                    cocRequired == false ? "COC not required" : "COC already available — not required",
                    "Green", NotApplicable: true)
                : BuildItem("SSMO Approval", ssmoShouldBe, ssmo?.ApprovalDate, today, holidaySet);

            // --- Vessel Arrival — actual vs ETA directly ---
            var vesselItem = new ReadinessItem("Vessel Arrival", eta, deliveryOrder?.ActualArrivalDate,
                deliveryOrder?.ActualArrivalDate.HasValue == true
                    ? (deliveryOrder.ActualArrivalDate.Value > eta ? "Arrived late" : "Arrived on time or early")
                    : (today > eta ? "Overdue — not yet arrived" : "Not yet due"),
                deliveryOrder?.ActualArrivalDate.HasValue == true
                    ? (deliveryOrder.ActualArrivalDate.Value > eta ? "Amber" : "Green")
                    : (today > eta ? "Red" : "Green"));

            // --- DO Received — its own track, forward from arrival ---
            var arrivalAnchor = deliveryOrder?.ActualArrivalDate ?? eta;
            var doShouldBe = AddBusinessDays(arrivalAnchor, (int)Math.Ceiling(doDays), holidaySet);
            var doItem = BuildItem("DO Received", doShouldBe, deliveryOrder?.DoReceivedDate, today, holidaySet);

            // --- Classification ---
            var hasArrived = deliveryOrder?.ActualArrivalDate.HasValue == true;
            var osSetDone = clearance?.OriginalShipmentSetReceivedDate.HasValue == true;
            var arrivalReference = deliveryOrder?.ActualArrivalDate ?? eta;

            var motApproved = mot?.ApprovalDate.HasValue ?? false;
            var motNotReadyAtArrival = hasArrived && !motApproved;
            var isRed = (hasArrived && !osSetDone)
                || lastDocProjected > arrivalReference
                || (doItem.ActualDate is null && doItem.Light == "Red")
                || motNotReadyAtArrival;

            var allItems = new List<ReadinessItem>(docItems) { motItem, ssmoItem, vesselItem, doItem };
            var hasOverdueOrLate = allItems.Any(i => i.Light != "Green");

            var classification = isRed ? "Red" : (hasOverdueOrLate ? "Yellow" : "Green");

            result.Add(new ShipmentReadiness(shipment.Id, shipment.BlAwbNo, businessUnit, category,
                shipment.Fcl20Count, shipment.Fcl40Count, etd, eta, classification, new List<TrackResult>
            {
                new TrackResult("Document Chain", docItems),
                new TrackResult("MOT Approval", new List<ReadinessItem> { motItem }),
                new TrackResult("SSMO Approval", new List<ReadinessItem> { ssmoItem }),
                new TrackResult("Vessel Arrival", new List<ReadinessItem> { vesselItem }),
                new TrackResult("DO Received", new List<ReadinessItem> { doItem })
            }));
        }

        return result;
    }

    private static ReadinessItem BuildItem(string name, DateOnly shouldBeDoneBy, DateOnly? actual, DateOnly today, HashSet<DateOnly> holidays)
    {
        if (actual.HasValue)
        {
            var diff = BusinessDaysBetween(shouldBeDoneBy, actual.Value, holidays);
            if (diff <= 0) return new ReadinessItem(name, shouldBeDoneBy, actual, diff == 0 ? "Done on time" : $"Done {-diff} day(s) early", "Green");
            return new ReadinessItem(name, shouldBeDoneBy, actual, $"Done {diff} day(s) late", "Amber");
        }
        var overdueDays = BusinessDaysBetween(shouldBeDoneBy, today, holidays);
        if (overdueDays <= 0) return new ReadinessItem(name, shouldBeDoneBy, null, "Not yet due", "Green");
        return new ReadinessItem(name, shouldBeDoneBy, null, $"Overdue by {overdueDays} day(s)", "Red");
    }

    private static DateOnly SubtractBusinessDays(DateOnly from, int days, HashSet<DateOnly> holidays)
    {
        var date = from;
        var remaining = days;
        while (remaining > 0)
        {
            date = date.AddDays(-1);
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday) continue;
            if (holidays.Contains(date)) continue;
            remaining--;
        }
        return date;
    }

    private static DateOnly AddBusinessDays(DateOnly from, int days, HashSet<DateOnly> holidays)
    {
        var date = from;
        var remaining = days;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday) continue;
            if (holidays.Contains(date)) continue;
            remaining--;
        }
        return date;
    }

    // Positive when `to` is later than `from`.
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
}
