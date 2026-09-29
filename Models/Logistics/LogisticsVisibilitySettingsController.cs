using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Logistics;

namespace ShippingPortal.Api.Controllers.Settings;

public record LogisticsVisibilityUpdateRequest(int ArrivalLeadTimeDays, int PreClearanceCatQtyRevealDays, int PostDeliveryRehideDays);

// Moved here from Controllers/Logistics — this is a genuine Settings screen
// (Settings > Shipping & Logistics > Logistics — Reveal Settings), not a
// Logistics-box page. It used to live at /logistics/settings, inside the
// box itself, gated only by logisticsBoxGuard on the frontend and by
// AppRoles.LogisticsRevealEditors (which included Coordinator) here — that
// let Coordinator widen their own reveal window and see shipments earlier
// than the visibility design intends. Authorization now follows the same
// pattern as every other Settings screen in this folder: GET is open to
// any authenticated user, PUT is Manager/SuperUser only.
[ApiController]
[Authorize]
[Route("api/logistics/visibility-settings")]
public class LogisticsVisibilitySettingsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public LogisticsVisibilitySettingsController(ShippingPortalDbContext db) => _db = db;

    // Always exactly one row (seeded Id = 1 by migration) — get-or-create
    // defensively in case an environment never ran the seed insert.
    private async Task<LogisticsVisibilitySettings> GetOrCreateAsync()
    {
        var settings = await _db.LogisticsVisibilitySettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new LogisticsVisibilitySettings();
            _db.LogisticsVisibilitySettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    [HttpGet]
    public async Task<ActionResult<LogisticsVisibilitySettings>> Get() => await GetOrCreateAsync();

    [HttpPut]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<LogisticsVisibilitySettings>> Update(LogisticsVisibilityUpdateRequest req)
    {
        var settings = await GetOrCreateAsync();
        settings.ArrivalLeadTimeDays = req.ArrivalLeadTimeDays;
        settings.PreClearanceCatQtyRevealDays = req.PreClearanceCatQtyRevealDays;
        settings.PostDeliveryRehideDays = req.PostDeliveryRehideDays;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return settings;
    }
}
