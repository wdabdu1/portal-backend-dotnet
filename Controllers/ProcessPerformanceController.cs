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
//
// The last three entries are used by Demurrage Analysis, not this
// dashboard: MOT/SSMO Certificate Delays are one-directional overruns
// against a Government approval, so they stay Government; OS Doc
// Dispatch is the processing team's own turnaround getting documents to
// the bank, so it's Internal; Container Return (Truck & Containers'
// actual completion -> the container physically handed back) defaults
// to Internal since it's our own trucking/logistics team's execution —
// change this one if the business decides gate delays at the shipping
// line's side are the more common cause.
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
        ["Truck & Containers"] = "Internal",
        ["MOT Certificate Delays"] = "Government",
        ["SSMO Certificate Delays"] = "Government",
        ["OS Doc Dispatch"] = "Internal",
        ["Container Return"] = "Internal"
    };
}

public record ProcessStepDetail(
    string StepName, string Category,
    DateOnly? ForecastStart, DateOnly? ForecastEnd,
    DateOnly? ActualStart, DateOnly? ActualEnd,
    // Actual/Target/Gap using Demurrage Analysis's own sign convention
    // (Gap = Actual − Target; overrun = positive = Red, ahead =
    // negative = Green) — the page's only day-count figure now.
    // ActualDaysTaken is the business-day duration between actualStart
    // and actualEnd; TargetDays is always known even before an actual
    // exists; Gap is only set once an actual duration exists.
    // Previously this record also carried a second, oppositely-signed
    // pair (ExecutionSpeedDays/CompletionDateDeltaDays) — removed per
    // user feedback that having both conventions on one page read as
    // more confusing than useful, now that Gap covers the same ground.
    int? ActualDaysTaken, decimal? TargetDays, decimal? Gap);

public record CategoryRollup(string Category, double AvgActualDays, decimal AvgTargetDays, double AvgGapDays, int StepInstanceCount);

public record ProcessPerformanceResult(
    bool IsSingleShipment, int ShipmentCount,
    string? BlAwbNo, string? BusinessUnit, string? Consignee,
    string? Supplier, DateOnly? SobActualDate, DateOnly? ActualArrivalDate,
    List<ProcessStepDetail> Steps,
    List<CategoryRollup> CategoryRollups,
    // How many of the shipments matching the CURRENT filters (targetIds
    // in Get(), before the per-shipment build loop) actually had a
    // genuine Demurrage/Storage charge hit, and how much was paid on
    // those — lets the user trace "where charges are likely coming
    // from" by narrowing any filter (Bank, Shipping Line, Supplier...)
    // and watching this figure move. Same "genuinely incurred" hit
    // eligibility test as DemurrageAnalysisController.
    // GetHitShipmentIdsAsync (see ComputeHitSummaryAsync below) — a
    // deliberate copy, not a shared call, consistent with how this
    // codebase already duplicates this kind of per-dashboard logic
    // rather than factoring out a shared base.
    int HitShipmentCount, decimal HitShipmentAmountSdg);

public record ShipmentSearchResult(int ShipmentId, string BlAwbNo, string Supplier, string Consignee);

public record FilterOption(int Id, string Name);

// Powers cascading filter dropdowns on the frontend: each list is
// restricted to values that actually appear among ongoing
// (non-Cancelled) shipments matching every OTHER currently-selected
// filter — never the dimension's own filter, so picking a value in a
// dropdown doesn't immediately collapse that same dropdown down to
// just the one entry.
public record ProcessPerformanceFilterOptions(
    List<FilterOption> BusinessUnits, List<FilterOption> Consignees, List<FilterOption> Categories,
    List<FilterOption> Suppliers, List<FilterOption> ShippingLines,
    List<FilterOption> SenderBanks, List<FilterOption> ReceiverBanks);

