using System.Net;
using System.Net.Http.Json;
using BakedManila.Core.Domain;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class ProductsEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync();
        db.Products.AddRange(
            new Product
            {
                Name = "Classic Chip", Slug = "classic-chip", Price = 280m, SortOrder = 1,
                Description = "Crisp edges", Images = [new ProductImage { BlobName = "products/1/a.jpg" }],
            },
            new Product { Name = "Hidden", Slug = "hidden", Price = 300m, IsAvailable = false });
        await db.SaveChangesAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetProducts_ReturnsAvailableOnly_WithCentavosAndImageUrls()
    {
        var products = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        var p = Assert.Single(products!);
        Assert.Equal("classic-chip", p.Slug);
        Assert.Equal(28000, p.PriceCentavos);
        Assert.Equal("https://img.test/products/1/a.jpg", Assert.Single(p.Images).Url);
    }

    [Fact]
    public async Task GetProductBySlug_Returns404ProblemDetails_ForUnknown()
    {
        var response = await _client.GetAsync("/api/products/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task GetProductBySlug_ReturnsUnavailableProductToo()
    {
        // detail page still viewable when sold out; storefront shows "sold out"
        var p = await _client.GetFromJsonAsync<ProductDto>("/api/products/hidden");
        Assert.False(p!.IsAvailable);
    }
}
