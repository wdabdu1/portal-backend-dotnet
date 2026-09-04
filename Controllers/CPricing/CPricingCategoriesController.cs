using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.CPricing;

// C_Cat — the parent half of the C_Cat/C_Type pair. Managed from the C
// Pricing "Settings" mini-page, not the general Settings admin screens —
// open to the CPricing role itself (plus Manager/SuperUser), unlike the
// generic LookupCrudController<T> which restricts writes to Manager/SuperUser.
[ApiController]
[Authorize(Roles = AppRoles.CPricingUsers)]
[Route("api/c-pricing/categories")]
public class CPricingCategoriesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public CPricingCategoriesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CPricingCategory>>> GetAll() =>
        await _db.CPricingCategories.OrderBy(c => c.Name).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<CPricingCategory>> Create(CPricingCategory entity)
    {
        entity.Id = 0;
        _db.CPricingCategories.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CPricingCategory req)
    {
        var existing = await _db.CPricingCategories.FindAsync(id);
        if (existing is null) return NotFound();
        existing.Name = req.Name;
        existing.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.CPricingCategories.FindAsync(id);
        if (entity is null) return NotFound();

        _db.CPricingCategories.Remove(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This C_Cat is in use (has a C_Type or a saved item) and can't be deleted." });
        }
        return NoContent();
    }
}
