using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

namespace ShippingPortal.Api.Controllers;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string DisplayName, IList<string> Roles);
public record CreateUserRequest(string Email, string Password, string DisplayName, string Role, List<CreateUserBuAccess> BusinessUnitAccess);
public record CreateUserBuAccess(int BusinessUnitId, AccessLevel AccessLevel);

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ShippingPortalDbContext _db;
    private readonly TokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ShippingPortalDbContext db, TokenService tokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);
        var buAccess = await _db.UserBusinessUnitAccess.Where(a => a.UserId == user.Id).ToListAsync();

        var token = _tokenService.CreateToken(user, roles, buAccess);
        return Ok(new LoginResponse(token, user.DisplayName, roles));
    }

    [HttpPost("users")]
    [Authorize(Roles = AppRoles.SuperUser)]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName, IsActive = true };
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded) return BadRequest(createResult.Errors);

        if (!AppRoles.All.Contains(request.Role)) return BadRequest(new { message = $"Unknown role '{request.Role}'." });
        await _userManager.AddToRoleAsync(user, request.Role);

        foreach (var access in request.BusinessUnitAccess)
        {
            _db.UserBusinessUnitAccess.Add(new UserBusinessUnitAccess
            {
                UserId = user.Id,
                BusinessUnitId = access.BusinessUnitId,
                AccessLevel = access.AccessLevel
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Email, user.DisplayName });
    }
}
