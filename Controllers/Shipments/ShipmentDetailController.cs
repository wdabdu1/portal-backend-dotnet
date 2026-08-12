using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Controllers.Shipments;

public record ShipmentForwarderRequest(int? ForwarderId, decimal? ActualShippingCost, int? CurrencyId, decimal? AmountSaved, bool MarineInsurance);
public record ShipmentAcdRequest(DateOnly? ProcessDate, DateOnly? CostSettledDate, string? RefNumber);
public record ShipmentDraftDocumentsRequest(DateOnly? InitialDraftReceivedDate, DateOnly? FinalDraftReceivedDate, DateOnly? FinalDraftConfirmedDate);
public record ShipmentSsmoRequest(DateOnly? ApplicationDate, decimal? Cost, DateOnly? CostSettledDate, string? RefNumber, DateOnly? ApprovalDate);
public record ShipmentMotRequest(DateOnly? ProcessDate, decimal? Cost, DateOnly? CostSettledDate, string? RefNumber, DateOnly? ApprovalDate, string? OffshoreApprovedPiNumber);
public record ShipmentSupplierFullSetRequest(string? SupplierInvoiceNo, DateOnly? SupplierInvoiceDate, DateOnly? FsDispatchDate, int? FsDispatchedViaId, string? FsTrackingNumber, DateOnly? FsReceivedDate);
public record PaymentRecordRequest(DateOnly PaymentDate, int CurrencyId, decimal Value, int? PaymentDueId);
public record PaymentRecordResponse(int Id, DateOnly PaymentDate, int CurrencyId, string CurrencyCode, decimal Value, decimal ValueUsd, int? PaymentDueId);

public record PaymentDueRequest(DateOnly DueDate, decimal Amount, int CurrencyId, string? Label);
public record PaymentDueResponse(int Id, DateOnly DueDate, decimal Amount, int CurrencyId, string CurrencyCode, string? Label, decimal PaidUsd, decimal AmountUsd);

public record SupplierInvoiceSummary(
    string? SupplierInvoiceNo, decimal InvoiceValue, string InvoiceCurrency, decimal InvoiceValueUsd,
    decimal TotalPaidUsd, decimal BalanceUsd);
public record ShipmentBankingRequest(
    int? SenderBankId, DateOnly? OsDocDispatchDate, int? OsDocDispatchedViaId, string? OsDocTrackingNumber,
    int? ReceivingBankId, bool NecessaryGoodType, string? CollectionRefNo, decimal? CollectionValue, int? CollectionCurrencyId,
    int? TenorId);

public record ShipmentLineItemHsCode(int LineItemId, string ModelProduct, string? HsCode);

public record ShipmentDetailResponse(
    int Id, string BlAwbNo, string PoNumber, string Status,
    ShipmentForwarder? Forwarder, ShipmentAcd? Acd, ShipmentDraftDocuments? DraftDocuments,
    ShipmentSsmo? Ssmo, ShipmentMot? Mot, ShipmentSupplierFullSet? SupplierFullSet,
    ShipmentBanking? Banking,
    List<string> OffshorePartnerNames,
    string BusinessUnit, string Supplier, string Category, DateOnly? SobActualDate,
    List<ShipmentLineItemHsCode> LineItemHsCodes, decimal? BuShippingBudget, string? OffshorePoNo, int Fcl20Count, int Fcl40Count,
    DateOnly? ReceivedSignedPiDate, DateOnly? OrderExecutionDate, DateOnly? LatestShippingDate);

public record SaveHsCodesRequest(List<ShipmentLineItemHsCode> LineItemHsCodes);

public record ShipOnBoardRequest(DateOnly? SobActualDate);

[ApiController]
[Authorize(Roles = AppRoles.OrdersShipmentsEditors)]
[Route("api/shipments/{shipmentId:int}")]
public class ShipmentDetailController : ControllerBase
{
    private readonly ShippingPortalDbContext _db;
    private readonly ShippingPortal.Api.Services.BuAccessService _buAccess;
    private readonly ShippingPortal.Api.Services.SectionLockService _sectionLock;
    public ShipmentDetailController(ShippingPortalDbContext db, ShippingPortal.Api.Services.BuAccessService buAccess, ShippingPortal.Api.Services.SectionLockService sectionLock)
    {
        _db = db;
        _buAccess = buAccess;
        _sectionLock = sectionLock;
    }

