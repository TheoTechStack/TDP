using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace DisputePortal.Api.Infrastructure.Web;

internal static class WebApiExtensions
{
    /// <summary>Controllers, JSON, RFC 9457 problem details, the exception handler, OpenAPI and health checks.</summary>
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        services
            .AddControllers(o =>
                // Validation errors are keyed by the JSON property name ("reason", not "Reason").
                o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    /// <summary>The middleware pipeline. Order matters: errors first, then headers, files, auth, rate limits.</summary>
    public static WebApplication UseWebApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSecurityHeaders();
        app.UseCorrelationId();

        // The Vue SPA is built into wwwroot by the Dockerfile, so one container serves UI and API (no CORS).
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }

    /// <summary>Endpoints: OpenAPI (Development), health probes, controllers and the SPA fallback.</summary>
    public static WebApplication MapWebApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

        app.MapControllers();

        // Unknown API routes are real 404s; everything else falls through to the SPA's client-side router.
        app.MapFallback("/api/{**path}", () => Results.NotFound());
        app.MapFallbackToFile("index.html");

        return app;
    }
}
