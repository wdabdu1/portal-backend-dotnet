using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;

namespace ShippingPortal.Api.Services;

// Produces an .xlsx with the exact same structure as the "Settings
// Upload Template" (title/note rows, headers on row 4, an example row
// on row 5), populated with the database's current real data starting
// row 6 — genuinely round-trippable: download it, and re-uploading it
// unmodified is a safe no-op (every row already matches what's there).
public class SettingsExportService
{
    private readonly ShippingPortalDbContext _db;
    public SettingsExportService(ShippingPortalDbContext db) => _db = db;

    private static readonly XLColor Navy = XLColor.FromHtml("#0A3D62");
    private static readonly XLColor LegendFill = XLColor.FromHtml("#FFF9C4");

    private IXLWorksheet NewSheet(XLWorkbook wb, string name, string title, string note, string[] headers, string[] exampleRow)
    {
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(1, 1).Style.Font.FontColor = Navy;

        ws.Cell(2, 1).Value = note;
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontSize = 9;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#666666");

        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = Navy;
            c.Style.Alignment.WrapText = true;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < exampleRow.Length; i++)
        {
            var c = ws.Cell(5, i + 1);
            c.Value = exampleRow[i];
            c.Style.Font.Italic = true;
            c.Style.Font.FontColor = XLColor.FromHtml("#888888");
            c.Style.Fill.BackgroundColor = LegendFill;
        }

