using System.ComponentModel.DataAnnotations;
using DisputePortal.Api.Domain;

namespace DisputePortal.Api.Features.Disputes;

public sealed record RaiseDisputeRequest(
    [Required(ErrorMessage = "Choose a reason for the dispute.")]
    DisputeReason? Reason,
    [MaxLength(DisputePolicy.MaxDescriptionLength, ErrorMessage = "Keep the description under 1000 characters.")]
    string? Description);

public sealed record ResolveDisputeRequest(
    [Required(ErrorMessage = "Choose whether the dispute is upheld or rejected.")]
    DisputeOutcome? Outcome,
    [Required(ErrorMessage = "Explain the decision to the customer.")]
    [MaxLength(DisputePolicy.MaxDescriptionLength, ErrorMessage = "Keep the note under 1000 characters.")]
    string? Note);

public sealed record TransactionBriefDto(
    Guid Id, string Reference, string Merchant, decimal Amount, string Currency, DateTimeOffset OccurredAt);

public sealed record DisputeListItemDto(
    Guid Id,
    DisputeStatus Status,
    DisputeReason Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CustomerName,
    TransactionBriefDto Transaction);

public sealed record DisputeEventDto(
    Guid Id,
    DisputeStatus? FromStatus,
    DisputeStatus ToStatus,
    string ActorName,
    string ActorRole,
    string? Note,
    DateTimeOffset OccurredAt);

public sealed record DisputeDetailDto(
    Guid Id,
    DisputeStatus Status,
    DisputeReason Reason,
    string Description,
    string? ResolutionNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CustomerName,
    TransactionBriefDto Transaction,
    IReadOnlyList<DisputeEventDto> History,
    IReadOnlyList<string> AllowedActions);
