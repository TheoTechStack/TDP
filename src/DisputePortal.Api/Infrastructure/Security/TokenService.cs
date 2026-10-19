using System.Security.Claims;
using System.Text;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Api.Infrastructure.Security;

public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenLifetimeMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimNames.Subject, user.Id.ToString()),
                new Claim(ClaimNames.Email, user.Email),
                new Claim(ClaimNames.Name, user.DisplayName),
                new Claim(ClaimNames.Role, user.Role),
            }),
            SigningCredentials = new SigningCredentials(CreateKey(jwt.SigningKey), SecurityAlgorithms.HmacSha256),
        });

        return (token, expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimNames.Subject)
            ?? throw new InvalidOperationException("Authenticated principal has no subject claim."));

    public static string GetRole(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimNames.Role) ?? Roles.Customer;

    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal) => new(principal.GetUserId(), principal.GetRole());
}
