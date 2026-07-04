using BakedManila.Core.Data;
using BakedManila.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace BakedManila.Core.Repositories;

public sealed class EfProductRepository(BakedManilaDbContext db) : IProductRepository
{
    public Task<List<Product>> GetAvailableAsync(CancellationToken ct) =>
        db.Products
            .Where(p => p.IsAvailable)
            .Include(p => p.Images)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken ct) =>
        db.Products
            .Include(p => p.Images)
            .SingleOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<List<Product>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken ct) =>
        db.Products
            .Where(p => slugs.Contains(p.Slug))
            .ToListAsync(ct);

    public Task<List<Product>> GetAllForAdminAsync(CancellationToken ct) =>
        db.Products.Include(p => p.Images).OrderBy(p => p.SortOrder).ToListAsync(ct);

    public Task<Product?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> SlugExistsAsync(string slug, int? exceptProductId, CancellationToken ct) =>
        db.Products.IgnoreQueryFilters()
            .AnyAsync(p => p.Slug == slug && (exceptProductId == null || p.Id != exceptProductId), ct);

    public void Add(Product product) => db.Products.Add(product);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
