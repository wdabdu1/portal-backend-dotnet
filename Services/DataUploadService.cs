using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Services;

// Parses the "Migration Workbook" (Main + two Supplier Payment sheets,
// built from the person's own original spreadsheet headers). One row
// on Main = one shipment's line item; PO-level columns repeat across
// every row of the same PO, matching their original sheet's shape.
// PO/Shipment/PO-Line-Item are found-or-created keyed by PoNumber /
// BlAwbNo / (PoNumber+Model) respectively, so repeated rows for the
// same PO or shipment correctly reuse the same record rather than
// duplicating it — the sub-sections are simply re-saved each time,
// which is safe since repeated rows for the same shipment carry the
// same values by construction.
public class DataUploadService
{
    private readonly ShippingPortalDbContext _db;
    private readonly FxRateService _fx;
    public DataUploadService(ShippingPortalDbContext db, FxRateService fx) { _db = db; _fx = fx; }

    // Main sheet uses a 3-row header block (section label / column
    // header / example) — data starts row 4. The two Payment sheets
    // use the standard template shape (title / note / header / example)
    // — data starts row 6.
    private const int MainFirstDataRow = 4;
    private const int PaymentFirstDataRow = 6;

    private static string? S(IXLWorksheet ws, int row, int col)
    {
        var v = ws.Cell(row, col).GetString().Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
    private static decimal? D(IXLWorksheet ws, int row, int col)
    {
        var s = S(ws, row, col);
        return s is not null && decimal.TryParse(s, out var d) ? d : null;
    }
    private static int? I(IXLWorksheet ws, int row, int col)
    {
        var s = S(ws, row, col);
        return s is not null && int.TryParse(s, out var i) ? i : null;
    }
    private static bool? B(IXLWorksheet ws, int row, int col)
    {
        var s = S(ws, row, col)?.ToUpperInvariant();
        return s switch { "TRUE" => true, "FALSE" => false, _ => null };
    }
    private static DateOnly? Dt(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.TryGetValue(out DateTime dt)) return DateOnly.FromDateTime(dt);
        var s = S(ws, row, col);
        return s is not null && DateOnly.TryParse(s, out var parsed) ? parsed : null;
    }
    private static bool RowIsBlank(IXLWorksheet ws, int row, int lastCol)
    {
        for (int c = 1; c <= lastCol; c++)
            if (!string.IsNullOrWhiteSpace(ws.Cell(row, c).GetString())) return false;
        return true;
    }

    public async Task<UploadSummary> ProcessAsync(Stream fileStream)
    {
        using var wb = new XLWorkbook(fileStream);
        var results = new List<SheetUploadResult>();

        var mainWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Main");
        if (mainWs is not null) results.Add(await ProcessMain(mainWs));

        var dueWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Supplier_Payment_Due_Schedule");
        if (dueWs is not null) results.Add(await ProcessPaymentDueSchedule(dueWs));

        var recWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Supplier_Payment_Records");
        if (recWs is not null) results.Add(await ProcessPaymentRecords(recWs));

        return new UploadSummary(results);
    }

    // Lightweight lookups, loaded once and reused across every row.
    private class LookupCache
    {
        public List<BusinessPartner> Partners = new();
        public List<ApprovalType> ApprovalTypes = new();
        public List<BusinessUnit> BusinessUnits = new(); // not on Main sheet directly but kept for future use
        public List<PaymentTerm> PaymentTerms = new();
        public List<Incoterm> Incoterms = new();
        public List<OriginCountry> OriginCountries = new();
        public List<ProductCategory> Categories = new();
        public List<ModelProduct> Models_ = new();
        public List<ProductType> Types = new();
        public List<UnitOfMeasure> Uoms = new();
        public List<Currency> Currencies = new();
        public List<ShippingLine> ShippingLines = new();
        public List<Forwarder> Forwarders = new();
        public List<Courier> Couriers = new();
    }

    private async Task<LookupCache> LoadLookups() => new LookupCache
    {
        Partners = await _db.BusinessPartners.ToListAsync(),
        ApprovalTypes = await _db.ApprovalTypes.ToListAsync(),
        PaymentTerms = await _db.PaymentTerms.ToListAsync(),
        Incoterms = await _db.Incoterms.ToListAsync(),
        OriginCountries = await _db.OriginCountries.ToListAsync(),
        Categories = await _db.ProductCategories.ToListAsync(),
        Models_ = await _db.ModelProducts.ToListAsync(),
        Types = await _db.ProductTypes.ToListAsync(),
        Uoms = await _db.UnitsOfMeasure.ToListAsync(),
        Currencies = await _db.Currencies.ToListAsync(),
        ShippingLines = await _db.ShippingLines.ToListAsync(),
        Forwarders = await _db.Forwarders.ToListAsync(),
        Couriers = await _db.Couriers.ToListAsync()
    };

