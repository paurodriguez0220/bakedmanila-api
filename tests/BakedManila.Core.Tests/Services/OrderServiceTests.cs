using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BakedManila.Core.Tests.Services;

file sealed class FakeProductRepository(List<Product> products) : IProductRepository
{
    public Task<List<Product>> GetAvailableAsync(CancellationToken ct) =>
        Task.FromResult(products.Where(p => p.IsAvailable && !p.IsDeleted).ToList());
    public Task<Product?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(products.FirstOrDefault(p => p.Slug == slug && !p.IsDeleted));
    public Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct) =>
        Task.FromResult(products.Where(p => slugs.Contains(p.Slug) && !p.IsDeleted).ToList());
}

file sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Added { get; } = [];
    public int SaveCount { get; private set; }
    private long _seq;

    public void Add(Order order) => Added.Add(order);
    public Task<Order?> GetByNumberAndPhoneAsync(string orderNumber, string phone, CancellationToken ct) =>
        Task.FromResult(Added.FirstOrDefault(o => o.OrderNumber == orderNumber && o.Phone == phone));
    public Task<long> GetNextOrderSequenceAsync(CancellationToken ct) => Task.FromResult(++_seq);
    public Task SaveChangesAsync(CancellationToken ct) { SaveCount++; return Task.CompletedTask; }
    public Task<List<Order>> GetFilteredAsync(OrderStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        Task.FromResult(new List<Order>());
    public Task<Order?> GetByIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(Added.FirstOrDefault(o => o.Id == id));
}

file sealed class RecordingNotificationSender : INotificationSender
{
    public List<OrderPlaced> Sent { get; } = [];
    public bool ThrowOnSend { get; set; }

    public Task SendOrderPlacedAsync(OrderPlaced notification, CancellationToken ct)
    {
        if (ThrowOnSend) throw new InvalidOperationException("smtp down");
        Sent.Add(notification);
        return Task.CompletedTask;
    }
}

public class OrderServiceTests
{
    private static readonly DateOnly Today = new(2026, 7, 4);

    // Declared by interface (not the file-scoped fake type) so these fields don't leak a
    // file-local type into this public class's member signatures (CS9051). Tests that need
    // fake-specific members cast back to the concrete type at the point of use.
    private readonly IOrderRepository _orders = new FakeOrderRepository();
    private readonly INotificationSender _notifier = new RecordingNotificationSender();
    private readonly IProductRepository _products;

    public OrderServiceTests()
    {
        _products = new FakeProductRepository([
            new Product { Id = 1, Name = "Classic Chip", Slug = "classic-chip", Price = 280m },
            new Product { Id = 2, Name = "Banana Bread", Slug = "banana-bread", Price = 350m },
            new Product { Id = 3, Name = "Sold Out", Slug = "sold-out", Price = 300m, IsAvailable = false },
        ]);
    }

    private OrderService CreateSut()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 4, 8, 0, 0, TimeSpan.FromHours(8)));
        return new OrderService(_products, _orders, [new ManualPayment()], _notifier,
            NullLogger<OrderService>.Instance, time);
    }

    private static NewOrder ValidOrder() => new(
        Items: [new NewOrderItem("classic-chip", 2), new NewOrderItem("banana-bread", 1)],
        CustomerName: "Maria", Phone: "09171234567", Email: null, MessengerHandle: null,
        PreferredDate: Today.AddDays(3), IsRush: false, Notes: null,
        FulfillmentType: FulfillmentType.Pickup, PaymentMethod: PaymentMethodType.ManualGcash);

    [Fact]
    public async Task PlaceOrder_RepricesServerSide_SnapshotsItems_AndSaves()
    {
        var order = await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);

        Assert.Equal(280m * 2 + 350m, order.Subtotal);
        Assert.Equal(2, order.Items.Count);
        var chip = order.Items.Single(i => i.ProductName == "Classic Chip");
        Assert.Equal(280m, chip.UnitPrice);
        Assert.Equal(1, chip.ProductId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(1, ((FakeOrderRepository)_orders).SaveCount);
        Assert.Equal("BM-2026-0001", order.OrderNumber);
    }

    [Fact]
    public async Task PlaceOrder_SendsNotification_AfterSave()
    {
        await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);
        var sent = Assert.Single(((RecordingNotificationSender)_notifier).Sent);
        Assert.Equal("BM-2026-0001", sent.OrderNumber);
        Assert.Equal(2, sent.Items.Count);
    }

    [Fact]
    public async Task PlaceOrder_NotificationFailure_DoesNotFailOrder()
    {
        ((RecordingNotificationSender)_notifier).ThrowOnSend = true;
        var order = await CreateSut().PlaceOrderAsync(ValidOrder(), CancellationToken.None);
        Assert.Equal(1, ((FakeOrderRepository)_orders).SaveCount);
        Assert.NotNull(order.OrderNumber);
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForUnknownProduct()
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("nope", 1)] };
        await Assert.ThrowsAsync<ProductNotFoundException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForUnavailableProduct()
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("sold-out", 1)] };
        await Assert.ThrowsAsync<ProductUnavailableException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PlaceOrder_Throws_ForInvalidQuantity(int quantity)
    {
        var request = ValidOrder() with { Items = [new NewOrderItem("classic-chip", quantity)] };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForEmptyItems()
    {
        var request = ValidOrder() with { Items = [] };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task PlaceOrder_Throws_ForPastPreferredDate()
    {
        var request = ValidOrder() with { PreferredDate = Today.AddDays(-1) };
        await Assert.ThrowsAsync<InvalidOrderException>(
            () => CreateSut().PlaceOrderAsync(request, CancellationToken.None));
    }
}
