using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/clearance-charge-types")]
public class ClearanceChargeTypesController : LookupCrudController<ClearanceChargeType>
{
    public ClearanceChargeTypesController(ShippingPortalDbContext db) : base(db) { }
}
