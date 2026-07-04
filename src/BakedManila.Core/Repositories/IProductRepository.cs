using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IProductRepository
{
    Task<List<Product>> GetAvailableAsync(CancellationToken ct);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct);
}
