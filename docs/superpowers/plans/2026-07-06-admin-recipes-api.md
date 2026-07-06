# Admin Recipes API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** JWT-protected admin CRUD endpoints for recipes (name, yield per batch, markdown notes, ordered ingredients, optional product link) per the spec at `docs/superpowers/specs/2026-07-06-admin-recipes-design.md`.

**Architecture:** Follows the repo's pragmatic layered monolith exactly: domain entities in `BakedManila.Core/Domain`, EF config in `BakedManilaDbContext`, repository pair (`IRecipeRepository`/`EfRecipeRepository`), controller in `BakedManila.Api/Controllers`, record DTOs in `BakedManila.Api/Dtos`. No scaling endpoint — the calculator is client-side.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (SQL Server), xUnit v2, WebApplicationFactory integration tests against LocalDB/SQL container.

## Global Constraints

- Work on branch `feat/admin-recipes` (already exists with the spec committed).
- Validation attributes on record DTOs target **constructor parameters** — never `[property:]` (net10 MVC throws).
- `[Required]` on value types needs nullable parameters (`int?`, `decimal?`).
- Money/measurement quantities are `decimal` (`decimal(9,2)` for ingredient quantity).
- xUnit v2 — no `TestContext.Current`.
- Commit messages via multiple `-m` flags (here-strings corrupt messages).
- Run tests with: `dotnet test` from repo root (`C:\Users\paulo.rodriguez\Paulo\bakedmanila-api`). Full suite must stay green.
- **Soft-delete nuance (spec §2/§5):** products are soft-deleted (`IsDeleted` + global query filter), so `ON DELETE SET NULL` only covers hard deletes. The observable "unlink" behavior comes from the DTO reading the *navigation property* (`r.Product?.Id`, `r.Product?.Name`), which the query filter nulls out for soft-deleted products. Both `productId` and `productName` read as null once the linked product is deleted.

---

### Task 1: Recipe domain entities, DbContext mapping, migration

**Files:**
- Create: `src/BakedManila.Core/Domain/Recipe.cs`
- Create: `src/BakedManila.Core/Domain/RecipeIngredient.cs`
- Modify: `src/BakedManila.Core/Data/BakedManilaDbContext.cs` (add `DbSet` + `OnModelCreating` config)
- Create: `tests/BakedManila.Core.Tests/Data/RecipeMappingTests.cs`
- Generated: `src/BakedManila.Core/Data/Migrations/*_AddRecipes.cs` (via `dotnet ef`)

**Interfaces:**
- Consumes: existing `Product` entity, `BakedManilaDbContext`, `TestDb.NewConnectionString()`.
- Produces: `Recipe` (`Id int`, `Name string`, `YieldPerBatch int`, `Notes string?`, `ProductId int?`, `Product Product?`, `CreatedAt/UpdatedAt DateTime`, `Ingredients List<RecipeIngredient>`) and `RecipeIngredient` (`Id int`, `RecipeId int`, `Name string`, `Quantity decimal`, `Unit string?`, `SortOrder int`). Tables `Recipes` / `RecipeIngredients`.

- [ ] **Step 1: Write the failing mapping test**

Create `tests/BakedManila.Core.Tests/Data/RecipeMappingTests.cs`:

```csharp
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Data;

public sealed class RecipeMappingTests : IAsyncLifetime
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
    public async Task Recipe_RoundTrips_WithIngredientsAndProductLink()
    {
        var product = new Product { Name = "Choc Chip Cookies", Slug = "choc-chip", Price = 300m };
        _db.Products.Add(product);
        var recipe = new Recipe
        {
            Name = "Chocolate Chip",
            YieldPerBatch = 8,
            Notes = "## Steps\n1. Cream butter",
            Product = product,
            Ingredients =
            [
                new RecipeIngredient { Name = "Flour", Quantity = 250m, Unit = "g", SortOrder = 0 },
                new RecipeIngredient { Name = "Eggs", Quantity = 1m, SortOrder = 1 },
            ],
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Product)
            .SingleAsync(r => r.Id == recipe.Id);

        Assert.Equal(8, loaded.YieldPerBatch);
        Assert.Equal("Choc Chip Cookies", loaded.Product!.Name);
        Assert.Equal(2, loaded.Ingredients.Count);
        Assert.Equal(250m, loaded.Ingredients.Single(i => i.Name == "Flour").Quantity);
        Assert.Null(loaded.Ingredients.Single(i => i.Name == "Eggs").Unit);
    }

    [Fact]
    public async Task DeletingRecipe_CascadesToIngredients()
    {
        var recipe = new Recipe
        {
            Name = "Banana Bread",
            YieldPerBatch = 2,
            Ingredients = [new RecipeIngredient { Name = "Bananas", Quantity = 3m, SortOrder = 0 }],
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        _db.Recipes.Remove(recipe);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _db.Set<RecipeIngredient>().CountAsync());
    }

    [Fact]
    public async Task SoftDeletedLinkedProduct_ReadsAsNullNavigation()
    {
        var product = new Product { Name = "Gone", Slug = "gone-product", Price = 100m };
        _db.Products.Add(product);
        var recipe = new Recipe { Name = "Orphaned", YieldPerBatch = 6, Product = product };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        product.IsDeleted = true;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _db.Recipes.Include(r => r.Product).SingleAsync(r => r.Id == recipe.Id);
        Assert.Null(loaded.Product);            // query filter hides the soft-deleted product
        Assert.NotNull(loaded.ProductId);       // FK column itself is untouched
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RecipeMappingTests"` from the repo root.
Expected: compile FAILURE — `Recipe` / `Recipes` do not exist.

