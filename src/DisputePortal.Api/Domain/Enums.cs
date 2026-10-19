namespace DisputePortal.Api.Domain;

public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Upheld,
    Rejected,
    Withdrawn,
}

public enum DisputeReason
{
    Unauthorised,
    Duplicate,
    IncorrectAmount,
    GoodsNotReceived,
    Other,
}

/// <summary>The outcome an agent can choose when resolving a dispute.</summary>
public enum DisputeOutcome
{
    Upheld,
    Rejected,
}

public static class Roles
{
    public const string Customer = "Customer";
    public const string Agent = "Agent";
}

public static class DisputeStatusExtensions
{
    /// <summary>Active disputes are still being worked; at most one may exist per transaction.</summary>
    public static bool IsActive(this DisputeStatus status) =>
        status is DisputeStatus.Submitted or DisputeStatus.UnderReview;
}