    // Every write action below calls this first — returns Forbid() if the
    // caller lacks ReadWrite on this specific shipment's Business Unit
    // (relevant only for BU-scoped roles; everyone else bypasses).
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

    [HttpGet("detail")]
    public async Task<ActionResult<ShipmentDetailResponse>> GetDetail(int shipmentId)
    {
        var shipment = await _db.Shipments
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.BusinessUnit)
            .Include(s => s.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ProductCategory)
            .Include(s => s.LineItems).ThenInclude(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.ModelProduct)
            .FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var forwarder = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var draftDocs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var mot = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);

        var offshorePartnerNames = await _db.PurchaseOrderOffshorePartners
            .Where(op => op.PurchaseOrderId == shipment.PurchaseOrderId)
            .OrderBy(op => op.SequenceOrder)
            .Select(op => op.BusinessPartner!.Name)
            .ToListAsync();

        var category = shipment.LineItems.FirstOrDefault()?.PurchaseOrderLineItem?.ProductCategory?.Name ?? "";

        var lineItemHsCodes = shipment.LineItems
            .Select(li => new ShipmentLineItemHsCode(li.Id, li.PurchaseOrderLineItem?.ModelProduct?.Name ?? "", li.HsCode))
            .ToList();

        return new ShipmentDetailResponse(shipment.Id, shipment.BlAwbNo, shipment.PurchaseOrder!.PoNumber, shipment.Status.ToString(),
            forwarder, acd, draftDocs, ssmo, mot, fullSet, banking, offshorePartnerNames,
            shipment.PurchaseOrder.BusinessUnit!.Name, shipment.PurchaseOrder.Supplier!.Name, category, shipment.SobActualDate,
            lineItemHsCodes, shipment.PurchaseOrder.BuShippingBudget, shipment.PurchaseOrder.OffshorePoNo, shipment.Fcl20Count, shipment.Fcl40Count,
            shipment.PurchaseOrder.ReceivedSignedPiDate, shipment.PurchaseOrder.OrderExecutionDate, shipment.PurchaseOrder.LatestShippingDate);
    }
    [HttpPut("hs-codes")]
    public async Task<IActionResult> SaveHsCodes(int shipmentId, SaveHsCodesRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "acd");
        if (lockDenied is not null) return lockDenied;

        var lineItems = await _db.ShipmentLineItems.Where(li => li.ShipmentId == shipmentId).ToListAsync();
        foreach (var update in req.LineItemHsCodes)
        {
            var li = lineItems.FirstOrDefault(x => x.Id == update.LineItemId);
            if (li is not null) li.HsCode = update.HsCode;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("ship-on-board")]
    public async Task<IActionResult> SaveShipOnBoard(int shipmentId, ShipOnBoardRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "shipOnBoard");
        if (lockDenied is not null) return lockDenied;
        
        var shipment = await _db.Shipments.FindAsync(shipmentId);
        if (shipment is null) return NotFound();

        shipment.SobActualDate = req.SobActualDate;
        shipment.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { shipment.SobActualDate });
    }

    [HttpPut("forwarder")]
    public async Task<IActionResult> UpsertForwarder(int shipmentId, ShipmentForwarderRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "forwarder");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentForwarders.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentForwarder { ShipmentId = shipmentId }; _db.ShipmentForwarders.Add(entity); }

        entity.ForwarderId = req.ForwarderId;
        entity.ActualShippingCost = req.ActualShippingCost;
        entity.CurrencyId = req.CurrencyId;
        entity.AmountSaved = req.AmountSaved;
        entity.MarineInsurance = req.MarineInsurance;

        if (req.ActualShippingCost.HasValue && req.CurrencyId.HasValue)
        {
            var rate = await _db.FxRates.Where(r => r.CurrencyId == req.CurrencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
            entity.ActualShippingCostUsd = req.ActualShippingCost.Value / (rate?.RateToUsd ?? 1m);
        }

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("acd")]
    public async Task<IActionResult> UpsertAcd(int shipmentId, ShipmentAcdRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "acd");
        if (lockDenied is not null) return lockDenied;

        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var entity = await _db.ShipmentAcds.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentAcd { ShipmentId = shipmentId }; _db.ShipmentAcds.Add(entity); }

        entity.ProcessDate = req.ProcessDate;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;

        // ACD cost is always computed, never entered directly — rate/FCL
        // comes from Settings and may change over time, so we use whichever
        // rate was in effect on or before this shipment's process date (or
        // today, if no process date is set yet).
        var asOfDate = req.ProcessDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rate = await _db.AcdCostSettings
            .Where(r => r.EffectiveDate <= asOfDate)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync();

        entity.CostUsd = rate is not null
            ? (rate.Rate20Usd * shipment.Fcl20Count) + (rate.Rate40Usd * shipment.Fcl40Count)
            : (decimal?)null;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("draft-documents")]
    public async Task<IActionResult> UpsertDraftDocuments(int shipmentId, ShipmentDraftDocumentsRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "draftDocuments");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentDraftDocuments { ShipmentId = shipmentId }; _db.ShipmentDraftDocuments.Add(entity); }

        entity.InitialDraftReceivedDate = req.InitialDraftReceivedDate;
        entity.FinalDraftReceivedDate = req.FinalDraftReceivedDate;
        entity.FinalDraftConfirmedDate = req.FinalDraftConfirmedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("ssmo")]
    public async Task<IActionResult> UpsertSsmo(int shipmentId, ShipmentSsmoRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "ssmo");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentSsmos.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentSsmo { ShipmentId = shipmentId }; _db.ShipmentSsmos.Add(entity); }

        entity.ApplicationDate = req.ApplicationDate;
        entity.Cost = req.Cost;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;
        entity.ApprovalDate = req.ApprovalDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("mot")]
    public async Task<IActionResult> UpsertMot(int shipmentId, ShipmentMotRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "mot");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentMots.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentMot { ShipmentId = shipmentId }; _db.ShipmentMots.Add(entity); }

        entity.ProcessDate = req.ProcessDate;
        entity.Cost = req.Cost;
        entity.CostSettledDate = req.CostSettledDate;
        entity.RefNumber = req.RefNumber;
        entity.ApprovalDate = req.ApprovalDate;
        entity.OffshoreApprovedPiNumber = req.OffshoreApprovedPiNumber;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("supplier-full-set")]
    public async Task<IActionResult> UpsertSupplierFullSet(int shipmentId, ShipmentSupplierFullSetRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "supplierFullSet");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentSupplierFullSet { ShipmentId = shipmentId }; _db.ShipmentSupplierFullSets.Add(entity); }

        entity.SupplierInvoiceNo = req.SupplierInvoiceNo;
        entity.SupplierInvoiceDate = req.SupplierInvoiceDate;
        entity.FsDispatchDate = req.FsDispatchDate;
        entity.FsDispatchedViaId = req.FsDispatchedViaId;
        entity.FsTrackingNumber = req.FsTrackingNumber;
        entity.FsReceivedDate = req.FsReceivedDate;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }

    private async Task<decimal> GetFxRateAsync(int? currencyId)
    {
        if (!currencyId.HasValue) return 1m;
        var rate = await _db.FxRates.Where(r => r.CurrencyId == currencyId).OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
        return rate?.RateToUsd ?? 1m;
    }

    // Invoice Value/Currency computed live from the Shipment's own line
    // items — no separate header record to maintain anymore.
    [HttpGet("supplier-invoice-summary")]
    public async Task<ActionResult<SupplierInvoiceSummary>> GetSupplierInvoiceSummary(int shipmentId)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId);
        if (shipment is null) return NotFound();

        var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);

        var lineItems = await _db.ShipmentLineItems
            .Where(li => li.ShipmentId == shipmentId)
            .Include(li => li.PurchaseOrderLineItem).ThenInclude(pli => pli!.Currency)
            .ToListAsync();

        var invoiceValue = lineItems.Sum(li => li.ItemSubtotal);
        var currencyCode = lineItems.FirstOrDefault()?.PurchaseOrderLineItem?.Currency?.Code ?? "";
        var currencyId = lineItems.FirstOrDefault()?.PurchaseOrderLineItem?.CurrencyId;
        var rate = await GetFxRateAsync(currencyId);
        var invoiceValueUsd = invoiceValue / rate;

        var records = await _db.ShipmentSupplierPaymentRecords.Where(r => r.ShipmentId == shipmentId).ToListAsync();
        var totalPaidUsd = records.Sum(r => r.ValueUsd);
        var balanceUsd = invoiceValueUsd - totalPaidUsd;

        return Ok(new SupplierInvoiceSummary(fullSet?.SupplierInvoiceNo, invoiceValue, currencyCode, invoiceValueUsd, totalPaidUsd, balanceUsd));
    }

    [HttpGet("supplier-payment/records")]
    public async Task<ActionResult<IEnumerable<PaymentRecordResponse>>> GetPaymentRecords(int shipmentId)
    {
        
        return await _db.ShipmentSupplierPaymentRecords
            .Where(r => r.ShipmentId == shipmentId)
            .Include(r => r.Currency)
            .OrderBy(r => r.PaymentDate)
            .Select(r => new PaymentRecordResponse(r.Id, r.PaymentDate, r.CurrencyId, r.Currency!.Code, r.Value, r.ValueUsd, r.PaymentDueId))
            .ToListAsync();
    }

    [HttpPost("supplier-payment/records")]
    public async Task<ActionResult<PaymentRecordResponse>> AddPaymentRecord(int shipmentId, PaymentRecordRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "supplierPayment");
        if (lockDenied is not null) return lockDenied;
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        if (req.PaymentDueId.HasValue && !await _db.ShipmentPaymentDues.AnyAsync(d => d.Id == req.PaymentDueId && d.ShipmentId == shipmentId))
            return BadRequest(new { message = "This due schedule row does not belong to this shipment." });

        var rate = await GetFxRateAsync(req.CurrencyId);
        var record = new ShipmentSupplierPaymentRecord
        {
            ShipmentId = shipmentId,
            PaymentDate = req.PaymentDate,
            CurrencyId = req.CurrencyId,
            Value = req.Value,
            ValueUsd = req.Value / rate,
            PaymentDueId = req.PaymentDueId
        };
        _db.ShipmentSupplierPaymentRecords.Add(record);
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        return Ok(new PaymentRecordResponse(record.Id, record.PaymentDate, record.CurrencyId, currency?.Code ?? "", record.Value, record.ValueUsd, record.PaymentDueId));
    }

    [HttpDelete("supplier-payment/records/{recordId:int}")]
    public async Task<IActionResult> DeletePaymentRecord(int shipmentId, int recordId)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        
        var record = await _db.ShipmentSupplierPaymentRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ShipmentId == shipmentId);
        if (record is null) return NotFound();

        _db.ShipmentSupplierPaymentRecords.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Payment Due Schedule ---

    [HttpGet("supplier-payment/dues")]
    public async Task<ActionResult<IEnumerable<PaymentDueResponse>>> GetPaymentDues(int shipmentId)
    {
        var dues = await _db.ShipmentPaymentDues
            .Where(d => d.ShipmentId == shipmentId)
            .Include(d => d.Currency)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        var paidByDue = await _db.ShipmentSupplierPaymentRecords
            .Where(r => r.ShipmentId == shipmentId && r.PaymentDueId != null)
            .GroupBy(r => r.PaymentDueId!.Value)
            .Select(g => new { DueId = g.Key, PaidUsd = g.Sum(r => r.ValueUsd) })
            .ToDictionaryAsync(x => x.DueId, x => x.PaidUsd);

        var rate = new Dictionary<int, decimal>();
        var result = new List<PaymentDueResponse>();
        foreach (var d in dues)
        {
            var r = await GetFxRateAsync(d.CurrencyId);
            var amountUsd = d.Amount / r;
            result.Add(new PaymentDueResponse(d.Id, d.DueDate, d.Amount, d.CurrencyId, d.Currency?.Code ?? "", d.Label, paidByDue.GetValueOrDefault(d.Id), amountUsd));
        }
        return Ok(result);
    }

    [HttpPost("supplier-payment/dues")]
    public async Task<ActionResult<PaymentDueResponse>> AddPaymentDue(int shipmentId, PaymentDueRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var due = new ShipmentPaymentDue { ShipmentId = shipmentId, DueDate = req.DueDate, Amount = req.Amount, CurrencyId = req.CurrencyId, Label = req.Label };
        _db.ShipmentPaymentDues.Add(due);
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        var rate = await GetFxRateAsync(req.CurrencyId);
        return Ok(new PaymentDueResponse(due.Id, due.DueDate, due.Amount, due.CurrencyId, currency?.Code ?? "", due.Label, 0, due.Amount / rate));
    }

    [HttpPut("supplier-payment/dues/{dueId:int}")]
    public async Task<ActionResult<PaymentDueResponse>> UpdatePaymentDue(int shipmentId, int dueId, PaymentDueRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;

        var due = await _db.ShipmentPaymentDues.FirstOrDefaultAsync(d => d.Id == dueId && d.ShipmentId == shipmentId);
        if (due is null) return NotFound();

        due.DueDate = req.DueDate;
        due.Amount = req.Amount;
        due.CurrencyId = req.CurrencyId;
        due.Label = req.Label;
        await _db.SaveChangesAsync();

        var currency = await _db.Currencies.FindAsync(req.CurrencyId);
        var rate = await GetFxRateAsync(req.CurrencyId);
        var paidUsd = await _db.ShipmentSupplierPaymentRecords.Where(r => r.PaymentDueId == dueId).SumAsync(r => (decimal?)r.ValueUsd) ?? 0;
        return Ok(new PaymentDueResponse(due.Id, due.DueDate, due.Amount, due.CurrencyId, currency?.Code ?? "", due.Label, paidUsd, due.Amount / rate));
    }

    [HttpDelete("supplier-payment/dues/{dueId:int}")]
    public async Task<IActionResult> DeletePaymentDue(int shipmentId, int dueId)
    {
        var denied = await CheckWriteAccessAsync(shipmentId);
        if (denied is not null) return denied;

        var due = await _db.ShipmentPaymentDues.FirstOrDefaultAsync(d => d.Id == dueId && d.ShipmentId == shipmentId);
        if (due is null) return NotFound();

        _db.ShipmentPaymentDues.Remove(due);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("banking")]
    public async Task<IActionResult> UpsertBanking(int shipmentId, ShipmentBankingRequest req)
    {
        var denied = await CheckWriteAccessAsync(shipmentId); 
        if (denied is not null) return denied;
        var lockDenied = await _sectionLock.EnsureNotLockedAsync("Shipment", shipmentId, "banking");
        if (lockDenied is not null) return lockDenied;
        
        if (!await _db.Shipments.AnyAsync(s => s.Id == shipmentId)) return NotFound();

        var entity = await _db.ShipmentBankings.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId);
        if (entity is null) { entity = new ShipmentBanking { ShipmentId = shipmentId }; _db.ShipmentBankings.Add(entity); }

        entity.SenderBankId = req.SenderBankId;
        entity.OsDocDispatchDate = req.OsDocDispatchDate;
        entity.OsDocDispatchedViaId = req.OsDocDispatchedViaId;
        entity.OsDocTrackingNumber = req.OsDocTrackingNumber;
        entity.ReceivingBankId = req.ReceivingBankId;
        entity.NecessaryGoodType = req.NecessaryGoodType;
        entity.CollectionRefNo = req.CollectionRefNo;
        entity.CollectionValue = req.CollectionValue;
        entity.CollectionCurrencyId = req.CollectionCurrencyId;
        entity.TenorId = req.TenorId;

        if (req.CollectionValue.HasValue && req.SenderBankId.HasValue)
        {
            var senderBank = await _db.SenderBanks.FindAsync(req.SenderBankId);
            if (senderBank is not null)
                entity.SenderBankCharges = Math.Max(req.CollectionValue.Value * senderBank.ChargeRate, senderBank.MinimumChargeAed);
        }

        if (req.CollectionValue.HasValue && req.ReceivingBankId.HasValue)
        {
            var receiverBank = await _db.ReceiverBanks.FindAsync(req.ReceivingBankId);
            if (receiverBank is not null)
                entity.ReceiverBankCharges = req.CollectionValue.Value * receiverBank.TotalChargeRate;
        }

        await _db.SaveChangesAsync();
        return Ok(entity);
    }
}
