using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Settings;

[ApiController]
[Authorize]
public abstract class LookupCrudController<TEntity> : ControllerBase where TEntity : class
{
    protected readonly ShippingPortalDbContext Db;
    protected LookupCrudController(ShippingPortalDbContext db) => Db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TEntity>>> GetAll() => await Db.Set<TEntity>().ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TEntity>> GetById(int id)
    {
        var entity = await Db.Set<TEntity>().FindAsync(id);
        return entity is null ? NotFound() : entity;
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public virtual async Task<ActionResult<TEntity>> Create(TEntity entity)
    {
        Db.Set<TEntity>().Add(entity);
        await Db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public virtual async Task<IActionResult> Update(int id, [FromBody] JsonElement raw)
    {
        var existing = await Db.Set<TEntity>().FindAsync(id);
        if (existing is null) return NotFound();

        // Bind manually from the same raw JSON we inspect below, rather
        // than taking a second, separately-bound TEntity parameter —
        // Kestrel's request stream can only be read once, so there's no
        // reliable way to both let MVC model-bind an entity AND inspect
        // the raw body afterward.
        var entity = JsonSerializer.Deserialize<TEntity>(raw.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (entity is null) return BadRequest();

        // The request body only ever carries the fields actually being
        // edited (e.g. a partial payload of just Name + TariffGroupId
        // from Simple Lookup's inline row editor) — never Id, and often
        // not IsActive either. Blindly applying SetValues() with
        // whatever happened to be in the body risks two failure modes:
        // a missing Id deserializes to 0, which EF refuses to apply as
        // a primary-key change on an already-tracked entity (the exact
        // cause of "Could not update this entry"), and a missing
        // IsActive would silently flip every edited row inactive. Force
        // the route's id regardless, and only touch IsActive if it was
        // genuinely present in the payload.
        var isActiveSupplied = raw.TryGetProperty("isActive", out _);
        var isActiveProp = typeof(TEntity).GetProperty("IsActive");
        var originalIsActive = isActiveProp?.GetValue(existing);

        Db.Entry(existing).CurrentValues.SetValues(entity);

        typeof(TEntity).GetProperty("Id")?.SetValue(existing, id);
        if (!isActiveSupplied && isActiveProp is not null) isActiveProp.SetValue(existing, originalIsActive);

        await Db.SaveChangesAsync();
        return NoContent();
    }
    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public virtual async Task<IActionResult> Delete(int id)
    {
        var entity = await Db.Set<TEntity>().FindAsync(id);
        if (entity is null) return NotFound();

        Db.Set<TEntity>().Remove(entity);
        try
        {
            await Db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This entry is in use and can't be deleted." });
        }
        return NoContent();
    }
}
