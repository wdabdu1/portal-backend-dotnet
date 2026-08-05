using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;

namespace ShippingPortal.Api.Services;

public class SectionLockService
{
    private readonly ShippingPortalDbContext _db;
    public SectionLockService(ShippingPortalDbContext db) => _db = db;

    public async Task<bool> IsLockedAsync(string entityType, int entityId, string sectionKey)
    {
        return await _db.SectionLocks.AnyAsync(l =>
            l.EntityType == entityType && l.EntityId == entityId && l.SectionKey == sectionKey);
    }

    // Call as the first line of any section-save endpoint. Returns a
    // Conflict result if locked, or null if the save should proceed.
    public async Task<ActionResult?> EnsureNotLockedAsync(string entityType, int entityId, string sectionKey)
    {
        if (await IsLockedAsync(entityType, entityId, sectionKey))
            return new ObjectResult(new { message = "This section is confirmed and locked. A Manager or SuperUser must unlock it before it can be edited." }) { StatusCode = 409 };
        return null;
    }
}
