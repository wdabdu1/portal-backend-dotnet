using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

public record ScheduleItem(string Division, string GroupItem, decimal TargetDays, DateOnly TargetDate, DateOnly? ActualDate, string Status, string Light);
public record ClearanceScheduleResult(DateOnly? AnchorDate, DateOnly? EstimatedCompletionDate, List<ScheduleItem> Items);

// Shared cascading schedule engine: each step's target date is calculated
// from the PREVIOUS step's actual completion date if known, otherwise its
// own target date — so a delay in one step pushes every step after it.
// Anchored on the DO's Actual Arrival Date once entered, falling back to
// the Shipment's ETA until then.
public class ClearanceScheduleService
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceScheduleService(ShippingPortalDbContext db) => _db = db;

    public async Task<ClearanceScheduleResult> GetScheduleAsync(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return new ClearanceScheduleResult(null, null, new List<ScheduleItem>());

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var route = clearance?.Route ?? ClearanceRouteType.NotSelected;

        // Route 3 (withdrawal from FZ) has no vessel arrival of its own —
        // it anchors on the withdrawal request instead of DO/ETA.
        DateOnly? anchor;
        if (route == ClearanceRouteType.Route3ClearFromFz)
        {
            anchor = clearance?.WithdrawalRequestDate;
        }
        else
        {
            var deliveryOrder = clearance is null ? null : await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id);
            anchor = deliveryOrder?.ActualArrivalDate ?? shipment.Eta;
        }
        if (!anchor.HasValue) return new ClearanceScheduleResult(null, null, new List<ScheduleItem>());

        if (route == ClearanceRouteType.NotSelected) return new ClearanceScheduleResult(anchor, null, new List<ScheduleItem>());

        var routeDivision = route switch
        {
            ClearanceRouteType.Route1ClearAtPort => ClearanceDivision.Route1,
            ClearanceRouteType.Route2FzDeposit => ClearanceDivision.Route2,
            _ => ClearanceDivision.Route3
        };

        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).OrderBy(s => s.SequenceOrder).ToListAsync();
        var orderedRows = new List<ClearanceSlaSetting>();
        if (routeDivision != ClearanceDivision.Route3)
            orderedRows.AddRange(slaRows.Where(s => s.Division == ClearanceDivision.General));
        orderedRows.AddRange(slaRows.Where(s => s.Division == routeDivision));

        var actualDates = clearance is null
            ? new Dictionary<(string, string), DateOnly?>()
            : await BuildActualDatesAsync(clearance.Id, routeDivision);

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();

        var items = new List<ScheduleItem>();
        var chainFrom = anchor.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var row in orderedRows)
        {
            var wholeDays = (int)Math.Ceiling(row.TargetDays);
            var targetDate = AddBusinessDays(chainFrom, wholeDays, holidaySet);
            actualDates.TryGetValue((row.Division, row.GroupItem), out var actualDate);

            string status;
            string light;
            if (actualDate.HasValue)
            {
                var diff = BusinessDaysBetween(actualDate.Value, targetDate, holidaySet);
                if (diff < 0) { status = $"Completed {-diff} business day(s) early"; light = "Green"; }
                else if (diff == 0) { status = "Completed on time"; light = "Green"; }
                else { status = $"Completed {diff} business day(s) late"; light = "Amber"; }
                chainFrom = actualDate.Value;
            }
            else
            {
                var diff = BusinessDaysBetween(today, targetDate, holidaySet);
                if (diff <= 0) { status = "Pending — on track"; light = "Green"; }
                else { status = $"Pending — delayed {diff} business day(s)"; light = "Red"; }
                chainFrom = targetDate;
            }

            items.Add(new ScheduleItem(row.Division, row.GroupItem, row.TargetDays, targetDate, actualDate, status, light));
        }

        var estimatedCompletion = items.Count > 0 ? items[^1].TargetDate : (DateOnly?)null;
        return new ClearanceScheduleResult(anchor, estimatedCompletion, items);
    }

    private async Task<Dictionary<(string, string), DateOnly?>> BuildActualDatesAsync(int clearanceId, string routeDivision)
    {
        var result = new Dictionary<(string, string), DateOnly?>();

        if (routeDivision != ClearanceDivision.Route3)
        {
            var deliveryOrder = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Delivery Order")] = deliveryOrder?.DoReceivedDate;

            var costEstimate = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Clearance Cost Estimate")] = costEstimate?.AmountSettledDate;

            var certEntry = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Customs Certificate Entry")] = certEntry?.CertificateEntryDate;
        }

        if (routeDivision == ClearanceDivision.Route1)
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
        else if (routeDivision == ClearanceDivision.Route2)
        {
            var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(routeDivision, "FZ Deposit Request")] = r2?.RequestApprovalDate;
            result[(routeDivision, "Customs Inspection")] = r2?.InspectionDate;
            result[(routeDivision, "SPC Bill")] = r2?.SpcBillSettlementDate;
            result[(routeDivision, "Truck & Containers")] = r2?.ClearanceActualCompletedDate;
        }
        else if (routeDivision == ClearanceDivision.Route3)
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

    public static DateOnly AddBusinessDays(DateOnly start, int days, HashSet<DateOnly> holidays)
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

    public static int BusinessDaysBetween(DateOnly from, DateOnly to, HashSet<DateOnly> holidays)
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
