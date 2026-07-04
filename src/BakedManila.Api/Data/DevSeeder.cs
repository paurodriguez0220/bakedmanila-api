using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Api.Data;

public static class DevSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, IConfiguration config, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakedManilaDbContext>();
        await db.Database.MigrateAsync(ct);

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.AddRange(
                new Product { Name = "Classic Chocolate Chip", Slug = "classic-chocolate-chip", Description = "Crisp edges, chewy middle.", Price = 280m, SortOrder = 1 },
                new Product { Name = "Double Chocolate", Slug = "double-chocolate", Description = "Fudgy with molten chips.", Price = 320m, SortOrder = 2 },
                new Product { Name = "Red Velvet", Slug = "red-velvet", Description = "White chocolate buttons.", Price = 320m, SortOrder = 3 },
                new Product { Name = "Chocolate Chunk Banana Bread", Slug = "banana-bread", Description = "Fluffy loaf, melty chunks.", Price = 350m, SortOrder = 4 });
            await db.SaveChangesAsync(ct);
        }

        await SeedAdminAsync(scope.ServiceProvider, config, ct);
    }

    private static async Task SeedAdminAsync(IServiceProvider scopedServices, IConfiguration config, CancellationToken ct)
    {
        _ = ct;
        var roles = scopedServices.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin"))
        {
            _ = await roles.CreateAsync(new IdentityRole("Admin"));
        }

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return; // no admin configured — skip (prod seeds via config in Plan 5)
        }

        var users = scopedServices.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }
        var admin = new IdentityUser { UserName = email, Email = email };
        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }
        _ = await users.AddToRoleAsync(admin, "Admin");
    }
}
