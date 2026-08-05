using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record SectionLockInfo(string SectionKey, string ConfirmedByUserId, string ConfirmedByName, DateTime ConfirmedAt);
public record ConfirmSectionRequest(string EntityType, int EntityId, string SectionKey);
public record UnlockSectionRequest(string EntityType, int EntityId, string SectionKey);

[ApiController]
[Authorize]
[Route("api/section-locks")]
public class SectionLocksController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SectionLocksController(ShippingPortalDbContext db) => _db = db;

    // Returns every locked section for one entity (e.g. all locked sections
    // on Shipment #6), so the frontend can render lock badges in one call.
    [HttpGet("{entityType}/{entityId:int}")]
    public async Task<ActionResult<IEnumerable<SectionLockInfo>>> GetLocks(string entityType, int entityId)
    {
        var locks = await _db.SectionLocks
            .Where(l => l.EntityType == entityType && l.EntityId == entityId)
            .ToListAsync();

        var userIds = locks.Select(l => l.ConfirmedByUserId).Distinct().ToList();
        var users = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return Ok(locks.Select(l => new SectionLockInfo(
            l.SectionKey, l.ConfirmedByUserId, users.GetValueOrDefault(l.ConfirmedByUserId, "Unknown"), l.ConfirmedAt
        )).ToList());
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmSectionRequest req)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Unauthorized();

        var existing = await _db.SectionLocks.FirstOrDefaultAsync(l =>
            l.EntityType == req.EntityType && l.EntityId == req.EntityId && l.SectionKey == req.SectionKey);
        if (existing is not null) return Ok(); // already locked, idempotent

        _db.SectionLocks.Add(new SectionLock
        {
            EntityType = req.EntityType,
            EntityId = req.EntityId,
            SectionKey = req.SectionKey,
            ConfirmedByUserId = userId
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("unlock")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Unlock(UnlockSectionRequest req)
    {
        var existing = await _db.SectionLocks.FirstOrDefaultAsync(l =>
            l.EntityType == req.EntityType && l.EntityId == req.EntityId && l.SectionKey == req.SectionKey);
        if (existing is null) return NotFound();

        _db.SectionLocks.Remove(existing);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
