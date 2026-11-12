using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

// Reads are open to any logged-in user (matches every other Settings table,
// since dropdowns elsewhere need these). Writes: Coordinator, Manager and
// SuperUser - see AppRoles.LogisticsSettingsEditors and the WriteRoles hook in
// LookupCrudController.
//
// NOTE: the previous version of this file put [Authorize(Roles = LogisticsEditors)]
// on overrides of Create/Update/Delete. Because the base actions already carried
// [Authorize(Roles = Manager,SuperUser)] and MVC combines both, the effective rule
// was "must satisfy BOTH", i.e. SuperUser only. The roles are now set once, here.

[Route("api/settings/logistics-cities")]
public class LogisticsCitiesController : LookupCrudController<LogisticsCity>
{
    public LogisticsCitiesController(ShippingPortalDbContext db) : base(db) { }
    protected override string[] WriteRoles => new[] { AppRoles.Coordinator, AppRoles.Manager, AppRoles.SuperUser };
}

[Route("api/settings/drivers")]
public class DriversController : LookupCrudController<Driver>
{
    public DriversController(ShippingPortalDbContext db) : base(db) { }
    protected override string[] WriteRoles => new[] { AppRoles.Coordinator, AppRoles.Manager, AppRoles.SuperUser };
}

[Route("api/settings/trucks")]
public class TrucksController : LookupCrudController<Truck>
{
    public TrucksController(ShippingPortalDbContext db) : base(db) { }
    protected override string[] WriteRoles => new[] { AppRoles.Coordinator, AppRoles.Manager, AppRoles.SuperUser };
}

[Route("api/settings/warehouses")]
public class WarehousesController : LookupCrudController<Warehouse>
{
    public WarehousesController(ShippingPortalDbContext db) : base(db) { }
    protected override string[] WriteRoles => new[] { AppRoles.Coordinator, AppRoles.Manager, AppRoles.SuperUser };
}

[Route("api/settings/external-transporters")]
public class ExternalTransportersController : LookupCrudController<ExternalTransporter>
{
    public ExternalTransportersController(ShippingPortalDbContext db) : base(db) { }
    protected override string[] WriteRoles => new[] { AppRoles.Coordinator, AppRoles.Manager, AppRoles.SuperUser };
}
