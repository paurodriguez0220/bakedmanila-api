using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Data;

public sealed class DbContextSmokeTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_CreateSchema_AndSoftDeleteFilterHidesDeletedProducts()
    {
        _db.Products.AddRange(
            new Product { Name = "Classic Chip", Slug = "classic-chip", Price = 280m },
            new Product { Name = "Old Flavor", Slug = "old-flavor", Price = 300m, IsDeleted = true });
        await _db.SaveChangesAsync();

        var visible = await _db.Products.ToListAsync();

        Assert.Single(visible);
        Assert.Equal("classic-chip", visible[0].Slug);
    }

    [Fact]
    public async Task OrderNumberSequence_Exists()
    {
        var results = await _db.Database
            .SqlQueryRaw<int>("SELECT CAST(NEXT VALUE FOR OrderNumberSeq AS int) AS [Value]")
            .ToListAsync();
        var next = Assert.Single(results);
        Assert.True(next >= 1);
    }
}
