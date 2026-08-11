using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers;

public record ProfileResponse(string Email, string DisplayName);
public record UpdateProfileRequest(string DisplayName);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    public ProfileController(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    [HttpGet]
    public async Task<ActionResult<ProfileResponse>> Get()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        return Ok(new ProfileResponse(user.Email ?? "", user.DisplayName));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdateProfileRequest req)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.DisplayName)) return BadRequest(new { message = "Display name cannot be empty." });

        user.DisplayName = req.DisplayName.Trim();
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded) return BadRequest(new { message = string.Join("; ", result.Errors.Select(e => e.Description)) });

        return NoContent();
    }
}
