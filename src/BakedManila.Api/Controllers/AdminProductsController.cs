using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Domain.Exceptions;
using BakedManila.Core.Repositories;
using BakedManila.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public sealed class AdminProductsController(
    IProductRepository products,
    TimeProvider time,
    IConfiguration config,
    IImageStore images,
    ILogger<AdminProductsController> logger) : ControllerBase
{
    private const long MaxImageBytes = 5 * 1024 * 1024;

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

    [HttpPost("{id:int}/images")]
    public async Task<ActionResult<ProductImageAdminDto>> UploadImage(int id, IFormFile file, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        if (product is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found");
        }
        if (!ImageContentTypes.TryGetExtension(file.ContentType, out _))
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Unsupported image type", detail: "Use JPEG, PNG, or WebP.");
        }
        if (file.Length is 0 or > MaxImageBytes)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Invalid image size", detail: "Images must be between 1 byte and 5 MB.");
        }

        await using var stream = file.OpenReadStream();
        var blobName = await images.SaveAsync(stream, file.ContentType, id, ct); // store first — orphan blobs are harmless
        var image = new ProductImage
        {
            ProductId = id,
            BlobName = blobName,
            SortOrder = product.Images.Count == 0 ? 1 : product.Images.Max(i => i.SortOrder) + 1,
        };
        product.Images.Add(image);
        await products.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), null,
            new ProductImageAdminDto(image.Id, $"{ImageBaseUrl}/{image.BlobName}", image.SortOrder));
    }

    [HttpDelete("{id:int}/images/{imageId:int}")]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(id, ct);
        var image = product?.Images.SingleOrDefault(i => i.Id == imageId);
        if (product is null || image is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Image not found");
        }

        product.Images.Remove(image);
        await products.SaveChangesAsync(ct);
        try
        {
            await images.DeleteAsync(image.BlobName, ct);
        }
        catch (Exception ex) // deliberate broad catch: an orphaned stored file is harmless; a failed API call is not
        {
            logger.LogError(ex, "Failed to delete stored image {BlobName}", image.BlobName);
        }
        return NoContent();
    }
}
