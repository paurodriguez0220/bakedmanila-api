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
