using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/tariff-groups")]
public class TariffGroupsController : LookupCrudController<TariffGroup>
{
    public TariffGroupsController(ShippingPortalDbContext db) : base(db) { }
}