[ApiController]
[Route("api/dashboards/process-performance")]
[Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser + "," + AppRoles.IpSupervisor)]
public class ProcessPerformanceController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ProcessPerformanceController(ShippingPortalDbContext db) => _db = db;

    // Powers the shipment search box — matches on BL/AWB, Supplier, or
    // Consignee, returning enough context in each result for the user
    // to confirm they're picking the right one before it's too late.
    [HttpGet("search-shipments")]
    public async Task<ActionResult<IEnumerable<ShipmentSearchResult>>> SearchShipments(
        [FromServices] BuAccessService buAccess, [FromQuery] string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 3) return Ok(new List<ShipmentSearchResult>());
        var t = term.Trim();

        var query = _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .Where(s =>
                s.BlAwbNo.Contains(t) ||
                (s.PurchaseOrder!.Supplier!.Name.Contains(t)) ||
                (s.PurchaseOrder!.Consignee!.Name.Contains(t)))
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var results = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(15)
            .Select(s => new ShipmentSearchResult(s.Id, s.BlAwbNo, s.PurchaseOrder!.Supplier!.Name, s.PurchaseOrder!.Consignee!.Name))
            .ToListAsync();

        return Ok(results);
    }

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
            return Ok(new ProcessPerformanceResult(shipmentId.HasValue, 0, null, null, null, null, null, null, new(), new(), 0, 0));

        // Computed on targetIds (the full filtered set, before the
        // per-shipment build loop below drops shipments with no ETA) so
        // the hit summary reflects exactly what the current filters
        // selected, same set the step table itself is built from.
        var (hitCount, hitAmountSdg) = await ComputeHitSummaryAsync(targetIds);

        var holidaySet = (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();
        var slaRows = await _db.ClearanceSlaSettings.Where(s => s.IsActive).ToListAsync();

        var perShipment = new List<ProcessPerformanceResult>();
        foreach (var id in targetIds)
        {
            var detail = await BuildSingleAsync(id, holidaySet, slaRows);
            if (detail is not null) perShipment.Add(detail);
        }

        if (perShipment.Count == 0)
            return Ok(new ProcessPerformanceResult(shipmentId.HasValue, 0, null, null, null, null, null, null, new(), new(), hitCount, hitAmountSdg));

        if (shipmentId.HasValue) return Ok(perShipment[0] with { HitShipmentCount = hitCount, HitShipmentAmountSdg = hitAmountSdg });

        // --- Group mode: average per step, drop dates entirely ---
        var allStepNames = perShipment.SelectMany(p => p.Steps.Select(s => s.StepName)).Distinct().ToList();
        var avgSteps = allStepNames.Select(name =>
        {
            var matching = perShipment.SelectMany(p => p.Steps).Where(s => s.StepName == name).ToList();
            var actualDaysList = matching.Where(s => s.ActualDaysTaken.HasValue).Select(s => s.ActualDaysTaken!.Value).ToList();
            var targetDaysList = matching.Where(s => s.TargetDays.HasValue).Select(s => s.TargetDays!.Value).ToList();
            var gapList = matching.Where(s => s.Gap.HasValue).Select(s => s.Gap!.Value).ToList();
            return new ProcessStepDetail(
                name, matching.First().Category, null, null, null, null,
                actualDaysList.Count > 0 ? (int?)Math.Round(actualDaysList.Average()) : null,
                targetDaysList.Count > 0 ? targetDaysList.Average() : null,
                gapList.Count > 0 ? gapList.Average() : null);
        }).ToList();

        // Worst (most positive/overrun) average Gap first, so the
        // category most responsible for delay leads the rollup.
        var rollups = avgSteps
            .Where(s => s.Gap.HasValue)
            .GroupBy(s => s.Category)
            .Select(g => new CategoryRollup(
                g.Key,
                g.Where(s => s.ActualDaysTaken.HasValue).Select(s => (double)s.ActualDaysTaken!.Value).DefaultIfEmpty(0).Average(),
                g.Where(s => s.TargetDays.HasValue).Select(s => s.TargetDays!.Value).DefaultIfEmpty(0).Average(),
                g.Select(s => (double)s.Gap!.Value).DefaultIfEmpty(0).Average(),
                g.Count()))
            .OrderByDescending(r => r.AvgGapDays)
            .ToList();

        return Ok(new ProcessPerformanceResult(false, perShipment.Count, null, null, null, null, null, null, avgSteps, rollups, hitCount, hitAmountSdg));
    }

    // Deliberate copy of DemurrageAnalysisController.GetHitShipmentIdsAsync's
    // own hit-eligibility logic (a shipment counts as "hit" only once the
    // relevant physical event — Containers Returned for Demurrage, Truck
    // Port Entry for Storage — has actually happened, not just because a
    // charge amount was entered early), scoped here to whatever shipment
    // ID set the caller already filtered down to, rather than
    // re-implementing Process Performance's fuller filter set (Category/
    // Supplier/Bank) a second time.
    private async Task<(int HitCount, decimal HitAmountSdg)> ComputeHitSummaryAsync(List<int> shipmentIds)
    {
        if (shipmentIds.Count == 0) return (0, 0);

        var clearanceByShipment = await _db.Clearances.Where(c => shipmentIds.Contains(c.ShipmentId)).ToDictionaryAsync(c => c.ShipmentId, c => c.Id);
        var clearanceIds = clearanceByShipment.Values.ToList();
        if (clearanceIds.Count == 0) return (0, 0);

        var charges = await _db.ClearanceActualCharges.Where(c => clearanceIds.Contains(c.ClearanceId)).ToListAsync();
        var route1Returns = await _db.ClearanceRoute1Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => new { r.ContainersReturnedDate, r.TruckPortEntryPermitDate });
        var route2Returns = await _db.ClearanceRoute2Details.Where(r => clearanceIds.Contains(r.ClearanceId)).ToDictionaryAsync(r => r.ClearanceId, r => new { r.ContainersReturnedDate, r.TruckPortEntryPermitDate });

        var hitCount = 0;
        var hitAmount = 0m;
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
            if (!demurrageReady && !storageReady) continue;

            hitCount++;
            if (demurrageReady) hitAmount += c.ActualDemurragePaidSdg ?? 0;
            if (storageReady) hitAmount += c.ActualStoragePaidSdg ?? 0;
        }

        return (hitCount, hitAmount);
    }

    // Cascading filter-option lists — see ProcessPerformanceFilterOptions
    // above for the self-exclusion rule. `skip` names the one dimension
    // whose own filter is NOT applied while building that dimension's
    // own candidate shipment-id set.
    [HttpGet("filter-options")]
    public async Task<ActionResult<ProcessPerformanceFilterOptions>> GetFilterOptions(
        [FromServices] BuAccessService buAccess,
        [FromQuery] DateOnly? etaFrom, [FromQuery] DateOnly? etaTo,
        [FromQuery] int? businessUnitId, [FromQuery] int? consigneeId, [FromQuery] int? categoryId,
        [FromQuery] int? supplierId, [FromQuery] int? shippingLineId,
        [FromQuery] int? senderBankId, [FromQuery] int? receiverBankId)
    {
        async Task<List<int>> IdsExcluding(string skip)
        {
            var q = _db.Shipments.Where(s => s.Status != ShipmentStatus.Cancelled)
                .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem)
                .AsQueryable();

            if (etaFrom.HasValue) q = q.Where(s => s.Eta >= etaFrom);
            if (etaTo.HasValue) q = q.Where(s => s.Eta <= etaTo);
            if (skip != "bu" && businessUnitId.HasValue) q = q.Where(s => s.PurchaseOrder!.BusinessUnitId == businessUnitId);
            if (skip != "consignee" && consigneeId.HasValue) q = q.Where(s => s.PurchaseOrder!.ConsigneeId == consigneeId);
            if (skip != "supplier" && supplierId.HasValue) q = q.Where(s => s.PurchaseOrder!.SupplierId == supplierId);
            if (skip != "shippingLine" && shippingLineId.HasValue) q = q.Where(s => s.ShippingLineId == shippingLineId);
            if (skip != "category" && categoryId.HasValue) q = q.Where(s => s.LineItems.Any(li => li.PurchaseOrderLineItem!.ProductCategoryId == categoryId));

            if (!buAccess.SeesAllBus(User))
            {
                var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
                q = q.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
            }

            var ids = await q.Select(s => s.Id).ToListAsync();

            var applySenderBank = skip != "senderBank" && senderBankId.HasValue;
            var applyReceiverBank = skip != "receiverBank" && receiverBankId.HasValue;
            if (applySenderBank || applyReceiverBank)
            {
                var bankQuery = _db.ShipmentBankings.Where(b => ids.Contains(b.ShipmentId)).AsQueryable();
                if (applySenderBank) bankQuery = bankQuery.Where(b => b.SenderBankId == senderBankId);
                if (applyReceiverBank) bankQuery = bankQuery.Where(b => b.ReceivingBankId == receiverBankId);
                ids = await bankQuery.Select(b => b.ShipmentId).ToListAsync();
            }

            return ids;
        }

        var buIds = await IdsExcluding("bu");
        var consigneeIds = await IdsExcluding("consignee");
        var supplierIds = await IdsExcluding("supplier");
        var shippingLineIds = await IdsExcluding("shippingLine");
        var categoryIds = await IdsExcluding("category");
        var senderBankIds = await IdsExcluding("senderBank");
        var receiverBankIds = await IdsExcluding("receiverBank");

        // Projected to an anonymous type first (not the FilterOption
        // record directly) — Distinct()/OrderBy() over a plain anonymous
        // type is the most reliably-translated EF Core pattern across
        // providers; the FilterOption records themselves are built
        // in-memory afterward, once the distinct rows are already back.
        var businessUnits = (await _db.Shipments.Where(s => buIds.Contains(s.Id))
            .Select(s => new { Id = s.PurchaseOrder!.BusinessUnitId, s.PurchaseOrder!.BusinessUnit!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var consignees = (await _db.Shipments.Where(s => consigneeIds.Contains(s.Id))
            .Select(s => new { Id = s.PurchaseOrder!.ConsigneeId, s.PurchaseOrder!.Consignee!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var suppliers = (await _db.Shipments.Where(s => supplierIds.Contains(s.Id))
            .Select(s => new { Id = s.PurchaseOrder!.SupplierId, s.PurchaseOrder!.Supplier!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var shippingLines = (await _db.Shipments.Where(s => shippingLineIds.Contains(s.Id))
            .Select(s => new { Id = s.ShippingLineId, s.ShippingLine!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var categories = (await _db.ShipmentLineItems.Where(li => categoryIds.Contains(li.ShipmentId))
            .Select(li => new { Id = li.PurchaseOrderLineItem!.ProductCategoryId, li.PurchaseOrderLineItem!.ProductCategory!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var senderBanks = (await _db.ShipmentBankings.Where(b => senderBankIds.Contains(b.ShipmentId) && b.SenderBankId != null)
            .Select(b => new { Id = b.SenderBankId!.Value, b.SenderBank!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        var receiverBanks = (await _db.ShipmentBankings.Where(b => receiverBankIds.Contains(b.ShipmentId) && b.ReceivingBankId != null)
            .Select(b => new { Id = b.ReceivingBankId!.Value, b.ReceivingBank!.Name })
            .Distinct().OrderBy(o => o.Name).ToListAsync())
            .Select(o => new FilterOption(o.Id, o.Name)).ToList();

        return Ok(new ProcessPerformanceFilterOptions(businessUnits, consignees, categories, suppliers, shippingLines, senderBanks, receiverBanks));
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
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
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
        if (route != ClearanceRouteType.NotSelected)
        {
            var routeDivision = route switch
            {
                ClearanceRouteType.Route1ClearAtPort => ClearanceDivision.Route1,
                ClearanceRouteType.Route2FzDeposit => ClearanceDivision.Route2,
                _ => ClearanceDivision.Route3
            };
            var orderedRows = new List<ClearanceSlaSetting>();
            // Route 3 (withdrawal from FZ) doesn't combine with General —
            // it has its own complete division, including its own Customs
            // Certificate Entry sourced from ClearanceRoute3Details rather
            // than the shared table, so General is skipped here.
            if (routeDivision != ClearanceDivision.Route3)
                orderedRows.AddRange(slaRows.Where(r => r.Division == ClearanceDivision.General).OrderBy(r => r.SequenceOrder));
            orderedRows.AddRange(slaRows.Where(r => r.Division == routeDivision).OrderBy(r => r.SequenceOrder));

            var actualDates = clearance is not null
                ? await BuildActualDatesAsync(clearance.Id, routeDivision)
                : new Dictionary<(string, string), DateOnly?>();

            // Route 3 has no vessel arrival of its own — it anchors on the
            // withdrawal request instead of DO/ETA, mirroring
            // ClearanceScheduleService's Route 3 anchor. The `?? eta`
            // fallback is an approximation for shipments where the
            // withdrawal date isn't set yet.
            var chainFrom = routeDivision == ClearanceDivision.Route3
                ? (clearance?.WithdrawalRequestDate ?? eta)
                : (deliveryOrder?.ActualArrivalDate ?? eta);
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

        // HitShipmentCount/HitShipmentAmountSdg are placeholders here (0)
        // — they're an aggregate over the whole filtered query, not a
        // per-shipment figure, so Get() overwrites them with the real
        // computed values via a `with` expression on its single-shipment
        // return path.
        return new ProcessPerformanceResult(
            true, 1, shipment.BlAwbNo, shipment.PurchaseOrder?.BusinessUnit?.Name, shipment.PurchaseOrder?.Consignee?.Name,
            shipment.PurchaseOrder?.Supplier?.Name, shipment.SobActualDate, deliveryOrder?.ActualArrivalDate,
            steps, new(), 0, 0);
    }

    private async Task<Dictionary<(string, string), DateOnly?>> BuildActualDatesAsync(int clearanceId, string routeDivision)
    {
        var result = new Dictionary<(string, string), DateOnly?>();

        // Route 3 doesn't share the General division (see BuildSingleAsync),
        // so these shared-table lookups are skipped entirely for it.
        if (routeDivision != ClearanceDivision.Route3)
        {
            var deliveryOrder = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Delivery Order")] = deliveryOrder?.DoReceivedDate;

            var costEstimate = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(x => x.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Clearance Cost Estimate")] = costEstimate?.AmountSettledDate;

            var certEntry = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(c => c.ClearanceId == clearanceId);
            result[(ClearanceDivision.General, "Customs Certificate Entry")] = certEntry?.CertificateEntryDate;
        }

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
        else if (routeDivision == ClearanceDivision.Route3)
        {
            var r3 = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(r => r.ClearanceId == clearanceId);
            result[(routeDivision, "Customs Certificate Entry")] = r3?.CertificateEntryDate;
            result[(routeDivision, "SSMO File Process")] = r3?.SsmoFileRequestDate;
            result[(routeDivision, "Customs Examination (Form 48)")] = r3?.CustExamCompletedDate;
            result[(routeDivision, "Customs Lab")] = r3?.LabResultIssuanceDate;
            result[(routeDivision, "SSMO Examination")] = r3?.SsmoCertIssuanceDate;
            result[(routeDivision, "Customs Evaluation")] = r3?.CustEvaluationDate;
            result[(routeDivision, "Truck & Containers")] = r3?.ClearanceActualCompletedDate;
        }

        return result;
    }

    private static void AddStep(
        List<ProcessStepDetail> steps, string name,
        DateOnly? forecastStart, DateOnly? forecastEnd, DateOnly? actualStart, DateOnly? actualEnd,
        decimal targetDays, HashSet<DateOnly> holidaySet)
    {
        int? actualDaysTaken = null;
        if (actualStart.HasValue && actualEnd.HasValue)
        {
            actualDaysTaken = BusinessDaysBetween(actualStart.Value, actualEnd.Value, holidaySet);
        }

        // Demurrage-Analysis-style sign: Gap = Actual − Target, so an
        // overrun (actual > target) is positive/Red, matching
        // ClearanceStepGap's own Gap field exactly.
        decimal? gap = actualDaysTaken.HasValue ? actualDaysTaken.Value - targetDays : null;

        var category = ProcessStepCategories.ByStep.GetValueOrDefault(name, "Internal");
        steps.Add(new ProcessStepDetail(name, category, forecastStart, forecastEnd, actualStart, actualEnd, actualDaysTaken, targetDays, gap));
    }
}
