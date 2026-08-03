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
    public const string IpUser = "IP_User";             // Import/Procurement — full ops access, BU-scoped
    public const string IpSupervisor = "IP_Supervisor";  // same as IP_User + Clearance module edit, BU-scoped
    public const string ClrUsr = "CLR_Usr";              // Clearance team — edits Clearance module, sees all BUs
    public const string ClrSupervisor = "CLR_Supervisor"; // same as CLR_Usr, sees all BUs
    public const string Bu = "BU";                       // business stakeholder, view-only, BU-scoped
    public const string Treasury = "Treasury";           // view-only across Shipments/Dues/Clearance, all BUs
    public const string CorpFinance = "CorpFinance";     // narrow: view-only Supplier/Bank Dues + Clearance, all BUs
    public const string Manager = "Manager";             // broad view + Settings edit, all BUs
    public const string SuperUser = "SuperUser";         // full access everywhere, creates users

    public static readonly string[] All = { IpUser, IpSupervisor, ClrUsr, ClrSupervisor, Bu, Treasury, CorpFinance, Manager, SuperUser };

    // Roles limited to their assigned Business Unit(s) — everyone else sees all BUs.
    public static readonly string[] BuScopedRoles = { IpUser, IpSupervisor, Bu };
}
