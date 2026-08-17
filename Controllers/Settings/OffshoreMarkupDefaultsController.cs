using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Settings;

public record OffshoreMarkupDefaultRow(int BusinessPartnerId, string BusinessPartnerName, decimal DefaultMarkupPercent, int DefaultCurrencyId, string DefaultCurrencyCode);
public record SaveOffshoreMarkupDefaultRequest(int BusinessPartnerId, decimal DefaultMarkupPercent, int DefaultCurrencyId);

[ApiController]
[Route("api/settings/offshore-markup-defaults")]
[Authorize]
public class OffshoreMarkupDefaultsController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public OffshoreMarkupDefaultsController(ShippingPortalDbContext db) => _db = db;

    // Every offshore company, whether or not a default has been set
    // yet — unset ones simply show 0% and USD, so Finance can see at a
    // glance which offshores still need a default configured.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OffshoreMarkupDefaultRow>>> GetAll()
    {
        var usdId = await _db.Currencies.Where(c => c.Code == "USD").Select(c => c.Id).FirstOrDefaultAsync();

        var offshores = await _db.BusinessPartners
            .Where(p => p.IsOffshoreEntity && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var defaults = await _db.OffshoreMarkupDefaults
            .Include(d => d.DefaultCurrency)
            .ToDictionaryAsync(d => d.BusinessPartnerId);

        var rows = offshores.Select(p =>
        {
            if (defaults.TryGetValue(p.Id, out var existing))
                return new OffshoreMarkupDefaultRow(p.Id, p.Name, existing.DefaultMarkupPercent, existing.DefaultCurrencyId, existing.DefaultCurrency?.Code ?? "");
            return new OffshoreMarkupDefaultRow(p.Id, p.Name, 0, usdId, "USD");
        }).ToList();

        return Ok(rows);
    }

    [HttpPut]
    [Authorize(Roles = AppRoles.CorpFinance + "," + AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Save(SaveOffshoreMarkupDefaultRequest req)
    {
        var entity = await _db.OffshoreMarkupDefaults.FirstOrDefaultAsync(d => d.BusinessPartnerId == req.BusinessPartnerId);
        if (entity is null)
        {
            entity = new OffshoreMarkupDefault { BusinessPartnerId = req.BusinessPartnerId };
            _db.OffshoreMarkupDefaults.Add(entity);
        }

        entity.DefaultMarkupPercent = req.DefaultMarkupPercent;
        entity.DefaultCurrencyId = req.DefaultCurrencyId;

        await _db.SaveChangesAsync();
        return Ok();
    }
}
