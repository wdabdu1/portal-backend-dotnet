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

// Shipping lines carry nested demurrage tariffs (one row per Tariff Group x
// container size), so they get a purpose-built controller rather than the
// generic lookup pattern.
public record TariffRow(int TariffGroupId, string ContainerSize, int FreeDays, int FirstPeriodDays, decimal FirstPeriodRateSdg, decimal AfterwardRateSdg);
public record ShippingLineWithTariffsRequest(string Name, List<TariffRow> Tariffs);
public record ShippingLineTariffResponse(int Id, int TariffGroupId, string TariffGroupName, string ContainerSize, int FreeDays, int FirstPeriodDays, decimal FirstPeriodRateSdg, decimal AfterwardRateSdg);public record ShippingLineResponse(int Id, string Name, bool IsActive, List<ShippingLineTariffResponse> Tariffs);

[ApiController]
[Authorize]
[Route("api/settings/shipping-lines")]
public class ShippingLinesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public ShippingLinesController(ShippingPortalDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingLineResponse>>> GetAll()
    {
        var lines = await _db.ShippingLines
            .Include(l => l.DemurrageTariffs).ThenInclude(t => t.TariffGroup)
            .ToListAsync();

        return lines.Select(l => new ShippingLineResponse(
            l.Id, l.Name, l.IsActive,
            l.DemurrageTariffs.Select(t => new ShippingLineTariffResponse(
                t.Id, t.TariffGroupId, t.TariffGroup?.Name ?? "", t.ContainerSize, t.FreeDays, t.FirstPeriodDays, t.FirstPeriodRateSdg, t.AfterwardRateSdg
            )).ToList()
        )).ToList();
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<ActionResult<ShippingLineResponse>> Create(ShippingLineWithTariffsRequest req)
    {
        var line = new ShippingLine { Name = req.Name, IsActive = true };
        _db.ShippingLines.Add(line);
        await _db.SaveChangesAsync();

        foreach (var t in req.Tariffs)
        {
            _db.ShippingLineDemurrageTariffs.Add(new ShippingLineDemurrageTariff
            {
                ShippingLineId = line.Id,
                TariffGroupId = t.TariffGroupId,
                ContainerSize = t.ContainerSize,
                FreeDays = t.FreeDays,
                FirstPeriodDays = t.FirstPeriodDays,
                FirstPeriodRateSdg = t.FirstPeriodRateSdg,
                AfterwardRateSdg = t.AfterwardRateSdg
            });
        }
        await _db.SaveChangesAsync();

        return await GetOne(line.Id);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShippingLineResponse>> GetOne(int id)
    {
        var line = await _db.ShippingLines
            .Include(l => l.DemurrageTariffs).ThenInclude(t => t.TariffGroup)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (line is null) return NotFound();

        return new ShippingLineResponse(
            line.Id, line.Name, line.IsActive,
            line.DemurrageTariffs.Select(t => new ShippingLineTariffResponse(
                t.Id, t.TariffGroupId, t.TariffGroup?.Name ?? "", t.ContainerSize, t.FreeDays, t.FirstPeriodDays, t.FirstPeriodRateSdg, t.AfterwardRateSdg
            )).ToList());
    }

    [HttpPut("{id:int}/tariffs")]
    [Authorize(Roles = AppRoles.Manager + "," + AppRoles.SuperUser)]
    public async Task<IActionResult> ReplaceTariffs(int id, List<TariffRow> tariffs)
    {
        var existing = await _db.ShippingLineDemurrageTariffs
            .Where(t => t.ShippingLineId == id)
            .ToListAsync();
        _db.ShippingLineDemurrageTariffs.RemoveRange(existing);

        foreach (var t in tariffs)
        {
            _db.ShippingLineDemurrageTariffs.Add(new ShippingLineDemurrageTariff
            {
                ShippingLineId = id,
                TariffGroupId = t.TariffGroupId,
                ContainerSize = t.ContainerSize,
                FreeDays = t.FreeDays,
                FirstPeriodDays = t.FirstPeriodDays,
                FirstPeriodRateSdg = t.FirstPeriodRateSdg,
                AfterwardRateSdg = t.AfterwardRateSdg
            });
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
