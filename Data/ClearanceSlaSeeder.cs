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

        // Remove the old "General Info" row and any pre-breakdown leftovers —
        // it's been dropped from the Clearance General division in the
        // updated spec.
        var toRemove = await db.ClearanceSlaSettings
            .Where(s => s.Division == "" || (s.Division == ClearanceDivision.General && s.GroupItem == "General Info"))
            .ToListAsync();
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
                // Keep sequence in sync with the latest spec even for
                // existing rows, so display order stays correct.
                existing.SequenceOrder = sequence;
            }
        }

        await db.SaveChangesAsync();
    }
}
