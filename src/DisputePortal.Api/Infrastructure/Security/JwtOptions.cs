using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "dispute-portal";

    [Required]
    public string Audience { get; set; } = "dispute-portal";

    /// <summary>HMAC-SHA256 key; at least 32 bytes. Comes from Secrets Manager in AWS.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 24 * 60)]
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}
