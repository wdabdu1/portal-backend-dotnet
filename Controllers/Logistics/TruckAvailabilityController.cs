using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;

namespace ShippingPortal.Api.Controllers.Logistics;

// A truck's status is never stored directly — it's computed fresh every
// time from its most recent load's last drop. "cityName" always shows
// whatever's relevant for dispatch planning: its current city if free,
// or where it's headed if mid-trip — never both, since only one matters
// at a time for "when can I use this truck".
public record TruckAvailabilityRow(int TruckId, string PlateNo, string? DriverName, bool IsAvailable, string? CityName, DateOnly? ExpectedAvailableDate);
public record TruckMovementRow(DateOnly MoveDate, string FromCity, string ToCity, string? Reason, decimal? Value, string? Notes);
public record MoveTruckRequest(int ToCityId, DateOnly MoveDate, string? Reason, decimal? Value, string? Notes);

[ApiController]
[Route("api/truck-availability")]
[Authorize(Roles = AppRoles.LogisticsViewers)]
public class TruckAvailabilityController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public TruckAvailabilityController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TruckAvailabilityRow>>> GetAll()
    {
        var trucks = await _db.Trucks.Where(t => t.IsActive).Include(t => t.Driver).Include(t => t.CurrentCity).ToListAsync();

        // For every truck, its "active" load (if any) is the one whose
        // last drop hasn't arrived yet — a truck should only ever have at
        // most one of these at a time.
        var openDrops = await _db.TruckLoadDrops
            .Include(d => d.Warehouse!).ThenInclude(w => w.City)
            .Include(d => d.TruckLoad)
            .Where(d => d.ActualDropOffDate == null)
            .ToListAsync();
        var lastDropIdByLoad = await _db.TruckLoadDrops
            .GroupBy(d => d.TruckLoadId)
            .Select(g => new { TruckLoadId = g.Key, LastDropId = g.Max(d => d.Id) })
            .ToDictionaryAsync(x => x.TruckLoadId, x => x.LastDropId);

        var activeDropByTruckId = openDrops
            .Where(d => lastDropIdByLoad.GetValueOrDefault(d.TruckLoadId) == d.Id)
            .Where(d => d.TruckLoad is not null)
            .ToDictionary(d => d.TruckLoad!.TruckId);

        var rows = trucks.Select(t =>
        {
            if (activeDropByTruckId.TryGetValue(t.Id, out var drop))
                return new TruckAvailabilityRow(t.Id, t.PlateNo, t.Driver?.Name, false, drop.Warehouse?.City?.Name, drop.ExpectedDeliveryDate);

            return new TruckAvailabilityRow(t.Id, t.PlateNo, t.Driver?.Name, t.CurrentCityId is not null, t.CurrentCity?.Name, null);
        }).OrderBy(r => r.PlateNo).ToList();

        return Ok(rows);
    }

    [HttpGet("{truckId:int}/movements")]
    public async Task<ActionResult<IEnumerable<TruckMovementRow>>> GetMovements(int truckId)
    {
        var movements = await _db.TruckMovements
            .Include(m => m.FromCity).Include(m => m.ToCity)
            .Where(m => m.TruckId == truckId)
            .OrderByDescending(m => m.MoveDate).ThenByDescending(m => m.Id)
            .ToListAsync();

        var rows = movements.Select(m => new TruckMovementRow(m.MoveDate, m.FromCity?.Name ?? "—", m.ToCity?.Name ?? "", m.Reason, m.Value, m.Notes));
        return Ok(rows);
    }

    [HttpPost("{truckId:int}/move")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> MoveTruck(int truckId, MoveTruckRequest req)
    {
        var truck = await _db.Trucks.FindAsync(truckId);
        if (truck is null) return NotFound();
        if (!await _db.LogisticsCities.AnyAsync(c => c.Id == req.ToCityId)) return BadRequest(new { message = "Destination city not found." });

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        _db.TruckMovements.Add(new TruckMovement
        {
            TruckId = truckId, FromCityId = truck.CurrentCityId, ToCityId = req.ToCityId,
            MoveDate = req.MoveDate, Reason = req.Reason, Value = req.Value, Notes = req.Notes,
            CreatedByUserId = userId
        });
        truck.CurrentCityId = req.ToCityId;

        await _db.SaveChangesAsync();
        return Ok();
    }

    // Initial placement only — no "from" city exists yet, so this isn't a
    // real move and doesn't get logged in TruckMovements, just sets where
    // the truck starts being tracked from.
    [HttpPost("{truckId:int}/set-starting-city")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> SetStartingCity(int truckId, [FromBody] int cityId)
    {
        var truck = await _db.Trucks.FindAsync(truckId);
        if (truck is null) return NotFound();
        if (!await _db.LogisticsCities.AnyAsync(c => c.Id == cityId)) return BadRequest(new { message = "City not found." });

        truck.CurrentCityId = cityId;
        await _db.SaveChangesAsync();
        return Ok();
    }
}
