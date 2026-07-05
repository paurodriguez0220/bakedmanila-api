using System.Net;
using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using BakedManila.Core.Data;
using BakedManila.Core.Tests.Data;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Config (Key Vault in prod) is the source of truth for the admin password: rotating
    /// Admin:Password and restarting must reset the stored Identity password to match, so
    /// the old password stops working and the new one logs in. Simulates a restart with a
    /// changed secret by booting a second ApiFactory over the same database as the first.
    /// </summary>
    [Fact]
    public async Task Login_UsesRotatedPassword_WhenAdminPasswordConfigChangesAcrossRestart()
    {
        const string RotatedPassword = "RotatedPass123!x";
        var sharedConnectionString = TestDb.NewConnectionString();

        await using (var factory1 = new ApiFactory(builder =>
        {
            builder.UseSetting("Migrations:ApplyAtStartup", "true");
            builder.UseSetting("Admin:Email", Email);
            builder.UseSetting("Admin:Password", Password);
            builder.UseSetting("ConnectionStrings:BakedManila", sharedConnectionString);
        }))
        {
            _ = factory1.CreateClient(); // boots the host; seeds admin with the original password
        }

        try
        {
            await using var factory2 = new ApiFactory(builder =>
            {
                builder.UseSetting("Migrations:ApplyAtStartup", "true");
                builder.UseSetting("Admin:Email", Email);
                builder.UseSetting("Admin:Password", RotatedPassword);
                builder.UseSetting("ConnectionStrings:BakedManila", sharedConnectionString);
            });
            var client = factory2.CreateClient(); // boots again over the same DB; must rotate the password

            var loginWithRotated = await client.PostAsJsonAsync("/api/auth/login",
                new { email = Email, password = RotatedPassword });
            Assert.Equal(HttpStatusCode.OK, loginWithRotated.StatusCode);

            var loginWithOriginal = await client.PostAsJsonAsync("/api/auth/login",
                new { email = Email, password = Password });
            Assert.Equal(HttpStatusCode.Unauthorized, loginWithOriginal.StatusCode);
        }
        finally
        {
            // Each factory generates its own (unused, never-created) connection string internally
            // and only the shared one above is actually connected to — so both factories' own
            // DisposeAsync no-op against a nonexistent DB, and the shared DB must be cleaned up here.
            await using var db = new BakedManilaDbContext(new DbContextOptionsBuilder<BakedManilaDbContext>()
                .UseSqlServer(sharedConnectionString).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
