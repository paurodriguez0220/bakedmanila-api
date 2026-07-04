using System.Net.Http.Json;
using BakedManila.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BakedManila.Core.Tests.Api;

public static class AdminAuth
{
    public const string Email = "admin@test.local";
    public const string Password = "Test!Passw0rd";

    public static async Task<string> GetTokenAsync(ApiFactory factory)
    {
        await EnsureAdminAsync(factory);
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = Email, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    public static async Task EnsureAdminAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Admin"))
        {
            _ = await roles.CreateAsync(new IdentityRole("Admin"));
        }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(Email) is null)
        {
            var user = new IdentityUser { UserName = Email, Email = Email };
            var created = await users.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            _ = await users.AddToRoleAsync(user, "Admin");
        }
    }
}
