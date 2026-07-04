using BakedManila.Core.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BakedManila.Api.Middleware;

public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string title) = exception switch
        {
            ProductNotFoundException => (StatusCodes.Status404NotFound, "Product not found"),
            ProductUnavailableException => (StatusCodes.Status409Conflict, "Product unavailable"),
            InvalidStatusTransitionException => (StatusCodes.Status409Conflict, "Invalid status transition"),
            InvalidOrderException => (StatusCodes.Status422UnprocessableEntity, "Invalid order"),
            _ => (0, string.Empty),
        };
        if (status == 0)
        {
            return false; // not ours — global handler logs and returns 500
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
            },
        });
    }
}
