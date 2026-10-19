namespace DisputePortal.Api.Domain;

/// <summary>
/// Rules that decide whether a transaction can be disputed. Used both when raising a dispute and
/// when listing transactions, so the UI and the API can never disagree.
/// </summary>
public static class DisputePolicy
{
    public const int DisputeWindowDays = 120;

    public const int MaxDescriptionLength = 1000;

    public static readonly TimeSpan DisputeWindow = TimeSpan.FromDays(DisputeWindowDays);

    /// <param name="occurredAt">When the transaction happened.</param>
    /// <param name="latestDisputeStatus">Status of the most recent dispute on the transaction, if any.</param>
    /// <param name="now">Current time.</param>
    /// <returns><c>null</c> if the transaction can be disputed, otherwise the reason it cannot.</returns>
    public static DomainException? CheckCanRaise(DateTimeOffset occurredAt, DisputeStatus? latestDisputeStatus, DateTimeOffset now)
    {
        // A withdrawn dispute can be raised again; anything else (open or decided) cannot.
        if (latestDisputeStatus is { } status && status != DisputeStatus.Withdrawn)
        {
            return new DomainException(
                "already_disputed",
                "This transaction already has a dispute.",
                DomainErrorKind.Conflict);
        }

        if (now - occurredAt > DisputeWindow)
        {
            return new DomainException(
                "dispute_window_closed",
                $"Transactions can only be disputed within {DisputeWindowDays} days.");
        }

        return null;
    }
}
