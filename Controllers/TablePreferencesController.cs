using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;

namespace ShippingPortal.Api.Controllers;

public record TableSortPreference(string SortColumn, bool SortAsc);

[ApiController]
[Authorize]
[Route("api/table-preferences")]
public class TablePreferencesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public TablePreferencesController(ShippingPortalDbContext db) => _db = db;

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet("{tableKey}")]
    public async Task<ActionResult<TableSortPreference?>> Get(string tableKey)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var pref = await _db.UserTablePreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.TableKey == tableKey);
        if (pref is null) return Ok(null);
        return Ok(new TableSortPreference(pref.SortColumn, pref.SortAsc));
    }

    [HttpPut("{tableKey}")]
    public async Task<IActionResult> Save(string tableKey, TableSortPreference req)
    {
        var userId = CurrentUserId;
        if (userId is null) return Unauthorized();

        var pref = await _db.UserTablePreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.TableKey == tableKey);
        if (pref is null)
        {
            pref = new UserTablePreference { UserId = userId, TableKey = tableKey };
            _db.UserTablePreferences.Add(pref);
        }
        pref.SortColumn = req.SortColumn;
        pref.SortAsc = req.SortAsc;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
