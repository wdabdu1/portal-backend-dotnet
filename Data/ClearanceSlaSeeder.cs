using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Data;

public static class ClearanceSlaSeeder
{
    // (Division, GroupItem, SequenceOrder, DefaultTargetDays)
    private static readonly (string Division, string GroupItem, int Sequence, int DefaultDays)[] Rows =
    {
        (ClearanceDivision.General, "General Info", 1, 2),
        (ClearanceDivision.General, "Delivery Order", 2, 2),
        (ClearanceDivision.General, "Clearance Cost Estimate", 3, 2),
        (ClearanceDivision.General, "Customs Certificate Entry", 4, 1),

        (ClearanceDivision.Route1, "Containers Move Process", 1, 1),
        (ClearanceDivision.Route1, "SSMO File Process", 2, 2),
        (ClearanceDivision.Route1, "Customs Examination (Form 48)", 3, 2),
        (ClearanceDivision.Route1, "Customs Lab", 4, 2),
        (ClearanceDivision.Route1, "SSMO Examination", 5, 2),
        (ClearanceDivision.Route1, "Customs Evaluation", 6, 2),
        (ClearanceDivision.Route1, "SPC Bill", 7, 1),
        (ClearanceDivision.Route1, "Truck & Containers", 8, 2),

        (ClearanceDivision.Route2, "FZ Deposit Request", 1, 1),
        (ClearanceDivision.Route2, "Customs Inspection", 2, 1),
        (ClearanceDivision.Route2, "SPC Bill", 3, 1),
        (ClearanceDivision.Route2, "Truck & Containers", 4, 2),

        (ClearanceDivision.Route3, "Customs Certificate Entry", 1, 1),
        (ClearanceDivision.Route3, "SSMO File Process", 2, 2),
        (ClearanceDivision.Route3, "Customs Examination (Form 48)", 3, 2),
        (ClearanceDivision.Route3, "Customs Lab", 4, 2),
        (ClearanceDivision.Route3, "SSMO Examination", 5, 2),
        (ClearanceDivision.Route3, "Customs Evaluation", 6, 2),
        (ClearanceDivision.Route3, "Truck & Containers", 7, 2),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingPortalDbContext>();

        // Purge any leftover rows from the old (pre-breakdown) schema, which
        // won't have a Division set. Safe: this table has never held real
        // user data beyond default target days.
        var orphaned = await db.ClearanceSlaSettings.Where(s => s.Division == "").ToListAsync();
        if (orphaned.Count > 0)
        {
            db.ClearanceSlaSettings.RemoveRange(orphaned);
            await db.SaveChangesAsync();
        }

        foreach (var (division, groupItem, sequence, defaultDays) in Rows)
        {
            var exists = await db.ClearanceSlaSettings.AnyAsync(s => s.Division == division && s.GroupItem == groupItem);
            if (!exists)
            {
                db.ClearanceSlaSettings.Add(new ClearanceSlaSetting
                {
                    Division = division,
                    GroupItem = groupItem,
                    SequenceOrder = sequence,
                    TargetDays = defaultDays,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