- [ ] **Step 3: Create the entities**

Create `src/BakedManila.Core/Domain/Recipe.cs`:

```csharp
namespace BakedManila.Core.Domain;

public class Recipe
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int YieldPerBatch { get; set; }
    public string? Notes { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = [];
}
```

Create `src/BakedManila.Core/Domain/RecipeIngredient.cs`:

```csharp
namespace BakedManila.Core.Domain;

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public required string Name { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
}
```

- [ ] **Step 4: Map them in the DbContext**

In `src/BakedManila.Core/Data/BakedManilaDbContext.cs`, add below the `Orders` DbSet:

```csharp
public DbSet<Recipe> Recipes => Set<Recipe>();
```

And at the end of `OnModelCreating` (after the `OrderItem` block):

```csharp
builder.Entity<Recipe>(e =>
{
    e.Property(r => r.Name).HasMaxLength(100).IsRequired();
    e.Property(r => r.Notes).HasMaxLength(8000);
    e.HasOne(r => r.Product)
        .WithMany()
        .HasForeignKey(r => r.ProductId)
        .OnDelete(DeleteBehavior.SetNull);
    e.HasMany(r => r.Ingredients)
        .WithOne()
        .HasForeignKey(i => i.RecipeId)
        .OnDelete(DeleteBehavior.Cascade);
});

builder.Entity<RecipeIngredient>(e =>
{
    e.ToTable("RecipeIngredients");
    e.Property(i => i.Name).HasMaxLength(100).IsRequired();
    e.Property(i => i.Quantity).HasColumnType("decimal(9,2)").IsRequired();
    e.Property(i => i.Unit).HasMaxLength(20);
});
```

Note: `Recipe.Product` is an *optional* navigation to an entity with a global query filter — that combination is fine (no EF warning); a filtered-out product simply loads as `null`.

- [ ] **Step 5: Generate the migration**

Run from the repo root:

```bash
dotnet ef migrations add AddRecipes --project src/BakedManila.Core --startup-project src/BakedManila.Api --output-dir Data/Migrations
```

Expected: `*_AddRecipes.cs` created under `src/BakedManila.Core/Data/Migrations/` containing `CreateTable` for `Recipes` and `RecipeIngredients` with `onDelete: ReferentialAction.SetNull` on the Product FK and `Cascade` on the recipe FK. Inspect the file to confirm both.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RecipeMappingTests"`
Expected: 3 PASS.

- [ ] **Step 7: Verify clean build and commit**

```bash
dotnet build
git add src/BakedManila.Core/Domain/Recipe.cs src/BakedManila.Core/Domain/RecipeIngredient.cs src/BakedManila.Core/Data/BakedManilaDbContext.cs src/BakedManila.Core/Data/Migrations tests/BakedManila.Core.Tests/Data/RecipeMappingTests.cs
git commit -m "feat(api): add Recipe and RecipeIngredient entities with migration"
```

---

### Task 2: Recipe repository

**Files:**
- Create: `src/BakedManila.Core/Repositories/IRecipeRepository.cs`
- Create: `src/BakedManila.Core/Repositories/EfRecipeRepository.cs`
- Modify: `src/BakedManila.Api/Program.cs` (register after line 136, `IOrderRepository` registration)
- Test: `tests/BakedManila.Core.Tests/Repositories/RecipeRepositoryTests.cs`

