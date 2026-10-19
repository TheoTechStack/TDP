using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Features.Auth;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;
using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using DisputePortal.Api.Infrastructure.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Infrastructure --------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DatabaseConnectionFactory>();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<DatabaseConnectionFactory>().ConnectionString));
builder.Services.AddScoped<SqlMigrator>();
builder.Services.AddScoped<DemoDataSeeder>();

// ---- Feature services (vertical slices: each feature owns its controller, service and contracts) --
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<DisputeService>();

builder.Services
    .AddControllers(o =>
        // Validation errors are keyed by the JSON property name ("reason", not "Reason").
        o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// ---- Authentication & authorisation ----------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = TokenService.CreateKey(jwt.Value.SigningKey),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimNames.Name,
            RoleClaimType = ClaimNames.Role,
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Customer, p => p.RequireRole(Roles.Customer))
    .AddPolicy(Roles.Agent, p => p.RequireRole(Roles.Agent));

// Brute-force protection on sign-in, per client IP (X-Forwarded-For is honoured behind the ALB).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthController.LoginRateLimitPolicy, ctx =>
    {
        var permits = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:LoginPermitPerMinute", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });
    });
});

var app = builder.Build();

// ---- Database ---------------------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        await scope.ServiceProvider.GetRequiredService<SqlMigrator>().MigrateAsync(CancellationToken.None);
    }

    if (app.Configuration.GetValue("Seed:Enabled", false))
    {
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(CancellationToken.None);
    }
}

// ---- HTTP pipeline ----------------------------------------------------------------------------
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

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
