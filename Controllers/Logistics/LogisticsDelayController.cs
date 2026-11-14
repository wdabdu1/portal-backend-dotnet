using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers.Logistics;

public record DelayEntryDto(
    int Id, int ShipmentId, int? WithdrawalId, int ReasonId, string Reason,
    DateOnly FromDate, DateOnly? ToDate, int Days, bool Ongoing, string? Note, string CreatedBy, DateTime CreatedAt);

public record SaveDelayRequest(int ShipmentId, int? WithdrawalId, int ReasonId, DateOnly FromDate, DateOnly? ToDate, string? Note);

public record DelayReasonTotal(int ReasonId, string Reason, int Entries, int Shipments, int Days);

public record DelaySummaryEntry(
    int Id, int ShipmentId, string BlAwbNo, string BusinessUnit, string Consignee, string Reason,
    DateOnly FromDate, DateOnly? ToDate, int Days, bool Ongoing, string? Note, string CreatedBy);

// Release (SPC Bill settlement) -> whole shipment on trucks, in business days.
// LoggedDays = delay-log days for that shipment, capped at Days: they explain
// the wait, they are never added on top of it.
public record DispatchRow(
    int ShipmentId, string BlAwbNo, string BusinessUnit, string Consignee,
    DateOnly ReleaseDate, DateOnly? FirstLoadDate, DateOnly? FullyDispatchedDate,
    int Days, bool Dispatched, int TargetDays, int Gap, int LoggedDays, int UnexplainedDays,
    decimal TotalQty, decimal LoadedQty);

// Delivery performance for the logistics operation: calendar days from truck
// load date to actual drop-off, against the city's own target.
public record CityDeliveryPerformance(string City, int? TargetDays, int Drops, double AvgDays, int OnTime, int Late);

public record DelaySummaryResponse(
    int DispatchTargetDays,
    List<DelayReasonTotal> Reasons, List<DelaySummaryEntry> Entries,
    List<DispatchRow> Dispatch, List<CityDeliveryPerformance> Delivery);

