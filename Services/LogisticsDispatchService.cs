using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

// Where a consignment stands once Clearance has handed it to Logistics.
//  - Port shipment (Route 1): release = SPC Bill settlement date.
//  - FZ withdrawal: release = the withdrawal's Release & Exit Pass date.
// FullyDispatched = the day the LAST unit of the LAST line item went onto a truck (null until
// every line is fully loaded). ReleaseIsForecast = the release date is the projected SPC Bill
// date, not a real settlement yet (only when the caller asks for projections).
public record DispatchInfo(
    int ShipmentId, DateOnly? ReleaseDate, DateOnly? FirstLoadDate, DateOnly? FullyDispatchedDate,
    decimal TotalQty, decimal LoadedQty,
    int? WithdrawalId = null, string? WithdrawalRef = null, bool ReleaseIsForecast = false);

public class LogisticsDispatchService
{
    private readonly ShippingPortalDbContext _db;
    private readonly ClearanceScheduleService _schedule;
    public LogisticsDispatchService(ShippingPortalDbContext db, ClearanceScheduleService schedule)
    {
        _db = db;
        _schedule = schedule;
    }

    public async Task<HashSet<DateOnly>> GetHolidaysAsync() =>
        (await _db.PublicHolidays.Where(h => h.AffectsClr).Select(h => h.Date).ToListAsync()).ToHashSet();

    public async Task<int> GetTargetDaysAsync()
    {
        var v = await _db.LogisticsVisibilitySettings.Select(s => (int?)s.DispatchTargetBusinessDays).FirstOrDefaultAsync();
        return v ?? 2;
    }

    // Business days in [from, to] INCLUDING both ends (a one-day hold-up = 1 day).
    // Used for delay-log entries. Returns 0 when to is before from.
    public static int InclusiveBusinessDays(DateOnly from, DateOnly to, HashSet<DateOnly> holidays) =>
        to < from ? 0 : ClearanceScheduleService.BusinessDaysBetween(from.AddDays(-1), to, holidays);

    private record LineQty(int Id, decimal Qty);
    private record LoadPoint(decimal Qty, DateOnly LoadDate);

    // Walks every line's truck loads in date order: a line is "done" on the load date where the
    // cumulative loaded quantity first reaches its quantity; the consignment is fully dispatched
    // on the latest such date, once ALL lines are done.
    private static (decimal Total, decimal Loaded, DateOnly? First, DateOnly? FullyDispatched) Summarize(
        List<LineQty> lines, Dictionary<int, List<LoadPoint>> loadsByLine)
    {
        decimal loaded = 0;
        DateOnly? firstLoad = null;
        DateOnly? lastLineDone = null;
        var everyLineDone = lines.Any(l => l.Qty > 0);

        foreach (var line in lines)
        {
            var items = loadsByLine.GetValueOrDefault(line.Id) ?? new List<LoadPoint>();
            decimal cumulative = 0;
            DateOnly? lineDoneOn = null;
            foreach (var it in items)
            {
                cumulative += it.Qty;
                if (firstLoad is null || it.LoadDate < firstLoad) firstLoad = it.LoadDate;
                if (lineDoneOn is null && cumulative >= line.Qty) lineDoneOn = it.LoadDate;
            }
            loaded += cumulative;

            if (line.Qty <= 0) continue;
            if (lineDoneOn is null) { everyLineDone = false; continue; }
            if (lastLineDone is null || lineDoneOn > lastLineDone) lastLineDone = lineDoneOn;
        }
        return (lines.Sum(l => l.Qty), loaded, firstLoad, everyLineDone ? lastLineDone : null);
    }

    // Route 1 (clear-at-port) shipments only. Keyed by shipment id.
    // includeProjectedRelease: when no real SPC Bill settlement exists yet, use the projected SPC Bill
    // date (flagged ReleaseIsForecast) so the step can be shown before the settlement happens.
    public async Task<Dictionary<int, DispatchInfo>> GetForPortShipmentsAsync(List<int> shipmentIds, bool includeProjectedRelease = false)
    {
        var result = new Dictionary<int, DispatchInfo>();
        if (shipmentIds.Count == 0) return result;

        var clearances = await _db.Clearances
            .Where(c => shipmentIds.Contains(c.ShipmentId) && c.Route == ClearanceRouteType.Route1ClearAtPort)
            .Select(c => new { c.Id, c.ShipmentId })
            .ToListAsync();
        if (clearances.Count == 0) return result;

        var clearanceIds = clearances.Select(c => c.Id).ToList();
        var releases = await _db.ClearanceRoute1Details
            .Where(r => clearanceIds.Contains(r.ClearanceId))
            .Select(r => new { r.ClearanceId, r.SpcBillSettlementDate })
            .ToListAsync();
        var releaseByClearance = releases
            .GroupBy(r => r.ClearanceId)
            .ToDictionary(g => g.Key, g => g.First().SpcBillSettlementDate);

        var portShipmentIds = clearances.Select(c => c.ShipmentId).Distinct().ToList();
        var projected = includeProjectedRelease
            ? await _schedule.GetSpcBillTargetDatesAsync(portShipmentIds)
            : new Dictionary<int, DateOnly?>();

        var lines = await _db.ShipmentLineItems
            .Where(li => portShipmentIds.Contains(li.ShipmentId))
            .Select(li => new { li.Id, li.ShipmentId, li.QtyInBl })
            .ToListAsync();
        var lineIds = lines.Select(l => l.Id).ToList();

        var loadItems = await _db.TruckLoadItems
            .Where(i => i.WarehouseAllocation != null
                && i.WarehouseAllocation.ShipmentLineItemId != null
                && lineIds.Contains(i.WarehouseAllocation.ShipmentLineItemId.Value))
            .Select(i => new { LineId = i.WarehouseAllocation!.ShipmentLineItemId!.Value, i.Qty, LoadDate = i.TruckLoadDrop!.TruckLoad!.LoadDate })
            .ToListAsync();
        var loadsByLine = loadItems.GroupBy(x => x.LineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.LoadDate).Select(x => new LoadPoint(x.Qty, x.LoadDate)).ToList());

