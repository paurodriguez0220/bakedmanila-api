using BakedManila.Api.Auth;
using BakedManila.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BakedManila.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<IdentityUser> users,
    JwtTokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(); // identical response for unknown email and wrong password
        }

        var roles = await users.GetRolesAsync(user);
        var (token, expiresAtUtc) = tokens.CreateToken(user, roles);
        return Ok(new LoginResponse(token, expiresAtUtc));
    }
}
