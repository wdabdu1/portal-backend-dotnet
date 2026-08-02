using Microsoft.AspNetCore.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<UserBusinessUnitAccess> BusinessUnitAccess { get; set; }
        = new List<UserBusinessUnitAccess>();
}

public enum AccessLevel
{
    Read = 0,
    ReadWrite = 1
}

public class UserBusinessUnitAccess
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public AccessLevel AccessLevel { get; set; } = AccessLevel.Read;
}

public static class AppRoles
{
    public const string Standard = "Standard";
    public const string Manager = "Manager";       // can edit Settings
    public const string SuperUser = "SuperUser";   // creates users, can cancel transactions
    public const string Clearance = "Clearance";   // sees all BUs, but Supplier + non-last-offshore info hidden

    public static readonly string[] All = { Standard, Manager, SuperUser, Clearance };
}
