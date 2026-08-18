using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record BuAccessRow(int BusinessUnitId, string BusinessUnitName, string AccessLevel);
public record UserSummary(string Id, string Username, string Email, string DisplayName, string Role, bool IsActive, List<BuAccessRow> BusinessUnits);
public record UpdateUserRolesRequest(string Role, List<CreateUserBuAccess> BusinessUnits);
public record UpdateUsernameRequest(string Username);
public record UpdateDisplayNameRequest(string DisplayName);

[ApiController]
[Authorize(Roles = AppRoles.SuperUser)]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ShippingPortalDbContext _db;

    public UsersController(UserManager<ApplicationUser> userManager, ShippingPortalDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserSummary>>> GetAll()
    {
        var users = await _userManager.Users.ToListAsync();
        var result = new List<UserSummary>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var access = await _db.UserBusinessUnitAccess
                .Where(a => a.UserId == user.Id)
                .Include(a => a.BusinessUnit)
                .Select(a => new BuAccessRow(a.BusinessUnitId, a.BusinessUnit!.Name, a.AccessLevel.ToString()))
                .ToListAsync();

            result.Add(new UserSummary(user.Id, user.UserName ?? "", user.Email ?? "", user.DisplayName, roles.FirstOrDefault() ?? "", user.IsActive, access));
        }

        return Ok(result.OrderBy(u => u.Email).ToList());
    }

// Uses SetUserNameAsync rather than setting UserName directly —
    // Identity also maintains a separate normalized-username index for
    // uniqueness/lookup, and only this method keeps both in sync.
    [HttpPut("{id}/username")]
    public async Task<IActionResult> UpdateUsername(string id, UpdateUsernameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username)) return BadRequest(new { message = "Username cannot be empty." });

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var existing = await _userManager.FindByNameAsync(req.Username);
        if (existing is not null && existing.Id != id) return BadRequest(new { message = "That username is already taken." });

        var result = await _userManager.SetUserNameAsync(user, req.Username);
        if (!result.Succeeded) return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }

[HttpPut("{id}/display-name")]
    public async Task<IActionResult> UpdateDisplayName(string id, UpdateDisplayNameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName)) return BadRequest(new { message = "Display name cannot be empty." });

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.DisplayName = req.DisplayName;
        await _userManager.UpdateAsync(user);

        return NoContent();
    }

    // Instantly invalidates every token already issued to this user —
    // the actual response to a stolen device or offboarding, since it
    // doesn't wait for the token's own natural expiry.
    [HttpPost("{id}/revoke-sessions")]

    public async Task<IActionResult> RevokeSessions(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.SessionVersion += 1;
        await _userManager.UpdateAsync(user);

        return NoContent();
    }

    [HttpPut("{id}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, UpdateUserRolesRequest req)
    {
        if (!AppRoles.All.Contains(req.Role)) return BadRequest(new { message = "Invalid role." });

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, req.Role);

        var existingAccess = await _db.UserBusinessUnitAccess.Where(a => a.UserId == id).ToListAsync();
        _db.UserBusinessUnitAccess.RemoveRange(existingAccess);

        foreach (var bu in req.BusinessUnits)
        {
            _db.UserBusinessUnitAccess.Add(new UserBusinessUnitAccess
            {
                UserId = id,
                BusinessUnitId = bu.BusinessUnitId,
                AccessLevel = bu.AccessLevel
            });
        }
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = false;
        await _userManager.UpdateAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = true;
        await _userManager.UpdateAsync(user);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var access = await _db.UserBusinessUnitAccess.Where(a => a.UserId == id).ToListAsync();
        _db.UserBusinessUnitAccess.RemoveRange(access);
        await _db.SaveChangesAsync();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded) return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }
}
