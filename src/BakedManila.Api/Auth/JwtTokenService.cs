using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace BakedManila.Api.Auth;

public sealed class JwtTokenService(IConfiguration config, TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public (string Token, DateTime ExpiresAtUtc) CreateToken(IdentityUser user, IList<string> roles)
    {
        var signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Missing Jwt:SigningKey configuration.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.Add(Lifetime);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
