using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

// Fixed, hand-curated attribution — not inferred from data. Confirmed
// with the business: Bank covers the OS Doc Dispatch -> Original
// Shipment Set Received span specifically (offshore + courier + local
// bank handling); Delivery Order stays Internal despite the Shipping
// Line's own release process being a real dependency.
public static class ProcessStepCategories
{
    public static readonly Dictionary<string, string> ByStep = new()
    {
        ["Final Draft Received"] = "Supplier",
        ["Final Draft Confirmed"] = "Internal",
        ["FS Received"] = "Supplier",
        ["Original Shipment Set Received"] = "Bank",
        ["MOT Approval"] = "Government",
        ["SSMO Approval"] = "Government",
        ["Vessel Arrival"] = "Shipping Line",
        ["Delivery Order"] = "Internal",
        ["Clearance Cost Estimate"] = "Internal",
        ["Customs Certificate Entry"] = "Internal",
        ["Containers Move Process"] = "Internal",
        ["FZ Deposit Request"] = "Internal",
        ["Customs Inspection"] = "Government",
        ["SSMO File Process"] = "Government",
        ["Customs Examination (Form 48)"] = "Government",
        ["Customs Lab"] = "Government",
        ["SSMO Examination"] = "Government",
        ["Customs Evaluation"] = "Government",
        ["SPC Bill"] = "Government",
        ["Truck & Containers"] = "Internal"
    };
}

public record ProcessStepDetail(
    string StepName, string Category,
    DateOnly? ForecastStart, DateOnly? ForecastEnd,
    DateOnly? ActualStart, DateOnly? ActualEnd,
    // Positive = faster/better throughout, matching how this
    // dashboard's own audience thinks about it — the opposite sign
    // convention from Demurrage Analysis's Gap column, which is
    // deliberate: these are two different dashboards for two
    // different audiences, not the same figure reused.
    double? ExecutionSpeedDays, double? CompletionDateDeltaDays);

public record CategoryRollup(string Category, double AvgExecutionSpeedDays, double AvgCompletionDateDeltaDays, int StepInstanceCount);

public record ProcessPerformanceResult(
    bool IsSingleShipment, int ShipmentCount,
    string? BlAwbNo, string? BusinessUnit, string? Consignee,
    List<ProcessStepDetail> Steps,
    List<CategoryRollup> CategoryRollups);

