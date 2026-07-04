namespace BakedManila.Core.Domain.Exceptions;

public sealed class ProductUnavailableException(string slug)
    : Exception($"Product '{slug}' is not available right now.")
{
    public string Slug { get; } = slug;
}
