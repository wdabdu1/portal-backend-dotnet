using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Settings;

public record ClearanceSlaUpdateRequest(int TargetDays);
public record RouteTotalResponse(string Division, int TotalDays);

[ApiController]
[Authorize]
[Route("api/settings/clearance-sla-settings")]
public class ClearanceSlaSettingsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ClearanceSlaSettingsController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClearanceSlaSetting>>> GetAll()
        => await _db.ClearanceSlaSettings.OrderBy(s => s.Division).ThenBy(s => s.SequenceOrder).ToListAsync();

    [HttpGet("route-totals")]
    public async Task<ActionResult<IEnumerable<RouteTotalResponse>>> GetRouteTotals()
    {
        return await _db.ClearanceSlaSettings
            .Where(s => s.IsActive)
            .GroupBy(s => s.Division)
            .Select(g => new RouteTotalResponse(g.Key, g.Sum(s => s.TargetDays)))
            .ToListAsync();
    }

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
