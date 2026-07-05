using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace BakedManila.Core.Tests.Api;

public sealed class OpenApiDocumentTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // MapOpenApi() is only wired up under IsDevelopment() in Program.cs, so the document
        // is only reachable when the host runs in the Development environment.
        _factory = new ApiFactory(configureHost: b => b.UseEnvironment("Development"));
        await using var db = await _factory.CreateDbAsync(); // ensures schema
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GetOpenApiDocument_DeclaresBearerSecurityScheme()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("securitySchemes", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bearer", body, StringComparison.OrdinalIgnoreCase);
    }
}
