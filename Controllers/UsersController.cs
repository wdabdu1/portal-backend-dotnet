using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record BuAccessRow(int BusinessUnitId, string BusinessUnitName, string AccessLevel);
public record UserSummary(string Id, string Email, string DisplayName, string Role, bool IsActive, List<BuAccessRow> BusinessUnits);
public record CreateUserRequest(string Email, string DisplayName, string Password, string Role, List<BuAccessInput> BusinessUnits);
public record BuAccessInput(int BusinessUnitId, string AccessLevel);
public record UpdateUserRolesRequest(string Role, List<BuAccessInput> BusinessUnits);

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

            result.Add(new UserSummary(user.Id, user.Email ?? "", user.DisplayName, roles.FirstOrDefault() ?? "", !user.LockoutEnabled || user.LockoutEnd == null, access));
        }

        return Ok(result.OrderBy(u => u.Email).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<UserSummary>> Create(CreateUserRequest req)
    {
        if (!AppRoles.All.Contains(req.Role)) return BadRequest(new { message = "Invalid role." });

        var user = new ApplicationUser { UserName = req.Email, Email = req.Email, DisplayName = req.DisplayName, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded) return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        await _userManager.AddToRoleAsync(user, req.Role);

        foreach (var bu in req.BusinessUnits)
        {
            _db.UserBusinessUnitAccess.Add(new UserBusinessUnitAccess
            {
                UserId = user.Id,
                BusinessUnitId = bu.BusinessUnitId,
                AccessLevel = Enum.Parse<BuAccessLevel>(bu.AccessLevel)
            });
        }
        await _db.SaveChangesAsync();

        var access = req.BusinessUnits.Count == 0 ? new List<BuAccessRow>() :
            await _db.UserBusinessUnitAccess.Where(a => a.UserId == user.Id).Include(a => a.BusinessUnit)
                .Select(a => new BuAccessRow(a.BusinessUnitId, a.BusinessUnit!.Name, a.AccessLevel.ToString())).ToListAsync();

        return Ok(new UserSummary(user.Id, user.Email ?? "", user.DisplayName, req.Role, true, access));
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
                AccessLevel = Enum.Parse<BuAccessLevel>(bu.AccessLevel)
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

        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await _userManager.UpdateAsync(user);
        return NoContent();
    }

    [HttpPost("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.LockoutEnabled = false;
        user.LockoutEnd = null;
        await _userManager.UpdateAsync(user);
        return NoContent();
    }
}
