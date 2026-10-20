using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Features.Auth;

public sealed record LoginRequest(
    [Required(ErrorMessage = "Email is required.")] string? Email,
    [Required(ErrorMessage = "Password is required.")] string? Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>A demo login shown on the sign-in page.</summary>
public sealed record DemoAccountDto(string Email, string DisplayName, string Role, string Hint);

/// <summary>
/// The demo logins for the sign-in page. <see cref="Password"/> is null unless the environment opts in with
/// <c>Seed:RevealDemoPassword</c>.
/// </summary>
public sealed record DemoAccountsResponse(DemoAccountDto[] Accounts, string? Password);
