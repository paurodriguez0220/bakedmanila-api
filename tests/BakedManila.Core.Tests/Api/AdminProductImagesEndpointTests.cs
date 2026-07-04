using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AdminProductImagesEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private string _imagesRoot = null!;
    private int _productId;

    public async Task InitializeAsync()
    {
        _imagesRoot = Path.Combine(Path.GetTempPath(), $"bm-images-{Guid.NewGuid():N}");
        _factory = new ApiFactory(configureHost: builder =>
        {
            builder.UseSetting("Images:Provider", "FileSystem");
            builder.UseSetting("Images:FileSystemRoot", _imagesRoot);
        });
        await using (var db = await _factory.CreateDbAsync())
        {
            db.Products.Add(new BakedManila.Core.Domain.Product
            {
                Name = "Classic Chip", Slug = "classic-chip", Price = 280m,
            });
            await db.SaveChangesAsync();
            _productId = db.Products.Local.First().Id;
        }
        _client = _factory.CreateClient();
        var token = await AdminAuth.GetTokenAsync(_factory);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_imagesRoot))
        {
            Directory.Delete(_imagesRoot, recursive: true);
        }
    }

    private static MultipartFormDataContent JpegUpload(int bytes = 1024)
    {
        var payload = new ByteArrayContent(new byte[bytes]);
        payload.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { payload, "file", "cookie.jpg" } };
    }

    [Fact]
    public async Task Upload_StoresFile_CreatesRow_AndServesUrl()
    {
        var response = await _client.PostAsync($"/api/admin/products/{_productId}/images", JpegUpload());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<ProductImageAdminDto>();
        Assert.EndsWith(".jpg", image!.Url);
        Assert.Equal(1, image.SortOrder);

        // file physically exists under the temp root
        var files = Directory.GetFiles(Path.Combine(_imagesRoot, "products", _productId.ToString()));
        Assert.Single(files);

        // image URL appears on the admin product
        var admin = await _client.GetFromJsonAsync<List<AdminProductDto>>("/api/admin/products");
        Assert.Single(admin!.Single(p => p.Id == _productId).Images);
    }

    [Fact]
    public async Task Upload_Returns422_ForWrongTypeOrTooLarge()
    {
        var gif = new ByteArrayContent(new byte[10]);
        gif.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
        var badType = await _client.PostAsync($"/api/admin/products/{_productId}/images",
            new MultipartFormDataContent { { gif, "file", "cookie.gif" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badType.StatusCode);

        var tooLarge = await _client.PostAsync($"/api/admin/products/{_productId}/images",
            JpegUpload(bytes: 6 * 1024 * 1024));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLarge.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns404_ForUnknownProduct()
    {
        var response = await _client.PostAsync("/api/admin/products/999999/images", JpegUpload());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRowAndFile()
    {
        var created = await (await _client.PostAsync($"/api/admin/products/{_productId}/images", JpegUpload()))
            .Content.ReadFromJsonAsync<ProductImageAdminDto>();

        var deleted = await _client.DeleteAsync($"/api/admin/products/{_productId}/images/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var dir = Path.Combine(_imagesRoot, "products", _productId.ToString());
        Assert.Empty(Directory.Exists(dir) ? Directory.GetFiles(dir) : []);
    }
}
