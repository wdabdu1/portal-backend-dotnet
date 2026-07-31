using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/clearance-sla-settings")]
public class ClearanceSlaSettingsController : LookupCrudController<ClearanceSlaSetting>
{
    public ClearanceSlaSettingsController(ShippingPortalDbContext db) : base(db) { }
}
