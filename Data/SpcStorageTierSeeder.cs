using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Lookups;

namespace ShippingPortal.Api.Data;

public static class SpcStorageTierSeeder
{
    private static readonly (int Order, string Label, int? Days, decimal Rate20, decimal Rate40)[] Rows =
    {
        (1, "Tarif-1", 20, 0m, 0m),
        (2, "Tarif-2", 20, 16m, 23m),
        (3, "Tarif-3", null, 29m, 46m),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingPortalDbContext>();

        foreach (var (order, label, days, rate20, rate40) in Rows)
        {
            var exists = await db.SpcStorageTiers.AnyAsync(t => t.TierOrder == order);
            if (!exists)
            {
                db.SpcStorageTiers.Add(new SpcStorageTier
                {
                    TierOrder = order,
                    Label = label,
                    DurationDays = days,
                    Rate20 = rate20,
                    Rate40 = rate40
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
