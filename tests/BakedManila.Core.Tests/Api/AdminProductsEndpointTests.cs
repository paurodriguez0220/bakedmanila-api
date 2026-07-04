using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminProductsEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using (var db = await _factory.CreateDbAsync()) { } // ensure schema
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object ValidProduct(string slug = "ube-crinkles") => new
    {
        name = "Ube Crinkles",
        slug,
        description = "Chewy, deep purple.",
        priceCentavos = 30000,
        isAvailable = true,
        sortOrder = 5,
    };

    [Fact]
    public async Task Crud_Roundtrip_CreateUpdateSoftDelete()
    {
        // Create
        var created = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<AdminProductDto>();
        Assert.Equal(30000, product!.PriceCentavos);
        Assert.NotEqual(default, product.CreatedAt);

        // Visible on public storefront
        var publicList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.Contains(publicList!, p => p.Slug == "ube-crinkles");

        // Update: mark sold out, raise price
        var updated = await _client.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
        {
            name = "Ube Crinkles",
            slug = "ube-crinkles",
            description = "Chewy, deep purple.",
            priceCentavos = 32000,
            isAvailable = false,
            sortOrder = 5,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDto = await updated.Content.ReadFromJsonAsync<AdminProductDto>();
        Assert.Equal(32000, updatedDto!.PriceCentavos);
        Assert.False(updatedDto.IsAvailable);
        Assert.True(updatedDto.UpdatedAt >= updatedDto.CreatedAt);

        // Sold-out product hidden from public list, still in admin list
        publicList = await _client.GetFromJsonAsync<List<ProductDto>>("/api/products");
        Assert.DoesNotContain(publicList!, p => p.Slug == "ube-crinkles");
        var adminList = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.Contains(adminList!, p => p.Slug == "ube-crinkles");

        // Soft delete: gone from admin list too
        var deleted = await _client.DeleteAsync($"/api/admin/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        adminList = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.DoesNotContain(adminList!, p => p.Slug == "ube-crinkles");
    }

    [Fact]
    public async Task Create_Returns409_ForDuplicateSlug()
    {
        _ = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("dup-slug"));
        var second = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("dup-slug"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_Returns409_ForSlugOfSoftDeletedProduct()
    {
        var created = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("reused-slug"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<AdminProductDto>();

        var deleted = await _client.DeleteAsync($"/api/admin/products/{product!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct("reused-slug"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("Bad Slug!")]
    [InlineData("UPPER")]
    [InlineData("-leading")]
    public async Task Create_Returns400_ForInvalidSlug(string slug)
    {
        var response = await _client.PostAsJsonAsync("/api/admin/products", ValidProduct(slug));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_Return401_WithoutToken()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/products", ValidProduct())).StatusCode);
    }
}