[ApiController]
[Authorize(Roles = AppRoles.DelayLogViewers)]
[Route("api/logistics/delays")]
public class LogisticsDelayController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly LogisticsDispatchService _dispatch;
    public LogisticsDelayController(ShippingPortalDbContext db, LogisticsDispatchService dispatch)
    {
        _db = db;
        _dispatch = dispatch;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<string> CurrentUserNameAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var name = await _db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(name) ? (User.Identity?.Name ?? "") : name;
    }

    private static DelayEntryDto ToDto(LogisticsDelayLog d, HashSet<DateOnly> holidays) =>
        new(d.Id, d.ShipmentId, d.WithdrawalId, d.ReasonId, d.Reason?.Name ?? "",
            d.FromDate, d.ToDate,
            LogisticsDispatchService.InclusiveBusinessDays(d.FromDate, d.ToDate ?? Today, holidays),
            !d.ToDate.HasValue, d.Note, d.CreatedBy, d.CreatedAt);

    // All entries of one shipment (newest first).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DelayEntryDto>>> GetForShipment([FromQuery] int shipmentId)
    {
        var holidays = await _dispatch.GetHolidaysAsync();
        var list = await _db.LogisticsDelayLogs.Include(d => d.Reason)
            .Where(d => d.ShipmentId == shipmentId)
            .OrderByDescending(d => d.FromDate).ThenByDescending(d => d.Id)
            .ToListAsync();
        return Ok(list.Select(d => ToDto(d, holidays)));
    }

    // Active reasons for the dropdown (everyone who can read the log).
    [HttpGet("reasons")]
    public async Task<ActionResult<IEnumerable<object>>> GetReasons()
    {
        var reasons = await _db.LogisticsDelayReasons.Where(r => r.IsActive).OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name }).ToListAsync();
        return Ok(reasons);
    }

    private async Task<string?> ValidateAsync(SaveDelayRequest req)
    {
        if (req.ToDate.HasValue && req.ToDate.Value < req.FromDate) return "The 'to' date cannot be before the 'from' date.";
        if (!await _db.Shipments.AnyAsync(s => s.Id == req.ShipmentId)) return "Shipment not found.";
        if (!await _db.LogisticsDelayReasons.AnyAsync(r => r.Id == req.ReasonId && r.IsActive)) return "Choose a delay reason.";
        if ((req.Note?.Trim().Length ?? 0) > 500) return "The note can be at most 500 characters.";
        return null;
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.DelayLogEditors)]
    public async Task<ActionResult<DelayEntryDto>> Create(SaveDelayRequest req)
    {
        var error = await ValidateAsync(req);
        if (error is not null) return BadRequest(new { message = error });

        var entry = new LogisticsDelayLog
        {
            ShipmentId = req.ShipmentId, WithdrawalId = req.WithdrawalId, ReasonId = req.ReasonId,
            FromDate = req.FromDate, ToDate = req.ToDate,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedBy = await CurrentUserNameAsync(), CreatedAt = DateTime.UtcNow
        };
        _db.LogisticsDelayLogs.Add(entry);
        await _db.SaveChangesAsync();

        var saved = await _db.LogisticsDelayLogs.Include(d => d.Reason).FirstAsync(d => d.Id == entry.Id);
        return Ok(ToDto(saved, await _dispatch.GetHolidaysAsync()));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.DelayLogEditors)]
    public async Task<ActionResult<DelayEntryDto>> Update(int id, SaveDelayRequest req)
    {
        var entry = await _db.LogisticsDelayLogs.FirstOrDefaultAsync(d => d.Id == id);
        if (entry is null) return NotFound();

        var error = await ValidateAsync(req with { ShipmentId = entry.ShipmentId });
        if (error is not null) return BadRequest(new { message = error });

        entry.ReasonId = req.ReasonId;
        entry.FromDate = req.FromDate;
        entry.ToDate = req.ToDate;
        entry.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        entry.UpdatedBy = await CurrentUserNameAsync();
        entry.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await _db.LogisticsDelayLogs.Include(d => d.Reason).FirstAsync(d => d.Id == id);
        return Ok(ToDto(saved, await _dispatch.GetHolidaysAsync()));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.DelayLogEditors)]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.LogisticsDelayLogs.FirstOrDefaultAsync(d => d.Id == id);
        if (entry is null) return NotFound();
        _db.LogisticsDelayLogs.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Delay Summary page: days lost per reason, every logged entry, the
    // release -> fully-dispatched wait per shipment, and delivery performance by city.
    // from/to filter delay entries by their from-date, dispatch rows by release date,
    // and delivery drops by actual drop-off date.
    [HttpGet("summary")]
    public async Task<ActionResult<DelaySummaryResponse>> GetSummary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var holidays = await _dispatch.GetHolidaysAsync();
        var target = await _dispatch.GetTargetDaysAsync();
        var today = Today;

        // --- Delay entries ---
        var logQuery = _db.LogisticsDelayLogs.Include(d => d.Reason).Include(d => d.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(d => d.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(p => p!.Consignee)
            .AsQueryable();
        if (from.HasValue) logQuery = logQuery.Where(d => d.FromDate >= from.Value);
        if (to.HasValue) logQuery = logQuery.Where(d => d.FromDate <= to.Value);
        var logs = await logQuery.OrderByDescending(d => d.FromDate).ThenByDescending(d => d.Id).ToListAsync();

        var entries = logs.Select(d => new DelaySummaryEntry(
            d.Id, d.ShipmentId, d.Shipment?.BlAwbNo ?? "",
            d.Shipment?.PurchaseOrder?.BusinessUnit?.Name ?? "", d.Shipment?.PurchaseOrder?.Consignee?.Name ?? "",
            d.Reason?.Name ?? "", d.FromDate, d.ToDate,
            LogisticsDispatchService.InclusiveBusinessDays(d.FromDate, d.ToDate ?? today, holidays),
            !d.ToDate.HasValue, d.Note, d.CreatedBy)).ToList();

        var reasons = entries
            .GroupBy(e => e.Reason)
            .Select(g => new DelayReasonTotal(
                logs.First(l => (l.Reason?.Name ?? "") == g.Key).ReasonId, g.Key,
                g.Count(), g.Select(e => e.ShipmentId).Distinct().Count(), g.Sum(e => e.Days)))
            .OrderByDescending(r => r.Days)
            .ToList();

        // --- Dispatch wait (Route 1 shipments Clearance has released) ---
        var releasedClearances = await _db.ClearanceRoute1Details
            .Where(r => r.SpcBillSettlementDate != null
                && (!from.HasValue || r.SpcBillSettlementDate >= from.Value)
                && (!to.HasValue || r.SpcBillSettlementDate <= to.Value))
            .Select(r => r.ClearanceId)
            .ToListAsync();
        var shipmentIds = await _db.Clearances.Where(c => releasedClearances.Contains(c.Id)).Select(c => c.ShipmentId).ToListAsync();
        var dispatchInfo = await _dispatch.GetForPortShipmentsAsync(shipmentIds);

        var shipmentInfo = await _db.Shipments
            .Where(s => shipmentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.BlAwbNo, BusinessUnit = s.PurchaseOrder!.BusinessUnit!.Name, Consignee = s.PurchaseOrder!.Consignee!.Name })
            .ToListAsync();

        var loggedByShipment = await _db.LogisticsDelayLogs
            .Where(d => shipmentIds.Contains(d.ShipmentId))
            .Select(d => new { d.ShipmentId, d.FromDate, d.ToDate })
            .ToListAsync();

        var dispatchRows = new List<DispatchRow>();
        foreach (var s in shipmentInfo)
        {
            var info = dispatchInfo.GetValueOrDefault(s.Id);
            if (info?.ReleaseDate is null) continue;
            var release = info.ReleaseDate.Value;
            var end = info.FullyDispatchedDate ?? today;
            var days = Math.Max(0, ClearanceScheduleService.BusinessDaysBetween(release, end, holidays));
            var logged = loggedByShipment.Where(l => l.ShipmentId == s.Id)
                .Sum(l => LogisticsDispatchService.InclusiveBusinessDays(l.FromDate, l.ToDate ?? today, holidays));
            logged = Math.Min(logged, days);
            dispatchRows.Add(new DispatchRow(
                s.Id, s.BlAwbNo, s.BusinessUnit ?? "", s.Consignee ?? "",
                release, info.FirstLoadDate, info.FullyDispatchedDate,
                days, info.FullyDispatchedDate.HasValue, target, days - target, logged, days - logged,
                info.TotalQty, info.LoadedQty));
        }
        dispatchRows = dispatchRows.OrderByDescending(r => r.Gap).ThenBy(r => r.ReleaseDate).ToList();

        // --- Delivery performance by city (calendar days, load -> drop-off) ---
        var dropQuery = _db.TruckLoadDrops
            .Include(d => d.TruckLoad)
            .Include(d => d.Warehouse).ThenInclude(w => w!.City)
            .Where(d => d.ActualDropOffDate != null);
        if (from.HasValue) dropQuery = dropQuery.Where(d => d.ActualDropOffDate >= from.Value);
        if (to.HasValue) dropQuery = dropQuery.Where(d => d.ActualDropOffDate <= to.Value);
        var drops = await dropQuery.ToListAsync();

        var delivery = drops
            .Where(d => d.TruckLoad != null)
            .Select(d => new
            {
                City = d.Warehouse?.City?.Name ?? "No City Set",
                Target = d.Warehouse?.City?.TargetDeliveryDays,
                Days = d.ActualDropOffDate!.Value.DayNumber - d.TruckLoad!.LoadDate.DayNumber
            })
            .GroupBy(x => x.City)
            .Select(g =>
            {
                var targetDays = g.First().Target;
                var onTime = targetDays.HasValue ? g.Count(x => x.Days <= targetDays.Value) : 0;
                var late = targetDays.HasValue ? g.Count(x => x.Days > targetDays.Value) : 0;
                return new CityDeliveryPerformance(g.Key, targetDays, g.Count(), Math.Round(g.Average(x => (double)x.Days), 1), onTime, late);
            })
            .OrderBy(c => c.City)
            .ToList();

        return Ok(new DelaySummaryResponse(target, reasons, entries, dispatchRows, delivery));
    }
}
