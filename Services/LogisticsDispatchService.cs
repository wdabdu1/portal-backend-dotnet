using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

// Where a Route 1 (clear-at-port) shipment stands once Clearance has handed it to
// Logistics. Release = SPC Bill settlement date. FullyDispatched = the day the LAST
// unit of the LAST line item went onto a truck (null until every line is fully
// loaded). Port shipments only; FZ withdrawals are not covered here yet.
public record DispatchInfo(
    int ShipmentId, DateOnly? ReleaseDate, DateOnly? FirstLoadDate, DateOnly? FullyDispatchedDate,
    decimal TotalQty, decimal LoadedQty);

public class LogisticsDispatchService
{
    private readonly ShippingPortalDbContext _db;
    public LogisticsDispatchService(ShippingPortalDbContext db) => _db = db;

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

    public async Task<Dictionary<int, DispatchInfo>> GetForPortShipmentsAsync(List<int> shipmentIds)
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
        var loadsByLine = loadItems.GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.LoadDate).ToList());

        foreach (var c in clearances.GroupBy(x => x.ShipmentId).Select(g => g.First()))
        {
            var release = releaseByClearance.GetValueOrDefault(c.Id);
            var shipLines = lines.Where(l => l.ShipmentId == c.ShipmentId).ToList();
            var total = shipLines.Sum(l => l.QtyInBl);

            decimal loaded = 0;
            DateOnly? firstLoad = null;
            DateOnly? lastLineDone = null;
            var everyLineDone = shipLines.Any(l => l.QtyInBl > 0);

            foreach (var line in shipLines)
            {
                var items = loadsByLine.GetValueOrDefault(line.Id) ?? new();
                decimal cumulative = 0;
                DateOnly? lineDoneOn = null;
                foreach (var it in items)
                {
                    cumulative += it.Qty;
                    if (firstLoad is null || it.LoadDate < firstLoad) firstLoad = it.LoadDate;
                    if (lineDoneOn is null && cumulative >= line.QtyInBl) lineDoneOn = it.LoadDate;
                }
                loaded += cumulative;

                if (line.QtyInBl <= 0) continue;
                if (lineDoneOn is null) { everyLineDone = false; continue; }
                if (lastLineDone is null || lineDoneOn > lastLineDone) lastLineDone = lineDoneOn;
            }

            result[c.ShipmentId] = new DispatchInfo(c.ShipmentId, release, firstLoad, everyLineDone ? lastLineDone : null, total, loaded);
        }
        return result;
    }
}
