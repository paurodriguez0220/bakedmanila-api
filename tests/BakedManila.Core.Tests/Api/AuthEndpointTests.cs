using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

public sealed class AuthEndpointTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await using var db = await _factory.CreateDbAsync(); // ensures schema
        await AdminAuth.EnsureAdminAsync(_factory);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Login_ReturnsToken_WithAdminRoleClaim()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = AdminAuth.Email, password = AdminAuth.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow.AddHours(7));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        Assert.Contains(jwt.Claims, c => c.Type is "role" or System.Security.Claims.ClaimTypes.Role
            && c.Value == "Admin");
    }

    [Theory]
    [InlineData("admin@test.local", "WrongPassword1!")]
    [InlineData("nobody@test.local", "Test!Passw0rd")]
    public async Task Login_Returns401_ForBadCredentials(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Returns400_ForMissingFields()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = AdminAuth.Email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
