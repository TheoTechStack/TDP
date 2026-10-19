using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Auth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .Produces<UserDto>();

        return routes;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db, TokenService tokens, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Problems.Validation(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Email and password are required."],
            });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);

        // Same response for unknown user and wrong password, so accounts cannot be enumerated.
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            return TypedResults.Problem(
                title: "Email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials" });
        }

        var (token, expiresAt) = tokens.Issue(user);
        return TypedResults.Ok(new LoginResponse(token, expiresAt, ToDto(user)));
    }

    private static async Task<IResult> GetMeAsync(System.Security.Claims.ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var id = principal.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(ToDto(user));
    }

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.DisplayName, user.Role);
}
