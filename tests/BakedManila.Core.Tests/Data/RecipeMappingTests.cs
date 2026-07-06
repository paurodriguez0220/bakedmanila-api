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
