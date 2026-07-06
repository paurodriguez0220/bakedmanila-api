namespace BakedManila.Core.Domain;

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public required string Name { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
}
