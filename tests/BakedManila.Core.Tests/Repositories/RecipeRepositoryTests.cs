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
