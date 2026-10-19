using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Features.Auth;

public sealed record LoginRequest(
    [Required(ErrorMessage = "Email is required.")] string? Email,
    [Required(ErrorMessage = "Password is required.")] string? Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);