**Interfaces:**
- Consumes: `Recipe`, `BakedManilaDbContext` from Task 1.
- Produces:
  - `IRecipeRepository`: `Task<List<Recipe>> GetAllAsync(CancellationToken ct)` (ingredients + product included, ordered by `Name`), `Task<Recipe?> GetByIdAsync(int id, CancellationToken ct)` (ingredients + product included), `void Add(Recipe recipe)`, `void Remove(Recipe recipe)`, `Task SaveChangesAsync(CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/BakedManila.Core.Tests/Repositories/RecipeRepositoryTests.cs`:

```csharp
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Tests.Repositories;

public sealed class RecipeRepositoryTests : IAsyncLifetime
{
    private BakedManilaDbContext _db = null!;
    private EfRecipeRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(TestDb.NewConnectionString())
            .Options;
        _db = new BakedManilaDbContext(options);
        await _db.Database.MigrateAsync();
        _repo = new EfRecipeRepository(_db);

        var product = new Product { Name = "Choc Chip Cookies", Slug = "choc-chip", Price = 300m };
        _db.Products.Add(product);
        _db.Recipes.AddRange(
            new Recipe
            {
                Name = "Zebra Cake",
                YieldPerBatch = 2,
                Ingredients = [new RecipeIngredient { Name = "Cocoa", Quantity = 30m, Unit = "g", SortOrder = 0 }],
            },
            new Recipe
            {
                Name = "Chocolate Chip",
                YieldPerBatch = 8,
                Product = product,
                Ingredients =
                [
                    new RecipeIngredient { Name = "Flour", Quantity = 250m, Unit = "g", SortOrder = 0 },
                    new RecipeIngredient { Name = "Butter", Quantity = 115m, Unit = "g", SortOrder = 1 },
                ],
            });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsRecipesOrderedByName_WithIngredientsAndProduct()
    {
        var recipes = await _repo.GetAllAsync(CancellationToken.None);

        Assert.Equal(["Chocolate Chip", "Zebra Cake"], recipes.Select(r => r.Name).ToArray());
        Assert.Equal(2, recipes[0].Ingredients.Count);
        Assert.Equal("Choc Chip Cookies", recipes[0].Product!.Name);
        Assert.Null(recipes[1].Product);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsRecipeWithIngredients()
    {
        var all = await _repo.GetAllAsync(CancellationToken.None);
        var recipe = await _repo.GetByIdAsync(all[0].Id, CancellationToken.None);

        Assert.NotNull(recipe);
        Assert.Equal("Chocolate Chip", recipe!.Name);
        Assert.Equal(2, recipe.Ingredients.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_ForUnknownId()
    {
        Assert.Null(await _repo.GetByIdAsync(999_999, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RecipeRepositoryTests"`
Expected: compile FAILURE — `EfRecipeRepository` does not exist.

- [ ] **Step 3: Implement the repository**

Create `src/BakedManila.Core/Repositories/IRecipeRepository.cs`:

```csharp
using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IRecipeRepository
{
    Task<List<Recipe>> GetAllAsync(CancellationToken ct);
    Task<Recipe?> GetByIdAsync(int id, CancellationToken ct);
    void Add(Recipe recipe);
    void Remove(Recipe recipe);
    Task SaveChangesAsync(CancellationToken ct);
}
```

Create `src/BakedManila.Core/Repositories/EfRecipeRepository.cs`:

