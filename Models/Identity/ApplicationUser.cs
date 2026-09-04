using Microsoft.AspNetCore.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;

    // Every JWT carries this as a claim. Bumping it (via "Revoke
    // Sessions") instantly invalidates every token already issued to
    // this user, regardless of how much time is left on it — the
    // actual mechanism behind stolen-device / offboarding response.
    public int SessionVersion { get; set; } = 1;

    // Updated on every authenticated request (see OnTokenValidated in
    // Program.cs) — used to approximate "Live Now" (recent activity)
    // and "Last Used" on the User Activity report.
    public DateTime? LastActivityAt { get; set; }
    // Incremented once per successful login — a simple, honest proxy
    // for how often an account is actually used.
    public int LoginCount { get; set; } = 0;

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
    public const string LogisticsOfficer = "LogisticsOfficer"; // warehouse allocation + truck loading, all BUs
    public const string CPricing = "CPricing";           // C Pricing data-entry role — locked to the C Pricing pages only, all BUs

    public static readonly string[] All = { IpUser, IpSupervisor, ClrUsr, ClrSupervisor, Bu, Treasury, CorpFinance, Manager, SuperUser, CPricing };

    // Roles limited to their assigned Business Unit(s) — everyone else sees all BUs.
    public static readonly string[] BuScopedRoles = { IpUser, IpSupervisor, Bu };

    // Reusable comma-joined role groups for [Authorize(Roles = "...")].
    public const string OrdersShipmentsEditors = IpUser + "," + IpSupervisor + "," + SuperUser;
    public const string OrdersShipmentsViewers = IpUser + "," + IpSupervisor + "," + SuperUser + "," + Bu + "," + Treasury + "," + Manager;

    public const string ClearanceEditors = ClrUsr + "," + ClrSupervisor + "," + SuperUser;
    public const string ClearanceViewers = IpUser + "," + IpSupervisor + "," + ClrUsr + "," + ClrSupervisor + "," + Bu + "," + Treasury + "," + CorpFinance + "," + Manager + "," + SuperUser;

    public const string ShipmentDetailsViewers = IpUser + "," + IpSupervisor + "," + ClrUsr + "," + ClrSupervisor + "," + Bu + "," + Treasury + "," + Manager + "," + SuperUser;

    public const string SettingsViewers = Manager + "," + SuperUser;

    public const string SupplierDuesEditors = IpUser + "," + IpSupervisor + "," + SuperUser;
    public const string SupplierDuesViewers = IpUser + "," + IpSupervisor + "," + SuperUser + "," + Bu + "," + Treasury + "," + CorpFinance + "," + Manager;

    public const string BankDuesEditors = IpUser + "," + IpSupervisor + "," + SuperUser;
    public const string BankDuesViewers = IpUser + "," + IpSupervisor + "," + SuperUser + "," + Treasury + "," + CorpFinance + "," + Manager;

    // Full read/write access to the Pay Bank Dues screen specifically —
    // a deliberately different set from the general BankDuesEditors above.
    public const string PayBankDuesUsers = IpUser + "," + IpSupervisor + "," + Treasury + "," + CorpFinance + "," + SuperUser;

    public const string LogisticsEditors = LogisticsOfficer + "," + SuperUser;
    public const string LogisticsViewers = LogisticsOfficer + "," + Manager + "," + SuperUser;

    // C Pricing pages (working table, history, C_Cat/C_Type mini-settings) —
    // deliberately excludes Treasury/CorpFinance: this feature moved out of
    // Finance's visibility entirely, not just added alongside it.
    public const string CPricingUsers = CPricing + "," + Manager + "," + SuperUser;
}
