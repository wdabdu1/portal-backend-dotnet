using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Settings;

public record ClearanceSlaUpdateRequest(int TargetDays);

[ApiController]
[Authorize]
[Route("api/settings/clearance-sla-settings")]
public class ClearanceSlaSettingsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceSlaSettingsController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClearanceSlaSetting>>> GetAll()
        => await _db.ClearanceSlaSettings.OrderBy(s => s.Id).ToListAsync();

    // Only the target-day count is editable — the milestone list itself is
    // fixed (seeded), not user-defined.
    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> UpdateTargetDays(int id, ClearanceSlaUpdateRequest req)
    {
        var setting = await _db.ClearanceSlaSettings.FindAsync(id);
        if (setting is null) return NotFound();

        setting.TargetDays = req.TargetDays;
        await _db.SaveChangesAsync();
        return Ok(setting);
    }
}