```csharp
using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Repositories;

public sealed class EfRecipeRepository(BakedManilaDbContext db) : IRecipeRepository
{
    public Task<List<Recipe>> GetAllAsync(CancellationToken ct) =>
        db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Product)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    public Task<Recipe?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Product)
            .SingleOrDefaultAsync(r => r.Id == id, ct);

    public void Add(Recipe recipe) => db.Recipes.Add(recipe);

    public void Remove(Recipe recipe) => db.Recipes.Remove(recipe);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Register in DI**

In `src/BakedManila.Api/Program.cs`, directly after `builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();` add:

```csharp
builder.Services.AddScoped<IRecipeRepository, EfRecipeRepository>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RecipeRepositoryTests"`
Expected: 3 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BakedManila.Core/Repositories/IRecipeRepository.cs src/BakedManila.Core/Repositories/EfRecipeRepository.cs src/BakedManila.Api/Program.cs tests/BakedManila.Core.Tests/Repositories/RecipeRepositoryTests.cs
git commit -m "feat(api): add recipe repository"
```

---

### Task 3: DTOs and AdminRecipesController

**Files:**
- Create: `src/BakedManila.Api/Dtos/AdminRecipeDto.cs`
- Create: `src/BakedManila.Api/Dtos/SaveRecipeRequest.cs`
- Create: `src/BakedManila.Api/Controllers/AdminRecipesController.cs`
- Test: `tests/BakedManila.Core.Tests/Api/AdminRecipesEndpointTests.cs`

**Interfaces:**
- Consumes: `IRecipeRepository` (Task 2), `IProductRepository.GetByIdAsync` (existing), `TimeProvider`, `ApiFactory`/`AdminAuth` test helpers.
- Produces (wire contract — the web plan depends on these exact shapes):
  - `GET  /api/admin/recipes` → `200` `AdminRecipeListItemDto[]`: `{ id, name, yieldPerBatch, ingredientCount, productId?, productName? }`
  - `GET  /api/admin/recipes/{id}` → `200` `AdminRecipeDto`: `{ id, name, yieldPerBatch, notes?, productId?, productName?, createdAt, updatedAt, ingredients: [{ id, name, quantity, unit?, sortOrder }] }`; `404` problem-details when missing
  - `POST /api/admin/recipes` body `SaveRecipeRequest`: `{ name, yieldPerBatch, notes?, productId?, ingredients: [{ name, quantity, unit? }] }` → `201` `AdminRecipeDto`; `400` when `productId` unknown or validation fails
  - `PUT  /api/admin/recipes/{id}` body `SaveRecipeRequest` → `200` `AdminRecipeDto` (full replace, ingredients delete-and-recreate, `SortOrder` = array index); `404`/`400`
  - `DELETE /api/admin/recipes/{id}` → `204`; `404`
  - All require `Authorization: Bearer` with the Admin role → `401` otherwise.

- [ ] **Step 1: Write the failing endpoint tests**

Create `tests/BakedManila.Core.Tests/Api/AdminRecipesEndpointTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AdminRecipesEndpointTests"`
Expected: compile FAILURE — `AdminRecipeDto` does not exist.

- [ ] **Step 3: Create the DTOs**

Create `src/BakedManila.Api/Dtos/AdminRecipeDto.cs`:

```csharp
using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record RecipeIngredientDto(int Id, string Name, decimal Quantity, string? Unit, int SortOrder);

public sealed record AdminRecipeListItemDto(
    int Id,
    string Name,
    int YieldPerBatch,
    int IngredientCount,
    int? ProductId,
    string? ProductName)
{
    // Product link is read via the navigation property, not the FK column: the global
    // query filter nulls it for soft-deleted products, which is exactly the "unlinked"
    // behavior the spec requires.
    public static AdminRecipeListItemDto FromEntity(Recipe r) => new(
        r.Id, r.Name, r.YieldPerBatch, r.Ingredients.Count, r.Product?.Id, r.Product?.Name);
}

public sealed record AdminRecipeDto(
    int Id,
    string Name,
    int YieldPerBatch,
    string? Notes,
    int? ProductId,
    string? ProductName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RecipeIngredientDto> Ingredients)
{
    public static AdminRecipeDto FromEntity(Recipe r) => new(
        r.Id,
        r.Name,
        r.YieldPerBatch,
        r.Notes,
        r.Product?.Id,
        r.Product?.Name,
        r.CreatedAt,
        r.UpdatedAt,
        r.Ingredients.OrderBy(i => i.SortOrder)
            .Select(i => new RecipeIngredientDto(i.Id, i.Name, i.Quantity, i.Unit, i.SortOrder))
            .ToList());
}
```

Create `src/BakedManila.Api/Dtos/SaveRecipeRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record SaveRecipeIngredient(
    [Required, MaxLength(100)] string? Name,
    [Required, Range(typeof(decimal), "0.01", "9999999.99")] decimal? Quantity,
    [MaxLength(20)] string? Unit);

public sealed record SaveRecipeRequest(
    [Required, MaxLength(100)] string? Name,
    [Required, Range(1, 10_000)] int? YieldPerBatch,
    [MaxLength(8000)] string? Notes,
    int? ProductId,
    [Required, MinLength(1)] List<SaveRecipeIngredient>? Ingredients);
```

- [ ] **Step 4: Create the controller**

Create `src/BakedManila.Api/Controllers/AdminRecipesController.cs`:

