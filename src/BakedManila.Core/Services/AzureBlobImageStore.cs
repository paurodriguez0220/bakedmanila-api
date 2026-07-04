using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BakedManila.Core.Services;

/// Production image store (wired + verified in the infra plan). Container: product-images.
public sealed class AzureBlobImageStore(BlobContainerClient container) : IImageStore
{
    public async Task<string> SaveAsync(Stream content, string contentType, int productId, CancellationToken ct)
    {
        if (!ImageContentTypes.TryGetExtension(contentType, out var extension))
        {
            throw new ArgumentException($"Unsupported image content type '{contentType}'.", nameof(contentType));
        }
        var blobName = $"products/{productId}/{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
        return blobName;
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct) =>
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
}
