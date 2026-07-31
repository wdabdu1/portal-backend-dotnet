using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Data;

public static class ClearanceSlaSeeder
{
    // Fixed set of clearance milestones. Users can adjust TargetDays via
    // Settings, but cannot add or remove rows — the process list itself is
    // defined here, in code, matching the actual Clearance workflow stages.
    private static readonly (string Key, string Label, int DefaultDays)[] Milestones =
    {
        ("TotalClearance", "Total Clearance Time (ETA to Cleared)", 14),
        ("Route1Total", "Route 1: Clear at Port — Total Duration", 10),
        ("Route2Total", "Route 2: FZ Deposit — Total Duration", 5),
        ("Route3Total", "Route 3: Clear from FZ — Total Duration", 12)
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingPortalDbContext>();

        foreach (var (key, label, defaultDays) in Milestones)
        {
            var exists = await db.ClearanceSlaSettings.AnyAsync(s => s.MilestoneKey == key);
            if (!exists)
            {
                db.ClearanceSlaSettings.Add(new ClearanceSlaSetting
                {
                    MilestoneKey = key,
                    Label = label,
                    TargetDays = defaultDays,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
