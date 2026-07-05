namespace BakedManila.Api.Middleware;

/// <summary>
/// Adds baseline security headers to every response. Registered first in the pipeline so the
/// headers are present even on responses short-circuited by later middleware.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });

        await next(context);
    }
}
