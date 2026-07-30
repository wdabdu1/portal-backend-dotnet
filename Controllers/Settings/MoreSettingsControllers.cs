using Microsoft.AspNetCore.Mvc;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/approval-types")]
public class ApprovalTypesController : LookupCrudController<ApprovalType>
{
    public ApprovalTypesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/payment-terms")]
public class PaymentTermsController : LookupCrudController<PaymentTerm>
{
    public PaymentTermsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/incoterms")]
public class IncotermsController : LookupCrudController<Incoterm>
{
    public IncotermsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/origin-countries")]
public class OriginCountriesController : LookupCrudController<OriginCountry>
{
    public OriginCountriesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/units-of-measure")]
public class UnitsOfMeasureController : LookupCrudController<UnitOfMeasure>
{
    public UnitsOfMeasureController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/shipment-modes")]
public class ShipmentModesController : LookupCrudController<ShipmentMode>
{
    public ShipmentModesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/product-categories")]
public class ProductCategoriesController : LookupCrudController<ProductCategory>
{
    public ProductCategoriesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/product-types")]
public class ProductTypesController : LookupCrudController<ProductType>
{
    public ProductTypesController(ShippingPortalDbContext db) : base(db) { }
}
