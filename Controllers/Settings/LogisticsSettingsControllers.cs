using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

// Reads are still open to any logged-in user (matches every other Settings
// table, since dropdowns elsewhere may need these), but writes are
// restricted to Logistics Editors instead of the default Manager/SuperUser.

[Route("api/settings/logistics-cities")]
public class LogisticsCitiesController : LookupCrudController<LogisticsCity>
{
    public LogisticsCitiesController(ShippingPortalDbContext db) : base(db) { }

    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Update(int id, System.Text.Json.JsonElement entity) => base.Update(id, entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Delete(int id) => base.Delete(id);
}

[Route("api/settings/drivers")]
public class DriversController : LookupCrudController<Driver>
{
    public DriversController(ShippingPortalDbContext db) : base(db) { }

    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<ActionResult<Driver>> Create(Driver entity) => base.Create(entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Update(int id, System.Text.Json.JsonElement entity) => base.Update(id, entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Delete(int id) => base.Delete(id);
}

[Route("api/settings/trucks")]
public class TrucksController : LookupCrudController<Truck>
{
    public TrucksController(ShippingPortalDbContext db) : base(db) { }

    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<ActionResult<Truck>> Create(Truck entity) => base.Create(entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Update(int id, System.Text.Json.JsonElement entity) => base.Update(id, entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Delete(int id) => base.Delete(id);
}

[Route("api/settings/warehouses")]
public class WarehousesController : LookupCrudController<Warehouse>
{
    public WarehousesController(ShippingPortalDbContext db) : base(db) { }

    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<ActionResult<Warehouse>> Create(Warehouse entity) => base.Create(entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Update(int id, System.Text.Json.JsonElement entity) => base.Update(id, entity);
    [Authorize(Roles = AppRoles.LogisticsEditors)]
    public override Task<IActionResult> Delete(int id) => base.Delete(id);
}
