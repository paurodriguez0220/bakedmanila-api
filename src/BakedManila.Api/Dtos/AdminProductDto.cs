using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record AdminProductDto(
    int Id,
    string Slug,
    string Name,
    string Description,
    int PriceCentavos,
    bool IsAvailable,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ProductImageDto> Images)
{
    public static AdminProductDto FromEntity(Product p, string imageBaseUrl) => new(
        p.Id,
        p.Slug,
        p.Name,
        p.Description,
        p.Price.ToCentavos(),
        p.IsAvailable,
        p.SortOrder,
        p.CreatedAt,
        p.UpdatedAt,
        p.Images.OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto($"{imageBaseUrl}/{i.BlobName}"))
            .ToList());
}
