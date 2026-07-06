using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminRecipesEndpointTests : IAsyncLifetime
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

    private static object ValidRecipe(string name = "Chocolate Chip", int? productId = null) => new
    {
        name,
        yieldPerBatch = 8,
        notes = "## Steps\n1. Cream **butter** and sugar\n2. Chill 30 mins",
        productId,
        ingredients = new object[]
        {
            new { name = "Flour", quantity = 250m, unit = "g" },
            new { name = "Eggs", quantity = 1m },
        },
    };

    private async Task<int> CreateProductAsync(string slug)
    {
        var response = await _client.PostAsJsonAsync("/api/admin/products", new
        {
            name = "Choc Chip Cookies",
            slug,
            description = "",
            priceCentavos = 30000,
            isAvailable = true,
            sortOrder = 1,
        });
        var product = await response.Content.ReadFromJsonAsync<AdminProductDto>();
        return product!.Id;
    }

    [Fact]
    public async Task Crud_Roundtrip_CreateGetUpdateDelete()
    {
        // Create
        var created = await _client.PostAsJsonAsync("/api/admin/recipes", ValidRecipe());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = await created.Content.ReadFromJsonAsync<AdminRecipeDto>();
        Assert.Equal(8, recipe!.YieldPerBatch);
        Assert.Equal(2, recipe.Ingredients.Count);
        Assert.Equal(["Flour", "Eggs"], recipe.Ingredients.Select(i => i.Name).ToArray());
        Assert.Equal([0, 1], recipe.Ingredients.Select(i => i.SortOrder).ToArray());
        Assert.Null(recipe.ProductId);

        // List includes it with the ingredient count
        var list = await _client.GetFromJsonAsync<List<AdminRecipeListItemDto>>("/api/admin/recipes");
        var item = Assert.Single(list!, r => r.Id == recipe.Id);
        Assert.Equal(2, item.IngredientCount);

        // Get by id
        var fetched = await _client.GetFromJsonAsync<AdminRecipeDto>($"/api/admin/recipes/{recipe.Id}");
        Assert.Equal("Chocolate Chip", fetched!.Name);
        Assert.Contains("**butter**", fetched.Notes);

        // Full-replace update: rename, change yield, replace the ingredient list entirely
        var updated = await _client.PutAsJsonAsync($"/api/admin/recipes/{recipe.Id}", new
        {
            name = "Chocolate Chip v2",
            yieldPerBatch = 12,
            notes = (string?)null,
            productId = (int?)null,
            ingredients = new object[]
            {
                new { name = "Bread Flour", quantity = 300m, unit = "g" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDto = await updated.Content.ReadFromJsonAsync<AdminRecipeDto>();
        Assert.Equal(12, updatedDto!.YieldPerBatch);
        var ingredient = Assert.Single(updatedDto.Ingredients);
        Assert.Equal("Bread Flour", ingredient.Name);
        Assert.Equal(0, ingredient.SortOrder);
        Assert.True(updatedDto.UpdatedAt >= updatedDto.CreatedAt);

        // Delete
        var deleted = await _client.DeleteAsync($"/api/admin/recipes/{recipe.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var gone = await _client.GetAsync($"/api/admin/recipes/{recipe.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Create_LinksProduct_AndSoftDeletingProductUnlinksIt()
    {
        var productId = await CreateProductAsync("linked-cookies");

        var created = await _client.PostAsJsonAsync("/api/admin/recipes", ValidRecipe(productId: productId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipe = await created.Content.ReadFromJsonAsync<AdminRecipeDto>();
        Assert.Equal(productId, recipe!.ProductId);
        Assert.Equal("Choc Chip Cookies", recipe.ProductName);

        // Soft-delete the product — the recipe must read as unlinked afterwards
        var deleted = await _client.DeleteAsync($"/api/admin/products/{productId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var fetched = await _client.GetFromJsonAsync<AdminRecipeDto>($"/api/admin/recipes/{recipe.Id}");
        Assert.Null(fetched!.ProductId);
        Assert.Null(fetched.ProductName);
    }

    [Fact]
    public async Task Create_Returns400_ForUnknownProductId()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/recipes", ValidRecipe(productId: 999_999));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns404_ForUnknownRecipe()
    {
        var response = await _client.PutAsJsonAsync("/api/admin/recipes/999999", ValidRecipe());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"","yieldPerBatch":8,"ingredients":[{"name":"Flour","quantity":1}]}""")]
    [InlineData("""{"name":"No Yield","yieldPerBatch":0,"ingredients":[{"name":"Flour","quantity":1}]}""")]
    [InlineData("""{"name":"No Ingredients","yieldPerBatch":8,"ingredients":[]}""")]
    [InlineData("""{"name":"Bad Quantity","yieldPerBatch":8,"ingredients":[{"name":"Flour","quantity":0}]}""")]
    public async Task Create_Returns400_ForInvalidBody(string json)
    {
        var response = await _client.PostAsync("/api/admin/recipes",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_Return401_WithoutToken()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/recipes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/recipes", ValidRecipe())).StatusCode);
    }
}
