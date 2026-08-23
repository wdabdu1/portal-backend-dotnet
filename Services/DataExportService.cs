using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Orders;
using ShippingPortal.Api.Models.Shipments;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Services;

// Produces an .xlsx in the exact same shape as the "Migration Workbook"
// (Main + two Supplier Payment sheets) — one row per Shipment Line Item
// on Main, with PO/Shipment/sub-section fields repeated across every
// line item of the same shipment, mirroring exactly what DataUploadService
// expects to read back in. Genuinely round-trippable: download it, and
// re-uploading it unmodified is a safe no-op.
public class DataExportService
{
    private readonly ShippingPortalDbContext _db;
    public DataExportService(ShippingPortalDbContext db) => _db = db;

    private static readonly XLColor Navy = XLColor.FromHtml("#0A3D62");
    private static readonly XLColor LegendFill = XLColor.FromHtml("#FFF9C4");
    private static readonly (string Key, string Label, XLColor Color)[] Sections =
    {
        ("PO", "PURCHASE ORDER (repeat for every line/shipment on this PO)", XLColor.FromHtml("#0A3D62")),
        ("POLINE", "PO LINE ITEM", XLColor.FromHtml("#1E88C7")),
        ("SHIP", "SHIPMENT", XLColor.FromHtml("#2E7D32")),
        ("SHIPLINE", "SHIPMENT LINE ITEM", XLColor.FromHtml("#558B2F")),
        ("FWD", "FORWARDER", XLColor.FromHtml("#8E24AA")),
        ("DOCS", "DRAFT DOCS / SUPPLIER FULL SET", XLColor.FromHtml("#6D4C41")),
        ("BANK", "BANKING", XLColor.FromHtml("#00838F")),
        ("ACD", "ACD", XLColor.FromHtml("#AD1457")),
        ("MOT", "MOT", XLColor.FromHtml("#EF6C00")),
        ("OFFSHORE", "LAST OFFSHORE DETAILS", XLColor.FromHtml("#5D4037")),
        ("CLR", "CLEARANCE", XLColor.FromHtml("#C62828")),
        ("SSMO", "SSMO", XLColor.FromHtml("#00695C")),
    };
    // (section key, header text) pairs in exact column order — matches the Migration Workbook builder 1:1.
    private static readonly (string Section, string Header)[] MainColumns =
    {
        ("PO","PoNumber (NEW — see note below)"),("PO","BUSINESS UNIT (NEW — must match a Business Unit Code)"),
        ("PO","DIVISION (NEW — must match a Division Code under this Business Unit)"),("PO","SUPPLIER"),("PO","BRAND"),
        ("PO","APPROVAL"),("PO","CONSIGNEE"),("PO","SUPPLIER PI NO"),("PO","SUPPLIER PI DATE"),("PO","SUPPLIER PAYMENT TERMS"),
        ("PO","RECEIVED SIGNED PI DATE"),("PO","SENT SIGNED PI DATE"),("PO","BU PO DATE"),("PO","ORDER EXECUTION DATE"),
        ("PO","LATEST SHIPMENT DT"),("PO","INCOTERM"),("PO","ORIGIN"),("PO","SHIPMENT MODE (NEW)"),("PO","BU ESTIMATED SHIPPING COST"),
        ("POLINE","CAT"),("POLINE","MODEL"),("POLINE","TYPE"),("POLINE","UOM (NEW)"),("POLINE","ORDERED QTY"),("POLINE","UNIT PRICE"),
        ("POLINE","CURRENCY"),("POLINE","TOTAL VALUE (auto-computed, reference only)"),
        ("SHIP","B/L NO"),("SHIP","BOL DATE"),("SHIP","ETD"),("SHIP","ETA"),("SHIP","STATUS (Draft/Confirmed/Cancelled)"),
        ("SHIP","SHIPPING LINE"),("SHIP","20 FT CNTR"),("SHIP","40 FT CNTR"),
        ("SHIPLINE","QTY SHIPPED"),("SHIPLINE","UNIT PRICE (reference only — see note)"),("SHIPLINE","TOTAL SHIPPED VALUE"),
        ("FWD","FORWARDER NAME"),("FWD","ACTUAL SHIPPING COST"),("FWD","CURRENCY"),("FWD","AMOUNT SAVED IN SHIPPING COST"),("FWD","MARINE INSURANCE (TRUE/FALSE)"),
        ("DOCS","DRAFT DOC RECV DATE"),("DOCS","FINAL DRAFT CONFIRMED DATE"),("DOCS","SUPPLIER INVOICE NO"),("DOCS","SUPPLIER INVOICE DATE"),
        ("DOCS","ORIGINAL DOCUMENTS SENT DATE"),("DOCS","DHL AIRWAY BILL NO."),("DOCS","ORIGINAL DOCUMENTS RCVD DATE"),
        ("BANK","DHL No. (Bank dispatch tracking)"),
        ("ACD","ACD COST $"),
        ("MOT","TECHUIP MOT APPROVED P.I. NO"),("MOT","TECHUIP MOT APPROVED P.I. DATE"),
        ("OFFSHORE","TECHUIP INVOICE NO."),("OFFSHORE","INSPECTION NO."),("OFFSHORE","GRN NO."),("OFFSHORE","APPROVED MOT UNIT PRICE USD"),
        ("OFFSHORE","APPROVED MOT TOTAL PRICE USD (auto-computed, reference only)"),
        ("CLR","REMARKS"),
        ("OFFSHORE","LAST OFFSHORE ITEM DESCRIPTION"),("OFFSHORE","LAST OFFSHORE CURRENCY"),
        ("OFFSHORE","LAST OFFSHORE REMARKS"),
        ("BANK","SENDER BANK NAME"),("BANK","OS DOC DISPATCH DATE"),("BANK","OS DOC DISPATCHED VIA (Courier Name)"),
        ("BANK","SENDER BANK CHARGES"),("BANK","RECEIVING BANK NAME"),("BANK","NECESSARY GOOD TYPE (TRUE/FALSE)"),
        ("BANK","COLLECTION REF NO."),("BANK","COLLECTION VALUE"),("BANK","COLLECTION CURRENCY"),
        ("BANK","TENOR DAYS"),("BANK","ADD CBOS ALLOWANCE DAYS"),("BANK","RECEIVER BANK CHARGES"),
        ("SSMO","COC REQUIRED (TRUE/FALSE)"),("SSMO","COC AVAILABLE (TRUE/FALSE)"),("SSMO","APPLICATION DATE"),
        ("SSMO","COST"),("SSMO","COST SETTLED DATE"),("SSMO","REF NUMBER"),("SSMO","APPROVAL DATE"),
    };
    private static void SetCell(IXLWorksheet ws, int row, int col, object? value)
    {
        string text = value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            DateOnly d => d.ToString("yyyy-MM-dd"),
            null => "",
            var v => v.ToString() ?? ""
        };
        ws.Cell(row, col).Value = text;
    }

    public async Task<byte[]> ExportAsync()
    {
        using var wb = new XLWorkbook();
        BuildMainSheet(wb);
        BuildPaymentDueSheet(wb);
        BuildPaymentRecordsSheet(wb);
        BuildPoOffshoreChainSheet(wb);
        BuildClearanceGeneralSheet(wb);
        BuildClearanceCostEstimateSheet(wb);
        BuildClearanceRoute1Sheet(wb);
        BuildClearanceRoute2Sheet(wb);
        BuildClearanceActualChargesSheet(wb);
        BuildTruckingSheet(wb);
        BuildFzStockOpeningBalanceSheet(wb);
        BuildTpConfirmationsSheet(wb);
        BuildBankCollectionRecordsSheet(wb);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void BuildMainSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Main");

        // Row 1: merged section-label bands; Row 2: column headers — both
        // color-coded per section, matching the original template exactly.
        int col = 1;
        while (col <= MainColumns.Length)
        {
            var sectionKey = MainColumns[col - 1].Section;
            var sectionInfo = Sections.First(s => s.Key == sectionKey);
            int start = col;
            while (col <= MainColumns.Length && MainColumns[col - 1].Section == sectionKey) col++;
            int end = col - 1;

            if (end > start) ws.Range(1, start, 1, end).Merge();
            var labelCell = ws.Cell(1, start);
            labelCell.Value = sectionInfo.Label;
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Font.FontColor = XLColor.White;
            labelCell.Style.Fill.BackgroundColor = sectionInfo.Color;
            labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            for (int c = start; c <= end; c++)
            {
                var hc = ws.Cell(2, c);
                hc.Value = MainColumns[c - 1].Header;
                hc.Style.Font.Bold = true;
                hc.Style.Font.FontColor = XLColor.White;
                hc.Style.Fill.BackgroundColor = sectionInfo.Color;
                hc.Style.Alignment.WrapText = true;
                hc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }
        // Row 3 left blank (no worked example needed in an export — real
        // data starts row 4, matching MainFirstDataRow in DataUploadService).
        for (int c = 1; c <= MainColumns.Length; c++)
            ws.Cell(3, c).Style.Fill.BackgroundColor = LegendFill;

        // Built from PO Line Items outward (not Shipment Line Items) so a
        // PO that hasn't shipped yet — a genuinely common, valid state —
        // still gets a row, with every shipment-related column left blank
        // rather than being silently dropped from the backup entirely.
        var poLines = _db.PurchaseOrderLineItems
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.BusinessUnit)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.Division)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.Supplier)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.BrandManufacturer)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.ApprovalType)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.Consignee)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.SupplierPaymentTerm)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.Incoterm)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.OriginCountry)
            .Include(li => li.PurchaseOrder!).ThenInclude(po => po.ShipmentMode)
            .Include(li => li.ProductCategory)
            .Include(li => li.ModelProduct)
            .Include(li => li.ProductType)
            .Include(li => li.UnitOfMeasure)
            .Include(li => li.Currency)
            .OrderBy(li => li.PurchaseOrder!.PoNumber).ThenBy(li => li.Id)
            .ToList();

        var shipmentLinesByPoLine = _db.ShipmentLineItems
            .Include(sl => sl.Shipment!).ThenInclude(s => s.ShippingLine)
            .ToList()
            .GroupBy(sl => sl.PurchaseOrderLineItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Sub-section tables keyed by ShipmentId — loaded separately (rather
        // than deeply nested .Include chains) to keep the main query simple.
        var forwarders = _db.ShipmentForwarders.Include(f => f.ForwarderEntity).Include(f => f.Currency).ToDictionary(f => f.ShipmentId);
        var draftDocs = _db.ShipmentDraftDocuments.ToDictionary(d => d.ShipmentId);
        var fullSets = _db.ShipmentSupplierFullSets.ToDictionary(f => f.ShipmentId);
        var bankings = _db.ShipmentBankings
            .Include(b => b.SenderBank).Include(b => b.OsDocDispatchedVia).Include(b => b.ReceivingBank)
            .Include(b => b.CollectionCurrency).Include(b => b.Tenor).Include(b => b.AddCbosAllowance)
            .ToDictionary(b => b.ShipmentId);
        var acds = _db.ShipmentAcds.ToDictionary(a => a.ShipmentId);
        var mots = _db.ShipmentMots.ToDictionary(m => m.ShipmentId);
        var ssmos = _db.ShipmentSsmos.ToDictionary(s => s.ShipmentId);
        var lastOffshores = _db.LastOffshoreDetails.Include(o => o.Currency).ToDictionary(o => o.ShipmentId);
        var lastOffshoreItems = _db.LastOffshoreItemDetails.ToDictionary(i => i.ShipmentLineItemId);
        var clearances = _db.Clearances.ToDictionary(c => c.ShipmentId);

        int row = 4;
        foreach (var poLine in poLines)
        {
            var po = poLine.PurchaseOrder!;
            shipmentLinesByPoLine.TryGetValue(poLine.Id, out var shipLines);

            // No shipment yet — write one row with just PO + PO Line data,
            // every shipment-related column left blank.
            if (shipLines is null || shipLines.Count == 0)
            {
                    WriteMainRow(ws, row, po, poLine, null, null, null, null, null, null, null, null, null, null, null);
                row++;
                continue;
            }

            // One row per shipment this line item actually shipped on
            // (almost always one, but partial shipments across multiple
            // BLs are a real, valid scenario).
            foreach (var sl in shipLines)
            {
                var ship = sl.Shipment!;
                forwarders.TryGetValue(ship.Id, out var fwd);
                draftDocs.TryGetValue(ship.Id, out var docs);
                fullSets.TryGetValue(ship.Id, out var fullSet);
                bankings.TryGetValue(ship.Id, out var banking);
                acds.TryGetValue(ship.Id, out var acd);
                mots.TryGetValue(ship.Id, out var mot);
                lastOffshores.TryGetValue(ship.Id, out var offshore);
                lastOffshoreItems.TryGetValue(sl.Id, out var offshoreItem);
                clearances.TryGetValue(ship.Id, out var clearance);
                ssmos.TryGetValue(ship.Id, out var ssmo);

                WriteMainRow(ws, row, po, poLine, ship, sl, fwd, docs, fullSet, banking, acd, mot, offshore, offshoreItem, clearance, ssmo);
                row++;
            }
        }

        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents();
    }

    private void WriteMainRow(IXLWorksheet ws, int row, PurchaseOrder po, PurchaseOrderLineItem poLine,
        Shipment? ship, ShipmentLineItem? sl, ShipmentForwarder? fwd,
        ShipmentDraftDocuments? docs, ShipmentSupplierFullSet? fullSet, ShipmentBanking? banking,
        ShipmentAcd? acd, ShipmentMot? mot, LastOffshoreDetail? offshore,
        LastOffshoreItemDetail? offshoreItem, Clearance? clearance = null, ShipmentSsmo? ssmo = null)
    {
        int c = 1;
        SetCell(ws, row, c++, po.PoNumber);
        SetCell(ws, row, c++, po.BusinessUnit?.Code);
        SetCell(ws, row, c++, po.Division?.Code);
        SetCell(ws, row, c++, po.Supplier?.Name);
        SetCell(ws, row, c++, po.BrandManufacturer?.Name);
        SetCell(ws, row, c++, po.ApprovalType?.Name);
        SetCell(ws, row, c++, po.Consignee?.Name);
        SetCell(ws, row, c++, po.SupplierPiNo);
        SetCell(ws, row, c++, po.SupplierPiDate);
        SetCell(ws, row, c++, po.SupplierPaymentTerm?.Name);
        SetCell(ws, row, c++, po.ReceivedSignedPiDate);
        SetCell(ws, row, c++, po.SentSignedPiDate);
        SetCell(ws, row, c++, po.BuPoDate);
        SetCell(ws, row, c++, po.OrderExecutionDate);
        SetCell(ws, row, c++, po.LatestShippingDate);
        SetCell(ws, row, c++, po.Incoterm?.Code);
        SetCell(ws, row, c++, po.OriginCountry?.Name);
        SetCell(ws, row, c++, po.ShipmentMode?.Name);
        SetCell(ws, row, c++, po.BuShippingBudget);

        SetCell(ws, row, c++, poLine.ProductCategory?.Name);
        SetCell(ws, row, c++, poLine.ModelProduct?.Name);
        SetCell(ws, row, c++, poLine.ProductType?.Name);
        SetCell(ws, row, c++, poLine.UnitOfMeasure?.Code);
        SetCell(ws, row, c++, poLine.Qty);
        SetCell(ws, row, c++, poLine.UnitPrice);
        SetCell(ws, row, c++, poLine.Currency?.Code);
        SetCell(ws, row, c++, poLine.Total);

        SetCell(ws, row, c++, ship?.BlAwbNo);
        SetCell(ws, row, c++, ship?.BlAwbDate);
        SetCell(ws, row, c++, ship?.Etd);
        SetCell(ws, row, c++, ship?.Eta);
        SetCell(ws, row, c++, ship?.Status.ToString());
        SetCell(ws, row, c++, ship?.ShippingLine?.Name);
        SetCell(ws, row, c++, ship?.Fcl20Count);
        SetCell(ws, row, c++, ship?.Fcl40Count);

        SetCell(ws, row, c++, sl?.QtyInBl);
        SetCell(ws, row, c++, poLine.UnitPrice);
        SetCell(ws, row, c++, sl?.ItemSubtotal);

        SetCell(ws, row, c++, fwd?.ForwarderEntity?.Name);
        SetCell(ws, row, c++, fwd?.ActualShippingCost);
        SetCell(ws, row, c++, fwd?.Currency?.Code);
        SetCell(ws, row, c++, fwd?.AmountSaved);
        SetCell(ws, row, c++, fwd?.MarineInsurance);

        SetCell(ws, row, c++, docs?.InitialDraftReceivedDate);
        SetCell(ws, row, c++, docs?.FinalDraftConfirmedDate);
        SetCell(ws, row, c++, fullSet?.SupplierInvoiceNo);
        SetCell(ws, row, c++, fullSet?.SupplierInvoiceDate);
        SetCell(ws, row, c++, fullSet?.FsDispatchDate);
        SetCell(ws, row, c++, fullSet?.FsTrackingNumber);
        SetCell(ws, row, c++, fullSet?.FsReceivedDate);

        SetCell(ws, row, c++, banking?.OsDocTrackingNumber);

        SetCell(ws, row, c++, acd?.CostUsd);

        SetCell(ws, row, c++, mot?.OffshoreApprovedPiNumber);
        SetCell(ws, row, c++, mot?.ApprovalDate);

        SetCell(ws, row, c++, offshore?.InvoiceNo);
        SetCell(ws, row, c++, offshore?.InspectionNo);
        SetCell(ws, row, c++, offshore?.Grn);
        SetCell(ws, row, c++, offshoreItem?.UnitPrice);
        SetCell(ws, row, c++, sl is not null && offshoreItem is not null && offshoreItem.UnitPrice.HasValue ? offshoreItem.UnitPrice.Value * sl.QtyInBl : (decimal?)null);

        SetCell(ws, row, c++, clearance?.Notes);
        SetCell(ws, row, c++, offshoreItem?.Description);
        SetCell(ws, row, c++, offshore?.Currency?.Code);
        SetCell(ws, row, c++, offshore?.Remarks);
        SetCell(ws, row, c++, banking?.SenderBank?.Name);
        SetCell(ws, row, c++, banking?.OsDocDispatchDate);
        SetCell(ws, row, c++, banking?.OsDocDispatchedVia?.Name);
        SetCell(ws, row, c++, banking?.SenderBankCharges);
        SetCell(ws, row, c++, banking?.ReceivingBank?.Name);
        SetCell(ws, row, c++, banking?.NecessaryGoodType);
        SetCell(ws, row, c++, banking?.CollectionRefNo);
        SetCell(ws, row, c++, banking?.CollectionValue);
        SetCell(ws, row, c++, banking?.CollectionCurrency?.Code);
        SetCell(ws, row, c++, banking?.Tenor?.Days);
        SetCell(ws, row, c++, banking?.AddCbosAllowance?.Days);
        SetCell(ws, row, c++, banking?.ReceiverBankCharges);
        SetCell(ws, row, c++, ssmo?.CocRequired);
        SetCell(ws, row, c++, ssmo?.CocAvailable);
        SetCell(ws, row, c++, ssmo?.ApplicationDate);
        SetCell(ws, row, c++, ssmo?.Cost);
        SetCell(ws, row, c++, ssmo?.CostSettledDate);
        SetCell(ws, row, c++, ssmo?.RefNumber);
        SetCell(ws, row, c++, ssmo?.ApprovalDate);
    }

    private void BuildPaymentDueSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Supplier_Payment_Due_Schedule");
        ws.Cell(1, 1).Value = "Supplier Payment Due Schedule (Planned)";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Font.FontColor = Navy;

        var headers = new[] { "B/L NO", "DUE DATE", "DUE AMOUNT", "CURRENCY", "LABEL" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var dues = _db.ShipmentPaymentDues.Include(d => d.Shipment).Include(d => d.Currency)
            .OrderBy(d => d.Shipment!.BlAwbNo).ThenBy(d => d.DueDate).ToList();

        int row = 6;
        foreach (var d in dues)
        {
            SetCell(ws, row, 1, d.Shipment?.BlAwbNo);
            SetCell(ws, row, 2, d.DueDate);
            SetCell(ws, row, 3, d.Amount);
            SetCell(ws, row, 4, d.Currency?.Code);
            SetCell(ws, row, 5, d.Label);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildPaymentRecordsSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Supplier_Payment_Records");
        ws.Cell(1, 1).Value = "Supplier Payment Records (Actual)";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Font.FontColor = Navy;

        var headers = new[] { "B/L NO", "PAYMENT DATE", "CURRENCY", "VALUE", "PAYMENT DUE LABEL (optional)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var records = _db.ShipmentSupplierPaymentRecords
            .Include(r => r.Shipment).Include(r => r.Currency).Include(r => r.PaymentDue)
            .OrderBy(r => r.Shipment!.BlAwbNo).ThenBy(r => r.PaymentDate).ToList();

        int row = 6;
        foreach (var r in records)
        {
            SetCell(ws, row, 1, r.Shipment?.BlAwbNo);
            SetCell(ws, row, 2, r.PaymentDate);
            SetCell(ws, row, 3, r.Currency?.Code);
            SetCell(ws, row, 4, r.Value);
            SetCell(ws, row, 5, r.PaymentDue?.Label);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildPoOffshoreChainSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("PO_Offshore_Chain");
        ws.Cell(1, 1).Value = "PO Offshore Partner Chain";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "PoNumber", "SEQUENCE", "OFFSHORE PARTNER NAME" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var rows = _db.PurchaseOrderOffshorePartners
            .Include(o => o.PurchaseOrder).Include(o => o.BusinessPartner)
            .OrderBy(o => o.PurchaseOrder!.PoNumber).ThenBy(o => o.SequenceOrder).ToList();

        int row = 6;
        foreach (var r in rows)
        {
            SetCell(ws, row, 1, r.PurchaseOrder?.PoNumber);
            SetCell(ws, row, 2, r.SequenceOrder);
            SetCell(ws, row, 3, r.BusinessPartner?.Name);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildClearanceGeneralSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clearance_General");
        ws.Cell(1, 1).Value = "Clearance — General Info";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "ROUTE (Route1ClearAtPort / Route2FzDeposit / Route3ClearFromFz)",
            "COPY OF BL RECEIVED DATE", "ORIGINAL SHIPMENT SET RECEIVED DATE", "LC NO.", "DECLARATION NO.",
            "IM FORM NO.", "IM FORM DATE", "REMARKS",
            "ACTUAL ARRIVAL DATE", "RECEIVE DO DATE", "COPY OF DO COLLECTED DATE", "DEPOSIT REQUIRED (TRUE/FALSE)",
            "DO ACTUAL FEES SDG", "DO FEES SETTLED DATE", "DO RECEIVED DATE",
            "COST ESTIMATE — ESTIMATE DATE", "COST ESTIMATE — NOTIFY BU DATE", "COST ESTIMATE — AMOUNT SETTLED DATE",
            "CERTIFICATE ENTRY DATE", "SCUDA DECLARATION NO." };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var clearances = _db.Clearances.Include(c => c.Shipment).ToList();
        var deliveryOrders = _db.ClearanceDeliveryOrders.ToDictionary(d => d.ClearanceId);
        var costEstimates = _db.ClearanceCostEstimates.ToDictionary(c => c.ClearanceId);
        var certEntries = _db.ClearanceCertificateEntries.ToDictionary(c => c.ClearanceId);

        int row = 6;
        foreach (var clr in clearances.OrderBy(c => c.Shipment?.BlAwbNo))
        {
            deliveryOrders.TryGetValue(clr.Id, out var d);
            costEstimates.TryGetValue(clr.Id, out var ce);
            certEntries.TryGetValue(clr.Id, out var cert);

            int c2 = 1;
            SetCell(ws, row, c2++, clr.Shipment?.BlAwbNo);
            SetCell(ws, row, c2++, clr.Route.ToString());
            SetCell(ws, row, c2++, clr.CopyOfBlReceivedDate);
            SetCell(ws, row, c2++, clr.OriginalShipmentSetReceivedDate);
            SetCell(ws, row, c2++, clr.LcNo);
            SetCell(ws, row, c2++, clr.DeclarationNo);
            SetCell(ws, row, c2++, clr.ImFormNo);
            SetCell(ws, row, c2++, clr.ImFormDate);
            SetCell(ws, row, c2++, clr.Notes);
            SetCell(ws, row, c2++, d?.ActualArrivalDate);
            SetCell(ws, row, c2++, d?.ReceiveDoDate);
            SetCell(ws, row, c2++, d?.CopyOfDoCollectedDate);
            SetCell(ws, row, c2++, d?.DepositRequired);
            SetCell(ws, row, c2++, d?.DoActualFeesSdg);
            SetCell(ws, row, c2++, d?.DoFeesSettledDate);
            SetCell(ws, row, c2++, d?.DoReceivedDate);
            SetCell(ws, row, c2++, ce?.EstimateDate);
            SetCell(ws, row, c2++, ce?.NotifyBuDate);
            SetCell(ws, row, c2++, ce?.AmountSettledDate);
            SetCell(ws, row, c2++, cert?.CertificateEntryDate);
            SetCell(ws, row, c2++, cert?.ScudaDeclarationNo);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildClearanceCostEstimateSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clearance_Cost_Estimate");
        ws.Cell(1, 1).Value = "Clearance Cost Estimate — Line Items";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "CHARGE TYPE", "VALUE SDG", "DUE DATE", "IS PAID (TRUE/FALSE)", "PAID DATE" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var rows = _db.ClearanceEstimateLineItems
            .Include(e => e.Clearance!).ThenInclude(c => c.Shipment)
            .Include(e => e.ChargeType)
            .OrderBy(e => e.Clearance!.Shipment!.BlAwbNo).ToList();

        int row = 6;
        foreach (var r in rows)
        {
            SetCell(ws, row, 1, r.Clearance?.Shipment?.BlAwbNo);
            SetCell(ws, row, 2, r.ChargeType?.Name);
            SetCell(ws, row, 3, r.ValueSdg);
            SetCell(ws, row, 4, r.DueDate);
            SetCell(ws, row, 5, r.IsPaid);
            SetCell(ws, row, 6, r.PaidDate);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildClearanceRoute1Sheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clearance_Route1");
        ws.Cell(1, 1).Value = "Clearance — Route 1 (Clear at Port) Progress";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO",
            "MOVE REQUEST DATE", "BILL AMOUNT SDG", "BILL SETTLEMENT DATE",
            "SSMO FILE REQUEST DATE", "SSMO INSPECTION AMOUNT SDG", "SSMO FEES SETTLEMENT DATE",
            "CUST EXAM START DATE", "CUST EXAM COMPLETED DATE",
            "CUSTOMS LAB REQUIRED (TRUE/FALSE)", "CUSTOMS LAB FEES SDG", "LAB FEES PAYMENT DATE", "LAB RESULT ISSUANCE DATE",
            "SSMO EXAM START DATE", "SSMO CERT ISSUANCE DATE",
            "CUST EVALUATION DATE", "CUSTOMS DUTY SDG", "CUSTOMS SETTLEMENT DATE", "RELEASE EXIT PASS DATE",
            "SPC BILL REQUEST DATE", "SPC BILL VALUE SDG", "SPC BILL SETTLEMENT DATE",
            "TRUCK PORT ENTRY PERMIT DATE", "CONTAINERS RETURNED DATE", "SHIPPING LINE DEPOSIT RETURN DATE",
            "DEPOSIT VALUE", "CLEARANCE ACTUAL COMPLETED DATE" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var rows = _db.ClearanceRoute1Details.Include(r => r.Clearance!).ThenInclude(c => c.Shipment)
            .OrderBy(r => r.Clearance!.Shipment!.BlAwbNo).ToList();

        int row = 6;
        foreach (var r in rows)
        {
            int c2 = 1;
            SetCell(ws, row, c2++, r.Clearance?.Shipment?.BlAwbNo);
            SetCell(ws, row, c2++, r.MoveRequestDate); SetCell(ws, row, c2++, r.BillAmountSdg); SetCell(ws, row, c2++, r.BillSettlementDate);
            SetCell(ws, row, c2++, r.SsmoFileRequestDate); SetCell(ws, row, c2++, r.SsmoInspectionAmountSdg); SetCell(ws, row, c2++, r.SsmoFeesSettlementDate);
            SetCell(ws, row, c2++, r.CustExamStartDate); SetCell(ws, row, c2++, r.CustExamCompletedDate);
            SetCell(ws, row, c2++, r.CustomsLabRequired); SetCell(ws, row, c2++, r.CustomsLabFeesSdg);
            SetCell(ws, row, c2++, r.LabFeesPaymentDate); SetCell(ws, row, c2++, r.LabResultIssuanceDate);
            SetCell(ws, row, c2++, r.SsmoExamStartDate); SetCell(ws, row, c2++, r.SsmoCertIssuanceDate);
            SetCell(ws, row, c2++, r.CustEvaluationDate); SetCell(ws, row, c2++, r.CustomsDutySdg);
            SetCell(ws, row, c2++, r.CustomsSettlementDate); SetCell(ws, row, c2++, r.ReleaseExitPassDate);
            SetCell(ws, row, c2++, r.SpcBillRequestDate); SetCell(ws, row, c2++, r.SpcBillValueSdg); SetCell(ws, row, c2++, r.SpcBillSettlementDate);
            SetCell(ws, row, c2++, r.TruckPortEntryPermitDate); SetCell(ws, row, c2++, r.ContainersReturnedDate);
            SetCell(ws, row, c2++, r.ShippingLineDepositReturnDate); SetCell(ws, row, c2++, r.DepositValue);
            SetCell(ws, row, c2++, r.ClearanceActualCompletedDate);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildClearanceRoute2Sheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clearance_Route2");
        ws.Cell(1, 1).Value = "Clearance — Route 2 (FZ Deposit) Progress";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO",
            "DEPOSIT REQUEST DATE", "REQUEST APPROVAL DATE", "DEPOSIT REF NO.", "FZ INVOICE NO.", "DESTINATION (FZ Name)",
            "INSPECTION DATE",
            "SPC BILL REQUEST DATE", "SPC BILL VALUE SDG", "SPC BILL SETTLEMENT DATE", "POLICE SECURITY APPOINTED DATE",
            "TRUCK PORT ENTRY PERMIT DATE", "CONTAINERS RECEIVED AT FZ DATE", "CONTAINERS RETURNED DATE",
            "SHIPPING LINE DEPOSIT RETURN DATE", "DEPOSIT VALUE", "CLEARANCE ACTUAL COMPLETED DATE" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var rows = _db.ClearanceRoute2Details
            .Include(r => r.Clearance!).ThenInclude(c => c.Shipment)
            .Include(r => r.Destination)
            .OrderBy(r => r.Clearance!.Shipment!.BlAwbNo).ToList();

        int row = 6;
        foreach (var r in rows)
        {
            int c2 = 1;
            SetCell(ws, row, c2++, r.Clearance?.Shipment?.BlAwbNo);
            SetCell(ws, row, c2++, r.DepositRequestDate); SetCell(ws, row, c2++, r.RequestApprovalDate);
            SetCell(ws, row, c2++, r.DepositRefNo); SetCell(ws, row, c2++, r.FzInvoiceNo); SetCell(ws, row, c2++, r.Destination?.Name);
            SetCell(ws, row, c2++, r.InspectionDate);
            SetCell(ws, row, c2++, r.SpcBillRequestDate); SetCell(ws, row, c2++, r.SpcBillValueSdg); SetCell(ws, row, c2++, r.SpcBillSettlementDate);
            SetCell(ws, row, c2++, r.PoliceSecurityAppointedDate);
            SetCell(ws, row, c2++, r.TruckPortEntryPermitDate); SetCell(ws, row, c2++, r.ContainersReceivedAtFzDate);
            SetCell(ws, row, c2++, r.ContainersReturnedDate); SetCell(ws, row, c2++, r.ShippingLineDepositReturnDate);
            SetCell(ws, row, c2++, r.DepositValue); SetCell(ws, row, c2++, r.ClearanceActualCompletedDate);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildClearanceActualChargesSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Clearance_Actual_Charges");
        ws.Cell(1, 1).Value = "Clearance — Actual Demurrage/Storage Charges";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "FORECAST DEMURRAGE SDG", "FORECAST STORAGE SDG", "FORECAST CAPTURED AT (date)",
            "PLANNED COMPLETION DATE", "ACTUAL DEMURRAGE PAID SDG", "ACTUAL STORAGE PAID SDG",
            "SHIPPING LINE DEPOSIT RETURN DATE", "AMOUNT RETURNED FROM DEPOSIT" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var rows = _db.ClearanceActualCharges.Include(r => r.Clearance!).ThenInclude(c => c.Shipment)
            .OrderBy(r => r.Clearance!.Shipment!.BlAwbNo).ToList();

        int row = 6;
        foreach (var r in rows)
        {
            int c2 = 1;
            SetCell(ws, row, c2++, r.Clearance?.Shipment?.BlAwbNo);
            SetCell(ws, row, c2++, r.ForecastDemurrageSdg); SetCell(ws, row, c2++, r.ForecastStorageSdg);
            SetCell(ws, row, c2++, r.ForecastCapturedAt.HasValue ? DateOnly.FromDateTime(r.ForecastCapturedAt.Value) : (DateOnly?)null);
            SetCell(ws, row, c2++, r.PlannedCompletionDate);
            SetCell(ws, row, c2++, r.ActualDemurragePaidSdg); SetCell(ws, row, c2++, r.ActualStoragePaidSdg);
            SetCell(ws, row, c2++, r.ShippingLineDepositReturnDate); SetCell(ws, row, c2++, r.AmountReturnedFromDeposit);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildTruckingSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Trucking");
        ws.Cell(1, 1).Value = "Trucking — Warehouse Allocation & Delivery";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "MODEL/PRODUCT", "QTY ALLOCATED", "WAREHOUSE NAME", "TRUCK PLATE NO.", "DRIVER NAME (optional)",
            "LOAD DATE", "EXPECTED DELIVERY DATE", "ACTUAL DROP OFF DATE" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        // TruckLoadItem is the leaf of the chain — walk back up through
        // Drop → TruckLoad, and via WarehouseAllocation → ShipmentLineItem
        // to get the B/L and Model needed to identify the row.
        var items = _db.TruckLoadItems
            .Include(i => i.TruckLoadDrop!).ThenInclude(d => d.TruckLoad!).ThenInclude(t => t.Truck)
            .Include(i => i.TruckLoadDrop!).ThenInclude(d => d.TruckLoad!).ThenInclude(t => t.Driver)
            .Include(i => i.TruckLoadDrop!).ThenInclude(d => d.Warehouse)
            .Include(i => i.WarehouseAllocation!).ThenInclude(a => a.ShipmentLineItem!).ThenInclude(sl => sl.Shipment)
            .Include(i => i.WarehouseAllocation!).ThenInclude(a => a.ShipmentLineItem!).ThenInclude(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .Where(i => i.WarehouseAllocation!.ShipmentLineItemId != null)
            .ToList();

        int row = 6;
        foreach (var item in items)
        {
            var drop = item.TruckLoadDrop!;
            var load = drop.TruckLoad!;
            var shipLine = item.WarehouseAllocation!.ShipmentLineItem!;

            int c2 = 1;
            SetCell(ws, row, c2++, shipLine.Shipment?.BlAwbNo);
            SetCell(ws, row, c2++, shipLine.PurchaseOrderLineItem?.ModelProduct?.Name);
            SetCell(ws, row, c2++, item.Qty);
            SetCell(ws, row, c2++, drop.Warehouse?.Name);
            SetCell(ws, row, c2++, load.Truck?.PlateNo);
            SetCell(ws, row, c2++, load.Driver?.Name);
            SetCell(ws, row, c2++, load.LoadDate);
            SetCell(ws, row, c2++, drop.ExpectedDeliveryDate);
            SetCell(ws, row, c2++, drop.ActualDropOffDate);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildFzStockOpeningBalanceSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("FZ_Stock_Opening_Balance");
        ws.Cell(1, 1).Value = "FZ Stock — Opening Balance (Already Withdrawn)";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "MODEL/PRODUCT", "ALREADY WITHDRAWN QTY" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        // Summed per (Shipment, Model) across every real WithdrawalLineItem
        // — whether it came from the original opening-balance record or a
        // genuine portal-made withdrawal since — so re-uploading this
        // export produces exactly one correct, combined total per item,
        // never double-counting or overwriting a prior partial figure.
        var lines = _db.WithdrawalLineItems
            .Include(l => l.DepositShipmentLineItem!).ThenInclude(sl => sl.Shipment)
            .Include(l => l.DepositShipmentLineItem!).ThenInclude(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .ToList();

        var grouped = lines
            .GroupBy(l => l.DepositShipmentLineItemId)
            .Select(g => new
            {
                BlAwbNo = g.First().DepositShipmentLineItem?.Shipment?.BlAwbNo,
                ModelName = g.First().DepositShipmentLineItem?.PurchaseOrderLineItem?.ModelProduct?.Name,
                TotalQty = g.Sum(x => x.Qty)
            })
            .OrderBy(x => x.BlAwbNo)
            .ToList();

        int row = 6;
        foreach (var g in grouped)
        {
            SetCell(ws, row, 1, g.BlAwbNo);
            SetCell(ws, row, 2, g.ModelName);
            SetCell(ws, row, 3, g.TotalQty);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildTpConfirmationsSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("TP_Confirmations");
        ws.Cell(1, 1).Value = "Transfer Pricing Confirmations";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "MODEL/PRODUCT", "SEQUENCE", "MARKUP PERCENT", "CURRENCY" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var entries = _db.TransferPricingEntries
            .Include(t => t.ShipmentLineItem!).ThenInclude(sl => sl.Shipment)
            .Include(t => t.ShipmentLineItem!).ThenInclude(sl => sl.PurchaseOrderLineItem!).ThenInclude(pl => pl.ModelProduct)
            .Include(t => t.PurchaseOrderOffshorePartner)
            .Include(t => t.Currency)
            .OrderBy(t => t.ShipmentLineItem!.Shipment!.BlAwbNo).ThenBy(t => t.PurchaseOrderOffshorePartner!.SequenceOrder)
            .ToList();

        int row = 6;
        foreach (var e in entries)
        {
            SetCell(ws, row, 1, e.ShipmentLineItem?.Shipment?.BlAwbNo);
            SetCell(ws, row, 2, e.ShipmentLineItem?.PurchaseOrderLineItem?.ModelProduct?.Name);
            SetCell(ws, row, 3, e.PurchaseOrderOffshorePartner?.SequenceOrder);
            SetCell(ws, row, 4, e.MarkupPercent);
            SetCell(ws, row, 5, e.Currency?.Code);
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private void BuildBankCollectionRecordsSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Bank_Collection_Records");
        ws.Cell(1, 1).Value = "Bank Collection Records (Actual)";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13; ws.Cell(1, 1).Style.Font.FontColor = Navy;
        var headers = new[] { "B/L NO", "PAYMENT DATE", "CURRENCY", "VALUE" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i]; c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = Navy;
        }
        for (int i = 0; i < headers.Length; i++) ws.Cell(5, i + 1).Style.Fill.BackgroundColor = LegendFill;

        var records = _db.ShipmentCollectionRecords
            .Include(r => r.Shipment).Include(r => r.Currency)
            .OrderBy(r => r.Shipment!.BlAwbNo).ThenBy(r => r.PaymentDate).ToList();

        int row = 6;
        foreach (var r in records)
        {
            SetCell(ws, row, 1, r.Shipment?.BlAwbNo);
            SetCell(ws, row, 2, r.PaymentDate);
            SetCell(ws, row, 3, r.Currency?.Code);
            SetCell(ws, row, 4, r.Value);
            row++;
        }
        ws.Columns().AdjustToContents();
    }
}