  private async Task<SheetUploadResult> ProcessMain(IXLWorksheet ws)
    {
        var errors = new List<string>();
        int posCreated = 0, poLinesCreated = 0, shipmentsCreated = 0, shipmentLinesCreated = 0, sectionsUpdated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? MainFirstDataRow - 1;
        var lk = await LoadLookups();

        var poCache = new Dictionary<string, PurchaseOrder>();
        var poLineCache = new Dictionary<(string Po, string Model), PurchaseOrderLineItem>();
        var shipmentCache = new Dictionary<string, Shipment>();

        for (int row = MainFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 67)) continue;

            // --- PO section (cols 1-16) ---
            var poNumber = S(ws, row, 1);
            var blAwbNo = S(ws, row, 25);
            if (poNumber is null || blAwbNo is null)
            {
                errors.Add($"Row {row}: PoNumber and B/L NO are both required — row skipped.");
                continue;
            }

            if (!poCache.TryGetValue(poNumber, out var po))
            {
                po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNumber == poNumber);
                if (po is null)
                {
                    var supplierName = S(ws, row, 2);
                    var supplier = lk.Partners.FirstOrDefault(p => p.Name == supplierName && p.IsSupplier);
                    var brandName = S(ws, row, 3);
                    var brand = lk.Partners.FirstOrDefault(p => p.Name == brandName && p.IsBrandManufacturer);
                    var approvalName = S(ws, row, 4);
                    var approval = lk.ApprovalTypes.FirstOrDefault(a => a.Name == approvalName);
                    var consigneeName = S(ws, row, 5);
                    var consignee = lk.Partners.FirstOrDefault(p => p.Name == consigneeName && p.IsConsignee);
                    var paymentTermName = S(ws, row, 8);
                    var paymentTerm = lk.PaymentTerms.FirstOrDefault(t => t.Name == paymentTermName);
                    var incotermCode = S(ws, row, 14);
                    var incoterm = lk.Incoterms.FirstOrDefault(t => t.Code == incotermCode);
                    var originName = S(ws, row, 15);
                    var origin = lk.OriginCountries.FirstOrDefault(o => o.Name == originName);

                    if (supplier is null) { errors.Add($"Row {row}: Supplier '{supplierName}' not found (or not flagged as Supplier)."); continue; }
                    if (consignee is null) { errors.Add($"Row {row}: Consignee '{consigneeName}' not found (or not flagged as Consignee)."); continue; }

                    po = new PurchaseOrder
                    {
                        PoNumber = poNumber,
                        SupplierId = supplier.Id,
                        BrandManufacturerId = brand?.Id ?? supplier.Id,
                        ApprovalTypeId = approval?.Id ?? 0,
                        ConsigneeId = consignee.Id,
                        SupplierPiNo = S(ws, row, 6),
                        SupplierPiDate = Dt(ws, row, 7),
                        SupplierPaymentTermId = paymentTerm?.Id ?? 0,
                        ReceivedSignedPiDate = Dt(ws, row, 9),
                        SentSignedPiDate = Dt(ws, row, 10),
                        BuPoDate = Dt(ws, row, 11),
                        OrderExecutionDate = Dt(ws, row, 12),
                        LatestShippingDate = Dt(ws, row, 13),
                        IncotermId = incoterm?.Id ?? 0,
                        OriginCountryId = origin?.Id ?? 0,
                        BuShippingBudget = D(ws, row, 16),
                        Status = OrderStatus.Confirmed,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.PurchaseOrders.Add(po);
                    await _db.SaveChangesAsync(); // need po.Id for line items/shipments below
                    posCreated++;
                }
                poCache[poNumber] = po;
            }

            // --- PO Line Item section (cols 17-23; col 24 is computed, ignored) ---
            var modelName = S(ws, row, 18);
            if (modelName is null) { errors.Add($"Row {row}: MODEL is required — row skipped."); continue; }

