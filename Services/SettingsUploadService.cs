using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Clearance;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Services;

public record SheetUploadResult(string Sheet, int Created, int Updated, List<string> Errors);
public record UploadSummary(List<SheetUploadResult> Results);

// Parses the "Settings Upload Template" workbook (one tab per Settings
// table, matching the Settings menu exactly) and upserts every row.
// Row 4 = headers, row 5 = the template's own worked example (always
// skipped), real data starts row 6. Upsert key varies per sheet — usually
// Name or Code, noted per method. Every row's errors are collected rather
// than aborting the whole sheet on the first bad row, so one typo doesn't
// block 200 good rows behind it.
public class SettingsUploadService
{
    private readonly ShippingPortalDbContext _db;
    public SettingsUploadService(ShippingPortalDbContext db) => _db = db;

    private const int FirstDataRow = 6;

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

        // Order matters — later sheets reference earlier ones by name/code.
        var handlers = new (string Sheet, Func<IXLWorksheet, Task<SheetUploadResult>> Handler)[]
        {
            ("BusinessUnits", UploadSimpleCodeNameActive<BusinessUnit>("BusinessUnits", bu => bu.Code, (bu, c) => bu.Code = c, (bu, n) => bu.Name = n, (bu, a) => bu.IsActive = a)),
            ("Divisions", UploadDivisions),
            ("BusinessPartners", UploadBusinessPartners),
            ("ApprovalTypes", UploadSimpleNameActive<ApprovalType>("ApprovalTypes")),
            ("PaymentTerms", UploadSimpleNameActive<PaymentTerm>("PaymentTerms")),
            ("Incoterms", UploadSimpleCodeNameActive<Incoterm>("Incoterms", x => x.Code, (x, c) => x.Code = c, (x, n) => x.Name = n, (x, a) => x.IsActive = a)),
            ("OriginCountries", UploadSimpleNameActive<OriginCountry>("OriginCountries")),
            ("UnitsOfMeasure", UploadUnitsOfMeasure),
            ("ShipmentModes", UploadSimpleNameActive<ShipmentMode>("ShipmentModes")),
            ("TariffGroups", UploadSimpleNameActive<TariffGroup>("TariffGroups")),
            ("ProductCategories", UploadProductCategories),
            ("ProductTypes", UploadSimpleNameActive<ProductType>("ProductTypes")),
            ("ModelProducts", UploadModelProducts),
            ("Currencies", UploadSimpleCodeNameActive<Currency>("Currencies", x => x.Code, (x, c) => x.Code = c, (x, n) => x.Name = n, (x, a) => x.IsActive = a)),
            ("FxRates", UploadFxRates),
            ("ShippingLines", UploadSimpleNameActive<ShippingLine>("ShippingLines")),
            ("ShippingLineDemurrageTariffs", UploadShippingLineDemurrageTariffs),
            ("Couriers", UploadSimpleNameActive<Courier>("Couriers")),
            ("Forwarders", UploadSimpleNameActive<Forwarder>("Forwarders")),
            ("ShipmentDestinations", UploadShipmentDestinations),
            ("PublicHolidays", UploadPublicHolidays),
            ("LogisticsCities", UploadSimpleNameActive<LogisticsCity>("LogisticsCities")),
            ("Drivers", UploadDrivers),
            ("Trucks", UploadTrucks),
            ("Warehouses", UploadWarehouses),
            ("Tenors", UploadTenors),
            ("SenderBanks", UploadSenderBanks),
            ("ReceiverBanks", UploadReceiverBanks),
            ("SpcStorageTiers", UploadSpcStorageTiers),
            ("AcdCostSettings", UploadAcdCostSettings),
            ("OffshoreMarkupDefaults", UploadOffshoreMarkupDefaults),
            ("ClearanceChargeTypes", UploadSimpleNameActive<ClearanceChargeType>("ClearanceChargeTypes")),
            ("ClearanceSlaSettings", UploadClearanceSlaSettings),
        };

        foreach (var (sheetName, handler) in handlers)
        {
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name == sheetName);
            if (ws is null) continue; // sheet not present in this workbook — skip silently
            results.Add(await handler(ws));
        }

        return new UploadSummary(results);
    }

    // ---------- Generic handlers for the simplest, most common shapes ----------

    // Name (col 1), IsActive (col 2) — used by 10 identical-shaped sheets.
    private Func<IXLWorksheet, Task<SheetUploadResult>> UploadSimpleNameActive<T>(string sheetLabel) where T : class, new()
    {
        return async ws =>
        {
            var errors = new List<string>();
            int created = 0, updated = 0;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
            var set = _db.Set<T>();

            for (int row = FirstDataRow; row <= lastRow; row++)
            {
                if (RowIsBlank(ws, row, 2)) continue;
                var name = S(ws, row, 1);
                if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
                var active = B(ws, row, 2) ?? true;

                var nameProp = typeof(T).GetProperty("Name")!;
                var activeProp = typeof(T).GetProperty("IsActive")!;

                var existing = (await set.ToListAsync()).FirstOrDefault(x => (string?)nameProp.GetValue(x) == name);
                if (existing is null)
                {
                    var entity = new T();
                    nameProp.SetValue(entity, name);
                    activeProp.SetValue(entity, active);
                    set.Add(entity);
                    created++;
                }
                else
                {
                    activeProp.SetValue(existing, active);
                    updated++;
                }
            }
            await _db.SaveChangesAsync();
            return new SheetUploadResult(sheetLabel, created, updated, errors);
        };
    }

    // Code (col1) + Name (col2) + IsActive (col3), upserted by Code.
    private Func<IXLWorksheet, Task<SheetUploadResult>> UploadSimpleCodeNameActive<T>(
        string sheetLabel, Func<T, string> getCode, Action<T, string> setCode, Action<T, string> setName, Action<T, bool> setActive)
        where T : class, new()
    {
        return async ws =>
        {
            var errors = new List<string>();
            int created = 0, updated = 0;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
            var set = _db.Set<T>();
            var all = await set.ToListAsync();

            for (int row = FirstDataRow; row <= lastRow; row++)
            {
                if (RowIsBlank(ws, row, 3)) continue;
                var code = S(ws, row, 1);
                var name = S(ws, row, 2);
                if (code is null || name is null) { errors.Add($"Row {row}: Code and Name are both required."); continue; }
                var active = B(ws, row, 3) ?? true;

                var existing = all.FirstOrDefault(x => getCode(x) == code);
                if (existing is null)
                {
                    var entity = new T();
                    setCode(entity, code);
                    setName(entity, name);
                    setActive(entity, active);
                    set.Add(entity);
                    all.Add(entity);
                    created++;
                }
                else
                {
                    setName(existing, name);
                    setActive(existing, active);
                    updated++;
                }
            }
            await _db.SaveChangesAsync();
            return new SheetUploadResult(sheetLabel, created, updated, errors);
        };
    }
