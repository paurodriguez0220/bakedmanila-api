namespace BakedManila.Core.Domain;

public class Recipe
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int YieldPerBatch { get; set; }
    public string? Notes { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = [];
}
