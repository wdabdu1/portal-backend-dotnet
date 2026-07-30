using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Data;

namespace ShippingPortal.Api.Services;

public class FxRateService
{
    private readonly ShippingPortalDbContext _db;
    public FxRateService(ShippingPortalDbContext db) => _db = db;

    // Returns the most recent "1 USD = X currency" rate. Falls back to 1
    // (correct for USD itself, and a safe default if no rate has been
    // entered for a currency yet) rather than blocking PO/Shipment creation.
    public async Task<decimal> GetRateToUsdAsync(int currencyId)
    {
        var rate = await _db.FxRates
            .Where(r => r.CurrencyId == currencyId)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync();

        return rate?.RateToUsd ?? 1m;
    }
}
