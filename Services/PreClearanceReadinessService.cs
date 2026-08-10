using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

public record ReadinessItem(string GroupItem, DateOnly ShouldBeDoneBy, DateOnly? ActualDate, string Status, string Light);
public record TrackResult(string Track, List<ReadinessItem> Items);
public record ShipmentReadiness(int ShipmentId, string BlAwbNo, DateOnly? Eta, List<TrackResult> Tracks);

// Pre-clearance readiness — entirely separate from the forward clearance
// cascade. Each track is measured BACKWARD from ETA: "should have been
// done by ETA minus N days," so risk shows up before the vessel even
// arrives, independent of how clearance itself is going.
public class PreClearanceReadinessService
{
    private readonly ShippingPortalDbContext _db;
    public PreClearanceReadinessService(ShippingPortalDbContext db) => _db = db;

    public async Task<List<ShipmentReadiness>> CalculateAsync(List<int> shipmentIds)
    {
        var result = new List<ShipmentReadiness>();
        if (shipmentIds.Count == 0) return result;

        var shipments = await _db.Shipments.Where(s => shipmentIds.Contains(s.Id)).ToListAsync();
        var draftDocs = await _db.ShipmentDraftDocuments.Where(d => shipmentIds.Contains(d.ShipmentId)).ToDictionaryAsync(d => d.ShipmentId);
        var fullSets = await _db.ShipmentSupplierFullSets.Where(f => shipmentIds.Contains(f.ShipmentId)).ToDictionaryAsync(f => f.ShipmentId);
        var mots = await _db.ShipmentMots.Where(m => shipmentIds.Contains(m.ShipmentId)).ToDictionaryAsync(m => m.ShipmentId);
        var ssmos = await _db.ShipmentSsmos.Where(s => shipmentIds.Contains(s.ShipmentId)).ToDictionaryAsync(s => s.ShipmentId);

        var clearances = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId);
        var clearanceIds = clearances.Values.Select(c => c.Id).ToList();
        var deliveryOrders = await _db.ClearanceDeliveryOrders.Where(d => clearanceIds.Contains(d.ClearanceId)).ToDictionaryAsync(d => d.ClearanceId);

        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();
        var docsRows = slaRows.Where(s => s.Division == ClearanceDivision.PreClearanceDocs).OrderByDescending(s => s.SequenceOrder).ToList();
        var motDays = slaRows.FirstOrDefault(s => s.Division == ClearanceDivision.PreClearanceMot)?.TargetDays ?? 0;
        var ssmoDays = slaRows.FirstOrDefault(s => s.Division == ClearanceDivision.PreClearanceSsmo)?.TargetDays ?? 0;
        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var shipment in shipments)
        {
            if (!shipment.Eta.HasValue)
            {
                result.Add(new ShipmentReadiness(shipment.Id, shipment.BlAwbNo, null, new List<TrackResult>()));
                continue;
            }
            var eta = shipment.Eta.Value;

            // --- Document Chain — walked BACKWARD from ETA, latest step first ---
            var docItems = new List<ReadinessItem>();
            var cascadeFrom = eta;
            draftDocs.TryGetValue(shipment.Id, out var dd);
            fullSets.TryGetValue(shipment.Id, out var fs);
            clearances.TryGetValue(shipment.Id, out var clearance);
            var deliveryOrder = clearance is not null ? deliveryOrders.GetValueOrDefault(clearance.Id) : null;

            foreach (var row in docsRows)
            {
                var shouldBeDoneBy = SubtractBusinessDays(cascadeFrom, (int)Math.Ceiling(row.TargetDays), holidaySet);
                DateOnly? actual = row.GroupItem switch
                {
                    "Final Draft Received" => dd?.FinalDraftReceivedDate,
                    "Final Draft Confirmed" => dd?.FinalDraftConfirmedDate,
                    "FS Received" => fs?.FsReceivedDate,
                    "Original Shipment Set Received" => clearance?.OriginalShipmentSetReceivedDate,
                    "DO Received" => deliveryOrder?.DoReceivedDate,
                    _ => null
                };
                docItems.Insert(0, BuildItem(row.GroupItem, shouldBeDoneBy, actual, today, holidaySet));
                cascadeFrom = shouldBeDoneBy;
            }

            // --- MOT / SSMO — independent, each backward from ETA directly ---
            mots.TryGetValue(shipment.Id, out var mot);
            ssmos.TryGetValue(shipment.Id, out var ssmo);
            var motShouldBe = SubtractBusinessDays(eta, (int)Math.Ceiling(motDays), holidaySet);
            var ssmoShouldBe = SubtractBusinessDays(eta, (int)Math.Ceiling(ssmoDays), holidaySet);

            // --- Vessel Arrival — actual vs ETA directly, no lead time ---
            var vesselItem = new ReadinessItem("Vessel Arrival", eta, deliveryOrder?.ActualArrivalDate,
                deliveryOrder?.ActualArrivalDate.HasValue == true
                    ? (deliveryOrder.ActualArrivalDate.Value > eta ? "Arrived late" : "Arrived on time or early")
                    : (today > eta ? "Overdue — not yet arrived" : "Not yet due"),
                deliveryOrder?.ActualArrivalDate.HasValue == true
                    ? (deliveryOrder.ActualArrivalDate.Value > eta ? "Amber" : "Green")
                    : (today > eta ? "Red" : "Green"));

            result.Add(new ShipmentReadiness(shipment.Id, shipment.BlAwbNo, eta, new List<TrackResult>
            {
                new TrackResult("Document Chain", docItems),
                new TrackResult("MOT Approval", new List<ReadinessItem> { BuildItem("MOT Approval", motShouldBe, mot?.ApprovalDate, today, holidaySet) }),
                new TrackResult("SSMO Approval", new List<ReadinessItem> { BuildItem("SSMO Approval", ssmoShouldBe, ssmo?.ApprovalDate, today, holidaySet) }),
                new TrackResult("Vessel Arrival", new List<ReadinessItem> { vesselItem })
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

    // Positive when `to` is later than `from` (same convention as
    // ClearanceScheduleService's own helper).
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