        return ws;
    }

    private static void WriteRow(IXLWorksheet ws, int row, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            string text = values[i] switch
            {
                bool b => b ? "TRUE" : "FALSE",
                DateOnly d => d.ToString("yyyy-MM-dd"),
                null => "",
                var v => (v.ToString() ?? "").Trim()
            };
            ws.Cell(row, i + 1).Value = text;
        }
    }

    public async Task<byte[]> ExportAsync()
    {
        using var wb = new XLWorkbook();

        var businessUnits = await _db.BusinessUnits.ToListAsync();
        var ws1 = NewSheet(wb, "BusinessUnits", "Business Units", "Code and Name must be unique.",
            new[] { "Code", "Name", "IsActive (TRUE/FALSE)" }, new[] { "CAD", "Consumer & Appliances Division", "TRUE" });
        for (int i = 0; i < businessUnits.Count; i++)
            WriteRow(ws1, 6 + i, businessUnits[i].Code, businessUnits[i].Name, businessUnits[i].IsActive);

        var divisions = await _db.Divisions.Include(d => d.BusinessUnit).ToListAsync();
        var ws2 = NewSheet(wb, "Divisions", "Divisions", "BusinessUnitCode must match an existing Business Unit Code.",
            new[] { "BusinessUnitCode", "Code", "Name", "IsActive (TRUE/FALSE)" }, new[] { "CAD", "TV", "Television Division", "TRUE" });
        for (int i = 0; i < divisions.Count; i++)
            WriteRow(ws2, 6 + i, divisions[i].BusinessUnit?.Code, divisions[i].Code, divisions[i].Name, divisions[i].IsActive);

        var partners = await _db.BusinessPartners.ToListAsync();
        var ws3 = NewSheet(wb, "BusinessPartners", "Business Partners (Suppliers / Consignees / Brands / Offshore)",
            "Set TRUE/FALSE for each role a partner plays — a single partner can be more than one (e.g. Supplier AND Offshore).",
            new[] { "Name", "IsSupplier (TRUE/FALSE)", "IsConsignee (TRUE/FALSE)", "IsBrandManufacturer (TRUE/FALSE)", "IsOffshoreEntity (TRUE/FALSE)", "IsActive (TRUE/FALSE)" },
            new[] { "LG Electronics", "TRUE", "FALSE", "TRUE", "FALSE", "TRUE" });
        for (int i = 0; i < partners.Count; i++)
            WriteRow(ws3, 6 + i, partners[i].Name, partners[i].IsSupplier, partners[i].IsConsignee, partners[i].IsBrandManufacturer, partners[i].IsOffshoreEntity, partners[i].IsActive);

        await WriteSimpleNameActive(wb, "ApprovalTypes", "Approval Types", "Board Approval", _db.ApprovalTypes.Select(x => new NameActive(x.Name, x.IsActive)));
        await WriteSimpleNameActive(wb, "PaymentTerms", "Payment Terms", "30% Advance / 70% on BL", _db.PaymentTerms.Select(x => new NameActive(x.Name, x.IsActive)));

        var incoterms = await _db.Incoterms.ToListAsync();
        var wsInco = NewSheet(wb, "Incoterms", "Incoterms", "", new[] { "Code", "Name", "IsActive (TRUE/FALSE)" }, new[] { "FOB", "Free On Board", "TRUE" });
        for (int i = 0; i < incoterms.Count; i++) WriteRow(wsInco, 6 + i, incoterms[i].Code, incoterms[i].Name, incoterms[i].IsActive);

        await WriteSimpleNameActive(wb, "OriginCountries", "Origin Countries", "China", _db.OriginCountries.Select(x => new NameActive(x.Name, x.IsActive)));

        var uoms = await _db.UnitsOfMeasure.ToListAsync();
        var wsUom = NewSheet(wb, "UnitsOfMeasure", "Units of Measure", "", new[] { "Code", "IsActive (TRUE/FALSE)" }, new[] { "PCS", "TRUE" });
        for (int i = 0; i < uoms.Count; i++) WriteRow(wsUom, 6 + i, uoms[i].Code, uoms[i].IsActive);

        await WriteSimpleNameActive(wb, "ShipmentModes", "Shipment Modes", "Sea Freight", _db.ShipmentModes.Select(x => new NameActive(x.Name, x.IsActive)));
        await WriteSimpleNameActive(wb, "TariffGroups", "Tariff Groups", "Standard", _db.TariffGroups.Select(x => new NameActive(x.Name, x.IsActive)));

        var cats = await _db.ProductCategories.Include(c => c.TariffGroup).ToListAsync();
        var wsCat = NewSheet(wb, "ProductCategories", "Product Categories", "TariffGroupName is optional but must match an existing Tariff Group if provided.",
            new[] { "Name", "TariffGroupName (optional)", "IsActive (TRUE/FALSE)" }, new[] { "Electronics", "Standard", "TRUE" });
        for (int i = 0; i < cats.Count; i++) WriteRow(wsCat, 6 + i, cats[i].Name, cats[i].TariffGroup?.Name, cats[i].IsActive);

        await WriteSimpleNameActive(wb, "ProductTypes", "Product Types", "Finished Goods", _db.ProductTypes.Select(x => new NameActive(x.Name, x.IsActive)));

        var models = await _db.ModelProducts.Include(m => m.ProductCategory).Include(m => m.ProductType).ToListAsync();
        var wsModel = NewSheet(wb, "ModelProducts", "Model / Product", "ProductCategoryName and ProductTypeName are optional but must match existing entries if provided.",
            new[] { "Name", "ProductCategoryName (optional)", "ProductTypeName (optional)", "Description (optional)", "IsActive (TRUE/FALSE)" },
            new[] { "DG-TV-HSDJAS4350BZ", "Electronics", "Finished Goods", "43-inch LED TV", "TRUE" });
        for (int i = 0; i < models.Count; i++)
            WriteRow(wsModel, 6 + i, models[i].Name, models[i].ProductCategory?.Name, models[i].ProductType?.Name, models[i].Description, models[i].IsActive);

        var currencies = await _db.Currencies.ToListAsync();
        var wsCur = NewSheet(wb, "Currencies", "Currencies", "", new[] { "Code", "Name", "IsActive (TRUE/FALSE)" }, new[] { "USD", "US Dollar", "TRUE" });
        for (int i = 0; i < currencies.Count; i++) WriteRow(wsCur, 6 + i, currencies[i].Code, currencies[i].Name, currencies[i].IsActive);

        var fxRates = await _db.FxRates.Include(f => f.Currency).ToListAsync();
        var wsFx = NewSheet(wb, "FxRates", "FX Rates", "One row per currency per effective date. RateToUsd = how many units of this currency equal 1 USD.",
            new[] { "CurrencyCode", "RateToUsd", "EffectiveDate (YYYY-MM-DD)" }, new[] { "AED", "3.6725", "2026-01-01" });
        for (int i = 0; i < fxRates.Count; i++) WriteRow(wsFx, 6 + i, fxRates[i].Currency?.Code, fxRates[i].RateToUsd, fxRates[i].EffectiveDate);

        await WriteSimpleNameActive(wb, "ShippingLines", "Shipping Lines", "MAERSK", _db.ShippingLines.Select(x => new NameActive(x.Name, x.IsActive)));

        var tariffs = await _db.ShippingLineDemurrageTariffs.Include(t => t.ShippingLine).Include(t => t.TariffGroup).ToListAsync();
        var wsTariff = NewSheet(wb, "ShippingLineDemurrageTariffs", "Shipping Line Demurrage Tariffs",
            "One row per (Shipping Line, Tariff Group, Container Size). ContainerSize must be 20 or 40.",
            new[] { "ShippingLineName", "TariffGroupName", "ContainerSize (20/40)", "FreeDays", "FirstPeriodDays", "FirstPeriodRateSdg", "AfterwardRateSdg" },
            new[] { "MAERSK", "Standard", "20", "14", "10", "5000", "8000" });
        for (int i = 0; i < tariffs.Count; i++)
            WriteRow(wsTariff, 6 + i, tariffs[i].ShippingLine?.Name, tariffs[i].TariffGroup?.Name, tariffs[i].ContainerSize, tariffs[i].FreeDays, tariffs[i].FirstPeriodDays, tariffs[i].FirstPeriodRateSdg, tariffs[i].AfterwardRateSdg);

        await WriteSimpleNameActive(wb, "Couriers", "Couriers", "DHL", _db.Couriers.Select(x => new NameActive(x.Name, x.IsActive)));
        await WriteSimpleNameActive(wb, "Forwarders", "Forwarders", "ABC Freight Forwarding", _db.Forwarders.Select(x => new NameActive(x.Name, x.IsActive)));

        var dests = await _db.ShipmentDestinations.ToListAsync();
        var wsDest = NewSheet(wb, "ShipmentDestinations", "Shipment Destinations", "DefaultDurationDays is the typical number of days goods stay at this destination.",
            new[] { "Name", "IsFreeZone (TRUE/FALSE)", "DefaultDurationDays", "IsActive (TRUE/FALSE)" }, new[] { "Garri Free Zone", "TRUE", "30", "TRUE" });
        for (int i = 0; i < dests.Count; i++) WriteRow(wsDest, 6 + i, dests[i].Name, dests[i].IsFreeZone, dests[i].DefaultDurationDays, dests[i].IsActive);

        var holidays = await _db.PublicHolidays.ToListAsync();
        var wsHol = NewSheet(wb, "PublicHolidays", "Public Holidays", "AffectsDxb/AffectsClr control whether this date is excluded from Dubai-side / Clearance-side day counting.",
            new[] { "Date (YYYY-MM-DD)", "Name", "AffectsDxb (TRUE/FALSE)", "AffectsClr (TRUE/FALSE)" }, new[] { "2026-01-01", "New Year's Day", "TRUE", "TRUE" });
        for (int i = 0; i < holidays.Count; i++) WriteRow(wsHol, 6 + i, holidays[i].Date, holidays[i].Name, holidays[i].AffectsDxb, holidays[i].AffectsClr);

        await WriteSimpleNameActive(wb, "LogisticsCities", "Logistics Cities", "Khartoum", _db.LogisticsCities.Select(x => new NameActive(x.Name, x.IsActive)));

        var drivers = await _db.Drivers.ToListAsync();
        var wsDriver = NewSheet(wb, "Drivers", "Drivers", "", new[] { "Name", "Phone (optional)", "IsActive (TRUE/FALSE)" }, new[] { "Ahmed Hassan", "+249911234567", "TRUE" });
        for (int i = 0; i < drivers.Count; i++) WriteRow(wsDriver, 6 + i, drivers[i].Name, drivers[i].Phone, drivers[i].IsActive);

        var trucks = await _db.Trucks.Include(t => t.Driver).ToListAsync();
        var wsTruck = NewSheet(wb, "Trucks", "Trucks", "DriverName is optional but must match an existing Driver if provided.",
            new[] { "PlateNo", "DriverName (optional)", "IsActive (TRUE/FALSE)" }, new[] { "KRT-12345", "Ahmed Hassan", "TRUE" });
        for (int i = 0; i < trucks.Count; i++) WriteRow(wsTruck, 6 + i, trucks[i].PlateNo, trucks[i].Driver?.Name, trucks[i].IsActive);

        var warehouses = await _db.Warehouses.Include(w => w.City).ToListAsync();
        var wsWh = NewSheet(wb, "Warehouses", "Warehouses", "CityName is optional but must match an existing Logistics City if provided.",
            new[] { "Name", "CityName (optional)", "ContactName (optional)", "ContactPhone (optional)", "IsActive (TRUE/FALSE)" },
            new[] { "Main Warehouse Khartoum", "Khartoum", "Omar Ali", "+249911111111", "TRUE" });
        for (int i = 0; i < warehouses.Count; i++)
            WriteRow(wsWh, 6 + i, warehouses[i].Name, warehouses[i].City?.Name, warehouses[i].ContactName, warehouses[i].ContactPhone, warehouses[i].IsActive);

        var tenors = await _db.Tenors.ToListAsync();
        var wsTenor = NewSheet(wb, "Tenors", "Tenors", "Used for both bank collection Tenor and the additional CBOS Allowance dropdown.",
            new[] { "Days", "IsActive (TRUE/FALSE)" }, new[] { "90", "TRUE" });
        for (int i = 0; i < tenors.Count; i++) WriteRow(wsTenor, 6 + i, tenors[i].Days, tenors[i].IsActive);

        var senderBanks = await _db.SenderBanks.ToListAsync();
        var wsSb = NewSheet(wb, "SenderBanks", "Sender Banks", "ChargeRate is a fraction (e.g. 0.001 = 0.1%). MinimumChargeAed is the floor charge.",
            new[] { "Name", "ChargeRate", "MinimumChargeAed", "IsActive (TRUE/FALSE)" }, new[] { "Emirates NBD", "0.001", "150", "TRUE" });
        for (int i = 0; i < senderBanks.Count; i++) WriteRow(wsSb, 6 + i, senderBanks[i].Name, senderBanks[i].ChargeRate, senderBanks[i].MinimumChargeAed, senderBanks[i].IsActive);

        var receiverBanks = await _db.ReceiverBanks.ToListAsync();
        var wsRb = NewSheet(wb, "ReceiverBanks", "Receiver Banks", "All rates are fractions (e.g. 0.001 = 0.1%).",
            new[] { "Name", "BankChargeRate", "ImChargeRate", "TotalChargeRate", "IsActive (TRUE/FALSE)" }, new[] { "Bank of Khartoum", "0.001", "0.0005", "0.0015", "TRUE" });
        for (int i = 0; i < receiverBanks.Count; i++)
            WriteRow(wsRb, 6 + i, receiverBanks[i].Name, receiverBanks[i].BankChargeRate, receiverBanks[i].ImChargeRate, receiverBanks[i].TotalChargeRate, receiverBanks[i].IsActive);

        var spcTiers = await _db.SpcStorageTiers.OrderBy(t => t.TierOrder).ToListAsync();
        var wsSpc = NewSheet(wb, "SpcStorageTiers", "SPC Storage Tiers", "TierOrder controls sequence (1, 2, 3...). Leave DurationDays blank for the final, open-ended tier. Rates are SPC Euro per FCL per day.",
            new[] { "TierOrder", "Label", "DurationDays (blank = open-ended)", "Rate20", "Rate40" }, new[] { "1", "Tier 1", "15", "5", "8" });
        for (int i = 0; i < spcTiers.Count; i++) WriteRow(wsSpc, 6 + i, spcTiers[i].TierOrder, spcTiers[i].Label, spcTiers[i].DurationDays, spcTiers[i].Rate20, spcTiers[i].Rate40);

        var acd = await _db.AcdCostSettings.ToListAsync();
        var wsAcd = NewSheet(wb, "AcdCostSettings", "ACD Cost Settings", "One row per effective date — rates in USD per container.",
            new[] { "Rate20Usd", "Rate40Usd", "EffectiveDate (YYYY-MM-DD)" }, new[] { "50", "80", "2026-01-01" });
        for (int i = 0; i < acd.Count; i++) WriteRow(wsAcd, 6 + i, acd[i].Rate20Usd, acd[i].Rate40Usd, acd[i].EffectiveDate);

        var markups = await _db.OffshoreMarkupDefaults.Include(m => m.BusinessPartner).Include(m => m.DefaultCurrency).ToListAsync();
        var wsMarkup = NewSheet(wb, "OffshoreMarkupDefaults", "TP — Default Markups", "BusinessPartnerName must be a partner already flagged IsOffshoreEntity=TRUE.",
            new[] { "BusinessPartnerName", "DefaultMarkupPercent", "DefaultCurrencyCode" }, new[] { "Cencom", "8", "USD" });
        for (int i = 0; i < markups.Count; i++) WriteRow(wsMarkup, 6 + i, markups[i].BusinessPartner?.Name, markups[i].DefaultMarkupPercent, markups[i].DefaultCurrency?.Code);

        await WriteSimpleNameActive(wb, "ClearanceChargeTypes", "Clearance Charge Types", "DO Fees", _db.ClearanceChargeTypes.Select(x => new NameActive(x.Name, x.IsActive)));

        var slaSettings = await _db.ClearanceSlaSettings.ToListAsync();
        var wsSla = NewSheet(wb, "ClearanceSlaSettings", "Process SLA (Clearance Step Targets)",
            "Division: ClearanceGeneral/Route1/Route2/Route3/PreClearanceDocs/PreClearanceMot/PreClearanceSsmo/PreClearanceDo. SequenceOrder controls step order within a Division. TargetDays can have decimals. TargetDaysEtd is only used by PreClearanceDocs rows (the forward-from-ETD leg alongside TargetDays' backward-from-ETA leg) — leave blank/0 for every other division.",
            new[] { "Division", "GroupItem", "SequenceOrder", "TargetDays", "IsActive (TRUE/FALSE)", "TargetDaysEtd" }, new[] { "Route1", "SPC Bill", "7", "3", "TRUE", "0" });
        for (int i = 0; i < slaSettings.Count; i++)
            WriteRow(wsSla, 6 + i, slaSettings[i].Division, slaSettings[i].GroupItem, slaSettings[i].SequenceOrder, slaSettings[i].TargetDays, slaSettings[i].IsActive, slaSettings[i].TargetDaysEtd);

        var spcRates = await _db.SpcRates.ToListAsync();
        var wsSpcRate = NewSheet(wb, "SpcRates", "SPC Euro-to-SDG Rates",
            "One row per effective date. EuroToSdgRate = how many SDG equal 1 Euro, used to convert SPC storage tier charges (in Euro) to SDG.",
            new[] { "EuroToSdgRate", "EffectiveDate" }, new[] { "650", "2026-01-01" });
        for (int i = 0; i < spcRates.Count; i++)
            WriteRow(wsSpcRate, 6 + i, spcRates[i].EuroToSdgRate, spcRates[i].EffectiveDate);

        var bankAccounts = await _db.ReceiverBankAccounts.Include(a => a.ReceiverBank).ToListAsync();
        var wsBankAcc = NewSheet(wb, "ReceiverBankAccounts", "Receiver Bank Accounts",
            "ReceiverBankName must match an existing Receiver Bank Name. A bank can have several accounts (e.g. one per currency).",
            new[] { "ReceiverBankName", "AccountNo", "AccountName", "IsActive (TRUE/FALSE)" }, new[] { "UCB", "1000269", "CTC Group Ltd - USD", "TRUE" });
        for (int i = 0; i < bankAccounts.Count; i++)
            WriteRow(wsBankAcc, 6 + i, bankAccounts[i].ReceiverBank?.Name, bankAccounts[i].AccountNo, bankAccounts[i].AccountName, bankAccounts[i].IsActive);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private record NameActive(string Name, bool IsActive);

    private Task WriteSimpleNameActive(XLWorkbook wb, string sheetName, string title, string exampleName, IQueryable<NameActive> query)
        => WriteSimpleNameActiveList(wb, sheetName, title, exampleName, query.ToListAsync());

    private async Task WriteSimpleNameActiveList(XLWorkbook wb, string sheetName, string title, string exampleName, Task<List<NameActive>> queryTask)
    {
        var items = await queryTask;
        var ws = NewSheet(wb, sheetName, title, "", new[] { "Name", "IsActive (TRUE/FALSE)" }, new[] { exampleName, "TRUE" });
        for (int i = 0; i < items.Count; i++) WriteRow(ws, 6 + i, items[i].Name, items[i].IsActive);
    }
}
