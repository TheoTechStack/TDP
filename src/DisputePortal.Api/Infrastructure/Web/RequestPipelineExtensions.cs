using System.Diagnostics;

namespace DisputePortal.Api.Infrastructure.Web;

internal static class RequestPipelineExtensions
{
    private const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>
    /// Echoes (or creates) a correlation id, adds it to the log scope and response, and tags the trace.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers[CorrelationHeader].ToString();
            var correlationId = IsSafe(incoming) ? incoming : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

            context.Response.Headers[CorrelationHeader] = correlationId;
            Activity.Current?.SetTag("correlation.id", correlationId);

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Request");
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next(context);
            }
        });

    /// <summary>Baseline browser hardening for the SPA and API responses.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
                "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            await next(context);
        });

    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
