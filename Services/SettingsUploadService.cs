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
            ("SpcRates", UploadSpcRates),
            ("ReceiverBankAccounts", UploadReceiverBankAccounts),
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
// ---------- Specific handlers ----------

    private async Task<SheetUploadResult> UploadDivisions(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var bus = await _db.BusinessUnits.ToListAsync();
        var existing = await _db.Divisions.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var buCode = S(ws, row, 1); var code = S(ws, row, 2); var name = S(ws, row, 3);
            if (buCode is null || code is null || name is null) { errors.Add($"Row {row}: BusinessUnitCode, Code, and Name are all required."); continue; }
            var bu = bus.FirstOrDefault(b => b.Code == buCode || b.Name == buCode);
            if (bu is null) { errors.Add($"Row {row}: Business Unit '{buCode}' not found (checked both Code and Name)."); continue; }
            var active = B(ws, row, 4) ?? true;

            var match = existing.FirstOrDefault(d => d.BusinessUnitId == bu.Id && d.Code == code);
            if (match is null)
            {
                var d = new Division { BusinessUnitId = bu.Id, Code = code, Name = name, IsActive = active };
                _db.Divisions.Add(d); existing.Add(d); created++;
            }
            else { match.Name = name; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Divisions", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadBusinessPartners(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.BusinessPartners.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 6)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var isSupplier = B(ws, row, 2) ?? false;
            var isConsignee = B(ws, row, 3) ?? false;
            var isBrand = B(ws, row, 4) ?? false;
            var isOffshore = B(ws, row, 5) ?? false;
            var active = B(ws, row, 6) ?? true;

            var match = existing.FirstOrDefault(p => p.Name == name);
            if (match is null)
            {
                var p = new BusinessPartner { Name = name, IsSupplier = isSupplier, IsConsignee = isConsignee, IsBrandManufacturer = isBrand, IsOffshoreEntity = isOffshore, IsActive = active };
                _db.BusinessPartners.Add(p); existing.Add(p); created++;
            }
            else
            {
                match.IsSupplier = isSupplier; match.IsConsignee = isConsignee; match.IsBrandManufacturer = isBrand; match.IsOffshoreEntity = isOffshore; match.IsActive = active;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("BusinessPartners", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadUnitsOfMeasure(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.UnitsOfMeasure.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 2)) continue;
            var code = S(ws, row, 1);
            if (code is null) { errors.Add($"Row {row}: Code is required."); continue; }
            var active = B(ws, row, 2) ?? true;
            var match = existing.FirstOrDefault(u => u.Code == code);
            if (match is null) { var u = new UnitOfMeasure { Code = code, IsActive = active }; _db.UnitsOfMeasure.Add(u); existing.Add(u); created++; }
            else { match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("UnitsOfMeasure", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadProductCategories(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var tariffGroups = await _db.TariffGroups.ToListAsync();
        var existing = await _db.ProductCategories.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var tariffName = S(ws, row, 2);
            int? tariffId = null;
            if (tariffName is not null)
            {
                var tg = tariffGroups.FirstOrDefault(t => t.Name == tariffName);
                if (tg is null) { errors.Add($"Row {row}: Tariff Group '{tariffName}' not found."); continue; }
                tariffId = tg.Id;
            }
            var active = B(ws, row, 3) ?? true;
            var match = existing.FirstOrDefault(c => c.Name == name);
            if (match is null)
            {
                var c = new ProductCategory { Name = name, TariffGroupId = tariffId, IsActive = active };
                _db.ProductCategories.Add(c); existing.Add(c); created++;
            }
            else { match.TariffGroupId = tariffId; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ProductCategories", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadModelProducts(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var categories = await _db.ProductCategories.ToListAsync();
        var types = await _db.ProductTypes.ToListAsync();
        var existing = await _db.ModelProducts.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var catName = S(ws, row, 2); var typeName = S(ws, row, 3); var desc = S(ws, row, 4);
            int? catId = null, typeId = null;
            if (catName is not null)
            {
                var c = categories.FirstOrDefault(x => x.Name == catName);
                if (c is null) { errors.Add($"Row {row}: Product Category '{catName}' not found."); continue; }
                catId = c.Id;
            }
            if (typeName is not null)
            {
                var t = types.FirstOrDefault(x => x.Name == typeName);
                if (t is null) { errors.Add($"Row {row}: Product Type '{typeName}' not found."); continue; }
                typeId = t.Id;
            }
            var active = B(ws, row, 5) ?? true;
            var match = existing.FirstOrDefault(m => m.Name == name);
            if (match is null)
            {
                var m = new ModelProduct { Name = name, ProductCategoryId = catId, ProductTypeId = typeId, Description = desc, IsActive = active };
                _db.ModelProducts.Add(m); existing.Add(m); created++;
            }
            else { match.ProductCategoryId = catId; match.ProductTypeId = typeId; match.Description = desc; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ModelProducts", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadFxRates(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var currencies = await _db.Currencies.ToListAsync();
        var existing = await _db.FxRates.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var code = S(ws, row, 1); var rate = D(ws, row, 2); var date = Dt(ws, row, 3);
            if (code is null || rate is null || date is null) { errors.Add($"Row {row}: CurrencyCode, RateToUsd, and EffectiveDate are all required."); continue; }
            var cur = currencies.FirstOrDefault(c => c.Code == code || c.Name == code);
            if (cur is null) { errors.Add($"Row {row}: Currency '{code}' not found (checked both Code and Name)."); continue; }

            var match = existing.FirstOrDefault(f => f.CurrencyId == cur.Id && f.EffectiveDate == date);
            if (match is null)
            {
                var f = new FxRate { CurrencyId = cur.Id, RateToUsd = rate.Value, EffectiveDate = date.Value };
                _db.FxRates.Add(f); existing.Add(f); created++;
            }
            else { match.RateToUsd = rate.Value; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("FxRates", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadShippingLineDemurrageTariffs(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var lines = await _db.ShippingLines.ToListAsync();
        var tariffGroups = await _db.TariffGroups.ToListAsync();
        var existing = await _db.ShippingLineDemurrageTariffs.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 7)) continue;
            var lineName = S(ws, row, 1); var tariffName = S(ws, row, 2); var size = S(ws, row, 3);
            var freeDays = I(ws, row, 4); var firstDays = I(ws, row, 5); var firstRate = D(ws, row, 6); var afterRate = D(ws, row, 7);
            if (lineName is null || tariffName is null || size is null) { errors.Add($"Row {row}: ShippingLineName, TariffGroupName, and ContainerSize are all required."); continue; }
            if (size != "20" && size != "40") { errors.Add($"Row {row}: ContainerSize must be 20 or 40."); continue; }
            var line = lines.FirstOrDefault(l => l.Name == lineName);
            if (line is null) { errors.Add($"Row {row}: Shipping Line '{lineName}' not found."); continue; }
                if (line is null) { errors.Add($"Row {row}: Shipping Line '{lineName}' not found."); continue; }
            var tg = tariffGroups.FirstOrDefault(t => t.Name == tariffName);
            if (tg is null) { errors.Add($"Row {row}: Tariff Group '{tariffName}' not found."); continue; }

            var match = existing.FirstOrDefault(x => x.ShippingLineId == line.Id && x.TariffGroupId == tg.Id && x.ContainerSize == size);
            if (match is null)
            {
                var x = new ShippingLineDemurrageTariff
                {
                    ShippingLineId = line.Id, TariffGroupId = tg.Id, ContainerSize = size,
                    FreeDays = freeDays ?? 0, FirstPeriodDays = firstDays ?? 0, FirstPeriodRateSdg = firstRate ?? 0, AfterwardRateSdg = afterRate ?? 0
                };
                _db.ShippingLineDemurrageTariffs.Add(x); existing.Add(x); created++;
            }
            else
            {
                match.FreeDays = freeDays ?? match.FreeDays; match.FirstPeriodDays = firstDays ?? match.FirstPeriodDays;
                match.FirstPeriodRateSdg = firstRate ?? match.FirstPeriodRateSdg; match.AfterwardRateSdg = afterRate ?? match.AfterwardRateSdg;
                updated++;
            }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ShippingLineDemurrageTariffs", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadShipmentDestinations(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.ShipmentDestinations.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var isFz = B(ws, row, 2) ?? false; var duration = I(ws, row, 3) ?? 0; var active = B(ws, row, 4) ?? true;
            var match = existing.FirstOrDefault(d => d.Name == name);
            if (match is null)
            {
                var d = new ShipmentDestination { Name = name, IsFreeZone = isFz, DefaultDurationDays = duration, IsActive = active };
                _db.ShipmentDestinations.Add(d); existing.Add(d); created++;
            }
            else { match.IsFreeZone = isFz; match.DefaultDurationDays = duration; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ShipmentDestinations", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadPublicHolidays(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.PublicHolidays.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var date = Dt(ws, row, 1); var name = S(ws, row, 2);
            if (date is null || name is null) { errors.Add($"Row {row}: Date and Name are both required."); continue; }
            var affectsDxb = B(ws, row, 3) ?? true; var affectsClr = B(ws, row, 4) ?? true;
            var match = existing.FirstOrDefault(h => h.Date == date && h.Name == name);
            if (match is null)
            {
                var h = new PublicHoliday { Date = date.Value, Name = name, AffectsDxb = affectsDxb, AffectsClr = affectsClr };
                _db.PublicHolidays.Add(h); existing.Add(h); created++;
            }
            else { match.AffectsDxb = affectsDxb; match.AffectsClr = affectsClr; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("PublicHolidays", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadDrivers(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.Drivers.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var phone = S(ws, row, 2); var active = B(ws, row, 3) ?? true;
            var match = existing.FirstOrDefault(d => d.Name == name);
            if (match is null) { var d = new Driver { Name = name, Phone = phone, IsActive = active }; _db.Drivers.Add(d); existing.Add(d); created++; }
            else { match.Phone = phone; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Drivers", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadTrucks(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var drivers = await _db.Drivers.ToListAsync();
        var existing = await _db.Trucks.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var plate = S(ws, row, 1);
            if (plate is null) { errors.Add($"Row {row}: PlateNo is required."); continue; }
            var driverName = S(ws, row, 2);
            int? driverId = null;
            if (driverName is not null)
            {
                var dr = drivers.FirstOrDefault(d => d.Name == driverName);
                if (dr is null) { errors.Add($"Row {row}: Driver '{driverName}' not found."); continue; }
                driverId = dr.Id;
            }
            var active = B(ws, row, 3) ?? true;
            var match = existing.FirstOrDefault(t => t.PlateNo == plate);
            if (match is null) { var t = new Truck { PlateNo = plate, DriverId = driverId, IsActive = active }; _db.Trucks.Add(t); existing.Add(t); created++; }
            else { match.DriverId = driverId; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Trucks", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadWarehouses(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var cities = await _db.LogisticsCities.ToListAsync();
        var existing = await _db.Warehouses.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var cityName = S(ws, row, 2);
            int? cityId = null;
            if (cityName is not null)
            {
                var city = cities.FirstOrDefault(c => c.Name == cityName);
                if (city is null) { errors.Add($"Row {row}: Logistics City '{cityName}' not found."); continue; }
                cityId = city.Id;
            }
            var contactName = S(ws, row, 3); var contactPhone = S(ws, row, 4); var active = B(ws, row, 5) ?? true;
            var match = existing.FirstOrDefault(w => w.Name == name);
            if (match is null)
            {
                var w = new Warehouse { Name = name, CityId = cityId, ContactName = contactName, ContactPhone = contactPhone, IsActive = active };
                _db.Warehouses.Add(w); existing.Add(w); created++;
            }
            else { match.CityId = cityId; match.ContactName = contactName; match.ContactPhone = contactPhone; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Warehouses", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadTenors(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.Tenors.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 2)) continue;
            var days = I(ws, row, 1);
            if (days is null) { errors.Add($"Row {row}: Days is required."); continue; }
            var active = B(ws, row, 2) ?? true;
            var match = existing.FirstOrDefault(t => t.Days == days);
            if (match is null) { var t = new Tenor { Days = days.Value, IsActive = active }; _db.Tenors.Add(t); existing.Add(t); created++; }
            else { match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("Tenors", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadSenderBanks(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.SenderBanks.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 4)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var rate = D(ws, row, 2) ?? 0; var min = D(ws, row, 3) ?? 0; var active = B(ws, row, 4) ?? true;
            var match = existing.FirstOrDefault(b => b.Name == name);
            if (match is null) { var b = new SenderBank { Name = name, ChargeRate = rate, MinimumChargeAed = min, IsActive = active }; _db.SenderBanks.Add(b); existing.Add(b); created++; }
            else { match.ChargeRate = rate; match.MinimumChargeAed = min; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("SenderBanks", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadReceiverBanks(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.ReceiverBanks.ToListAsync();
        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var name = S(ws, row, 1);
            if (name is null) { errors.Add($"Row {row}: Name is required."); continue; }
            var bankRate = D(ws, row, 2) ?? 0; var imRate = D(ws, row, 3) ?? 0; var totalRate = D(ws, row, 4) ?? 0; var active = B(ws, row, 5) ?? true;
            var match = existing.FirstOrDefault(b => b.Name == name);
            if (match is null)
            {
                var b = new ReceiverBank { Name = name, BankChargeRate = bankRate, ImChargeRate = imRate, TotalChargeRate = totalRate, IsActive = active };
                _db.ReceiverBanks.Add(b); existing.Add(b); created++;
            }
            else { match.BankChargeRate = bankRate; match.ImChargeRate = imRate; match.TotalChargeRate = totalRate; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ReceiverBanks", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadSpcStorageTiers(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.SpcStorageTiers.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var order = I(ws, row, 1); var label = S(ws, row, 2);
            if (order is null || label is null) { errors.Add($"Row {row}: TierOrder and Label are both required."); continue; }
            var duration = I(ws, row, 3); var rate20 = D(ws, row, 4) ?? 0; var rate40 = D(ws, row, 5) ?? 0;
            var match = existing.FirstOrDefault(t => t.TierOrder == order);
            if (match is null)
            {
                var t = new SpcStorageTier { TierOrder = order.Value, Label = label, DurationDays = duration, Rate20 = rate20, Rate40 = rate40 };
                _db.SpcStorageTiers.Add(t); existing.Add(t); created++;
            }
            else { match.Label = label; match.DurationDays = duration; match.Rate20 = rate20; match.Rate40 = rate40; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("SpcStorageTiers", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadAcdCostSettings(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.AcdCostSettings.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var rate20 = D(ws, row, 1); var rate40 = D(ws, row, 2); var date = Dt(ws, row, 3);
            if (rate20 is null || rate40 is null || date is null) { errors.Add($"Row {row}: Rate20Usd, Rate40Usd, and EffectiveDate are all required."); continue; }
            var match = existing.FirstOrDefault(a => a.EffectiveDate == date);
            if (match is null)
            {
                var a = new AcdCostSetting { Rate20Usd = rate20.Value, Rate40Usd = rate40.Value, EffectiveDate = date.Value };
                _db.AcdCostSettings.Add(a); existing.Add(a); created++;
            }
            else { match.Rate20Usd = rate20.Value; match.Rate40Usd = rate40.Value; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("AcdCostSettings", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadOffshoreMarkupDefaults(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var partners = await _db.BusinessPartners.ToListAsync();
        var currencies = await _db.Currencies.ToListAsync();
        var existing = await _db.OffshoreMarkupDefaults.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var partnerName = S(ws, row, 1); var percent = D(ws, row, 2); var curCode = S(ws, row, 3);
            if (partnerName is null || percent is null || curCode is null) { errors.Add($"Row {row}: BusinessPartnerName, DefaultMarkupPercent, and DefaultCurrencyCode are all required."); continue; }
            var partner = partners.FirstOrDefault(p => p.Name == partnerName);
            if (partner is null) { errors.Add($"Row {row}: Business Partner '{partnerName}' not found."); continue; }
            if (!partner.IsOffshoreEntity) { errors.Add($"Row {row}: '{partnerName}' is not flagged as an Offshore Entity."); continue; }
            var cur = currencies.FirstOrDefault(c => c.Code == curCode || c.Name == curCode);
            if (cur is null) { errors.Add($"Row {row}: Currency '{curCode}' not found (checked both Code and Name)."); continue; }

            var match = existing.FirstOrDefault(m => m.BusinessPartnerId == partner.Id);
            if (match is null)
            {
                var m = new Models.OffshoreMarkupDefault { BusinessPartnerId = partner.Id, DefaultMarkupPercent = percent.Value, DefaultCurrencyId = cur.Id };
                _db.OffshoreMarkupDefaults.Add(m); existing.Add(m); created++;
            }
            else { match.DefaultMarkupPercent = percent.Value; match.DefaultCurrencyId = cur.Id; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("OffshoreMarkupDefaults", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadClearanceSlaSettings(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.ClearanceSlaSettings.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 5)) continue;
            var division = S(ws, row, 1); var groupItem = S(ws, row, 2); var seq = I(ws, row, 3); var target = D(ws, row, 4);
            if (division is null || groupItem is null || seq is null || target is null) { errors.Add($"Row {row}: Division, GroupItem, SequenceOrder, and TargetDays are all required."); continue; }
            var active = B(ws, row, 5) ?? true;
            var match = existing.FirstOrDefault(x => x.Division == division && x.GroupItem == groupItem);
            if (match is null)
            {
                var x = new ClearanceSlaSetting { Division = division, GroupItem = groupItem, SequenceOrder = seq.Value, TargetDays = target.Value, IsActive = active };
                _db.ClearanceSlaSettings.Add(x); existing.Add(x); created++;
            }
            else { match.SequenceOrder = seq.Value; match.TargetDays = target.Value; match.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ClearanceSlaSettings", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadSpcRates(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var existing = await _db.SpcRates.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 2)) continue;
            var rate = D(ws, row, 1); var date = Dt(ws, row, 2);
            if (rate is null || date is null) { errors.Add($"Row {row}: EuroToSdgRate and EffectiveDate are both required."); continue; }

            var match2 = existing.FirstOrDefault(s => s.EffectiveDate == date);
            if (match2 is null)
            {
                var s = new SpcRate { EuroToSdgRate = rate.Value, EffectiveDate = date.Value };
                _db.SpcRates.Add(s); existing.Add(s); created++;
            }
            else { match2.EuroToSdgRate = rate.Value; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("SpcRates", created, updated, errors);
    }

    private async Task<SheetUploadResult> UploadReceiverBankAccounts(IXLWorksheet ws)
    {
        var errors = new List<string>(); int created = 0, updated = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? FirstDataRow - 1;
        var banks = await _db.ReceiverBanks.ToListAsync();
        var existingAccounts = await _db.ReceiverBankAccounts.ToListAsync();

        for (int row = FirstDataRow; row <= lastRow; row++)
        {
            if (RowIsBlank(ws, row, 3)) continue;
            var bankName = S(ws, row, 1); var accountNo = S(ws, row, 2); var accountName = S(ws, row, 3);
            if (bankName is null || accountNo is null || accountName is null) { errors.Add($"Row {row}: Receiver Bank Name, Account No., and Account Name are all required."); continue; }

            var bank = banks.FirstOrDefault(b => b.Name == bankName);
            if (bank is null) { errors.Add($"Row {row}: Receiver Bank '{bankName}' not found."); continue; }
            var active = B(ws, row, 4) ?? true;

            var accMatch = existingAccounts.FirstOrDefault(a => a.ReceiverBankId == bank.Id && a.AccountNo == accountNo);
            if (accMatch is null)
            {
                var a = new ReceiverBankAccount { ReceiverBankId = bank.Id, AccountNo = accountNo, AccountName = accountName, IsActive = active };
                _db.ReceiverBankAccounts.Add(a); existingAccounts.Add(a); created++;
            }
            else { accMatch.AccountName = accountName; accMatch.IsActive = active; updated++; }
        }
        await _db.SaveChangesAsync();
        return new SheetUploadResult("ReceiverBankAccounts", created, updated, errors);
    }
}
