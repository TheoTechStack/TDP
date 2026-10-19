using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;
using DisputePortal.Api.Infrastructure.Security;

namespace DisputePortal.Api.Features.Auth;

public sealed class AuthService(IUserRepository users, TokenService tokens)
{
    /// <returns>A session, or <c>null</c> if the credentials are wrong.</returns>
    public async Task<LoginResponse?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(email, ct);

        // Same outcome for unknown user and wrong password, so accounts cannot be enumerated.
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        var (token, expiresAt) = tokens.Issue(user);
        return new LoginResponse(token, expiresAt, ToDto(user));
    }

    public async Task<UserDto?> GetUserAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetAsync(id, ct);
        return user is null ? null : ToDto(user);
    }

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.DisplayName, user.Role);
}