            var poLineKey = (poNumber, modelName);
            if (!poLineCache.TryGetValue(poLineKey, out var poLine))
            {
                poLine = await _db.PurchaseOrderLineItems.FirstOrDefaultAsync(li => li.PurchaseOrderId == po.Id && li.ModelProduct!.Name == modelName);
                if (poLine is null)
                {
                    var catName = S(ws, row, 17);
                    var category = lk.Categories.FirstOrDefault(c => c.Name == catName);
                    var model = lk.Models_.FirstOrDefault(m => m.Name == modelName);
                    var typeName = S(ws, row, 19);
                    var type = lk.Types.FirstOrDefault(t => t.Name == typeName);
                    var uomCode = S(ws, row, 20);
                    var uom = lk.Uoms.FirstOrDefault(u => u.Code == uomCode);
                    var currencyCode = S(ws, row, 23);
                    var currency = lk.Currencies.FirstOrDefault(c => c.Code == currencyCode);

                    if (category is null) { errors.Add($"Row {row}: Product Category '{catName}' not found."); continue; }
                    if (model is null) { errors.Add($"Row {row}: Model/Product '{modelName}' not found."); continue; }
                    if (currency is null) { errors.Add($"Row {row}: Currency '{currencyCode}' not found."); continue; }

                    poLine = new PurchaseOrderLineItem
                    {
                        PurchaseOrderId = po.Id,
                        ProductCategoryId = category.Id,
                        ModelProductId = model.Id,
                        ProductTypeId = type?.Id ?? 0,
                        Qty = D(ws, row, 21) ?? 0,
                        UnitOfMeasureId = uom?.Id ?? 0,
                        UnitPrice = D(ws, row, 22) ?? 0,
                        CurrencyId = currency.Id
                    };
                    _db.PurchaseOrderLineItems.Add(poLine);
                    await _db.SaveChangesAsync();
                    poLinesCreated++;
                }
                poLineCache[poLineKey] = poLine;
            }

            // --- Shipment section (cols 25-32) ---
            if (!shipmentCache.TryGetValue(blAwbNo, out var shipment))
            {
                shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
                if (shipment is null)
                {
                    var shippingLineName = S(ws, row, 30);
                    var shippingLine = lk.ShippingLines.FirstOrDefault(l => l.Name == shippingLineName);
                    var statusText = S(ws, row, 29);
                    var status = statusText?.ToUpperInvariant() switch
                    {
                        "CONFIRMED" => ShipmentStatus.Confirmed,
                        "CANCELLED" => ShipmentStatus.Cancelled,
                        _ => ShipmentStatus.Draft
                    };

                    shipment = new Shipment
                    {
                        PurchaseOrderId = po.Id,
                        BlAwbNo = blAwbNo,
                        BlAwbDate = Dt(ws, row, 26),
                        Etd = Dt(ws, row, 27),
                        Eta = Dt(ws, row, 28),
                        Status = status,
                        ShippingLineId = shippingLine?.Id,
                        Fcl20Count = I(ws, row, 31),
                        Fcl40Count = I(ws, row, 32),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.Shipments.Add(shipment);
                    await _db.SaveChangesAsync();
                    shipmentsCreated++;
                }
                shipmentCache[blAwbNo] = shipment;
            }

            // --- Shipment Line Item (cols 33, 35; col 34 is reference-only, ignored) ---
            var existingShipLine = await _db.ShipmentLineItems.FirstOrDefaultAsync(sl => sl.ShipmentId == shipment.Id && sl.PurchaseOrderLineItemId == poLine.Id);
            if (existingShipLine is null)
            {
                existingShipLine = new ShipmentLineItem
                {
                    ShipmentId = shipment.Id,
                    PurchaseOrderLineItemId = poLine.Id,
                    QtyInBl = D(ws, row, 33) ?? 0,
                    ItemSubtotal = D(ws, row, 35) ?? 0
                };
                _db.ShipmentLineItems.Add(existingShipLine);
                await _db.SaveChangesAsync();
                shipmentLinesCreated++;
            }

            await UpsertShipmentSections(shipment.Id, ws, row, lk);

            // --- Last Offshore Item Detail (per line item; col 55 = Approved MOT Unit Price USD) ---
            var lastOffshoreUnitPrice = D(ws, row, 55);
            if (lastOffshoreUnitPrice.HasValue)
            {
                var existingItemDetail = await _db.LastOffshoreItemDetails.FirstOrDefaultAsync(d => d.ShipmentLineItemId == existingShipLine.Id);
                if (existingItemDetail is null)
                {
                    _db.LastOffshoreItemDetails.Add(new LastOffshoreItemDetail { ShipmentLineItemId = existingShipLine.Id, UnitPrice = lastOffshoreUnitPrice });
                }
                else
                {
                    existingItemDetail.UnitPrice = lastOffshoreUnitPrice;
                }
            }

            // --- Clearance Remarks (col 57) ---
            var remarks = S(ws, row, 57);
            if (remarks is not null)
            {
                var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == shipment.Id);
                if (clearance is null)
                {
                    _db.Clearances.Add(new Clearance { ShipmentId = shipment.Id, Notes = remarks });
                }
                else
                {
                    clearance.Notes = remarks;
                }
            }

