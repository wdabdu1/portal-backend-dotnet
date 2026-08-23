using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Lookups;
using ShippingPortal.Api.Models.Logistics;

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

        var chainWs = wb.Worksheets.FirstOrDefault(w => w.Name == "PO_Offshore_Chain");
        if (chainWs is not null) results.Add(await ProcessPoOffshoreChain(chainWs));

        var clrGeneralWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_General");
        if (clrGeneralWs is not null) results.Add(await ProcessClearanceGeneral(clrGeneralWs));

        var clrCostWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Cost_Estimate");
        if (clrCostWs is not null) results.Add(await ProcessClearanceCostEstimate(clrCostWs));

        var route1Ws = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Route1");
        if (route1Ws is not null) results.Add(await ProcessClearanceRoute1(route1Ws));

        var route2Ws = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Route2");
        if (route2Ws is not null) results.Add(await ProcessClearanceRoute2(route2Ws));

        var actualChargesWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Actual_Charges");
        if (actualChargesWs is not null) results.Add(await ProcessClearanceActualCharges(actualChargesWs));

        var truckingWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Trucking");
        if (truckingWs is not null) results.Add(await ProcessTrucking(truckingWs));

        var fzStockWs = wb.Worksheets.FirstOrDefault(w => w.Name == "FZ_Stock_Opening_Balance");
        if (fzStockWs is not null) results.Add(await ProcessFzStockOpeningBalance(fzStockWs));

        var tpWs = wb.Worksheets.FirstOrDefault(w => w.Name == "TP_Confirmations");
        if (tpWs is not null) results.Add(await ProcessTpConfirmations(tpWs));

        var bankCollWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Bank_Collection_Records");
        if (bankCollWs is not null) results.Add(await ProcessBankCollectionRecords(bankCollWs));

        var route3Ws = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Route3");
        if (route3Ws is not null) results.Add(await ProcessClearanceRoute3(route3Ws));

        var route3WWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Clearance_Route3_Withdrawals");
        if (route3WWs is not null) results.Add(await ProcessClearanceRoute3Withdrawals(route3WWs));

        var withdrawalsWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Withdrawals");
        if (withdrawalsWs is not null) results.Add(await ProcessWithdrawals(withdrawalsWs));

        var wCostWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Withdrawal_Cost_Estimate");
        if (wCostWs is not null) results.Add(await ProcessWithdrawalCostEstimate(wCostWs));

        var wEstLineWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Withdrawal_Estimate_Line_Items");
        if (wEstLineWs is not null) results.Add(await ProcessWithdrawalEstimateLineItems(wEstLineWs));

        var wLineWs = wb.Worksheets.FirstOrDefault(w => w.Name == "Withdrawal_Line_Items");
        if (wLineWs is not null) results.Add(await ProcessWithdrawalLineItems(wLineWs));

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
        public List<SenderBank> SenderBanks = new();
        public List<ReceiverBank> ReceiverBanks = new();
        public List<Tenor> Tenors = new();
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
        Couriers = await _db.Couriers.ToListAsync(),
        SenderBanks = await _db.SenderBanks.ToListAsync(),
        ReceiverBanks = await _db.ReceiverBanks.ToListAsync(),
        Tenors = await _db.Tenors.ToListAsync()
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
                        // B/L No. is intentionally optional — a row with a PoNumber but
            // no B/L No. means this PO line item exists but hasn't shipped
            // yet (a genuinely common, valid state). Every shipment-related
            // section below is skipped for such a row.
            if (poNumber is null)
            {
                errors.Add($"Row {row}: PoNumber is required — row skipped.");
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

                        // Everything from here on only applies once the line item has
            // actually shipped — a row with no B/L No. stops here, having
            // already found-or-created its PO and PO Line Item above.
            if (blAwbNo is null)
            {
                await _db.SaveChangesAsync();
                sectionsUpdated++;
                continue;
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

            // --- Last Offshore Item Detail (per line item; col 58 = Approved MOT Unit Price USD, col 61 = Description) ---
            var lastOffshoreUnitPrice = D(ws, row, 58);
            var lastOffshoreDescription = S(ws, row, 61);
            if (lastOffshoreUnitPrice.HasValue || lastOffshoreDescription is not null)
            {
                var existingItemDetail = await _db.LastOffshoreItemDetails.FirstOrDefaultAsync(d => d.ShipmentLineItemId == existingShipLine.Id);
                if (existingItemDetail is null)
                {
                    _db.LastOffshoreItemDetails.Add(new LastOffshoreItemDetail { ShipmentLineItemId = existingShipLine.Id, UnitPrice = lastOffshoreUnitPrice, Description = lastOffshoreDescription });
                }
                else
                {
                    existingItemDetail.UnitPrice = lastOffshoreUnitPrice;
                    existingItemDetail.Description = lastOffshoreDescription;
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

        // Banking (col 51 = dispatch tracking number; cols 64-75 = full fields, appended at the end of the sheet)
        var bankTrackingNo = S(ws, row, 51);
        var senderBankName = S(ws, row, 64);
        var osDocDispatchDate = Dt(ws, row, 65);
        var osDocDispatchedViaName = S(ws, row, 66);
        var senderBankCharges = D(ws, row, 67);
        var receivingBankName = S(ws, row, 68);
        var necessaryGoodType = B(ws, row, 69);
        var collectionRefNo = S(ws, row, 70);
        var collectionValue = D(ws, row, 71);
        var collectionCurrencyCode = S(ws, row, 72);
        var tenorDays = I(ws, row, 73);
        var addCbosAllowanceDays = I(ws, row, 74);
        var receiverBankCharges = D(ws, row, 75);
        if (bankTrackingNo is not null || senderBankName is not null || receivingBankName is not null || collectionRefNo is not null || collectionValue is not null)
        {
            var banking = await _db.ShipmentBankings.FirstOrDefaultAsync(b => b.ShipmentId == shipmentId) ?? new ShipmentBanking { ShipmentId = shipmentId };
            if (banking.Id == 0) _db.ShipmentBankings.Add(banking);
            banking.OsDocTrackingNumber = bankTrackingNo;
            banking.OsDocDispatchDate = osDocDispatchDate;
            banking.SenderBankCharges = senderBankCharges;
            banking.NecessaryGoodType = necessaryGoodType ?? banking.NecessaryGoodType;
            banking.CollectionRefNo = collectionRefNo;
            banking.CollectionValue = collectionValue;
            banking.ReceiverBankCharges = receiverBankCharges;
            if (senderBankName is not null)
            {
                var sb = lk.SenderBanks.FirstOrDefault(x => x.Name == senderBankName);
                if (sb is not null) banking.SenderBankId = sb.Id;
            }
            if (osDocDispatchedViaName is not null)
            {
                var courier = lk.Couriers.FirstOrDefault(x => x.Name == osDocDispatchedViaName);
                if (courier is not null) banking.OsDocDispatchedViaId = courier.Id;
            }
            if (receivingBankName is not null)
            {
                var rb = lk.ReceiverBanks.FirstOrDefault(x => x.Name == receivingBankName);
                if (rb is not null) banking.ReceivingBankId = rb.Id;
            }
            if (collectionCurrencyCode is not null)
            {
                var cur = lk.Currencies.FirstOrDefault(x => x.Code == collectionCurrencyCode);
                if (cur is not null) banking.CollectionCurrencyId = cur.Id;
            }
            if (tenorDays is not null)
            {
                var tenor = lk.Tenors.FirstOrDefault(x => x.Days == tenorDays);
                if (tenor is not null) banking.TenorId = tenor.Id;
            }
            if (addCbosAllowanceDays is not null)
            {
                var allowance = lk.Tenors.FirstOrDefault(x => x.Days == addCbosAllowanceDays);
                if (allowance is not null) banking.AddCbosAllowanceId = allowance.Id;
            }
        }

        // SSMO (cols 76-82)
        var cocRequired = B(ws, row, 76);
        var cocAvailable = B(ws, row, 77);
        var ssmoApplicationDate = Dt(ws, row, 78);
        var ssmoCost = D(ws, row, 79);
        var ssmoCostSettledDate = Dt(ws, row, 80);
        var ssmoRefNumber = S(ws, row, 81);
        var ssmoApprovalDate = Dt(ws, row, 82);
        if (cocRequired is not null || cocAvailable is not null || ssmoApplicationDate is not null || ssmoRefNumber is not null)
        {
            var ssmo = await _db.ShipmentSsmos.FirstOrDefaultAsync(s => s.ShipmentId == shipmentId) ?? new ShipmentSsmo { ShipmentId = shipmentId };
            if (ssmo.Id == 0) _db.ShipmentSsmos.Add(ssmo);
            ssmo.CocRequired = cocRequired;
            ssmo.CocAvailable = cocAvailable;
            ssmo.ApplicationDate = ssmoApplicationDate;
            ssmo.Cost = ssmoCost;
            ssmo.CostSettledDate = ssmoCostSettledDate;
            ssmo.RefNumber = ssmoRefNumber;
            ssmo.ApprovalDate = ssmoApprovalDate;
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

        // Last Offshore Details header (cols 55-57; 58 and 61 handled per-line-item by the caller; 62 = Currency; 63 = Remarks)
        var offshoreInvoiceNo = S(ws, row, 55);
        var inspectionNo = S(ws, row, 56);
        var grn = S(ws, row, 57);
        var offshoreCurrencyCode = S(ws, row, 62);
        var offshoreRemarks = S(ws, row, 63);
        if (offshoreInvoiceNo is not null || inspectionNo is not null || grn is not null || offshoreCurrencyCode is not null || offshoreRemarks is not null)
        {
            var offshore = await _db.LastOffshoreDetails.FirstOrDefaultAsync(o => o.ShipmentId == shipmentId) ?? new LastOffshoreDetail { ShipmentId = shipmentId };
            if (offshore.Id == 0) _db.LastOffshoreDetails.Add(offshore);
            offshore.InvoiceNo = offshoreInvoiceNo;
            offshore.InspectionNo = inspectionNo;
            offshore.Grn = grn;
            offshore.Remarks = offshoreRemarks;
            if (offshoreCurrencyCode is not null)
            {
                var currency = lk.Currencies.FirstOrDefault(c => c.Code == offshoreCurrencyCode);
                if (currency is not null) offshore.CurrencyId = currency.Id;
            }
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

                        // Matched by (Shipment, Date, Currency, Value) — the same real
            // payment described the same way twice (e.g. re-uploading an
            // export) is treated as the same record rather than duplicated.
            // A genuinely new payment on the same date for the same amount
            // is rare enough that this is a safe, practical compromise.
            var existing = await _db.ShipmentSupplierPaymentRecords.FirstOrDefaultAsync(r =>
                r.ShipmentId == shipment.Id && r.PaymentDate == paymentDate && r.CurrencyId == currency.Id && r.Value == value);

            var rate = await _fx.GetRateToUsdAsync(currency.Id);

            if (existing is null)
            {
                _db.ShipmentSupplierPaymentRecords.Add(new ShipmentSupplierPaymentRecord
                {
                    ShipmentId = shipment.Id, PaymentDate = paymentDate.Value, CurrencyId = currency.Id,
                    Value = value.Value, ValueUsd = value.Value / rate, PaymentDueId = paymentDueId
                });
                created++;
            }
            else
            {
                existing.PaymentDueId = paymentDueId;
                existing.ValueUsd = value.Value / rate;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Supplier_Payment_Records", created, updated, errors);
    }

    // ---------- PO Offshore Chain ----------
    private async Task<SheetUploadResult> ProcessPoOffshoreChain(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;
        var partners = await _db.BusinessPartners.ToListAsync();

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var poNumber = S(ws, row, 1); var sequence = I(ws, row, 2); var partnerName = S(ws, row, 3);
            if (poNumber is null || sequence is null || partnerName is null)
            { errors.Add($"Row {row}: PoNumber, Sequence, and Offshore Partner Name are all required."); continue; }

            var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNumber == poNumber);
            if (po is null) { errors.Add($"Row {row}: PO '{poNumber}' not found — upload Main first."); continue; }

            var partner = partners.FirstOrDefault(p => p.Name == partnerName);
            if (partner is null) { errors.Add($"Row {row}: Business Partner '{partnerName}' not found."); continue; }

            var existing = await _db.PurchaseOrderOffshorePartners.FirstOrDefaultAsync(o => o.PurchaseOrderId == po.Id && o.SequenceOrder == sequence);
            if (existing is null)
            {
                _db.PurchaseOrderOffshorePartners.Add(new PurchaseOrderOffshorePartner { PurchaseOrderId = po.Id, SequenceOrder = sequence.Value, BusinessPartnerId = partner.Id });
                created++;
            }
            else
            {
                existing.BusinessPartnerId = partner.Id;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("PO_Offshore_Chain", created, updated, errors);
    }

    // Shared by every Clearance-related sheet below — finds the Shipment
    // this row's B/L No. belongs to, or records a clear error and returns
    // null so the caller can skip that row.
    private async Task<Shipment?> FindShipmentForRow(int row, string? blAwbNo, List<string> errors)
    {
        if (blAwbNo is null) { errors.Add($"Row {row}: B/L NO is required."); return null; }
        var ship = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
        if (ship is null) errors.Add($"Row {row}: Shipment '{blAwbNo}' not found — upload Main first.");
        return ship;
    }

    // ---------- Clearance — General (Clearance + Delivery Order + Cost Estimate header + Certificate Entry) ----------
    private async Task<SheetUploadResult> ProcessClearanceGeneral(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 21)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var routeText = S(ws, row, 2);
            if (routeText is null || !Enum.TryParse<ClearanceRouteType>(routeText, ignoreCase: true, out var route))
            { errors.Add($"Row {row}: Route '{routeText}' not recognized — must be Route1ClearAtPort, Route2FzDeposit, or Route3ClearFromFz."); continue; }

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            var isNewClearance = clearance is null;
            clearance ??= new Clearance { ShipmentId = ship.Id };
            clearance.Route = route;
            clearance.CopyOfBlReceivedDate = Dt(ws, row, 3);
            clearance.OriginalShipmentSetReceivedDate = Dt(ws, row, 4);
            clearance.LcNo = S(ws, row, 5);
            clearance.DeclarationNo = S(ws, row, 6);
            clearance.ImFormNo = S(ws, row, 7);
            clearance.ImFormDate = Dt(ws, row, 8);
            clearance.Notes = S(ws, row, 9);
            if (isNewClearance) { _db.Clearances.Add(clearance); await _db.SaveChangesAsync(); created++; } else updated++;

            var deliveryOrder = await _db.ClearanceDeliveryOrders.FirstOrDefaultAsync(d => d.ClearanceId == clearance.Id) ?? new ClearanceDeliveryOrder { ClearanceId = clearance.Id };
            if (deliveryOrder.Id == 0) _db.ClearanceDeliveryOrders.Add(deliveryOrder);
            deliveryOrder.ActualArrivalDate = Dt(ws, row, 10);
            deliveryOrder.ReceiveDoDate = Dt(ws, row, 11);
            deliveryOrder.CopyOfDoCollectedDate = Dt(ws, row, 12);
            deliveryOrder.DepositRequired = B(ws, row, 13) ?? false;
            deliveryOrder.DoActualFeesSdg = D(ws, row, 14);
            deliveryOrder.DoFeesSettledDate = Dt(ws, row, 15);
            deliveryOrder.DoReceivedDate = Dt(ws, row, 16);

            var costEstimate = await _db.ClearanceCostEstimates.FirstOrDefaultAsync(c => c.ClearanceId == clearance.Id) ?? new ClearanceCostEstimate { ClearanceId = clearance.Id };
            if (costEstimate.Id == 0) _db.ClearanceCostEstimates.Add(costEstimate);
            costEstimate.EstimateDate = Dt(ws, row, 17);
            costEstimate.NotifyBuDate = Dt(ws, row, 18);
            costEstimate.AmountSettledDate = Dt(ws, row, 19);

            var certEntryDate = Dt(ws, row, 20);
            var scudaNo = S(ws, row, 21);
            if (certEntryDate.HasValue || scudaNo is not null)
            {
                var certEntry = await _db.ClearanceCertificateEntries.FirstOrDefaultAsync(c => c.ClearanceId == clearance.Id) ?? new ClearanceCertificateEntry { ClearanceId = clearance.Id };
                if (certEntry.Id == 0) _db.ClearanceCertificateEntries.Add(certEntry);
                certEntry.CertificateEntryDate = certEntryDate;
                certEntry.ScudaDeclarationNo = scudaNo;
            }

            await _db.SaveChangesAsync();
        }
        return new SheetUploadResult("Clearance_General", created, updated, errors);
    }

    // ---------- Clearance — Cost Estimate line items ----------
    private async Task<SheetUploadResult> ProcessClearanceCostEstimate(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;
        var chargeTypes = await _db.ClearanceChargeTypes.ToListAsync();

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 6)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet — upload Clearance_General first."); continue; }

            var chargeTypeName = S(ws, row, 2);
            var chargeType = chargeTypes.FirstOrDefault(c => c.Name == chargeTypeName);
            if (chargeType is null) { errors.Add($"Row {row}: Charge Type '{chargeTypeName}' not found."); continue; }

            var value = D(ws, row, 3) ?? 0;
            var isPaid = B(ws, row, 5) ?? false;

            var existing = await _db.ClearanceEstimateLineItems.FirstOrDefaultAsync(e => e.ClearanceId == clearance.Id && e.ChargeTypeId == chargeType.Id);
            if (existing is null)
            {
                _db.ClearanceEstimateLineItems.Add(new ClearanceEstimateLineItem
                {
                    ClearanceId = clearance.Id, ChargeTypeId = chargeType.Id, ValueSdg = value,
                    DueDate = Dt(ws, row, 4), IsPaid = isPaid, PaidDate = Dt(ws, row, 6)
                });
                created++;
            }
            else
            {
                existing.ValueSdg = value; existing.DueDate = Dt(ws, row, 4); existing.IsPaid = isPaid; existing.PaidDate = Dt(ws, row, 6);
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Cost_Estimate", created, updated, errors);
    }

    // ---------- Clearance — Route 1 (Clear at Port) ----------
    private async Task<SheetUploadResult> ProcessClearanceRoute1(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 27)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet — upload Clearance_General first."); continue; }

            var r = await _db.ClearanceRoute1Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            var isNew = r is null;
            r ??= new ClearanceRoute1Details { ClearanceId = clearance.Id };

            r.MoveRequestDate = Dt(ws, row, 2); r.BillAmountSdg = D(ws, row, 3); r.BillSettlementDate = Dt(ws, row, 4);
            r.SsmoFileRequestDate = Dt(ws, row, 5); r.SsmoInspectionAmountSdg = D(ws, row, 6); r.SsmoFeesSettlementDate = Dt(ws, row, 7);
            r.CustExamStartDate = Dt(ws, row, 8); r.CustExamCompletedDate = Dt(ws, row, 9);
            r.CustomsLabRequired = B(ws, row, 10) ?? false; r.CustomsLabFeesSdg = D(ws, row, 11);
            r.LabFeesPaymentDate = Dt(ws, row, 12); r.LabResultIssuanceDate = Dt(ws, row, 13);
            r.SsmoExamStartDate = Dt(ws, row, 14); r.SsmoCertIssuanceDate = Dt(ws, row, 15);
            r.CustEvaluationDate = Dt(ws, row, 16); r.CustomsDutySdg = D(ws, row, 17);
            r.CustomsSettlementDate = Dt(ws, row, 18); r.ReleaseExitPassDate = Dt(ws, row, 19);
            r.SpcBillRequestDate = Dt(ws, row, 20); r.SpcBillValueSdg = D(ws, row, 21); r.SpcBillSettlementDate = Dt(ws, row, 22);
            r.TruckPortEntryPermitDate = Dt(ws, row, 23); r.ContainersReturnedDate = Dt(ws, row, 24);
            r.ShippingLineDepositReturnDate = Dt(ws, row, 25); r.DepositValue = D(ws, row, 26);
            r.ClearanceActualCompletedDate = Dt(ws, row, 27);

            if (isNew) { _db.ClearanceRoute1Details.Add(r); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Route1", created, updated, errors);
    }

    // ---------- Clearance — Route 2 (FZ Deposit) ----------
    private async Task<SheetUploadResult> ProcessClearanceRoute2(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;
        var destinations = await _db.ShipmentDestinations.ToListAsync();

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 17)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet — upload Clearance_General first."); continue; }

            var destName = S(ws, row, 6);
            int? destId = null;
            if (destName is not null)
            {
                var dest = destinations.FirstOrDefault(d => d.Name == destName);
                if (dest is null) { errors.Add($"Row {row}: Destination '{destName}' not found."); continue; }
                destId = dest.Id;
            }

            var r = await _db.ClearanceRoute2Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            var isNew = r is null;
            r ??= new ClearanceRoute2Details { ClearanceId = clearance.Id };

            r.DepositRequestDate = Dt(ws, row, 2); r.RequestApprovalDate = Dt(ws, row, 3);
            r.DepositRefNo = S(ws, row, 4); r.FzInvoiceNo = S(ws, row, 5); r.DestinationId = destId;
            r.InspectionDate = Dt(ws, row, 7);
            r.SpcBillRequestDate = Dt(ws, row, 8); r.SpcBillValueSdg = D(ws, row, 9); r.SpcBillSettlementDate = Dt(ws, row, 10);
            r.PoliceSecurityAppointedDate = Dt(ws, row, 11);
            r.TruckPortEntryPermitDate = Dt(ws, row, 12); r.ContainersReceivedAtFzDate = Dt(ws, row, 13);
            r.ContainersReturnedDate = Dt(ws, row, 14); r.ShippingLineDepositReturnDate = Dt(ws, row, 15);
            r.DepositValue = D(ws, row, 16); r.ClearanceActualCompletedDate = Dt(ws, row, 17);

            if (isNew) { _db.ClearanceRoute2Details.Add(r); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Route2", created, updated, errors);
    }

    // ---------- Clearance — Actual Demurrage/Storage Charges ----------
    private async Task<SheetUploadResult> ProcessClearanceActualCharges(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 9)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet — upload Clearance_General first."); continue; }

            var r = await _db.ClearanceActualCharges.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            var isNew = r is null;
            r ??= new ClearanceActualCharges { ClearanceId = clearance.Id };

            r.ForecastDemurrageSdg = D(ws, row, 2); r.ForecastStorageSdg = D(ws, row, 3);
            var forecastCaptured = Dt(ws, row, 4);
            r.ForecastCapturedAt = forecastCaptured.HasValue ? forecastCaptured.Value.ToDateTime(TimeOnly.MinValue) : null;
            r.PlannedCompletionDate = Dt(ws, row, 5);
            r.ActualDemurragePaidSdg = D(ws, row, 6); r.ActualStoragePaidSdg = D(ws, row, 7);
            r.ShippingLineDepositReturnDate = Dt(ws, row, 8); r.AmountReturnedFromDeposit = D(ws, row, 9);

            if (isNew) { _db.ClearanceActualCharges.Add(r); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Actual_Charges", created, updated, errors);
    }

    // ---------- Trucking — Warehouse Allocation & Delivery ----------
    // Each row is treated as its own independent Truck Load → Drop → Item
    // chain (no attempt to merge multi-drop trips across rows) — simpler
    // and safer for a one-time migration than daily operational use.
    private async Task<SheetUploadResult> ProcessTrucking(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;
        var trucks = await _db.Trucks.ToListAsync();
        var drivers = await _db.Drivers.ToListAsync();
        var warehouses = await _db.Warehouses.ToListAsync();

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 9)) continue;
            var blAwbNo = S(ws, row, 1);
            var modelName = S(ws, row, 2);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;
            if (modelName is null) { errors.Add($"Row {row}: MODEL/PRODUCT is required."); continue; }

            var shipLine = await _db.ShipmentLineItems
                .Include(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
                .FirstOrDefaultAsync(sl => sl.ShipmentId == ship.Id && sl.PurchaseOrderLineItem!.ModelProduct!.Name == modelName);
            if (shipLine is null) { errors.Add($"Row {row}: No shipment line item found for Model '{modelName}' on '{blAwbNo}'."); continue; }

            var warehouseName = S(ws, row, 4);
            var warehouse = warehouses.FirstOrDefault(w => w.Name == warehouseName);
            if (warehouse is null) { errors.Add($"Row {row}: Warehouse '{warehouseName}' not found."); continue; }

            var plateNo = S(ws, row, 5);
            var truck = trucks.FirstOrDefault(t => t.PlateNo == plateNo);
            if (truck is null) { errors.Add($"Row {row}: Truck with Plate No. '{plateNo}' not found."); continue; }

            var driverName = S(ws, row, 6);
            var driver = driverName is not null ? drivers.FirstOrDefault(d => d.Name == driverName) : null;

            var qty = D(ws, row, 3) ?? 0;
            var loadDate = Dt(ws, row, 7) ?? DateOnly.FromDateTime(DateTime.UtcNow);

            var allocation = new WarehouseAllocation
            {
                ShipmentLineItemId = shipLine.Id, WarehouseId = warehouse.Id, Qty = qty, AllocatedAt = DateTime.UtcNow
            };
            _db.WarehouseAllocations.Add(allocation);
            await _db.SaveChangesAsync();

            var load = new TruckLoad { TruckId = truck.Id, DriverId = driver?.Id, LoadDate = loadDate, Notes = "Migration import" };
            _db.TruckLoads.Add(load);
            await _db.SaveChangesAsync();

            var drop = new TruckLoadDrop { TruckLoadId = load.Id, WarehouseId = warehouse.Id, ExpectedDeliveryDate = Dt(ws, row, 8), ActualDropOffDate = Dt(ws, row, 9) };
            _db.TruckLoadDrops.Add(drop);
            await _db.SaveChangesAsync();

            _db.TruckLoadItems.Add(new TruckLoadItem { TruckLoadDropId = drop.Id, WarehouseAllocationId = allocation.Id, Qty = qty });
            await _db.SaveChangesAsync();
            created++;
        }
        return new SheetUploadResult("Trucking", created, updated, errors);
    }

    // ---------- FZ Stock — Opening Balance (already-withdrawn quantity) ----------
    // Creates ONE synthetic, clearly-labeled "Opening Balance" Withdrawal
    // record per shipment with a nonzero already-withdrawn quantity —
    // full historical withdrawal events are deliberately not replicated.
    private async Task<SheetUploadResult> ProcessFzStockOpeningBalance(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 5;
        const string OpeningBalanceRefNo = "Opening Balance — Migration";

        for (int row = 6; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var blAwbNo = S(ws, row, 1);
            var modelName = S(ws, row, 2);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;
            if (modelName is null) { errors.Add($"Row {row}: MODEL/PRODUCT is required."); continue; }

            var qty = D(ws, row, 3) ?? 0;
            if (qty <= 0) continue; // nothing withdrawn yet — full deposit stays available, nothing to record

            var shipLine = await _db.ShipmentLineItems
                .Include(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
                .FirstOrDefaultAsync(sl => sl.ShipmentId == ship.Id && sl.PurchaseOrderLineItem!.ModelProduct!.Name == modelName);
            if (shipLine is null) { errors.Add($"Row {row}: No shipment line item found for Model '{modelName}' on '{blAwbNo}'."); continue; }

            var withdrawal = await _db.Withdrawals.FirstOrDefaultAsync(w => w.DepositShipmentId == ship.Id && w.WithdrawalRequestRefNo == OpeningBalanceRefNo);
            var isNew = withdrawal is null;
            if (isNew)
            {
                withdrawal = new Withdrawal
                {
                    DepositShipmentId = ship.Id,
                    WithdrawalRequestRefNo = OpeningBalanceRefNo,
                    WithdrawalRequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    ClearanceActualCompletedDate = DateOnly.FromDateTime(DateTime.UtcNow)
                };
                _db.Withdrawals.Add(withdrawal);
                await _db.SaveChangesAsync();
            }

            var existingLine = await _db.WithdrawalLineItems.FirstOrDefaultAsync(l => l.WithdrawalId == withdrawal!.Id && l.DepositShipmentLineItemId == shipLine.Id);
            if (existingLine is null)
            {
                _db.WithdrawalLineItems.Add(new WithdrawalLineItem { WithdrawalId = withdrawal!.Id, DepositShipmentLineItemId = shipLine.Id, Qty = qty });
                created++;
            }
            else
            {
                existingLine.Qty = qty;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        await _db.SaveChangesAsync();
        return new SheetUploadResult("FZ_Stock_Opening_Balance", created, updated, errors);
    }

    // ---------- TP Confirmations ----------
    private async Task<SheetUploadResult> ProcessTpConfirmations(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;
        var currencies = await _db.Currencies.ToListAsync();

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var blAwbNo = S(ws, row, 1); var modelName = S(ws, row, 2); var sequence = I(ws, row, 3);
            if (blAwbNo is null || modelName is null || sequence is null) { errors.Add($"Row {row}: B/L NO, MODEL/PRODUCT, and SEQUENCE are all required."); continue; }

            var shipLine = await _db.ShipmentLineItems
                .Include(sl => sl.Shipment!)
                .Include(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
                .FirstOrDefaultAsync(sl => sl.Shipment!.BlAwbNo == blAwbNo && sl.PurchaseOrderLineItem!.ModelProduct!.Name == modelName);
            if (shipLine is null) { errors.Add($"Row {row}: No shipment line item found for Model '{modelName}' on '{blAwbNo}'."); continue; }

            var partner = await _db.PurchaseOrderOffshorePartners
                .FirstOrDefaultAsync(p => p.PurchaseOrderId == shipLine.Shipment!.PurchaseOrderId && p.SequenceOrder == sequence);
            if (partner is null) { errors.Add($"Row {row}: No Offshore Partner at sequence {sequence} for this shipment's PO — upload PO_Offshore_Chain first."); continue; }

            var markup = D(ws, row, 4); var curCode = S(ws, row, 5);
            var currency = currencies.FirstOrDefault(c => c.Code == curCode);
            if (currency is null) { errors.Add($"Row {row}: Currency '{curCode}' not found."); continue; }

            var existing = await _db.TransferPricingEntries.FirstOrDefaultAsync(t => t.ShipmentLineItemId == shipLine.Id && t.PurchaseOrderOffshorePartnerId == partner.Id);
            if (existing is null)
            {
                _db.TransferPricingEntries.Add(new TransferPricingEntry
                {
                    ShipmentLineItemId = shipLine.Id, PurchaseOrderOffshorePartnerId = partner.Id,
                    MarkupPercent = markup, CurrencyId = currency.Id
                });
                created++;
            }
            else
            {
                existing.MarkupPercent = markup; existing.CurrencyId = currency.Id;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("TP_Confirmations", created, updated, errors);
    }

    // ---------- Bank Collection Records ----------
    private async Task<SheetUploadResult> ProcessBankCollectionRecords(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;
        var currencies = await _db.Currencies.ToListAsync();

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var blAwbNo = S(ws, row, 1); var paymentDate = Dt(ws, row, 2); var curCode = S(ws, row, 3); var value = D(ws, row, 4);
            if (blAwbNo is null || paymentDate is null || curCode is null || value is null)
            { errors.Add($"Row {row}: B/L NO, PAYMENT DATE, CURRENCY, and VALUE are all required."); continue; }

            var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == blAwbNo);
            if (shipment is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' not found — upload Main first."); continue; }
            var currency = currencies.FirstOrDefault(c => c.Code == curCode);
            if (currency is null) { errors.Add($"Row {row}: Currency '{curCode}' not found."); continue; }

            // Matched by (Shipment, Date, Currency, Value) — same convention
            // as Supplier_Payment_Records, so re-uploading an export doesn't
            // duplicate the same real payment.
            var existing = await _db.ShipmentCollectionRecords.FirstOrDefaultAsync(r =>
                r.ShipmentId == shipment.Id && r.PaymentDate == paymentDate && r.CurrencyId == currency.Id && r.Value == value);
            if (existing is null)
            {
                var rate = await _db.FxRates.Where(f => f.CurrencyId == currency.Id).OrderByDescending(f => f.EffectiveDate).FirstOrDefaultAsync();
                var rateToUsd = rate?.RateToUsd ?? 1m;
                _db.ShipmentCollectionRecords.Add(new ShipmentCollectionRecord
                {
                    ShipmentId = shipment.Id, PaymentDate = paymentDate.Value, CurrencyId = currency.Id,
                    Value = value.Value, ValueUsd = value.Value / rateToUsd
                });
                created++;
            }
            else updated++;
        }
        await _db.SaveChangesAsync();
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Bank_Collection_Records", created, updated, errors);
    }

    // ---------- Clearance — Route 3 (Clear from FZ / Withdrawal) ----------
    private async Task<SheetUploadResult> ProcessClearanceRoute3(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 23)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet — upload Clearance_General first (Route should be Route3ClearFromFz)."); continue; }

            int? depositShipmentId = null;
            var depositBlAwbNo = S(ws, row, 2);
            if (depositBlAwbNo is not null)
            {
                var depositShip = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == depositBlAwbNo);
                if (depositShip is null) { errors.Add($"Row {row}: Deposit Shipment '{depositBlAwbNo}' not found."); continue; }
                depositShipmentId = depositShip.Id;
            }

            var r = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            var isNew = r is null;
            r ??= new ClearanceRoute3Details { ClearanceId = clearance.Id };

            r.DepositShipmentId = depositShipmentId;
            r.CertificateEntryDate = Dt(ws, row, 3); r.ScudaDeclarationNo = S(ws, row, 4);
            r.SsmoFileRequestDate = Dt(ws, row, 5); r.SsmoInspectionAmountSdg = D(ws, row, 6); r.SsmoFeesSettlementDate = Dt(ws, row, 7);
            r.CustExamStartDate = Dt(ws, row, 8); r.CustExamCompletedDate = Dt(ws, row, 9);
            r.CustomsLabRequired = B(ws, row, 10) ?? false; r.CustomsLabFeesSdg = D(ws, row, 11);
            r.LabFeesPaymentDate = Dt(ws, row, 12); r.LabResultIssuanceDate = Dt(ws, row, 13);
            r.SsmoExamStartDate = Dt(ws, row, 14); r.SsmoCertIssuanceDate = Dt(ws, row, 15);
            r.CustEvaluationDate = Dt(ws, row, 16); r.CustomsDutySdg = D(ws, row, 17);
            r.CustomsSettlementDate = Dt(ws, row, 18); r.ReleaseExitPassDate = Dt(ws, row, 19);
            r.TruckPortEntryPermitDate = Dt(ws, row, 20); r.ClearanceActualCompletedDate = Dt(ws, row, 21);

            if (isNew) { _db.ClearanceRoute3Details.Add(r); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Route3", created, updated, errors);
    }

    // ---------- Clearance — Route 3 Withdrawal Line Items ----------
    private async Task<SheetUploadResult> ProcessClearanceRoute3Withdrawals(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var blAwbNo = S(ws, row, 1);
            var ship = await FindShipmentForRow(row, blAwbNo, errors);
            if (ship is null) continue;

            var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.ShipmentId == ship.Id);
            if (clearance is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Clearance record yet."); continue; }
            var route3 = await _db.ClearanceRoute3Details.FirstOrDefaultAsync(x => x.ClearanceId == clearance.Id);
            if (route3 is null || route3.DepositShipmentId is null) { errors.Add($"Row {row}: Shipment '{blAwbNo}' has no Route 3 record with a Deposit Shipment set yet — upload Clearance_Route3 first."); continue; }

            var modelName = S(ws, row, 2);
            var qty = D(ws, row, 3);
            if (modelName is null || qty is null) { errors.Add($"Row {row}: DEPOSIT MODEL/PRODUCT and QTY are both required."); continue; }

            var depositLine = await _db.ShipmentLineItems
                .Include(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
                .FirstOrDefaultAsync(sl => sl.ShipmentId == route3.DepositShipmentId && sl.PurchaseOrderLineItem!.ModelProduct!.Name == modelName);
            if (depositLine is null) { errors.Add($"Row {row}: No line item found for Model '{modelName}' on the deposit shipment."); continue; }

            var existing = await _db.ClearanceRoute3Withdrawals.FirstOrDefaultAsync(w => w.ClearanceRoute3DetailsId == route3.Id && w.DepositShipmentLineItemId == depositLine.Id);
            if (existing is null)
            {
                _db.ClearanceRoute3Withdrawals.Add(new ClearanceRoute3Withdrawal { ClearanceRoute3DetailsId = route3.Id, DepositShipmentLineItemId = depositLine.Id, Qty = qty.Value });
                created++;
            }
            else { existing.Qty = qty.Value; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Clearance_Route3_Withdrawals", created, updated, errors);
    }

    // ---------- Withdrawals — the standalone workflow, distinct from Route 3 ----------
    private async Task<SheetUploadResult> ProcessWithdrawals(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 27)) continue;
            var depositBlAwbNo = S(ws, row, 1);
            var refNo = S(ws, row, 2);
            if (depositBlAwbNo is null || refNo is null) { errors.Add($"Row {row}: DEPOSIT B/L NO and WITHDRAWAL REQUEST REF NO. are both required."); continue; }
            if (refNo == "Opening Balance — Migration") { errors.Add($"Row {row}: '{refNo}' is a reserved Ref No. used by FZ_Stock_Opening_Balance — please use a different one."); continue; }

            var depositShip = await _db.Shipments.FirstOrDefaultAsync(s => s.BlAwbNo == depositBlAwbNo);
            if (depositShip is null) { errors.Add($"Row {row}: Deposit Shipment '{depositBlAwbNo}' not found."); continue; }

            var w = await _db.Withdrawals.FirstOrDefaultAsync(x => x.DepositShipmentId == depositShip.Id && x.WithdrawalRequestRefNo == refNo);
            var isNew = w is null;
            w ??= new Withdrawal { DepositShipmentId = depositShip.Id, WithdrawalRequestRefNo = refNo };

            w.WithdrawalRequestDate = Dt(ws, row, 3);
            w.CertificateEntryDate = Dt(ws, row, 4); w.ScudaDeclarationNo = S(ws, row, 5);
            w.SsmoCocRequired = B(ws, row, 6); w.SsmoCocAvailable = B(ws, row, 7);
            w.SsmoApplicationDate = Dt(ws, row, 8); w.SsmoCost = D(ws, row, 9);
            w.SsmoCostSettledDate = Dt(ws, row, 10); w.SsmoRefNumber = S(ws, row, 11); w.SsmoApprovalDate = Dt(ws, row, 12);
            w.MotApprovalDate = Dt(ws, row, 13);
            w.SsmoFileRequestDate = Dt(ws, row, 14); w.SsmoInspectionAmountSdg = D(ws, row, 15); w.SsmoFeesSettlementDate = Dt(ws, row, 16);
            w.CustExamStartDate = Dt(ws, row, 17); w.CustExamCompletedDate = Dt(ws, row, 18);
            w.CustomsLabRequired = B(ws, row, 19) ?? false; w.CustomsLabFeesSdg = D(ws, row, 20);
            w.LabFeesPaymentDate = Dt(ws, row, 21); w.LabResultIssuanceDate = Dt(ws, row, 22);
            w.SsmoExamStartDate = Dt(ws, row, 23); w.SsmoCertIssuanceDate = Dt(ws, row, 24);
            w.CustEvaluationDate = Dt(ws, row, 25); w.CustomsDutySdg = D(ws, row, 26); w.CustomsSettlementDate = Dt(ws, row, 27);

            if (isNew) { _db.Withdrawals.Add(w); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Withdrawals", created, updated, errors);
    }

    private async Task<Withdrawal?> FindWithdrawalForRow(int row, string? depositBlAwbNo, string? refNo, List<string> errors)
    {
        if (depositBlAwbNo is null || refNo is null) { errors.Add($"Row {row}: DEPOSIT B/L NO and WITHDRAWAL REQUEST REF NO. are both required."); return null; }
        var w = await _db.Withdrawals.Include(x => x.DepositShipment)
            .FirstOrDefaultAsync(x => x.DepositShipment!.BlAwbNo == depositBlAwbNo && x.WithdrawalRequestRefNo == refNo);
        if (w is null) errors.Add($"Row {row}: No Withdrawal found for '{depositBlAwbNo}' / '{refNo}' — upload Withdrawals first.");
        return w;
    }

    // ---------- Withdrawal — Cost Estimate header ----------
    private async Task<SheetUploadResult> ProcessWithdrawalCostEstimate(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var w = await FindWithdrawalForRow(row, S(ws, row, 1), S(ws, row, 2), errors);
            if (w is null) continue;

            var existing = await _db.WithdrawalCostEstimates.FirstOrDefaultAsync(x => x.WithdrawalId == w.Id);
            var isNew = existing is null;
            existing ??= new WithdrawalCostEstimate { WithdrawalId = w.Id };
            existing.EstimateDate = Dt(ws, row, 3); existing.NotifyBuDate = Dt(ws, row, 4); existing.AmountSettledDate = Dt(ws, row, 5);

            if (isNew) { _db.WithdrawalCostEstimates.Add(existing); created++; } else updated++;
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Withdrawal_Cost_Estimate", created, updated, errors);
    }

    // ---------- Withdrawal — Estimate Line Items ----------
    private async Task<SheetUploadResult> ProcessWithdrawalEstimateLineItems(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;
        var chargeTypes = await _db.ClearanceChargeTypes.ToListAsync();

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var w = await FindWithdrawalForRow(row, S(ws, row, 1), S(ws, row, 2), errors);
            if (w is null) continue;

            var chargeTypeName = S(ws, row, 3);
            var chargeType = chargeTypes.FirstOrDefault(c => c.Name == chargeTypeName);
            if (chargeType is null) { errors.Add($"Row {row}: Charge Type '{chargeTypeName}' not found."); continue; }
            var value = D(ws, row, 4) ?? 0;

            var existing = await _db.WithdrawalEstimateLineItems.FirstOrDefaultAsync(x => x.WithdrawalId == w.Id && x.ChargeTypeId == chargeType.Id);
            if (existing is null)
            {
                _db.WithdrawalEstimateLineItems.Add(new WithdrawalEstimateLineItem { WithdrawalId = w.Id, ChargeTypeId = chargeType.Id, ValueSdg = value, DueDate = Dt(ws, row, 5) });
                created++;
            }
            else { existing.ValueSdg = value; existing.DueDate = Dt(ws, row, 5); updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Withdrawal_Estimate_Line_Items", created, updated, errors);
    }

    // ---------- Withdrawal — Line Items (which deposited items, how much) ----------
    private async Task<SheetUploadResult> ProcessWithdrawalLineItems(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? PaymentFirstDataRow - 1;

        for (int row = PaymentFirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var w = await FindWithdrawalForRow(row, S(ws, row, 1), S(ws, row, 2), errors);
            if (w is null) continue;

            var modelName = S(ws, row, 3); var qty = D(ws, row, 4);
            if (modelName is null || qty is null) { errors.Add($"Row {row}: MODEL/PRODUCT and QTY are both required."); continue; }

            var depositLine = await _db.ShipmentLineItems
                .Include(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
                .FirstOrDefaultAsync(sl => sl.ShipmentId == w.DepositShipmentId && sl.PurchaseOrderLineItem!.ModelProduct!.Name == modelName);
            if (depositLine is null) { errors.Add($"Row {row}: No line item found for Model '{modelName}' on the deposit shipment."); continue; }

            var existing = await _db.WithdrawalLineItems.FirstOrDefaultAsync(l => l.WithdrawalId == w.Id && l.DepositShipmentLineItemId == depositLine.Id);
            if (existing is null)
            {
                _db.WithdrawalLineItems.Add(new WithdrawalLineItem { WithdrawalId = w.Id, DepositShipmentLineItemId = depositLine.Id, Qty = qty.Value });
                created++;
            }
            else { existing.Qty = qty.Value; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Withdrawal_Line_Items", created, updated, errors);
    }
}
