using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Api.Data;

public static class DevSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakedManilaDbContext>();
        await db.Database.MigrateAsync(ct);

        if (await db.Products.AnyAsync(ct))
        {
            return;
        }

        db.Products.AddRange(
            new Product { Name = "Classic Chocolate Chip", Slug = "classic-chocolate-chip", Description = "Crisp edges, chewy middle.", Price = 280m, SortOrder = 1 },
            new Product { Name = "Double Chocolate", Slug = "double-chocolate", Description = "Fudgy with molten chips.", Price = 320m, SortOrder = 2 },
            new Product { Name = "Red Velvet", Slug = "red-velvet", Description = "White chocolate buttons.", Price = 320m, SortOrder = 3 },
            new Product { Name = "Chocolate Chunk Banana Bread", Slug = "banana-bread", Description = "Fluffy loaf, melty chunks.", Price = 350m, SortOrder = 4 });
        await db.SaveChangesAsync(ct);
    }
}
