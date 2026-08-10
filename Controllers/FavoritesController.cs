using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;

namespace ShippingPortal.Api.Controllers;

public record FavoriteResponse(int Id, string Label, string Route, int SortOrder);
public record AddFavoriteRequest(string Label, string Route);

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public FavoritesController(ShippingPortalDbContext db) => _db = db;

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FavoriteResponse>>> GetAll()
    {
        var userId = CurrentUserId;
        var favorites = await _db.UserFavorites
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FavoriteResponse(f.Id, f.Label, f.Route, f.SortOrder))
            .ToListAsync();
        return Ok(favorites);
    }

    [HttpPost]
    public async Task<ActionResult<FavoriteResponse>> Add(AddFavoriteRequest req)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        // Same item can't be pinned twice.
        var existing = await _db.UserFavorites.FirstOrDefaultAsync(f => f.UserId == userId && f.Route == req.Route && f.Label == req.Label);
        if (existing is not null) return Ok(new FavoriteResponse(existing.Id, existing.Label, existing.Route, existing.SortOrder));

        var maxOrder = await _db.UserFavorites.Where(f => f.UserId == userId).Select(f => (int?)f.SortOrder).MaxAsync() ?? -1;
        var favorite = new UserFavorite { UserId = userId, Label = req.Label, Route = req.Route, SortOrder = maxOrder + 1 };
        _db.UserFavorites.Add(favorite);
        await _db.SaveChangesAsync();
        return Ok(new FavoriteResponse(favorite.Id, favorite.Label, favorite.Route, favorite.SortOrder));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = CurrentUserId;
        var favorite = await _db.UserFavorites.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (favorite is null) return NotFound();

        _db.UserFavorites.Remove(favorite);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