            await _db.SaveChangesAsync();
            sectionsUpdated++;
        }

    // Forwarder, Draft Docs/Supplier Full Set, Banking, ACD, MOT, Last
    // Offshore Details — all 1:1 with the shipment, so each is simply
    // found-or-created then overwritten with this row's values. Safe to
    // call repeatedly for the same shipment (later rows just re-save
    // the same data, by construction of the sheet's own shape).
    private async Task UpsertShipmentSections(int shipmentId, IXLWorksheet ws, int row, LookupCache lk)
    {
        // Forwarder (cols 36-40)
        var forwarderName = S(ws, row, 36);
        if (forwarderName is not null)
        {
            var forwarder = lk.Forwarders.FirstOrDefault(f => f.Name == forwarderName);
            var fwd = await _db.ShipmentForwarders.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId) ?? new ShipmentForwarder { ShipmentId = shipmentId };
            if (fwd.Id == 0) _db.ShipmentForwarders.Add(fwd);
            fwd.ForwarderId = forwarder?.Id;
            fwd.ActualShippingCost = D(ws, row, 37);
            var fwdCurrency = lk.Currencies.FirstOrDefault(c => c.Code == S(ws, row, 38));
            fwd.CurrencyId = fwdCurrency?.Id;
            fwd.AmountSaved = D(ws, row, 39);
            fwd.MarineInsurance = B(ws, row, 40) ?? false;
        }

        // Draft Documents (cols 41-42) + Supplier Full Set (cols 43-47)
        var draftDate = Dt(ws, row, 41);
        var finalConfirmedDate = Dt(ws, row, 42);
        if (draftDate.HasValue || finalConfirmedDate.HasValue)
        {
            var docs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId) ?? new ShipmentDraftDocuments { ShipmentId = shipmentId };
            if (docs.Id == 0) _db.ShipmentDraftDocuments.Add(docs);
            docs.InitialDraftReceivedDate = draftDate;
            docs.FinalDraftConfirmedDate = finalConfirmedDate;
        }

        var supplierInvoiceNo = S(ws, row, 43);
        if (supplierInvoiceNo is not null)
        {
            var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId) ?? new ShipmentSupplierFullSet { ShipmentId = shipmentId };
            if (fullSet.Id == 0) _db.ShipmentSupplierFullSets.Add(fullSet);
            fullSet.SupplierInvoiceNo = supplierInvoiceNo;
            fullSet.SupplierInvoiceDate = Dt(ws, row, 44);
            fullSet.FsDispatchDate = Dt(ws, row, 45);
            fullSet.FsTrackingNumber = S(ws, row, 46);
            fullSet.FsReceivedDate = Dt(ws, row, 47);
        }

        // Banking (col 48 — dispatch tracking number only, per this sheet's reduced scope)
        var bankTrackingNo = S(ws, row, 48);
        if (bankTrackingNo is not null)
        {
            var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(b => b.ShipmentId == shipmentId) ?? new ShipmentBanking { ShipmentId = shipmentId };
            if (banking.Id == 0) _db.ShipmentBankings.Add(banking);
            banking.OsDocTrackingNumber = bankTrackingNo;
        }

        // ACD (col 49)
        var acdCost = D(ws, row, 49);
        if (acdCost.HasValue)
        {
            var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(a => a.ShipmentId == shipmentId) ?? new ShipmentAcd { ShipmentId = shipmentId };
            if (acd.Id == 0) _db.ShipmentAcds.Add(acd);
            acd.CostUsd = acdCost;
        }

        // MOT (cols 50-51)
        var motPiNo = S(ws, row, 50);
        if (motPiNo is not null)
        {
            var mot = await _db.ShipmentMots.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId) ?? new ShipmentMot { ShipmentId = shipmentId };
            if (mot.Id == 0) _db.ShipmentMots.Add(mot);
            mot.OffshoreApprovedPiNumber = motPiNo;
            mot.ApprovalDate = Dt(ws, row, 51);
        }

        // Last Offshore Details header (cols 52-54; 55-56 handled per-line-item by the caller)
        var offshoreInvoiceNo = S(ws, row, 52);
        var inspectionNo = S(ws, row, 53);
        var grn = S(ws, row, 54);
        if (offshoreInvoiceNo is not null || inspectionNo is not null || grn is not null)
        {
            var offshore = await _db.LastOffshoreDetails.FirstOrDefaultAsync(o => o.ShipmentId == shipmentId) ?? new LastOffshoreDetail { ShipmentId = shipmentId };
            if (offshore.Id == 0) _db.LastOffshoreDetails.Add(offshore);
            offshore.InvoiceNo = offshoreInvoiceNo;
            offshore.InspectionNo = inspectionNo;
            offshore.Grn = grn;
        }

        await _db.SaveChangesAsync();
    }

    private async Task<SheetUploadResult> ProcessPaymentDueSchedule(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;
        var currencies = await _db.Currencies.ToListAsync();

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var blAwbNo = S(ws, row, 1); var dueDate = Dt(ws, row, 2); var amount = D(ws, row, 3); var curCode = S(ws, row, 4);
            if (blAwbNo is null || dueDate is null || amount is null || curCode is null)
            { errors.Add($"Row {row}: B/L NO, DUE DATE, DUE AMOUNT, and CURRENCY are all required."); continue; }

            var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
            if (shipment is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' not found — upload Main first."); continue; }
            var currency = currencies.FirstOrDefault(c => c.Code == curCode);
            if (currency is null) { errors.Add($"Row {row}: Currency '{curCode}' not found."); continue; }

            var label = S(ws, row, 5);
            var existing = await _db.ShipmentPaymentDues.FirstOrDefaultAsync(d => d.ShipmentId == shipment.Id && d.Label == label);
            if (existing is null)
            {
                _db.ShipmentPaymentDues.Add(new ShipmentPaymentDue { ShipmentId = shipment.Id, DueDate = dueDate.Value, Amount = amount.Value, CurrencyId = currency.Id, Label = label });
                created++;
            }
            else
            {
                existing.DueDate = dueDate.Value; existing.Amount = amount.Value; existing.CurrencyId = currency.Id;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Supplier_Payment_Due_Schedule", created, updated, errors);
    }

    private async Task<SheetUploadResult> ProcessPaymentRecords(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;
        var currencies = await _db.Currencies.ToListAsync();

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var blAwbNo = S(ws, row, 1); var paymentDate = Dt(ws, row, 2); var curCode = S(ws, row, 3); var value = D(ws, row, 4);
            if (blAwbNo is null || paymentDate is null || curCode is null || value is null)
            { errors.Add($"Row {row}: B/L NO, PAYMENT DATE, CURRENCY, and VALUE are all required."); continue; }

            var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
            if (shipment is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' not found — upload Main first."); continue; }
            var currency = currencies.FirstOrDefault(c => c.Code == curCode);
            if (currency is null) { errors.Add($"Row {row}: Currency '{curCode}' not found."); continue; }

            var dueLabel = S(ws, row, 5);
            int? paymentDueId = null;
            if (dueLabel is not null)
            {
                var due = await _db.ShipmentPaymentDues.FirstOrDefaultAsync(d => d.ShipmentId == shipment.Id && d.Label == dueLabel);
                paymentDueId = due?.Id;
            }

            var rate = await _fx.GetRateToUsdAsync(currency.Id);

            // No natural unique key for a payment record beyond its own
            // content — always inserted fresh, matching how actual
            // payments are recorded one at a time in the portal itself.
            _db.ShipmentSupplierPaymentRecords.Add(new ShipmentSupplierPaymentRecord
            {
                ShipmentId = shipment.Id, PaymentDate = paymentDate.Value, CurrencyId = currency.Id,
                Value = value.Value, ValueUsd = value.Value / rate, PaymentDueId = paymentDueId
            });
            created++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Supplier_Payment_Records", created, updated, errors);
    }
}

        var totalCreated = posCreated + poLinesCreated + shipmentsCreated + shipmentLinesCreated;
        return new SheetUploadResult("Main", totalCreated, sectionsUpdated, errors);
    }
