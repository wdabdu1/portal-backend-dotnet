using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/couriers")]
public class CouriersController : LookupCrudController<Courier>
{
    public CouriersController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/forwarders")]
public class ForwardersController : LookupCrudController<Forwarder>
{
    public ForwardersController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/tenors")]
public class TenorsController : LookupCrudController<Tenor>
{
    public TenorsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/sender-banks")]
public class SenderBanksController : LookupCrudController<SenderBank>
{
    public SenderBanksController(ShippingPortalDbContext db) : base(db) { }
}


[Route("api/settings/shipment-destinations")]
public class ShipmentDestinationsController : LookupCrudController<ShipmentDestination>
{
    public ShipmentDestinationsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/public-holidays")]
public class PublicHolidaysController : LookupCrudController<PublicHoliday>
{
    public PublicHolidaysController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/spc-rates")]
public class SpcRatesController : LookupCrudController<SpcRate>
{
    public SpcRatesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/acd-cost-settings")]
public class AcdCostSettingsController : LookupCrudController<AcdCostSetting>
{
    public AcdCostSettingsController(ShippingPortalDbContext db) : base(db) { }
}
