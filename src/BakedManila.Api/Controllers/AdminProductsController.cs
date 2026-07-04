using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public sealed class AdminProductsController(
    IProductRepository products,
    TimeProvider time,
    IConfiguration config) : ControllerBase
{
    private string ImageBaseUrl => config["Storage:PublicBaseUrl"] ?? string.Empty;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminProductDto>>> List(CancellationToken ct)
    {
        var list = await products.GetAllForAdminAsync(ct);
        return Ok(list.Select(p => AdminProductDto.FromEntity(p, ImageBaseUrl)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AdminProductDto>> Create(SaveProductRequest request, CancellationToken ct)
    {
        if (await products.SlugExistsAsync(request.Slug!, exceptProductId: null, ct))
        {
            throw new DuplicateSlugException(request.Slug!);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var product = new Product
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description ?? string.Empty,
            Price = request.PriceCentavos!.Value / 100m,
            IsAvailable = request.IsAvailable!.Value,
            SortOrder = request.SortOrder!.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };
        products.Add(product);
        await products.SaveChangesAsync(ct);

        var dto = AdminProductDto.FromEntity(product, ImageBaseUrl);
        return CreatedAtAction(nameof(List), null, dto);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminProductDto>> Update(int id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        }
        if (await products.SlugExistsAsync(request.Slug!, exceptProductId: id, ct))
        {
            throw new DuplicateSlugException(request.Slug!);
        }

        product.Name = request.Name!;
        product.Slug = request.Slug!;
        product.Description = request.Description ?? string.Empty;
        product.Price = request.PriceCentavos!.Value / 100m;
        product.IsAvailable = request.IsAvailable!.Value;
        product.SortOrder = request.SortOrder!.Value;
        product.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await products.SaveChangesAsync(ct);

        return Ok(AdminProductDto.FromEntity(product, ImageBaseUrl));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        }
        product.IsDeleted = true;
        product.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await products.SaveChangesAsync(ct);
        return NoContent();
    }
}
