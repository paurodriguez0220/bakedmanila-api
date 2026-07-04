using BakedManila.Core.Data;
using BakedManila.Core.Tests.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace BakedManila.Core.Tests.Api;

public sealed class ApiFactory(Action<IWebHostBuilder>? configureHost = null) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = TestDb.NewConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BakedManila", _connectionString);
        builder.UseSetting("Storage:PublicBaseUrl", "https://img.test");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-bakedmanila-0123456789abcdef0123456789abcdef");
        builder.UseSetting("Jwt:Issuer", "BakedManila");
        builder.UseSetting("Jwt:Audience", "BakedManila");
        configureHost?.Invoke(builder);
    }

    public async Task<BakedManilaDbContext> CreateDbAsync()
    {
        var db = new BakedManilaDbContext(new DbContextOptionsBuilder<BakedManilaDbContext>()
            .UseSqlServer(_connectionString).Options);
        await db.Database.MigrateAsync();
        return db;
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var db = await CreateDbAsync())
        {
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
