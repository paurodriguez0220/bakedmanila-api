using BakedManila.Core.Domain;

namespace BakedManila.Core.Repositories;

public interface IProductRepository
{
    Task<List<Product>> GetAvailableAsync(CancellationToken ct);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct);
    Task<List<Product>> GetAllForAdminAsync(CancellationToken ct);
    Task<Product?> GetByIdAsync(int id, CancellationToken ct);
    Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct);
    void Add(Product product);
    Task SaveChangesAsync(CancellationToken ct);
}
