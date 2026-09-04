using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.CPricing;

// C_Type — always created against an existing C_Cat (CPricingCategoryId is
// required, not nullable, on the model itself). GET supports an optional
// ?categoryId= filter so the working table's C_Type dropdown can be
// re-populated to just that category's types once a C_Cat is picked
// (cascading dropdown pair) — omit it to get every C_Type, used by the
// Settings mini-page's own listing.
[ApiController]
[Authorize(Roles = AppRoles.CPricingUsers)]
[Route("api/c-pricing/types")]
public class CPricingTypesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public CPricingTypesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CPricingType>>> GetAll([FromQuery] int? categoryId)
    {
        var query = _db.CPricingTypes.AsQueryable();
        if (categoryId.HasValue) query = query.Where(t => t.CPricingCategoryId == categoryId.Value);
        return await query.OrderBy(t => t.Name).ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<CPricingType>> Create(CPricingType entity)
    {
        entity.Id = 0;
        var categoryExists = await _db.CPricingCategories.AnyAsync(c => c.Id == entity.CPricingCategoryId);
        if (!categoryExists) return BadRequest(new { message = "Select a C_Cat first — a C_Type must belong to one." });

        _db.CPricingTypes.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CPricingType req)
    {
        var existing = await _db.CPricingTypes.FindAsync(id);
        if (existing is null) return NotFound();

        var categoryExists = await _db.CPricingCategories.AnyAsync(c => c.Id == req.CPricingCategoryId);
        if (!categoryExists) return BadRequest(new { message = "Select a C_Cat first — a C_Type must belong to one." });

        existing.Name = req.Name;
        existing.CPricingCategoryId = req.CPricingCategoryId;
        existing.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.CPricingTypes.FindAsync(id);
        if (entity is null) return NotFound();

        _db.CPricingTypes.Remove(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This C_Type is in use and can't be deleted." });
        }
        return NoContent();
    }
}
