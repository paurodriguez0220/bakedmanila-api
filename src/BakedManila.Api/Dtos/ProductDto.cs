using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record ProductImageDto(string Url);

public sealed record ProductDto(
    string Slug,
    string Name,
    string Description,
    int PriceCentavos,
    bool IsAvailable,
    IReadOnlyList<ProductImageDto> Images)
{
    public static ProductDto FromEntity(Product p, string imageBaseUrl) => new(
        p.Slug,
        p.Name,
        p.Description,
        p.Price.ToCentavos(),
        p.IsAvailable,
        p.Images.OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto($"{imageBaseUrl}/{i.BlobName}"))
            .ToList());
}
