using DisputePortal.Api.Domain;

namespace DisputePortal.Api.Features.Transactions;

public sealed record DisputeSummaryDto(Guid Id, DisputeStatus Status, DateTimeOffset CreatedAt);

public sealed record TransactionDto(
    Guid Id,
    string Reference,
    string Merchant,
    string Category,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    DisputeSummaryDto? LatestDispute,
    bool CanDispute,
    string? CannotDisputeReason);