        foreach (var c in clearances.GroupBy(x => x.ShipmentId).Select(g => g.First()))
        {
            var actualRelease = releaseByClearance.GetValueOrDefault(c.Id);
            var release = actualRelease ?? (includeProjectedRelease ? projected.GetValueOrDefault(c.ShipmentId) : null);
            var shipLines = lines.Where(l => l.ShipmentId == c.ShipmentId).Select(l => new LineQty(l.Id, l.QtyInBl)).ToList();
            var s = Summarize(shipLines, loadsByLine);
            result[c.ShipmentId] = new DispatchInfo(c.ShipmentId, release, s.First, s.FullyDispatched, s.Total, s.Loaded,
                null, null, !actualRelease.HasValue && release.HasValue);
        }
        return result;
    }

    // FZ withdrawals, keyed by withdrawal id. Release = Release & Exit Pass date.
    public async Task<Dictionary<int, DispatchInfo>> GetForWithdrawalsAsync(List<int> withdrawalIds)
    {
        var result = new Dictionary<int, DispatchInfo>();
        if (withdrawalIds.Count == 0) return result;

        var withdrawals = await _db.Withdrawals
            .Where(w => withdrawalIds.Contains(w.Id))
            .Select(w => new { w.Id, w.DepositShipmentId, w.WithdrawalRequestRefNo, w.ReleaseExitPassDate })
            .ToListAsync();
        var lines = await _db.WithdrawalLineItems
            .Where(x => withdrawalIds.Contains(x.WithdrawalId))
            .Select(x => new { x.Id, x.WithdrawalId, x.Qty })
            .ToListAsync();
        var lineIds = lines.Select(l => l.Id).ToList();

        var loadItems = await _db.TruckLoadItems
            .Where(i => i.WarehouseAllocation != null
                && i.WarehouseAllocation.WithdrawalLineItemId != null
                && lineIds.Contains(i.WarehouseAllocation.WithdrawalLineItemId.Value))
            .Select(i => new { LineId = i.WarehouseAllocation!.WithdrawalLineItemId!.Value, i.Qty, LoadDate = i.TruckLoadDrop!.TruckLoad!.LoadDate })
            .ToListAsync();
        var loadsByLine = loadItems.GroupBy(x => x.LineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.LoadDate).Select(x => new LoadPoint(x.Qty, x.LoadDate)).ToList());

        foreach (var w in withdrawals)
        {
            var wLines = lines.Where(l => l.WithdrawalId == w.Id).Select(l => new LineQty(l.Id, l.Qty)).ToList();
            var s = Summarize(wLines, loadsByLine);
            result[w.Id] = new DispatchInfo(w.DepositShipmentId, w.ReleaseExitPassDate, s.First, s.FullyDispatched, s.Total, s.Loaded,
                w.Id, w.WithdrawalRequestRefNo, false);
        }
        return result;
    }

    // Everything Logistics owes for these shipments: the port clock (Route 1) plus one clock per FZ
    // withdrawal made from them. Keyed by shipment id (the deposit shipment for withdrawals). Entries
    // without a release date yet are included - callers decide whether to show them.
    public async Task<Dictionary<int, List<DispatchInfo>>> GetAllAsync(List<int> shipmentIds, bool includeProjectedRelease = false)
    {
        var result = new Dictionary<int, List<DispatchInfo>>();
        if (shipmentIds.Count == 0) return result;

        foreach (var kv in await GetForPortShipmentsAsync(shipmentIds, includeProjectedRelease))
            result.GetOrAdd(kv.Key).Add(kv.Value);

        var withdrawalIds = await _db.Withdrawals
            .Where(w => shipmentIds.Contains(w.DepositShipmentId))
            .Select(w => w.Id).ToListAsync();
        foreach (var info in (await GetForWithdrawalsAsync(withdrawalIds)).Values.OrderBy(i => i.ReleaseDate))
            result.GetOrAdd(info.ShipmentId).Add(info);

        return result;
    }
}

internal static class DispatchDictionaryExtensions
{
    public static List<DispatchInfo> GetOrAdd(this Dictionary<int, List<DispatchInfo>> d, int key)
    {
        if (!d.TryGetValue(key, out var list)) { list = new List<DispatchInfo>(); d[key] = list; }
        return list;
    }
}
