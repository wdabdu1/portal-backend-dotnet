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
    string ShippingLine, decimal SlaPercent, bool IsCompleted);

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
    DateOnly? WithdrawalRequestDate, string? WithdrawalRequestRefNo);

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

    // Selection screen: only Confirmed shipments (nothing to clear on a Draft),
    // sorted by ETA ascending — soonest-arriving first, per the requirement.
    [HttpGet("shipments")]
    [Authorize(Roles = AppRoles.ClearanceViewers)]
    public async Task<ActionResult<IEnumerable<ClearanceShipmentSummary>>> GetShipmentsForClearance([FromQuery] string? search, [FromServices] ShippingPortal.Api.Services.BuAccessService buAccess)
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
            .Include(s => s.ShippingLine)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => EF.Functions.Like(s.BlAwbNo, $"%{search}%"));
        }

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

        var results = shipments.Select(s =>
        {
            clearances.TryGetValue(s.Id, out var clearance);
            string? declarationNo = null;
            if (clearance is not null && certificateEntries.TryGetValue(clearance.Id, out var certEntry))
            {
                declarationNo = certEntry.ScudaDeclarationNo;
            }

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

            DateOnly? actualCompletedDate = clearance is null ? null : routeDivision switch
            {
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route1 => route1Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route2 => route2Completions.GetValueOrDefault(clearance.Id),
                ShippingPortal.Api.Models.Clearance.ClearanceDivision.Route3 => route3Completions.GetValueOrDefault(clearance.Id),
                _ => null
            };

            var trafficLight = ComputeTrafficLight(s.Eta, actualCompletedDate, targetDays);
            var slaPercent = ComputeSlaPercent(s.Eta, actualCompletedDate, targetDays);
            var routeStatus = clearance is null || clearance.Route == 0 ? "Not Started" : clearance.Route.ToString();

            return new ClearanceShipmentSummary(
                s.Id, s.BlAwbNo, s.PurchaseOrder?.BusinessUnit?.Name ?? "", firstLine?.ProductCategory?.Name ?? "",
                s.Eta, s.Fcl20Count + s.Fcl40Count, declarationNo, firstLine?.ModelProduct?.Name ?? "", totalQty, firstLine?.UnitOfMeasure?.Code ?? "",
                trafficLight, routeStatus, s.ShippingLine?.Name ?? "", slaPercent, actualCompletedDate.HasValue);
        })
        .Where(x => x is not null)
        .OrderBy(x => x!.Eta ?? DateOnly.MaxValue)
        .ToList();

        return Ok(results);
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

        return new ClearanceDetailResponse(
            shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Eta,
            clearance?.CopyOfBlReceivedDate, clearance?.OriginalShipmentSetReceivedDate, clearance?.LcNo,
            clearance?.DeclarationNo, clearance?.Notes, (int)(clearance?.Route ?? 0), clearance?.ClearanceCompleteDate,
            clearance?.ImFormNo, clearance?.ImFormDate,
            shipment.PurchaseOrder.Consignee?.Name ?? "", firstCategory, fclCount,
            clearance?.WithdrawalRequestDate, clearance?.WithdrawalRequestRefNo);
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
    [Authorize(Roles = AppRoles.ClearanceEditors)]
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
