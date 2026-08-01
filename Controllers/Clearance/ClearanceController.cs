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
    string? DeclarationNo, string? Notes, DateOnly? ClearanceCompleteDate,
    string? ImFormNo, DateOnly? ImFormDate, DateOnly? ShipmentEta);

public record ClearanceRouteRequest(int Route); // 0=NotSelected,1=Route1,2=Route2,3=Route3

public record ClearanceDetailResponse(
    int ShipmentId, string BlAwbNo, string PoNumber, DateOnly? Eta, DateOnly? CopyOfBlReceivedDate,
    DateOnly? OriginalShipmentSetReceivedDate, string? LcNo, string? DeclarationNo, string? Notes,
    int Route, DateOnly? ClearanceCompleteDate, string? ImFormNo, DateOnly? ImFormDate);

public record ScheduleItem(string Division, string GroupItem, decimal TargetDays, DateOnly TargetDate, DateOnly? ActualDate, string Status, string Light);
public record ClearanceScheduleResponse(DateOnly? EstimatedCompletionDate, List<ScheduleItem> Items);

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

            var routeDivision = clearance?.Route switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1,
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2,
                ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route3ClearFromFz => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3,
                _ => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1
            };
            var routeDays = slaByDivision.GetValueOrDefault(routeDivision, 0);
            // Route 3 excludes the General clearance steps — goods are
            // already cleared into the FZ by that point.
            var targetDays = routeDivision == ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3
                ? routeDays
                : generalDays + routeDays;

            var trafficLight = ComputeTrafficLight(s.Eta, clearance?.ClearanceCompleteDate, targetDays);
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
            shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Eta,
            clearance?.CopyOfBlReceivedDate, clearance?.OriginalShipmentSetReceivedDate, clearance?.LcNo,
            clearance?.DeclarationNo, clearance?.Notes, (int)(clearance?.Route ?? 0), clearance?.ClearanceCompleteDate,
            clearance?.ImFormNo, clearance?.ImFormDate);
    }

    // Sequential cascading schedule: each step's target date is calculated
    // from the PREVIOUS step's actual completion date if known, otherwise
    // its own target date — so a delay in one step pushes every step after
    // it. Traffic light per step reflects real performance: completed
    // early/on-time/late, or pending on-track/delayed.
    [HttpGet("{shipmentId:int}/sla-schedule")]
    public async Task<ActionResult<ClearanceScheduleResponse>> GetSlaSchedule(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();
        if (!shipment.Eta.HasValue) return Ok(new ClearanceScheduleResponse(null, new List<ScheduleItem>()));

        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var route = clearance?.Route ?? ShippingPortal.Api.Models.Clearance.ClearanceRouteType.NotSelected;
        if (route == ShippingPortal.Api.Models.Clearance.ClearanceRouteType.NotSelected)
            return Ok(new ClearanceScheduleResponse(null, new List<ScheduleItem>()));

        var routeDivision = route switch
        {
            ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route1ClearAtPort => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1,
            ShippingPortal.Api.Models.Clearance.ClearanceRouteType.Route2FzDeposit => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2,
            _ => ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3
        };

        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).OrderBy(s => s.SequenceOrder).ToListAsync();
        var orderedRows = new List<ShippingPortal.Api.Models.Clearance.ClearanceSlaSetting>();
        if (routeDivision != ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3)
            orderedRows.AddRange(slaRows.Where(s => s.Division == ShippingPortal.Api.Models.Clearance.ClearanceDivision.General));
        orderedRows.AddRange(slaRows.Where(s => s.Division == routeDivision));

        var actualDates = clearance is null
            ? new Dictionary<(string, string), DateOnly?>()
            : await BuildActualDatesAsync(clearance.Id, routeDivision);

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();

        var items = new List<ScheduleItem>();
        var chainFrom = shipment.Eta.Value;
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
        return Ok(new ClearanceScheduleResponse(estimatedCompletion, items));
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
        clearance.ImFormNo = req.ImFormNo;
        clearance.ImFormDate = req.ImFormDate;
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
}
