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
        public List<BusinessUnit> BusinessUnits = new();
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
        BusinessUnits = await _db.BusinessUnits.ToListAsync(),
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
            if (RowIsBlank(ws, row, 70)) continue;

            // --- PO section (cols 1-19) ---
            var poNumber = S(ws, row, 1);
            var blAwbNo = S(ws, row, 28);
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
                    var buCode = S(ws, row, 2);
                    var businessUnit = lk.BusinessUnits.FirstOrDefault(b => b.Code == buCode || b.Name == buCode);
                    if (businessUnit is null) { errors.Add($"Row {row}: Business Unit '{buCode}' not found (checked both Code and Name)."); continue; }

                    var divisionCode = S(ws, row, 3);
                    var division = await _db.Divisions.FirstOrDefaultAsync(d => d.BusinessUnitId == businessUnit.Id && (d.Code == divisionCode || d.Name == divisionCode));
                    if (division is null) { errors.Add($"Row {row}: Division '{divisionCode}' not found under Business Unit '{buCode}'."); continue; }

                    var supplierName = S(ws, row, 4);
                    var supplier = lk.Partners.FirstOrDefault(p => p.Name == supplierName && p.IsSupplier);
                    if (supplier is null) { errors.Add($"Row {row}: Supplier '{supplierName}' not found (or not flagged as Supplier)."); continue; }

                    var brandName = S(ws, row, 5);
                    var brand = lk.Partners.FirstOrDefault(p => p.Name == brandName && p.IsBrandManufacturer);

                    var approvalName = S(ws, row, 6);
                    var approval = lk.ApprovalTypes.FirstOrDefault(a => a.Name == approvalName);
                    if (approval is null) { errors.Add($"Row {row}: Approval Type '{approvalName}' not found."); continue; }

                    var consigneeName = S(ws, row, 7);
                    var consignee = lk.Partners.FirstOrDefault(p => p.Name == consigneeName && p.IsConsignee);
                    if (consignee is null) { errors.Add($"Row {row}: Consignee '{consigneeName}' not found (or not flagged as Consignee)."); continue; }

                    var paymentTermName = S(ws, row, 10);
                    var paymentTerm = lk.PaymentTerms.FirstOrDefault(t => t.Name == paymentTermName);
                    if (paymentTerm is null) { errors.Add($"Row {row}: Payment Term '{paymentTermName}' not found."); continue; }

                    var incotermCode = S(ws, row, 16);
                    var incoterm = lk.Incoterms.FirstOrDefault(t => t.Code == incotermCode);
                    if (incoterm is null) { errors.Add($"Row {row}: Incoterm '{incotermCode}' not found."); continue; }

                    var originName = S(ws, row, 17);
                    var origin = lk.OriginCountries.FirstOrDefault(o => o.Name == originName);
                    if (origin is null) { errors.Add($"Row {row}: Origin Country '{originName}' not found."); continue; }

                    var shipmentModeName = S(ws, row, 18);
                    var shipmentMode = await _db.ShipmentModes.FirstOrDefaultAsync(m => m.Name == shipmentModeName);
                    if (shipmentMode is null) { errors.Add($"Row {row}: Shipment Mode '{shipmentModeName}' not found."); continue; }

                    po = new PurchaseOrder
                    {
                        PoNumber = poNumber,
                        BusinessUnitId = businessUnit.Id,
                        DivisionId = division.Id,
                        SupplierId = supplier.Id,
                        BrandManufacturerId = brand?.Id ?? supplier.Id,
                        ApprovalTypeId = approval.Id,
                        ConsigneeId = consignee.Id,
                        SupplierPiNo = S(ws, row, 8),
                        SupplierPiDate = Dt(ws, row, 9),
                        SupplierPaymentTermId = paymentTerm.Id,
                        ReceivedSignedPiDate = Dt(ws, row, 11),
                        SentSignedPiDate = Dt(ws, row, 12),
                        BuPoDate = Dt(ws, row, 13),
                        OrderExecutionDate = Dt(ws, row, 14),
                        LatestShippingDate = Dt(ws, row, 15),
                        IncotermId = incoterm.Id,
                        OriginCountryId = origin.Id,
                        ShipmentModeId = shipmentMode.Id,
                        BuShippingBudget = D(ws, row, 19),
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

            // --- PO Line Item section (cols 20-26; col 27 is computed, ignored) ---
            var modelName = S(ws, row, 21);
            if (modelName is null) { errors.Add($"Row {row}: MODEL is required — row skipped."); continue; }

            var poLineKey = (poNumber, modelName);
            if (!poLineCache.TryGetValue(poLineKey, out var poLine))
            {
                poLine = await _db.PurchaseOrderLineItems.FirstOrDefaultAsync(li => li.PurchaseOrderId == po.Id && li.ModelProduct!.Name == modelName);
                if (poLine is null)
                {
                    var catName = S(ws, row, 20);
                    var category = lk.Categories.FirstOrDefault(c => c.Name == catName);
                    var model = lk.Models_.FirstOrDefault(m => m.Name == modelName);
                    var typeName = S(ws, row, 22);
                    var type = lk.Types.FirstOrDefault(t => t.Name == typeName);
                    var uomCode = S(ws, row, 23);
                    var uom = lk.Uoms.FirstOrDefault(u => u.Code == uomCode);
                    var currencyCode = S(ws, row, 26);
                    var currency = lk.Currencies.FirstOrDefault(c => c.Code == currencyCode);

                    if (category is null) { errors.Add($"Row {row}: Product Category '{catName}' not found."); continue; }
                    if (model is null) { errors.Add($"Row {row}: Model/Product '{modelName}' not found."); continue; }
                    if (type is null) { errors.Add($"Row {row}: Product Type '{typeName}' not found."); continue; }
                    if (uom is null) { errors.Add($"Row {row}: Unit of Measure '{uomCode}' not found."); continue; }
                    if (currency is null) { errors.Add($"Row {row}: Currency '{currencyCode}' not found."); continue; }

                    var lineQty = D(ws, row, 24) ?? 0;
                    var lineUnitPrice = D(ws, row, 25) ?? 0;
                    var lineTotal = lineQty * lineUnitPrice;
                    var lineFxRate = await _fx.GetRateToUsdAsync(currency.Id);

                    poLine = new PurchaseOrderLineItem
                    {
                        PurchaseOrderId = po.Id,
                        ProductCategoryId = category.Id,
                        ModelProductId = model.Id,
                        ProductTypeId = type.Id,
                        Qty = lineQty,
                        UnitOfMeasureId = uom.Id,
                        UnitPrice = lineUnitPrice,
                        CurrencyId = currency.Id,
                        Total = lineTotal,
                        TotalUsd = lineTotal / lineFxRate
                    };
                    _db.PurchaseOrderLineItems.Add(poLine);
                    await _db.SaveChangesAsync();
                    poLinesCreated++;
                }
                poLineCache[poLineKey] = poLine;
            }

            // --- Shipment section (cols 28-35) ---
            if (!shipmentCache.TryGetValue(blAwbNo, out var shipment))
            {
                shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
                if (shipment is null)
                {
                    var shippingLineName = S(ws, row, 33);
                    var shippingLine = lk.ShippingLines.FirstOrDefault(l => l.Name == shippingLineName);
                    if (shippingLine is null) { errors.Add($"Row {row}: Shipping Line '{shippingLineName}' not found."); continue; }

                    var statusText = S(ws, row, 32);
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
                        BlAwbDate = Dt(ws, row, 29),
                        Etd = Dt(ws, row, 30),
                        Eta = Dt(ws, row, 31),
                        Status = status,
                        ShippingLineId = shippingLine.Id,
                        Fcl20Count = I(ws, row, 34) ?? 0,
                        Fcl40Count = I(ws, row, 35) ?? 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.Shipments.Add(shipment);
                    await _db.SaveChangesAsync();
                    shipmentsCreated++;
                }
                shipmentCache[blAwbNo] = shipment;
            }

            // --- Shipment Line Item (cols 36, 38; col 37 is reference-only, ignored) ---
            var existingShipLine = await _db.ShipmentLineItems.FirstOrDefaultAsync(sl => sl.ShipmentId == shipment.Id && sl.PurchaseOrderLineItemId == poLine.Id);
            if (existingShipLine is null)
            {
                existingShipLine = new ShipmentLineItem
                {
                    ShipmentId = shipment.Id,
                    PurchaseOrderLineItemId = poLine.Id,
                    QtyInBl = D(ws, row, 36) ?? 0,
                    ItemSubtotal = D(ws, row, 38) ?? 0
                };
                _db.ShipmentLineItems.Add(existingShipLine);
                await _db.SaveChangesAsync();
                shipmentLinesCreated++;
            }

            await UpsertShipmentSections(shipment.Id, ws, row, lk);

            // --- Last Offshore Item Detail (per line item; col 58 = Approved MOT Unit Price USD) ---
            var lastOffshoreUnitPrice = D(ws, row, 58);
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

            // --- Clearance Remarks (col 60) ---
            var remarks = S(ws, row, 60);
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

        var totalCreated = posCreated + poLinesCreated + shipmentsCreated + shipmentLinesCreated;
        return new SheetUploadResult("Main", totalCreated, sectionsUpdated, errors);
    }

    // Forwarder, Draft Docs/Supplier Full Set, Banking, ACD, MOT, Last
    // Offshore Details — all 1:1 with the shipment, so each is simply
    // found-or-created then overwritten with this row's values. Safe to
    // call repeatedly for the same shipment (later rows just re-save
    // the same data, by construction of the sheet's own shape).
    // the same data, by construction of the sheet's own shape).
    private async Task UpsertShipmentSections(int shipmentId, IXLWorksheet ws, int row, LookupCache lk)
    {
        // Forwarder (cols 39-43)
        var forwarderName = S(ws, row, 39);
        if (forwarderName is not null)
        {
            var forwarder = lk.Forwarders.FirstOrDefault(f => f.Name == forwarderName);
            var fwd = await _db.ShipmentForwarders.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId) ?? new ShipmentForwarder { ShipmentId = shipmentId };
            if (fwd.Id == 0) _db.ShipmentForwarders.Add(fwd);
            fwd.ForwarderId = forwarder?.Id;
            var fwdCost = D(ws, row, 40);
            fwd.ActualShippingCost = fwdCost;
            var fwdCurrency = lk.Currencies.FirstOrDefault(c => c.Code == S(ws, row, 41));
            fwd.CurrencyId = fwdCurrency?.Id;
            fwd.ActualShippingCostUsd = fwdCost.HasValue && fwdCurrency is not null ? fwdCost.Value / await _fx.GetRateToUsdAsync(fwdCurrency.Id) : null;
            fwd.AmountSaved = D(ws, row, 42);
            fwd.MarineInsurance = B(ws, row, 43) ?? false;
        }

        // Draft Documents (cols 44-45) + Supplier Full Set (cols 46-50)
        var draftDate = Dt(ws, row, 44);
        var finalConfirmedDate = Dt(ws, row, 45);
        if (draftDate.HasValue || finalConfirmedDate.HasValue)
        {
            var docs = await _db.ShipmentDraftDocuments.FirstOrDefaultAsync(d => d.ShipmentId == shipmentId) ?? new ShipmentDraftDocuments { ShipmentId = shipmentId };
            if (docs.Id == 0) _db.ShipmentDraftDocuments.Add(docs);
            docs.InitialDraftReceivedDate = draftDate;
            docs.FinalDraftConfirmedDate = finalConfirmedDate;
        }

        var supplierInvoiceNo = S(ws, row, 46);
        if (supplierInvoiceNo is not null)
        {
            var fullSet = await _db.ShipmentSupplierFullSets.FirstOrDefaultAsync(f => f.ShipmentId == shipmentId) ?? new ShipmentSupplierFullSet { ShipmentId = shipmentId };
            if (fullSet.Id == 0) _db.ShipmentSupplierFullSets.Add(fullSet);
            fullSet.SupplierInvoiceNo = supplierInvoiceNo;
            fullSet.SupplierInvoiceDate = Dt(ws, row, 47);
            fullSet.FsDispatchDate = Dt(ws, row, 48);
            fullSet.FsTrackingNumber = S(ws, row, 49);
            fullSet.FsReceivedDate = Dt(ws, row, 50);
        }

        // Banking (col 51 — dispatch tracking number only, per this sheet's reduced scope)
        var bankTrackingNo = S(ws, row, 51);
        if (bankTrackingNo is not null)
        {
            var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(b => b.ShipmentId == shipmentId) ?? new ShipmentBanking { ShipmentId = shipmentId };
            if (banking.Id == 0) _db.ShipmentBankings.Add(banking);
            banking.OsDocTrackingNumber = bankTrackingNo;
        }

        // ACD (col 52)
        var acdCost = D(ws, row, 52);
        if (acdCost.HasValue)
        {
            var acd = await _db.ShipmentAcds.FirstOrDefaultAsync(a => a.ShipmentId == shipmentId) ?? new ShipmentAcd { ShipmentId = shipmentId };
            if (acd.Id == 0) _db.ShipmentAcds.Add(acd);
            acd.CostUsd = acdCost;
        }

        // MOT (cols 53-54)
        var motPiNo = S(ws, row, 53);
        if (motPiNo is not null)
        {
            var mot = await _db.ShipmentMots.FirstOrDefaultAsync(m => m.ShipmentId == shipmentId) ?? new ShipmentMot { ShipmentId = shipmentId };
            if (mot.Id == 0) _db.ShipmentMots.Add(mot);
            mot.OffshoreApprovedPiNumber = motPiNo;
            mot.ApprovalDate = Dt(ws, row, 54);
        }

        // Last Offshore Details header (cols 55-57; 58-59 handled per-line-item by the caller)
        var offshoreInvoiceNo = S(ws, row, 55);
        var inspectionNo = S(ws, row, 56);
        var grn = S(ws, row, 57);
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
