using Microsoft.AspNetCore.Identity;
using ShippingPortal.Api.Models.Identity;

namespace ShippingPortal.Api.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var bootstrapEmail = config["BOOTSTRAP_SUPERUSER_EMAIL"];
        var bootstrapPassword = config["BOOTSTRAP_SUPERUSER_PASSWORD"];
        if (string.IsNullOrWhiteSpace(bootstrapEmail) || string.IsNullOrWhiteSpace(bootstrapPassword))
            return;

        var existing = await userManager.FindByEmailAsync(bootstrapEmail);
        if (existing is not null) return;

        var user = new ApplicationUser
        {
            UserName = bootstrapEmail,
            Email = bootstrapEmail,
            DisplayName = "System Administrator",
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, bootstrapPassword);
        if (result.Succeeded)
            await userManager.AddToRoleAsync(user, AppRoles.SuperUser);
    }
}