```csharp
using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/recipes")]
[Authorize(Roles = "Admin")]
public sealed class AdminRecipesController(
    IRecipeRepository recipes,
    IProductRepository products,
    TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminRecipeListItemDto>>> List(CancellationToken ct)
    {
        var list = await recipes.GetAllAsync(ct);
        return Ok(list.Select(AdminRecipeListItemDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminRecipeDto>> Get(int id, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }
        return Ok(AdminRecipeDto.FromEntity(recipe));
    }

    [HttpPost]
    public async Task<ActionResult<AdminRecipeDto>> Create(SaveRecipeRequest request, CancellationToken ct)
    {
        var product = await ResolveProductAsync(request.ProductId, ct);
        if (request.ProductId is not null && product is null)
        {
            return UnknownProductProblem(request.ProductId.Value);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var recipe = new Recipe
        {
            Name = request.Name!,
            YieldPerBatch = request.YieldPerBatch!.Value,
            Notes = request.Notes,
            Product = product,
            CreatedAt = now,
            UpdatedAt = now,
            Ingredients = ToIngredients(request.Ingredients!),
        };
        recipes.Add(recipe);
        await recipes.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = recipe.Id }, AdminRecipeDto.FromEntity(recipe));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminRecipeDto>> Update(int id, SaveRecipeRequest request, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }

        var product = await ResolveProductAsync(request.ProductId, ct);
        if (request.ProductId is not null && product is null)
        {
            return UnknownProductProblem(request.ProductId.Value);
        }

        recipe.Name = request.Name!;
        recipe.YieldPerBatch = request.YieldPerBatch!.Value;
        recipe.Notes = request.Notes;
        recipe.ProductId = product?.Id;
        recipe.Product = product;
        recipe.UpdatedAt = time.GetUtcNow().UtcDateTime;

        // Full replace per spec: delete-and-recreate the ingredient rows.
        recipe.Ingredients.Clear();
        foreach (var ingredient in ToIngredients(request.Ingredients!))
        {
            recipe.Ingredients.Add(ingredient);
        }
        await recipes.SaveChangesAsync(ct);

        return Ok(AdminRecipeDto.FromEntity(recipe));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }
        recipes.Remove(recipe);
        await recipes.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Product?> ResolveProductAsync(int? productId, CancellationToken ct) =>
        productId is null ? null : await products.GetByIdAsync(productId.Value, ct);

    private ObjectResult UnknownProductProblem(int productId) =>
        Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown product",
            detail: $"No product with id {productId} exists.");

    private static List<RecipeIngredient> ToIngredients(List<SaveRecipeIngredient> items) =>
        items.Select((item, index) => new RecipeIngredient
        {
            Name = item.Name!,
            Quantity = item.Quantity!.Value,
            Unit = string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit,
            SortOrder = index,
        }).ToList();
}
```

- [ ] **Step 5: Run the endpoint tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AdminRecipesEndpointTests"`
Expected: 7 PASS (2 facts + 4 theory cases + 1 fact may total differently — all green is the bar).

- [ ] **Step 6: Run the FULL suite**

Run: `dotnet test`
Expected: all green. If `OpenApiDocumentTests` asserts on the endpoint inventory, extend its expectations with the five new `/api/admin/recipes` operations — read that test file before editing and follow its existing structure.

- [ ] **Step 7: Commit**

```bash
git add src/BakedManila.Api/Dtos/AdminRecipeDto.cs src/BakedManila.Api/Dtos/SaveRecipeRequest.cs src/BakedManila.Api/Controllers/AdminRecipesController.cs tests/BakedManila.Core.Tests/Api/AdminRecipesEndpointTests.cs
git commit -m "feat(api): admin recipe CRUD endpoints with optional product link"
```

---

### Task 4: Pre-PR verification and PR

**Files:**
- No new files. Verification + PR only.

**Interfaces:**
- Consumes: everything above.
- Produces: an open PR `feat/admin-recipes → main` on `paurodriguez0220/bakedmanila-api`.

- [ ] **Step 1: Build clean, full suite green**

```bash
dotnet build
dotnet test
```
Expected: 0 warnings introduced, all tests pass (existing 71 + new).

- [ ] **Step 2: Verify remote and account per push policy**

```bash
git remote get-url origin
gh auth status
```
Expected: origin URL contains `paurodriguez0220/bakedmanila-api`; active gh account is **paurodriguez0220**. If the work account (`paulo-rodriguez_fefi`) is active, run `gh auth switch --user paurodriguez0220` first. NEVER push with the work account.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin feat/admin-recipes
gh pr create --title "feat: admin recipes with optional product link" --body "## Summary
- Recipe + RecipeIngredient entities, migration AddRecipes
- JWT-protected admin CRUD at /api/admin/recipes (PUT = full replace, ingredients delete-and-recreate)
- Optional product link via nullable FK (ON DELETE SET NULL); soft-deleted products read as unlinked via the filtered navigation
- Spec: docs/superpowers/specs/2026-07-06-admin-recipes-design.md

## Test plan
- [x] Mapping tests (round-trip, cascade, soft-delete unlink)
- [x] Repository tests
- [x] Endpoint tests (auth, CRUD, full replace, validation, unknown productId)
- [x] Full suite green

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
Expected: PR URL printed. CI (`cicd-bkdmnl`) must go green — the deploy job runs on merge to main only.
