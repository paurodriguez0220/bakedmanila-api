using BakedManila.Core.Services;

namespace BakedManila.Api.Services;

/// Dev/test image store: writes under a local root, mirroring blob-name layout.
public sealed class FileSystemImageStore(string root) : IImageStore
{
    public async Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct)
    {
        if (!ImageContentTypes.TryGetExtension(contentType, out var extension))
        {
            throw new ArgumentException($"Unsupported image content type '{contentType}'.", nameof(contentType));
        }
        var blobName = $"products/{productId}/{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, blobName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return blobName;
    }

    public Task DeleteAsync(string blobName, CancellationToken ct)
    {
        var path = Path.Combine(root, blobName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
