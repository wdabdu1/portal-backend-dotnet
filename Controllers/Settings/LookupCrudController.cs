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
    public async Task<ActionResult<TEntity>> Create(TEntity entity)
    {
        Db.Set<TEntity>().Add(entity);
        await Db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Update(int id, TEntity entity)
    {
        var existing = await Db.Set<TEntity>().FindAsync(id);
        if (existing is null) return NotFound();
        Db.Entry(existing).CurrentValues.SetValues(entity);
        await Db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> Delete(int id)
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
