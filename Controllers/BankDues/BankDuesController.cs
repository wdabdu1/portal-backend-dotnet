using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace ShippingPortal.Api.Controllers.BankDues;

public record BankDueRow(
    int ShipmentId, string BusinessUnit, string Consignee, string Category, string? ReceiverBank, string BlAwbNo,
    DateOnly? Sob, DateOnly? BlAwbDate, bool NecessaryGoodType,
    string? LastOffshoreInvoiceNo, int? TenorDays, DateOnly? DueDate, DateOnly? CbosDueDate,
    string? ImFormNo, DateOnly? ImFormDate,
    decimal? Value, string? Currency, decimal ValueAed, decimal PaidAed, decimal BalanceAed);

public record CollectionRecordRequest(DateOnly PaymentDate, int CurrencyId, [Range(0.0001, double.MaxValue, ErrorMessage = "Value must be greater than zero.")] decimal Value);
public record CollectionRecordResponse(int Id, DateOnly PaymentDate, int CurrencyId, string CurrencyCode, decimal Value, decimal ValueAed);

[ApiController]
[Authorize(Roles = AppRoles.BankDuesViewers)]
[Route("api/bank-dues")]
public class BankDuesController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.BuAccessService _buAccess;
    public BankDuesController(ShippingPortalDbContext db, ShippingPortal.Api.Services.BuAccessService buAccess)
    {
        _db = db;
        _buAccess = buAccess;
    }

    private async Task<ActionResult?> CheckWriteAccessAsync(int shipmentId)
    {
        var buId = await _db.Shipments
            .Where(s => s.Id == shipmentId)
            .Select(s => (int?)s.PurchaseOrder!.BusinessUnitId)
            .FirstOrDefaultAsync();

        if (buId is null) return NotFound();
        if (!_buAccess.CanWriteBusinessUnit(User, buId.Value)) return Forbid();
        return null;
    }

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

    // Converts a value in `currencyId` to AED via the USD cross-rate:
    // value / rateToUsd(currency) * rateToUsd(AED). Rates are cached per
    // request instance, so a list with many rows only hits the DB once
    // per distinct currency instead of once per row.
    private async Task<decimal> ConvertToAedAsync(decimal value, int? currencyId)
    {
        var sourceRate = await GetFxRateAsync(currencyId);
        var aedCurrency = await _db.Currencies.FirstOrDefaultAsync(c => c.Code == "AED");
        var aedRate = aedCurrency is null ? 1m : await GetFxRateAsync(aedCurrency.Id);
        var usd = value / sourceRate;
        return usd * aedRate;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BankDueRow>>> GetOpen([FromServices] BuAccessService buAccess)
    {
        var query = _db.ShipmentBankings
            .Include(b => b.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(po => po!.Consignee)
            .Include(b => b.Shipment).ThenInclude(s => s!.PurchaseOrder).ThenInclude(po => po!.BusinessUnit)
            .Include(b => b.ReceivingBank)
            .Include(b => b.Tenor)
            .Include(b => b.AddCbosAllowance)
            .Include(b => b.CollectionCurrency)
            .AsQueryable();

        if (!buAccess.SeesAllBus(User))
        {
            var allowedBus = buAccess.GetAllowedBusinessUnitIds(User);
            query = query.Where(b => allowedBus.Contains(b.Shipment!.PurchaseOrder!.BusinessUnitId));
        }

        var bankings = await query.ToListAsync();

        var clearances = await _db.Clearances.ToDictionaryAsync(c => c.ShipmentId);
        var lastOffshoreInvoicesByShipment = await _db.LastOffshoreDetails.ToDictionaryAsync(d => d.ShipmentId, d => d.InvoiceNo);

        var shipmentIdsForCategory = bankings.Select(b => b.ShipmentId).ToList();
        var categoriesByShipment = await _db.ShipmentLineItems
            .Where(li => shipmentIdsForCategory.Contains(li.ShipmentId))
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .GroupBy(li => li.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, Category = g.First().PurchaseOrderLineItem!.ProductCategory!.Name })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Category);

        var collectionsByShipment = new Dictionary<int, List<ShipmentCollectionRecord>>();
        foreach (var record in await _db.ShipmentCollectionRecords.ToListAsync())
        {
            if (!collectionsByShipment.TryGetValue(record.ShipmentId, out var list))
            {
                list = new List<ShipmentCollectionRecord>();
                collectionsByShipment[record.ShipmentId] = list;
            }
            list.Add(record);
        }

        var rows = new List<BankDueRow>();
        foreach (var banking in bankings)
        {
            if (!banking.CollectionValue.HasValue) continue;

            var shipment = banking.Shipment!;
            clearances.TryGetValue(shipment.Id, out var clearance);

            var valueAed = await ConvertToAedAsync(banking.CollectionValue.Value, banking.CollectionCurrencyId);

            decimal paidAed = 0;
            if (collectionsByShipment.TryGetValue(shipment.Id, out var records))
            {
                foreach (var r in records)
                    paidAed += await ConvertToAedAsync(r.Value, r.CurrencyId);
            }

            var balanceAed = valueAed - paidAed;
            if (balanceAed <= 0) continue;

            // Due Date is anchored to BL/AWB Date, not SOB — BL/AWB
            // Date is captured once at shipment creation and never
            // moves, whereas SOB can be entered/adjusted later and
            // isn't the contractual reference point here.
            DateOnly? dueDate = null;
            if (shipment.BlAwbDate.HasValue && banking.Tenor is not null)
                dueDate = shipment.BlAwbDate.Value.AddDays(banking.Tenor.Days);

            DateOnly? cbosDueDate = null;
            if (dueDate.HasValue && banking.AddCbosAllowance is not null)
                cbosDueDate = dueDate.Value.AddDays(banking.AddCbosAllowance.Days);

            rows.Add(new BankDueRow(
                shipment.Id, shipment.PurchaseOrder?.BusinessUnit?.Name ?? "", shipment.PurchaseOrder?.Consignee?.Name ?? "",
                categoriesByShipment.GetValueOrDefault(shipment.Id, ""), banking.ReceivingBank?.Name, shipment.BlAwbNo,
                shipment.SobActualDate, shipment.BlAwbDate, banking.NecessaryGoodType,
                lastOffshoreInvoicesByShipment.GetValueOrDefault(shipment.Id), banking.Tenor?.Days, dueDate, cbosDueDate,
                clearance?.ImFormNo, clearance?.ImFormDate, banking.CollectionValue, banking.CollectionCurrency?.Code,
                valueAed, paidAed, balanceAed));
        }

        return Ok(rows.OrderBy(r => r.Consignee).ToList());
    }

    [HttpGet("{shipmentId:int}/records")]
    public async Task<ActionResult<IEnumerable<CollectionRecordResponse>>> GetRecords(int shipmentId)
    {
        var records = await _db.ShipmentCollectionRecords
            .Where(r => r.ShipmentId == shipmentId)
            .Include(r => r.Currency)
            .OrderBy(r => r.PaymentDate)
            .ToListAsync();

        var result = new List<CollectionRecordResponse>();
        foreach (var r in records)
        {
            var aed = await ConvertToAedAsync(r.Value, r.CurrencyId);
            result.Add(new CollectionRecordResponse(r.Id, r.PaymentDate, r.CurrencyId, r.Currency!.Code, r.Value, aed));
        }
        return Ok(result);
    }

    [HttpPost("{shipmentId:int}/records")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<ActionResult<CollectionRecordResponse>> AddRecord(int shipmentId, CollectionRecordRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); if (denied is not null) return denied;
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var record = new ShipmentCollectionRecord
        {
            ShipmentId = shipmentId,
            PaymentDate = req.PaymentDate,
            CurrencyId = req.CurrencyId,
            Value = req.Value,
            ValueUsd = req.Value / await GetFxRateAsync(req.CurrencyId)
        };
        _db.ShipmentCollectionRecords.Add(record);
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        var aed = await ConvertToAedAsync(req.Value, req.CurrencyId);
        return Ok(new CollectionRecordResponse(record.Id, record.PaymentDate, record.CurrencyId, currency?.Code ?? "", record.Value, aed));
    }

    [HttpDelete("{shipmentId:int}/records/{recordId:int}")]
    [Authorize(Roles = AppRoles.BankDuesEditors)]
    public async Task<IActionResult> DeleteRecord(int shipmentId, int recordId)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); if (denied is not null) return denied;
        var record = await _db.ShipmentCollectionRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ShipmentId == shipmentId);
        if (record is null) return NotFound();

        _db.ShipmentCollectionRecords.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
