using System.ComponentModel.DataAnnotations;

namespace BakedManila.Api.Dtos;

public sealed record SaveRecipeIngredient(
    [Required, MaxLength(100)] string? Name,
    [Required, Range(typeof(decimal), "0.01", "9999999.99")] decimal? Quantity,
    [MaxLength(20)] string? Unit);

public sealed record SaveRecipeRequest(
    [Required, MaxLength(100)] string? Name,
    [Required, Range(1, 10_000)] int? YieldPerBatch,
    [MaxLength(8000)] string? Notes,
    int? ProductId,
    [Required, MinLength(1)] List<SaveRecipeIngredient>? Ingredients);
