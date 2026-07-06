using BakedManila.Api.Dtos;
using BakedManila.Core.Domain;
using BakedManila.Core.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/admin/recipes")]
[Authorize(Roles = "Admin")]
public sealed class AdminRecipesController(
    IRecipeRepository recipes,
    IProductRepository products,
    TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminRecipeListItemDto>>> List(CancellationToken ct)
    {
        var list = await recipes.GetAllAsync(ct);
        return Ok(list.Select(AdminRecipeListItemDto.FromEntity).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminRecipeDto>> Get(int id, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }
        return Ok(AdminRecipeDto.FromEntity(recipe));
    }

    [HttpPost]
    public async Task<ActionResult<AdminRecipeDto>> Create(SaveRecipeRequest request, CancellationToken ct)
    {
        var product = await ResolveProductAsync(request.ProductId, ct);
        if (request.ProductId is not null && product is null)
        {
            return UnknownProductProblem(request.ProductId.Value);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var recipe = new Recipe
        {
            Name = request.Name!,
            YieldPerBatch = request.YieldPerBatch!.Value,
            Notes = request.Notes,
            Product = product,
            CreatedAt = now,
            UpdatedAt = now,
            Ingredients = ToIngredients(request.Ingredients!),
        };
        recipes.Add(recipe);
        await recipes.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = recipe.Id }, AdminRecipeDto.FromEntity(recipe));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminRecipeDto>> Update(int id, SaveRecipeRequest request, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }

        var product = await ResolveProductAsync(request.ProductId, ct);
        if (request.ProductId is not null && product is null)
        {
            return UnknownProductProblem(request.ProductId.Value);
        }

        recipe.Name = request.Name!;
        recipe.YieldPerBatch = request.YieldPerBatch!.Value;
        recipe.Notes = request.Notes;
        recipe.ProductId = product?.Id;
        recipe.Product = product;
        recipe.UpdatedAt = time.GetUtcNow().UtcDateTime;

        // Full replace per spec: delete-and-recreate the ingredient rows.
        recipe.Ingredients.Clear();
        foreach (var ingredient in ToIngredients(request.Ingredients!))
        {
            recipe.Ingredients.Add(ingredient);
        }
        await recipes.SaveChangesAsync(ct);

        return Ok(AdminRecipeDto.FromEntity(recipe));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var recipe = await recipes.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recipe not found");
        }
        recipes.Remove(recipe);
        await recipes.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Product?> ResolveProductAsync(int? productId, CancellationToken ct) =>
        productId is null ? null : await products.GetByIdAsync(productId.Value, ct);

    private ObjectResult UnknownProductProblem(int productId) =>
        Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown product",
            detail: $"No product with id {productId} exists.");

    private static List<RecipeIngredient> ToIngredients(List<SaveRecipeIngredient> items) =>
        items.Select((item, index) => new RecipeIngredient
        {
            Name = item.Name!,
            Quantity = item.Quantity!.Value,
            Unit = string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit,
            SortOrder = index,
        }).ToList();
}
