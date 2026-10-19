namespace DisputePortal.Api.Domain;

/// <summary>
/// Dispute aggregate. Every status change goes through <see cref="Transition"/>, which appends an
/// immutable <see cref="DisputeEvent"/>; that event stream is the dispute's history.
/// </summary>
public sealed class Dispute
{
    public static class Actions
    {
        public const string Withdraw = "withdraw";
        public const string StartReview = "startReview";
        public const string Resolve = "resolve";
    }

    private static readonly Dictionary<DisputeStatus, DisputeStatus[]> AllowedTransitions = new()
    {
        [DisputeStatus.Submitted] = [DisputeStatus.UnderReview, DisputeStatus.Withdrawn],
        [DisputeStatus.UnderReview] = [DisputeStatus.Upheld, DisputeStatus.Rejected, DisputeStatus.Withdrawn],
        [DisputeStatus.Upheld] = [],
        [DisputeStatus.Rejected] = [],
        [DisputeStatus.Withdrawn] = [],
    };

    private readonly List<DisputeEvent> _events = [];

    private Dispute()
    {
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid CustomerId { get; private set; }

    public DisputeReason Reason { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DisputeStatus Status { get; private set; }

    public string? ResolutionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<DisputeEvent> Events => _events;

    public static Dispute Raise(
        Transaction transaction,
        DisputeStatus? latestDisputeStatus,
        DisputeReason reason,
        string? description,
        DateTimeOffset now)
    {
        if (DisputePolicy.CheckCanRaise(transaction.OccurredAt, latestDisputeStatus, now) is { } violation)
        {
            throw violation;
        }

        var text = description?.Trim() ?? string.Empty;
        if (reason == DisputeReason.Other && text.Length == 0)
        {
            throw new DomainException("description_required", "Please describe the problem when the reason is 'Other'.");
        }

        var dispute = new Dispute
        {
            Id = Guid.CreateVersion7(now),
            TransactionId = transaction.Id,
            CustomerId = transaction.CustomerId,
            Reason = reason,
            Description = text,
            Status = DisputeStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dispute._events.Add(new DisputeEvent(
            dispute.Id, fromStatus: null, DisputeStatus.Submitted, transaction.CustomerId, Roles.Customer, note: null, now));

        return dispute;
    }

    public void Withdraw(Guid customerId, DateTimeOffset now) =>
        Transition(DisputeStatus.Withdrawn, customerId, Roles.Customer, note: null, now);

    public void StartReview(Guid agentId, DateTimeOffset now) =>
        Transition(DisputeStatus.UnderReview, agentId, Roles.Agent, note: null, now);

    public void Resolve(DisputeOutcome outcome, string? note, Guid agentId, DateTimeOffset now)
    {
        var text = note?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new DomainException("resolution_note_required", "A resolution note is required.");
        }

        var target = outcome == DisputeOutcome.Upheld ? DisputeStatus.Upheld : DisputeStatus.Rejected;
        Transition(target, agentId, Roles.Agent, text, now);
        ResolutionNote = text;
    }

    /// <summary>
    /// Which actions the given role may take on a dispute in the given status. The UI renders buttons
    /// from this list, so workflow rules live in one place.
    /// </summary>
    public static IReadOnlyList<string> AllowedActionsFor(DisputeStatus status, string role) => role switch
    {
        Roles.Customer when status.IsActive() => [Actions.Withdraw],
        Roles.Agent when status == DisputeStatus.Submitted => [Actions.StartReview],
        Roles.Agent when status == DisputeStatus.UnderReview => [Actions.Resolve],
        _ => [],
    };

    private void Transition(DisputeStatus to, Guid actorId, string actorRole, string? note, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(to))
        {
            throw new DomainException(
                "invalid_status_transition",
                $"A dispute that is {Status} cannot move to {to}.",
                DomainErrorKind.Conflict);
        }

        _events.Add(new DisputeEvent(Id, Status, to, actorId, actorRole, note, now));
        Status = to;
        UpdatedAt = now;
    }
}
