using BakedManila.Core.Domain;

namespace BakedManila.Api.Dtos;

public sealed record RecipeIngredientDto(int Id, string Name, decimal Quantity, string? Unit, int SortOrder);

public sealed record AdminRecipeListItemDto(
    int Id,
    string Name,
    int YieldPerBatch,
    int IngredientCount,
    int? ProductId,
    string? ProductName)
{
    // Product link is read via the navigation property, not the FK column: the global
    // query filter nulls it for soft-deleted products, which is exactly the "unlinked"
    // behavior the spec requires.
    public static AdminRecipeListItemDto FromEntity(Recipe r) => new(
        r.Id, r.Name, r.YieldPerBatch, r.Ingredients.Count, r.Product?.Id, r.Product?.Name);
}

public sealed record AdminRecipeDto(
    int Id,
    string Name,
    int YieldPerBatch,
    string? Notes,
    int? ProductId,
    string? ProductName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<RecipeIngredientDto> Ingredients)
{
    public static AdminRecipeDto FromEntity(Recipe r) => new(
        r.Id,
        r.Name,
        r.YieldPerBatch,
        r.Notes,
        r.Product?.Id,
        r.Product?.Name,
        r.CreatedAt,
        r.UpdatedAt,
        r.Ingredients.OrderBy(i => i.SortOrder)
            .Select(i => new RecipeIngredientDto(i.Id, i.Name, i.Quantity, i.Unit, i.SortOrder))
            .ToList());
}
