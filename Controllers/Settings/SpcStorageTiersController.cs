using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

public record SpcStorageTierUpdateRequest(int? DurationDays, decimal Rate20, decimal Rate40);

// Fixed 3-row table (Tarif-1/2/3) — same pattern as Clearance SLA. No
// create/delete, only editing the values on the seeded rows.
[ApiController]
[Authorize]
[Route("api/settings/spc-storage-tiers")]
public class SpcStorageTiersController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public SpcStorageTiersController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SpcStorageTier>>> GetAll()
        => await _db.SpcStorageTiers.OrderBy(t => t.TierOrder).ToListAsync();

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Update(int id, SpcStorageTierUpdateRequest req)
    {
        var tier = await _db.SpcStorageTiers.FindAsync(id);
        if (tier is null) return NotFound();

        tier.DurationDays = req.DurationDays;
        tier.Rate20 = req.Rate20;
        tier.Rate40 = req.Rate40;

        await _db.SaveChangesAsync();
        return Ok(tier);
    }
}
