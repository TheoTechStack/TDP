using System.Threading.RateLimiting;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Api.Infrastructure.Security;

internal static class SecurityExtensions
{
    /// <summary>JWT issuing and validation, plus the Customer and Agent authorisation policies.</summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<TokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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

        services.AddAuthorizationBuilder()
            .AddPolicy(Roles.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Roles.Agent, p => p.RequireRole(Roles.Agent));

        return services;
    }

    /// <summary>Brute-force protection on sign-in, per client IP (X-Forwarded-For is honoured behind the ALB).</summary>
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
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

        return services;
    }
}
