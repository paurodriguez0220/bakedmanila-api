using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;

namespace BakedManila.Core.Tests.Api;

/// <summary>
/// Covers Program.cs's non-Development startup path: Migrations:ApplyAtStartup=true plus
/// Admin:Email / Admin:Password must migrate the throwaway database and seed the Admin
/// role/user exactly like the Development path does, since the "Testing" environment used
/// by ApiFactory exercises that branch (it is not Development).
/// </summary>
public sealed class ProdAdminSeedingTests : IAsyncLifetime
{
    private const string Email = "prod-admin@test.local";
    private const string Password = "Test!Passw0rd";

    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(builder =>
        {
            builder.UseSetting("Migrations:ApplyAtStartup", "true");
            builder.UseSetting("Admin:Email", Email);
            builder.UseSetting("Admin:Password", Password);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Login_Succeeds_WhenAdminSeededViaApplyAtStartupBranch()
    {
        var client = _factory.CreateClient(); // builds the host, running the ApplyAtStartup seed path

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.Token));
    }
}
