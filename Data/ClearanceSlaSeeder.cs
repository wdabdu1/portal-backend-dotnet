using Microsoft.EntityFrameworkCore;
using ShippingPortal.Api.Models.Clearance;

namespace ShippingPortal.Api.Data;

public static class ClearanceSlaSeeder
{
    private static readonly (string Division, string GroupItem, int Sequence, decimal DefaultDays)[] Rows =
    {
        (ClearanceDivision.General, "Delivery Order", 1, 1m),
        (ClearanceDivision.General, "Clearance Cost Estimate", 2, 0.25m),
        (ClearanceDivision.General, "Customs Certificate Entry", 3, 0.5m),

        (ClearanceDivision.Route1, "Containers Move Process", 1, 2m),
        (ClearanceDivision.Route1, "SSMO File Process", 2, 1m),
        (ClearanceDivision.Route1, "Customs Examination (Form 48)", 3, 1m),
        (ClearanceDivision.Route1, "Customs Lab", 4, 1m),
        (ClearanceDivision.Route1, "SSMO Examination", 5, 1m),
        (ClearanceDivision.Route1, "Customs Evaluation", 6, 1m),
        (ClearanceDivision.Route1, "SPC Bill", 7, 1m),
        (ClearanceDivision.Route1, "Truck & Containers", 8, 1m),

        (ClearanceDivision.Route2, "FZ Deposit Request", 1, 1m),
        (ClearanceDivision.Route2, "Customs Inspection", 2, 1m),
        (ClearanceDivision.Route2, "SPC Bill", 3, 1m),
        (ClearanceDivision.Route2, "Truck & Containers", 4, 2m),

        (ClearanceDivision.Route3, "Customs Certificate Entry", 1, 1m),
        (ClearanceDivision.Route3, "SSMO File Process", 2, 1m),
        (ClearanceDivision.Route3, "Customs Examination (Form 48)", 3, 1m),
        (ClearanceDivision.Route3, "Customs Lab", 4, 1m),
        (ClearanceDivision.Route3, "SSMO Examination", 5, 1m),
        (ClearanceDivision.Route3, "Customs Evaluation", 6, 1m),
        (ClearanceDivision.Route3, "Truck & Containers", 7, 1m),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingPortalDbContext>();

        // Self-healing cleanup: remove ANY row whose (Division, GroupItem)
        // pair isn't in the current Rows list above — catches leftovers from
        // any previous version of this seeder (old milestone-key rows,
        // "General Info", "...Total Duration" rows, etc.) without needing to
        // special-case each one by name.
        var validKeys = Rows.Select(r => (r.Division, r.GroupItem)).ToHashSet();
        var existingRows = await db.ClearanceSlaSettings.ToListAsync();
        var toRemove = existingRows.Where(s => !validKeys.Contains((s.Division, s.GroupItem))).ToList();
        if (toRemove.Count > 0)
        {
            db.ClearanceSlaSettings.RemoveRange(toRemove);
            await db.SaveChangesAsync();
        }

        foreach (var (division, groupItem, sequence, defaultDays) in Rows)
        {
            var existing = await db.ClearanceSlaSettings.FirstOrDefaultAsync(s => s.Division == division && s.GroupItem == groupItem);
            if (existing is null)
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
            else
            {
                existing.SequenceOrder = sequence;
            }
        }

        await db.SaveChangesAsync();
    }
}
