namespace BakedManila.Core.Services;

public interface IImageStore
{
    /// Returns the stored blob name, e.g. "products/12/3f9c….jpg".
    Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct);
    Task DeleteAsync(string blobName, CancellationToken ct);
}

public static class ImageContentTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static bool TryGetExtension(string contentType, out string extension) =>
        Map.TryGetValue(contentType, out extension!);
}
