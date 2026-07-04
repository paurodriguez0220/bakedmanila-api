using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record SaveProductRequest(
    [Required, MaxLength(100)] string? Name,
    [Required, MaxLength(120), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Slug must be lowercase letters, digits, and single hyphens.")] string? Slug,
    [MaxLength(2000)] string? Description,
    [Required, Range(1, 100_000_000)] int? PriceCentavos,
    [Required] bool? IsAvailable,
    [Required] int? SortOrder);
