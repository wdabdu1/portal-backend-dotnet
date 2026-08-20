using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;

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
        var bankings = _db.ShipmentBankings.ToDictionary(b => b.ShipmentId);
        var acds = _db.ShipmentAcds.ToDictionary(a => a.ShipmentId);
        var mots = _db.ShipmentMots.ToDictionary(m => m.ShipmentId);
        var lastOffshores = _db.LastOffshoreDetails.ToDictionary(o => o.ShipmentId);
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
                WriteMainRow(ws, row, po, poLine, null, null, null, null, null, null, null, null, null, null);
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

                WriteMainRow(ws, row, po, poLine, ship, sl, fwd, docs, fullSet, banking, acd, mot, offshore, offshoreItem, clearance);
                row++;
            }
        }

        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents();
    }

    private void WriteMainRow(IXLWorksheet ws, int row, Models.Orders.PurchaseOrder po, Models.Orders.PurchaseOrderLineItem poLine,
        Models.Shipments.Shipment? ship, Models.Shipments.ShipmentLineItem? sl, Models.Shipments.ShipmentForwarder? fwd,
        Models.Shipments.ShipmentDraftDocuments? docs, Models.Shipments.ShipmentSupplierFullSet? fullSet, Models.Shipments.ShipmentBanking? banking,
        Models.Shipments.ShipmentAcd? acd, Models.Shipments.ShipmentMot? mot, Models.Shipments.LastOffshoreDetail? offshore,
        Models.Shipments.LastOffshoreItemDetail? offshoreItem, Models.Clearance.Clearance? clearance = null)
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
}
