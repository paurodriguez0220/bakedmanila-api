namespace BakedManila.Core.Domain.Exceptions;

public sealed class DuplicateSlugException(string slug)
    : Exception($"A product with slug '{slug}' already exists.")
{
    public string Slug { get; } = slug;
}
