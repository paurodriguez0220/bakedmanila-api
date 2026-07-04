namespace BakedManila.Core.Domain.Exceptions;

public sealed class ProductNotFoundException(string slug)
    : Exception($"Product '{slug}' was not found.")
{
    public string Slug { get; } = slug;
}
