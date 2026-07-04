using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Repositories;

public sealed class OrderRepositoryTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;
    private EfOrderRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
        _repo = new EfOrderRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private static Order NewOrder() => new()
    {
        OrderNumber = "BM-2026-0001",
        CustomerName = "Maria",
        Phone = "09171234567",
        PreferredDate = new DateOnly(2026, 7, 10),
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
        Subtotal = 280m,
        Items = [new OrderItem { ProductId = 0, ProductName = "snap", UnitPrice = 280m, Quantity = 1 }],
    };

    [Fact]
    public async Task GetByNumberAndPhone_ReturnsOrderWithItems_OnExactMatch()
    {
        var ct = CancellationToken.None;
        _db.Products.Add(new Product { Id = 0, Name = "P", Slug = "p", Price = 280m });
        await _db.SaveChangesAsync(ct);
        var order = NewOrder();
        order.Items[0].ProductId = _db.Products.Local.First().Id;
        _repo.Add(order);
        await _repo.SaveChangesAsync(ct);

        var found = await _repo.GetByNumberAndPhoneAsync("BM-2026-0001", "09171234567", ct);
        Assert.NotNull(found);
        Assert.Single(found.Items);

        Assert.Null(await _repo.GetByNumberAndPhoneAsync("BM-2026-0001", "09990000000", ct));
        Assert.Null(await _repo.GetByNumberAndPhoneAsync("BM-2026-9999", "09171234567", ct));
    }

    [Fact]
    public async Task GetNextOrderSequence_Increments()
    {
        var ct = CancellationToken.None;
        var first = await _repo.GetNextOrderSequenceAsync(ct);
        var second = await _repo.GetNextOrderSequenceAsync(ct);
        Assert.Equal(first + 1, second);
    }
}
