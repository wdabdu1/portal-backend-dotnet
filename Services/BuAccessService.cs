using System.Security.Claims;

namespace ShippingPortal.Api.Services;

// Central rule for "which Business Unit IDs can this user see":
// Manager / SuperUser / Clearance bypass entirely (see all BUs).
// Everyone else (Standard) is limited to their assigned BUs, read from
// the "bu" claims baked into their JWT at login (format "buId:AccessLevel").
public class BuAccessService
{
    public bool SeesAllBus(ClaimsPrincipal user)
    {
        // Anyone NOT in the BU-scoped role list sees all BUs.
        return !Models.Identity.AppRoles.BuScopedRoles.Any(user.IsInRole);
    }

    public HashSet<int> GetAllowedBusinessUnitIds(ClaimsPrincipal user)
    {
        return user.Claims
            .Where(c => c.Type == "bu")
            .Select(c => c.Value.Split(':')[0])
            .Where(id => int.TryParse(id, out _))
            .Select(int.Parse)
            .ToHashSet();
    }

    public bool CanSeeBusinessUnit(ClaimsPrincipal user, int businessUnitId)
    {
        if (SeesAllBus(user)) return true;
        return GetAllowedBusinessUnitIds(user).Contains(businessUnitId);
    }

    // Non-BU-scoped roles (Manager/SuperUser/Treasury/etc.) bypass entirely.
    // BU-scoped roles (IP_User/IP_Supervisor/BU) must specifically have
    // ReadWrite on that exact BU — Read-only access never permits writes.
    public bool CanWriteBusinessUnit(ClaimsPrincipal user, int businessUnitId)
    {
        if (SeesAllBus(user)) return true;

        foreach (var claim in user.Claims.Where(c => c.Type == "bu"))
        {
            var parts = claim.Value.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var id) && id == businessUnitId)
            {
                return parts[1] == "ReadWrite";
            }
        }
        return false;
    }
}
