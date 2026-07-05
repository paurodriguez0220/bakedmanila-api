using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BakedManila.Core.Tests.Api;

public sealed class SecurityHeadersTests : IAsyncLifetime
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
    public async Task GetProducts_CarriesSecurityHeaders_WithExactValues()
    {
        var response = await _client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task GetProducts_BehindTlsTerminatingProxy_RewritesSchemeAndEmitsHsts()
    {
        // Simulate App Service: a non-loopback front-end hop forwarding an HTTPS request.
        // Proves both that the proxy allow-lists are actually cleared (a loopback-only default
        // would reject the 10.0.0.42 hop) and that ForwardedHeaders runs before UseHsts (HSTS
        // only emits when the rewritten scheme is https).
        await using var factory = new ApiFactory(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new FakeProxyHopStartupFilter())));
        await using var db = await factory.CreateDbAsync();
        _ = db;
        using var client = factory.CreateClient();
        // Non-localhost host: HstsMiddleware skips its default excluded hosts (localhost et al).
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://bakedmanila.test/api/products");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    /// <summary>
    /// Wraps the pipeline so every request arrives from a non-loopback immediate hop,
    /// like App Service's front-end proxy. Startup filters run before app middleware.
    /// </summary>
    private sealed class FakeProxyHopStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.42");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
