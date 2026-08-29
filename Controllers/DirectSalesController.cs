using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Controllers;

public record DirectSalesDueRow(
    int ShipmentId, string BusinessUnit, string Division, string Consignee, string BlAwbNo, string Category,
    DateOnly DueDate, decimal DueAmount, string DueCurrency, decimal DueAmountUsd,
    decimal CollectedUsd, decimal RemainingUsd, bool Settled);

public record CustomerDueRequest(DateOnly DueDate, int CurrencyId, [Range(typeof(decimal), "0.0001", "79228162514264337593543950335", ErrorMessage = "Value must be greater than zero.")] decimal Value);
public record CustomerDueResponse(int Id, DateOnly DueDate, int CurrencyId, string CurrencyCode, decimal Value);

public record CustomerCollectionRequest(DateOnly PaymentDate, int CurrencyId, [Range(typeof(decimal), "0.0001", "79228162514264337593543950335", ErrorMessage = "Value must be greater than zero.")] decimal Value);
public record CustomerCollectionResponse(int Id, DateOnly PaymentDate, int CurrencyId, string CurrencyCode, decimal Value);

// Direct Sales: shipments dispatched straight under a client/consignee's
// name, tracked separately from the normal Bank Dues flow (see
// Shipment.IsDirectSales). "Customer Agreed Payment" (ShipmentCustomerDue)
// is the due schedule; "Customer Collected Payment" reuses
// ShipmentCollectionRecord — the same table Bank Dues collections use,
// safe to share since a shipment is either Direct Sales or not.
[ApiController]
[Authorize(Roles = AppRoles.BankDuesViewers)]
[Route("api/direct-sales")]
public class DirectSalesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    public DirectSalesController(ShippingPortalDbContext db) => _db = db;

    private readonly Dictionary<int, decimal> _fxCache = new();

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        if (_fxCache.TryGetValue(currencyId.Value, out var cached)) return cached;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        var value = rate?.RateToUsd ?? 1m;
        _fxCache[currencyId.Value] = value;
        return value;
    }

    private async Task<decimal> ConvertToUsdAsync(decimal value, int? currencyId)
    {
        var rate = await GetFxRateAsync(currencyId);
        return rate == 0 ? value : value / rate;
    }

    private async Task<ActionResult?> CheckAccessAsync(int shipmentId, BuAccessService buAccess, bool requireIsDirectSales = true)
    {
        var shipment = await _db.Shipments.Include(s => s.PurchaseOrder).FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();
        if (requireIsDirectSales && !shipment.IsDirectSales) return NotFound(new { message = "This shipment is not a Direct Sales shipment." });
        if (!buAccess.CanWriteBusinessUnit(User, shipment.PurchaseOrder!.BusinessUnitId)) return Forbid();
        return null;
    }

    // Every due, across every Direct Sales shipment, with collections
    // allocated FIFO by due date. A collection can only settle a due whose
    // DueDate is on or after the collection's own PaymentDate — a
    // collection made after a due's date can't retroactively cover it,
    // only a later due.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DirectSalesDueRow>>> GetDues([FromQuery] bool includeSettled, [FromServices] BuAccessService buAccess)
    {
        var query = _db.Shipments
            .Where(s => s.IsDirectSales)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Division)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(s => allowedBus.Contains(s.PurchaseOrder!.BusinessUnitId));
        }

        var shipments = await query.ToListAsync();
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        var categoriesByShipment = await _db.ShipmentLineItems
            .Where(li => shipmentIds.Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .GroupBy(li => li.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, Category = g.First().PurchaseOrderLineItem!.ProductCategory!.Name })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Category);

        var duesByShipment = (await _db.ShipmentCustomerDues
                .Where(d => shipmentIds.Contains(d.ShipmentId))
                .Include(d => d.Currency)
                .OrderBy(d => d.DueDate)
                .ToListAsync())
            .GroupBy(d => d.ShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var collectionsByShipment = (await _db.ShipmentCollectionRecords
                .Where(c => shipmentIds.Contains(c.ShipmentId))
                .OrderBy(c => c.PaymentDate)
                .ToListAsync())
            .GroupBy(c => c.ShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DirectSalesDueRow>();
        foreach (var shipment in shipments)
        {
            var shipmentDues = duesByShipment.GetValueOrDefault(shipment.Id) ?? new List<ShipmentCustomerDue>();
            if (shipmentDues.Count == 0) continue;

            var shipmentCollections = collectionsByShipment.GetValueOrDefault(shipment.Id) ?? new List<ShipmentCollectionRecord>();

            // Everything converted to USD up front — dues and collections
            // can each be in different currencies, so FIFO allocation only
            // makes sense once they're in a common unit.
            var collectionRemainingUsd = new decimal[shipmentCollections.Count];
            for (var i = 0; i < shipmentCollections.Count; i++)
                collectionRemainingUsd[i] = await ConvertToUsdAsync(shipmentCollections[i].Value, shipmentCollections[i].CurrencyId);

            var category = categoriesByShipment.GetValueOrDefault(shipment.Id, "");

            foreach (var due in shipmentDues)
            {
                var dueAmountUsd = await ConvertToUsdAsync(due.Value, due.CurrencyId);
                var remainingToAllocate = dueAmountUsd;
                decimal collectedUsd = 0;

                for (var i = 0; i < shipmentCollections.Count && remainingToAllocate > 0; i++)
                {
                    if (collectionRemainingUsd[i] <= 0) continue;
                    if (shipmentCollections[i].PaymentDate > due.DueDate) continue;

                    var use = Math.Min(remainingToAllocate, collectionRemainingUsd[i]);
                    collectedUsd += use;
                    remainingToAllocate -= use;
                    collectionRemainingUsd[i] -= use;
                }

                var remainingUsd = dueAmountUsd - collectedUsd;
                var settled = remainingUsd <= 0.005m;
                if (!includeSettled && settled) continue;

                rows.Add(new DirectSalesDueRow(
                    shipment.Id, shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.PurchaseOrder?.Division?.Name ?? "",
                    shipment.ConsigneeName ?? "", shipment.BlAwbNo, category,
                    due.DueDate, due.Value, due.Currency?.Code ?? "", dueAmountUsd,
                    collectedUsd, remainingUsd, settled));
            }
        }

        return Ok(rows.OrderBy(r => r.DueDate).ToList());
    }

    // --- Customer Agreed Payment (dues) ---

    [HttpGet("{shipmentId:int}/dues")]
    public async Task<ActionResult<IEnumerable<CustomerDueResponse>>> GetShipmentDues(int shipmentId, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var dues = await _db.ShipmentCustomerDues
            .Where(d => d.ShipmentId == shipmentId)
            .Include(d => d.Currency)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        return Ok(dues.Select(d => new CustomerDueResponse(d.Id, d.DueDate, d.CurrencyId, d.Currency!.Code, d.Value)).ToList());
    }

    [HttpPost("{shipmentId:int}/dues")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<ActionResult<CustomerDueResponse>> AddShipmentDue(int shipmentId, CustomerDueRequest req, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var due = new ShipmentCustomerDue { ShipmentId = shipmentId, DueDate = req.DueDate, CurrencyId = req.CurrencyId, Value = req.Value };
        _db.ShipmentCustomerDues.Add(due);
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        return Ok(new CustomerDueResponse(due.Id, due.DueDate, due.CurrencyId, currency?.Code ?? "", due.Value));
    }

    [HttpDelete("{shipmentId:int}/dues/{dueId:int}")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<IActionResult> DeleteShipmentDue(int shipmentId, int dueId, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var due = await _db.ShipmentCustomerDues.FirstOrDefaultAsync(d => d.Id == dueId && d.ShipmentId == shipmentId);
        if (due is null) return NotFound();

        _db.ShipmentCustomerDues.Remove(due);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Customer Collected Payment (reuses ShipmentCollectionRecord) ---

    [HttpGet("{shipmentId:int}/collections")]
    public async Task<ActionResult<IEnumerable<CustomerCollectionResponse>>> GetShipmentCollections(int shipmentId, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var records = await _db.ShipmentCollectionRecords
            .Where(c => c.ShipmentId == shipmentId)
            .Include(c => c.Currency)
            .OrderBy(c => c.PaymentDate)
            .ToListAsync();

        return Ok(records.Select(c => new CustomerCollectionResponse(c.Id, c.PaymentDate, c.CurrencyId, c.Currency!.Code, c.Value)).ToList());
    }

    [HttpPost("{shipmentId:int}/collections")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<ActionResult<CustomerCollectionResponse>> AddShipmentCollection(int shipmentId, CustomerCollectionRequest req, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var record = new ShipmentCollectionRecord
        {
            ShipmentId = shipmentId,
            PaymentDate = req.PaymentDate,
            CurrencyId = req.CurrencyId,
            Value = req.Value,
            ValueUsd = await ConvertToUsdAsync(req.Value, req.CurrencyId)
        };
        _db.ShipmentCollectionRecords.Add(record);
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        return Ok(new CustomerCollectionResponse(record.Id, record.PaymentDate, record.CurrencyId, currency?.Code ?? "", record.Value));
    }

    [HttpDelete("{shipmentId:int}/collections/{recordId:int}")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<IActionResult> DeleteShipmentCollection(int shipmentId, int recordId, [FromServices] BuAccessService buAccess)
    {
        var denied = await CheckAccessAsync(shipmentId, buAccess); if (denied is not null) return denied;

        var record = await _db.ShipmentCollectionRecords.FirstOrDefaultAsync(c => c.Id == recordId && c.ShipmentId == shipmentId);
        if (record is null) return NotFound();

        _db.ShipmentCollectionRecords.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
