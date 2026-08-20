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