[ApiController]
[Route("api/dashboards/process-performance")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
public class ProcessPerformanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ProcessPerformanceController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ProcessPerformanceResult>> Get(
        [FromServices] BuAccessService buAccess,
        [FromQuery] int? shipmentId,
        [FromQuery] DateOnly? etaFrom, [FromQuery] DateOnly? etaTo,
        [FromQuery] int? businessUnitId, [FromQuery] int? consigneeId, [FromQuery] int? categoryId,
        [FromQuery] int? supplierId, [FromQuery] int? shippingLineId,
        [FromQuery] int? senderBankId, [FromQuery] int? receiverBankId)
    {
        List<int> targetIds;

        if (shipmentId.HasValue)
        {
            targetIds = new List<int> { shipmentId.Value };
        }
        else
        {
            var query = _db.Shipments.Where(s => s.Status != ShipmentStatus.Cancelled)
                .Include(s => s.PurchaseOrder)
                .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem)
                .AsQueryable();

            if (etaFrom.HasValue) query = query.Where(s => s.Eta >= etaFrom);
            if (etaTo.HasValue) query = query.Where(s => s.Eta <= etaTo);
            if (businessUnitId.HasValue) query = query.Where(s => s.PurchaseOrder!.BusinessUnitId == businessUnitId);
            if (consigneeId.HasValue) query = query.Where(s => s.PurchaseOrder!.ConsigneeId == consigneeId);
            if (supplierId.HasValue) query = query.Where(s => s.PurchaseOrder!.SupplierId == supplierId);
            if (shippingLineId.HasValue) query = query.Where(s => s.ShippingLineId == shippingLineId);
            if (categoryId.HasValue) query = query.Where(s => s.LineItems.Any(li => li.PurchaseOrderLineItem!.ProductCategoryId == categoryId));

            if (!buAccess.SeesAllBus(User))
            {
                var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
                query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
            }

            targetIds = await query.Select(s => s.Id).ToListAsync();

            if (senderBankId.HasValue || receiverBankId.HasValue)
            {
                var bankQuery = _db.ShipmentBankings.Where(b => targetIds.Contains(b.ShipmentId)).AsQueryable();
                if (senderBankId.HasValue) bankQuery = bankQuery.Where(b => b.SenderBankId == senderBankId);
                if (receiverBankId.HasValue) bankQuery = bankQuery.Where(b => b.ReceivingBankId == receiverBankId);
                targetIds = await bankQuery.Select(b => b.ShipmentId).ToListAsync();
            }
        }

        if (targetIds.Count == 0)
            return Ok(new ProcessPerformanceResult(shipmentId.HasValue, 0, null, null, null, new(), new()));

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();

        var perShipment = new List<ProcessPerformanceResult>();
        foreach (var id in targetIds)
        {
            var detail = await BuildSingleAsync(id, holidaySet, slaRows);
            if (detail is not null) perShipment.Add(detail);
        }

        if (perShipment.Count == 0)
            return Ok(new ProcessPerformanceResult(shipmentId.HasValue, 0, null, null, null, new(), new()));

        if (shipmentId.HasValue) return Ok(perShipment[0]);

        // --- Group mode: average per step, drop dates entirely ---
        var allStepNames = perShipment.SelectMany(p => p.Steps.Select(s => s.StepName)).Distinct().ToList();
        var avgSteps = allStepNames.Select(name =>
        {
            var matching = perShipment.SelectMany(p => p.Steps).Where(s => s.StepName == name).ToList();
            var speeds = matching.Where(s => s.ExecutionSpeedDays.HasValue).Select(s => s.ExecutionSpeedDays!.Value).ToList();
            var deltas = matching.Where(s => s.CompletionDateDeltaDays.HasValue).Select(s => s.CompletionDateDeltaDays!.Value).ToList();
            return new ProcessStepDetail(
                name, matching.First().Category, null, null, null, null,
                speeds.Count > 0 ? speeds.Average() : null,
                deltas.Count > 0 ? deltas.Average() : null);
        }).ToList();

        var rollups = avgSteps
            .Where(s => s.ExecutionSpeedDays.HasValue || s.CompletionDateDeltaDays.HasValue)
            .GroupBy(s => s.Category)
            .Select(g => new CategoryRollup(
                g.Key,
                g.Where(s => s.ExecutionSpeedDays.HasValue).Select(s => s.ExecutionSpeedDays!.Value).DefaultIfEmpty(0).Average(),
                g.Where(s => s.CompletionDateDeltaDays.HasValue).Select(s => s.CompletionDateDeltaDays!.Value).DefaultIfEmpty(0).Average(),
                g.Count()))
            .OrderBy(r => r.AvgCompletionDateDeltaDays)
            .ToList();

        return Ok(new ProcessPerformanceResult(false, perShipment.Count, null, null, null, avgSteps, rollups));
    }

    private static DateOnly SubtractBusinessDays(DateOnly start, int days, HashSet<DateOnly> holidays) =>
        ClearanceScheduleService.SubtractBusinessDays(start, days, holidays);
    private static DateOnly AddBusinessDays(DateOnly start, int days, HashSet<DateOnly> holidays) =>
        ClearanceScheduleService.AddBusinessDays(start, days, holidays);
    private static int BusinessDaysBetween(DateOnly from, DateOnly to, HashSet<DateOnly> holidays) =>
        ClearanceScheduleService.BusinessDaysBetween(from, to, holidays);

    private async Task<ProcessPerformanceResult?> BuildSingleAsync(int shipmentId, HashSet<DateOnly> holidaySet, List<ClearanceSlaSetting> slaRows)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null || !shipment.Eta.HasValue) return null;

        var eta = shipment.Eta.Value;
        var draftDoc = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId);
        var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId);
        var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(b => b.ShipmentId == shipmentId);
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId);
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(s => s.ShipmentId == shipmentId);
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipmentId);
        var deliveryOrder = clearance is not null ? await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id) : null;

        decimal TargetDaysFor(string division, string groupItem) =>
            slaRows.FirstOrDefault(r => r.Division == division && r.GroupItem == groupItem)?.TargetDays ?? 0;

        var steps = new List<ProcessStepDetail>();

        // --- Document Chain (backward from ETA, sequential) ---
        // "Original Shipment Set Received" is excluded here — it's
        // measured separately below, from OS Doc Dispatch specifically,
        // replacing rather than duplicating this chain-based version.
        var docsRows = slaRows.Where(r => r.Division == ClearanceDivision.PreClearanceDocs && r.GroupItem != "Original Shipment Set Received").OrderBy(r => r.SequenceOrder).ToList();
        var etaTargets = new List<DateOnly>();
        var cascadeBack = eta;
        for (var i = docsRows.Count - 1; i >= 0; i--)
        {
            var target = SubtractBusinessDays(cascadeBack, (int)Math.Ceiling(docsRows[i].TargetDays), holidaySet);
            etaTargets.Insert(0, target);
            cascadeBack = target;
        }

        DateOnly? forecastChainFrom = etaTargets.Count > 0 ? SubtractBusinessDays(etaTargets[0], (int)Math.Ceiling(docsRows[0].TargetDays), holidaySet) : null;
        DateOnly? actualChainFrom = null;

        for (var i = 0; i < docsRows.Count; i++)
        {
            var row = docsRows[i];
            var forecastEnd = etaTargets[i];
            var forecastStart = i == 0 ? forecastChainFrom : etaTargets[i - 1];

            DateOnly? actualEnd = row.GroupItem switch
            {
                "Final Draft Received" => draftDoc?.FinalDraftReceivedDate,
                "Final Draft Confirmed" => draftDoc?.FinalDraftConfirmedDate,
                "FS Received" => fullSet?.FsReceivedDate,
                _ => null
            };
            var actualStart = actualChainFrom;

            AddStep(steps, row.GroupItem, forecastStart, forecastEnd, actualStart, actualEnd, row.TargetDays, holidaySet);
            if (actualEnd.HasValue) actualChainFrom = actualEnd;
        }

        // --- Original Shipment Set Received: measured from OS Doc Dispatch, not the chain above ---
        var origSetTargetDays = TargetDaysFor(ClearanceDivision.PreClearanceDocs, "Original Shipment Set Received");
        var bankForecastStart = banking?.OsDocDispatchDate;
        var bankForecastEnd = bankForecastStart.HasValue ? AddBusinessDays(bankForecastStart.Value, (int)Math.Ceiling(origSetTargetDays), holidaySet) : (DateOnly?)null;
        AddStep(steps, "Original Shipment Set Received", bankForecastStart, bankForecastEnd, banking?.OsDocDispatchDate, clearance?.OriginalShipmentSetReceivedDate, origSetTargetDays, holidaySet);

        // --- MOT / SSMO (parallel, backward from ETA, single-step each) ---
        var motDays = TargetDaysFor(ClearanceDivision.PreClearanceMot, "MOT Approval");
        var motTarget = SubtractBusinessDays(eta, (int)Math.Ceiling(motDays), holidaySet);
        AddStep(steps, "MOT Approval", motTarget, motTarget, motTarget, mot?.ApprovalDate, 0, holidaySet);

        var ssmoDays = TargetDaysFor(ClearanceDivision.PreClearanceSsmo, "SSMO Approval");
        var ssmoTarget = SubtractBusinessDays(eta, (int)Math.Ceiling(ssmoDays), holidaySet);
        AddStep(steps, "SSMO Approval", ssmoTarget, ssmoTarget, ssmoTarget, ssmo?.ApprovalDate, 0, holidaySet);

        // --- Vessel Arrival: expected exactly on ETA, no duration of its own ---
        AddStep(steps, "Vessel Arrival", eta, eta, eta, deliveryOrder?.ActualArrivalDate, 0, holidaySet);

        // --- Clearance cascade (Delivery Order onward, route-specific) ---
        var route = clearance?.Route ?? ClearanceRouteType.NotSelected;
        if (route != ClearanceRouteType.NotSelected && route != ClearanceRouteType.Route3ClearFromFz)
        {
            var routeDivision = route == ClearanceRouteType.Route1ClearAtPort ? ClearanceDivision.Route1 : ClearanceDivision.Route2;
            var orderedRows = new List<ClearanceSlaSetting>();
            orderedRows.AddRange(slaRows.Where(r => r.Division == ClearanceDivision.General).OrderBy(r => r.SequenceOrder));
            orderedRows.AddRange(slaRows.Where(r => r.Division == routeDivision).OrderBy(r => r.SequenceOrder));

            var actualDates = clearance is not null
                ? await BuildActualDatesAsync(clearance.Id, routeDivision)
                : new Dictionary<(string, string), DateOnly?>();

            var chainFrom = deliveryOrder?.ActualArrivalDate ?? eta;
            var forecastChain = chainFrom;

            foreach (var row in orderedRows)
            {
                var wholeDays = (int)Math.Ceiling(row.TargetDays);
                var forecastStart = forecastChain;
                var forecastEnd = AddBusinessDays(forecastStart, wholeDays, holidaySet);
                forecastChain = forecastEnd;

                actualDates.TryGetValue((row.Division, row.GroupItem), out var actualEnd);
                var actualStart = chainFrom;
                AddStep(steps, row.GroupItem, forecastStart, forecastEnd, actualStart, actualEnd, row.TargetDays, holidaySet);
                if (actualEnd.HasValue) chainFrom = actualEnd.Value;
            }
        }

        return new ProcessPerformanceResult(
            true, 1, shipment.BlAwbNo, shipment.PurchaseOrder?.BusinessUnit?.Name, shipment.PurchaseOrder?.Consignee?.Name,
            steps, new());
    }

    private async Task<Dictionary<(string, string), DateOnly?>> BuildActualDatesAsync(int clearanceId, string routeDivision)
    {
        var result = new Dictionary<(string, string), DateOnly?>();
        var deliveryOrder = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearanceId);
        result[(ClearanceDivision.General, "Delivery Order")] = deliveryOrder?.DoReceivedDate;

        var costEstimate = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
        result[(ClearanceDivision.General, "Clearance Cost Estimate")] = costEstimate?.AmountSettledDate;

        var certEntry = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(c => c.ClearanceId == clearanceId);
        result[(ClearanceDivision.General, "Customs Certificate Entry")] = certEntry?.CertificateEntryDate;

        if (routeDivision == ClearanceDivision.Route1)
        {
            var r1 = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(r => r.ClearanceId == clearanceId);
            result[(routeDivision, "Containers Move Process")] = r1?.MoveRequestDate;
            result[(routeDivision, "SSMO File Process")] = r1?.SsmoFileRequestDate;
            result[(routeDivision, "Customs Examination (Form 48)")] = r1?.CustExamCompletedDate;
            result[(routeDivision, "Customs Lab")] = r1?.LabResultIssuanceDate;
            result[(routeDivision, "SSMO Examination")] = r1?.SsmoCertIssuanceDate;
            result[(routeDivision, "Customs Evaluation")] = r1?.CustEvaluationDate;
            result[(routeDivision, "SPC Bill")] = r1?.SpcBillSettlementDate;
            result[(routeDivision, "Truck & Containers")] = r1?.ClearanceActualCompletedDate;
        }
        else if (routeDivision == ClearanceDivision.Route2)
        {
            var r2 = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(r => r.ClearanceId == clearanceId);
            result[(routeDivision, "FZ Deposit Request")] = r2?.RequestApprovalDate;
            result[(routeDivision, "Customs Inspection")] = r2?.InspectionDate;
            result[(routeDivision, "SPC Bill")] = r2?.SpcBillSettlementDate;
            result[(routeDivision, "Truck & Containers")] = r2?.ClearanceActualCompletedDate;
        }

        return result;
    }

    private static void AddStep(
        List<ProcessStepDetail> steps, string name,
        DateOnly? forecastStart, DateOnly? forecastEnd, DateOnly? actualStart, DateOnly? actualEnd,
        decimal targetDays, HashSet<DateOnly> holidaySet)
    {
        double? executionSpeed = null;
        if (actualStart.HasValue && actualEnd.HasValue)
        {
            var actualDuration = BusinessDaysBetween(actualStart.Value, actualEnd.Value, holidaySet);
            executionSpeed = (double)targetDays - actualDuration;
        }

        double? completionDelta = null;
        if (forecastEnd.HasValue && actualEnd.HasValue)
        {
            completionDelta = actualEnd.Value <= forecastEnd.Value
                ? BusinessDaysBetween(actualEnd.Value, forecastEnd.Value, holidaySet)
                : -BusinessDaysBetween(forecastEnd.Value, actualEnd.Value, holidaySet);
        }

        var category = ProcessStepCategories.ByStep.GetValueOrDefault(name, "Internal");
        steps.Add(new ProcessStepDetail(name, category, forecastStart, forecastEnd, actualStart, actualEnd, executionSpeed, completionDelta));
    }
}
