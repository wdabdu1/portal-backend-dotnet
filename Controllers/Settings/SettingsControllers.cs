using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Controllers.Settings;

[Route("api/settings/business-units")]
public class BusinessUnitsController : LookupCrudController<BusinessUnit>
{
    public BusinessUnitsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/divisions")]
public class DivisionsController : LookupCrudController<Division>
{
    public DivisionsController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/currencies")]
public class CurrenciesController : LookupCrudController<Currency>
{
    public CurrenciesController(ShippingPortalDbContext db) : base(db) { }
}

[Route("api/settings/business-partners")]
public class BusinessPartnersController : LookupCrudController<BusinessPartner>
{
    public BusinessPartnersController(ShippingPortalDbContext db) : base(db) { }

    [HttpGet("suppliers")]
    public async Task<ActionResult<IEnumerable<BusinessPartner>>> GetSuppliers()
        => await Db.BusinessPartners.Where(p => p.IsSupplier && p.IsActive).ToListAsync();

    [HttpGet("consignees")]
    public async Task<ActionResult<IEnumerable<BusinessPartner>>> GetConsignees()
        => await Db.BusinessPartners.Where(p => p.IsConsignee && p.IsActive).ToListAsync();

    [HttpGet("brands")]
    public async Task<ActionResult<IEnumerable<BusinessPartner>>> GetBrands()
        => await Db.BusinessPartners.Where(p => p.IsBrandManufacturer && p.IsActive).ToListAsync();

    [HttpGet("offshore")]
    public async Task<ActionResult<IEnumerable<BusinessPartner>>> GetOffshoreEntities()
        => await Db.BusinessPartners.Where(p => p.IsOffshoreEntity && p.IsActive).ToListAsync();
}

[ApiController]
[Authorize]
[Route("api/settings/model-products")]
public class ModelProductsController : LookupCrudController<ModelProduct>
{
    public ModelProductsController(ShippingPortalDbContext db) : base(db) { }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<ModelProduct>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3) return Ok(Array.Empty<ModelProduct>());
        return await Db.ModelProducts
            .Where(m => m.IsActive && EF.Functions.Like(m.Name, $"%{q}%"))
            .Include(m => m.ProductCategory)
            .Include(m => m.ProductType)
            .Take(20)
            .ToListAsync();
    }
}

[ApiController]
[Authorize]
[Route("api/settings/shipping-lines")]
public class ShippingLinesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShippingLinesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingLine>>> GetAll()
        => await _db.ShippingLines.Include(l => l.DemurrageTariffs).ToListAsync();

    [HttpPost]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ShippingLine>> Create(ShippingLine line)
    {
        _db.ShippingLines.Add(line);
        await _db.SaveChangesAsync();
        return Ok(line);
    }

    [HttpPut("{id:int}/tariffs")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> ReplaceTariffs(int id, List<ShippingLineDemurrageTariff> tariffs)
    {
        var existing = await _db.ShippingLineDemurrageTariffs.Where(t => t.ShippingLineId == id).ToListAsync();
        _db.ShippingLineDemurrageTariffs.RemoveRange(existing);
        foreach (var t in tariffs) { t.Id = 0; t.ShippingLineId = id; _db.ShippingLineDemurrageTariffs.Add(t); }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
