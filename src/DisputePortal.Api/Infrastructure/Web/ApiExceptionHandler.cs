using DisputePortal.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DisputePortal.Api.Infrastructure.Web;

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Every error carries a stable <c>code</c> the client
/// can switch on; unexpected errors are logged and never leak internals.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            DomainException { Kind: DomainErrorKind.Conflict } e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "concurrent_modification",
                "This dispute was changed by someone else. Refresh and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => (
                StatusCodes.Status409Conflict,
                "already_disputed",
                "This transaction already has an open dispute."),
            BadHttpRequestException e => (e.StatusCode, "bad_request", "The request could not be read."),
            _ => (StatusCodes.Status500InternalServerError, "unexpected_error", "Something went wrong on our side."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected with {Code}: {Message}", code, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code },
            },
        });
    }
}
