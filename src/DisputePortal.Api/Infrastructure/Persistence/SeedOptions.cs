using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Infrastructure.Persistence;

/// <summary>
/// Demo data settings. The demo password is configuration, never code: Secrets Manager in AWS,
/// a git-ignored <c>.env</c> for Docker Compose, user-secrets for <c>dotnet run</c>.
/// </summary>
public sealed class SeedOptions : IValidatableObject
{
    public const string SectionName = "Seed";
    public const int MinimumPasswordLength = 12;

    public bool Enabled { get; set; }

    public string? DemoPassword { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && (DemoPassword is null || DemoPassword.Length < MinimumPasswordLength))
        {
            yield return new ValidationResult(
                $"Seed:DemoPassword must be set (at least {MinimumPasswordLength} characters) when seeding is enabled.",
                [nameof(DemoPassword)]);
        }
    }
}
