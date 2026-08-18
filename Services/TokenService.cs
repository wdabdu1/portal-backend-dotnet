using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Services;

public class TokenService
{
    private readonly IConfiguration _config;
    public TokenService(IConfiguration config) => _config = config;

    public string CreateToken(ApplicationUser user, IList<string> roles, IEnumerable<UserBusinessUnitAccess> buAccess)
    {
        var jwtKey = _config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is not configured.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new("displayName", user.DisplayName),
            new("sessionVersion", user.SessionVersion.ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(buAccess.Select(a => new Claim("bu", $"{a.BusinessUnitId}:{a.AccessLevel}")));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["JWT_ISSUER"] ?? "ShippingPortal.Api",
            audience: _config["JWT_AUDIENCE"] ?? "ShippingPortal.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
