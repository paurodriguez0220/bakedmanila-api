using BakedManila.Api.Dtos;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductRepository products, IConfiguration config)
    : ControllerBase
{
    private string ImageBaseUrl => config["Storage:PublicBaseUrl"] ?? string.Empty;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> GetAvailable(CancellationToken ct)
    {
        var list = await products.GetAvailableAsync(ct);
        return Ok(list.Select(p => ProductDto.FromEntity(p, ImageBaseUrl)).ToList());
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ProductDto>> GetBySlug(string slug, CancellationToken ct)
    {
        var product = await products.GetBySlugAsync(slug, ct);
        return product is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found")
            : Ok(ProductDto.FromEntity(product, ImageBaseUrl));
    }
}
