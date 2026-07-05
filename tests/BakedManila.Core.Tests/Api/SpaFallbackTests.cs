using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace BakedManila.Core.Tests.Api;

public sealed class SpaFallbackTests : IAsyncLifetime
{
    private const string Sentinel = "spa-fallback-sentinel-3f9a";
    private const string CssSentinel = "/* css-sentinel-7b2e */ body { color: rebeccapurple; }";

    private string _wwwrootDir = null!;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _wwwrootDir = Path.Combine(Path.GetTempPath(), $"bm-wwwroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_wwwrootDir);
        Directory.CreateDirectory(Path.Combine(_wwwrootDir, "assets"));
        await File.WriteAllTextAsync(
            Path.Combine(_wwwrootDir, "index.html"),
            $"<!DOCTYPE html><html><body>{Sentinel}</body></html>");
        await File.WriteAllTextAsync(
            Path.Combine(_wwwrootDir, "assets", "app.css"),
            CssSentinel);

        _factory = new ApiFactory(builder => builder.UseWebRoot(_wwwrootDir));
        await using var db = await _factory.CreateDbAsync();
        _ = db;
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_wwwrootDir))
        {
            Directory.Delete(_wwwrootDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetSpaRoute_WithWwwroot_ServesIndexHtml()
    {
        var response = await _client.GetAsync("/admin/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(Sentinel, body);
    }

    [Fact]
    public async Task GetStaticAsset_WithWwwroot_ServesTheFileNotTheSpaFallback()
    {
        // Regression: MapFallback("{*path}") matches every URL at routing time, and
        // StaticFileMiddleware skips requests with a matched endpoint — so unless static
        // files are registered before UseRouting, every asset came back as index.html
        // (broke all CSS/JS in the first production deploy).
        var response = await _client.GetAsync("/assets/app.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("css-sentinel-7b2e", body);
        Assert.DoesNotContain(Sentinel, body);
    }

    [Fact]
    public async Task GetUnknownApiRoute_WithWwwroot_StillReturnsProblemJson()
    {
        var response = await _client.GetAsync("/api/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }
}

public sealed class SpaFallbackWithoutWwwrootTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync();
        _ = db;
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetSpaRoute_WithoutWwwroot_Returns404()
    {
        var response = await _client.GetAsync("/admin/orders");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
