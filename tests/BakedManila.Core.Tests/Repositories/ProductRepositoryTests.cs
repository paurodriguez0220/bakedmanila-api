using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Repositories;

public sealed class ProductRepositoryTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;
    private EfProductRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
        _repo = new EfProductRepository(_db);

        _db.Products.AddRange(
            new Product { Name = "B Second", Slug = "b-second", Price = 300m, SortOrder = 2 },
            new Product { Name = "A First", Slug = "a-first", Price = 280m, SortOrder = 1 },
            new Product { Name = "Sold Out", Slug = "sold-out", Price = 280m, IsAvailable = false },
            new Product { Name = "Gone", Slug = "gone", Price = 100m, IsDeleted = true });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task GetAvailableAsync_ReturnsOnlyAvailable_SortedBySortOrder()
    {
        var products = await _repo.GetAvailableAsync(CancellationToken.None);
        Assert.Equal(["a-first", "b-second"], products.Select(p => p.Slug).ToArray());
    }

    [Fact]
    public async Task GetBySlugAsync_ReturnsNull_ForUnknownSlug()
    {
        Assert.Null(await _repo.GetBySlugAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task GetBySlugsAsync_ReturnsMatchingNonDeleted()
    {
        var products = await _repo.GetBySlugsAsync(["a-first", "gone"], CancellationToken.None);
        Assert.Single(products);
        Assert.Equal("a-first", products[0].Slug);
    }
}
