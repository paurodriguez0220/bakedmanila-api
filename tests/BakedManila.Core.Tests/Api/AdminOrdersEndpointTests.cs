using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminOrdersEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using (var db = await _factory.CreateDbAsync())
        {
            var product = new Product { Name = "Snap", Slug = "snap", Price = 280m };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            db.Orders.AddRange(
                MakeOrder("BM-2026-0101", OrderStatus.Pending, new DateOnly(2026, 7, 10), new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), product.Id),
                MakeOrder("BM-2026-0102", OrderStatus.Confirmed, new DateOnly(2026, 7, 12), new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc), product.Id),
                MakeOrder("BM-2026-0103", OrderStatus.Pending, new DateOnly(2026, 7, 20), new DateTime(2026, 7, 3, 8, 0, 0, DateTimeKind.Utc), product.Id));
            await db.SaveChangesAsync();
        }
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Order MakeOrder(string number, OrderStatus status, DateOnly preferred, DateTime created, int productId) => new()
    {
        OrderNumber = number,
        Status = status,
        CustomerName = "Maria",
        Phone = "09171234567",
        PreferredDate = preferred,
        FulfillmentType = FulfillmentType.Pickup,
        PaymentMethod = PaymentMethodType.ManualGcash,
        Subtotal = 280m,
        CreatedAt = created,
        Items = [new OrderItem { ProductName = "Snap", UnitPrice = 280m, Quantity = 1, ProductId = productId }],
    };

    [Fact]
    public async Task List_Returns401_WithoutToken()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/api/admin/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsAllOrders_NewestFirst()
    {
        var orders = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders");
        Assert.Equal(["BM-2026-0103", "BM-2026-0102", "BM-2026-0101"],
            orders!.Select(o => o.OrderNumber).ToArray());
        Assert.All(orders!, o => Assert.NotEmpty(o.Items));
    }

    [Fact]
    public async Task List_FiltersByStatusAndDateRange()
    {
        var pending = await _client.GetFromJsonAsync<List<AdminOrderDto>>("/api/admin/orders?status=Pending");
        Assert.Equal(2, pending!.Count);

        var inWindow = await _client.GetFromJsonAsync<List<AdminOrderDto>>(
            "/api/admin/orders?from=2026-07-11&to=2026-07-15");
        var only = Assert.Single(inWindow!);
        Assert.Equal("BM-2026-0102", only.OrderNumber);
    }
}
