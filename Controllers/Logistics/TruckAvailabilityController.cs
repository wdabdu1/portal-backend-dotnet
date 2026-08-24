using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;

namespace ShippingPortal.Api.Controllers.Logistics;

// A truck's status is never stored directly — it's computed fresh every
// time from its most recent load's last drop. "Available" means nothing
// is in progress; "InTransit" means its last drop hasn't been marked
// arrived yet. This keeps the truck's real-world state impossible to get
// out of sync with its actual deliveries.
public record TruckAvailabilityRow(
    int TruckId, string PlateNo, string? DriverName,
    string Status, // "Available" | "InTransit" | "Unplaced"
    int? CurrentCityId, string? CurrentCityName,
    int? InTransitToCityId, string? InTransitToCityName, DateOnly? ExpectedArrivalDate);

public record TruckMovementRow(int Id, string PlateNo, string? FromCityName, string ToCityName, DateOnly MoveDate, string? Reason, decimal? Value, string? Notes, string ConfirmedByName, DateTime CreatedAt);
public record MoveTruckRequest(int TruckId, int ToCityId, DateOnly MoveDate, string? Reason, decimal? Value, string? Notes);

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
            {
                return new TruckAvailabilityRow(
                    t.Id, t.PlateNo, t.Driver?.Name, "InTransit",
                    t.CurrentCityId, t.CurrentCity?.Name,
                    drop.Warehouse?.CityId, drop.Warehouse?.City?.Name, drop.ExpectedDeliveryDate);
            }
            var status = t.CurrentCityId is null ? "Unplaced" : "Available";
            return new TruckAvailabilityRow(t.Id, t.PlateNo, t.Driver?.Name, status, t.CurrentCityId, t.CurrentCity?.Name, null, null, null);
        }).OrderBy(r => r.PlateNo).ToList();

        return Ok(rows);
    }

    [HttpGet("movements")]
    public async Task<ActionResult<IEnumerable<TruckMovementRow>>> GetMovements()
    {
        var movements = await _db.TruckMovements
            .Include(m => m.Truck).Include(m => m.FromCity).Include(m => m.ToCity)
            .OrderByDescending(m => m.MoveDate).ThenByDescending(m => m.Id)
            .ToListAsync();

        var userIds = movements.Select(m => m.CreatedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var rows = movements.Select(m => new TruckMovementRow(
            m.Id, m.Truck?.PlateNo ?? "", m.FromCity?.Name, m.ToCity?.Name ?? "", m.MoveDate, m.Reason, m.Value, m.Notes,
            users.GetValueOrDefault(m.CreatedByUserId, "Unknown"), m.CreatedAt));

        return Ok(rows);
    }

    [HttpPost("move")]
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public async Task<IActionResult> MoveTruck(MoveTruckRequest req)
    {
        var truck = await _db.Trucks.FindAsync(req.TruckId);
        if (truck is null) return NotFound();
        if (!await _db.LogisticsCities.AnyAsync(c => c.Id == req.ToCityId)) return BadRequest(new { message = "Destination city not found." });

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        _db.TruckMovements.Add(new TruckMovement
        {
            TruckId = req.TruckId, FromCityId = truck.CurrentCityId, ToCityId = req.ToCityId,
            MoveDate = req.MoveDate, Reason = req.Reason, Value = req.Value, Notes = req.Notes,
            CreatedByUserId = userId
        });
        truck.CurrentCityId = req.ToCityId;

        await _db.SaveChangesAsync();
        return Ok();
    }
}
