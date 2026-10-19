namespace DisputePortal.Api.Domain;

public enum DomainErrorKind
{
    /// <summary>The request breaks a business rule (HTTP 422).</summary>
    RuleViolation,

    /// <summary>The request conflicts with the current state of the resource (HTTP 409).</summary>
    Conflict,
}

/// <summary>
/// Raised when a business rule rejects an operation. Carries a stable, machine-readable
/// <see cref="Code"/> that the API returns in its problem details so clients never parse messages.
/// </summary>
public sealed class DomainException(string code, string message, DomainErrorKind kind = DomainErrorKind.RuleViolation)
    : Exception(message)
{
    public string Code { get; } = code;

    public DomainErrorKind Kind { get; } = kind;
}
