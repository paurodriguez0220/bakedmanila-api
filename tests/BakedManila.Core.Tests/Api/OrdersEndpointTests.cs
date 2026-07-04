using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;

namespace BakedManila.Core.Tests.Api;

public sealed class OrdersEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync();
        db.Products.Add(new Product { Name = "Classic Chip", Slug = "classic-chip", Price = 280m });
        await db.SaveChangesAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object ValidBody(string date) => new
    {
        items = new[] { new { productSlug = "classic-chip", quantity = 2 } },
        customerName = "Maria",
        phone = "09171234567",
        preferredDate = date,
        isRush = false,
        fulfillmentType = "Pickup",
        paymentMethod = "ManualGcash",
    };

    private static string FutureDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");

    [Fact]
    public async Task PlaceOrder_Returns201_WithServerPricedOrder()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.StartsWith("BM-", order!.OrderNumber);
        Assert.Equal(56000, order.SubtotalCentavos);
        Assert.Equal("Pending", order.Status);
        Assert.Contains($"/api/orders/{order.OrderNumber}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PlaceOrder_Returns422_ForPastDate()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidBody("2020-01-01"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_Returns400_ForMissingPhone()
    {
        var body = new
        {
            items = new[] { new { productSlug = "classic-chip", quantity = 1 } },
            customerName = "Maria",
            preferredDate = FutureDate,
            fulfillmentType = "Pickup",
            paymentMethod = "ManualGcash",
        };
        var response = await _client.PostAsJsonAsync("/api/orders", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_RequiresMatchingPhone()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate)))
            .Content.ReadFromJsonAsync<OrderDto>();

        var ok = await _client.GetAsync($"/api/orders/{created!.OrderNumber}?phone=09171234567");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var wrongPhone = await _client.GetAsync($"/api/orders/{created.OrderNumber}?phone=09990000000");
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_RateLimits_After5PerWindow()
    {
        for (var i = 0; i < 5; i++)
        {
            var ok = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        var limited = await _client.PostAsJsonAsync("/api/orders", ValidBody(FutureDate));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }
}
