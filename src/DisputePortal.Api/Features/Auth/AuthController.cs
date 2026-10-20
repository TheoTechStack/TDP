using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Features.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    public const string LoginRateLimitPolicy = "login";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var session = await auth.LoginAsync(request.Email!, request.Password!, ct);
        if (session is null)
        {
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Email or password is incorrect.",
                Extensions = { ["code"] = "invalid_credentials" },
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" },
            };
        }

        return session;
    }

    /// <summary>
    /// The seeded demo logins (no passwords), so the sign-in page can offer them. 404 when seeding is off,
    /// so a real environment never advertises accounts.
    /// </summary>
    [HttpGet("demo-accounts")]
    [AllowAnonymous]
    [ProducesResponseType<DemoAccountDto[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DemoAccountDto[]> DemoAccounts([FromServices] IOptions<SeedOptions> seed)
    {
        if (!seed.Value.Enabled)
        {
            return NotFound();
        }

        return DemoDataSeeder.Accounts
            .Select(a => new DemoAccountDto(a.Email, a.DisplayName, a.Role, a.Hint))
            .ToArray();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await auth.GetUserAsync(User.GetUserId(), ct);
        return user is null ? Unauthorized() : user;
    }
}
