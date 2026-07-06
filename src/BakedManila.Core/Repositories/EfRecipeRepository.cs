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
