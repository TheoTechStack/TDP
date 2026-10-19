namespace DisputePortal.Api.Domain;

/// <summary>
/// One entry in a dispute's history. Append-only: the database rejects UPDATE and DELETE on this table.
/// </summary>
public sealed class DisputeEvent
{
    private DisputeEvent()
    {
    }

    internal DisputeEvent(
        Guid disputeId,
        DisputeStatus? fromStatus,
        DisputeStatus toStatus,
        Guid actorId,
        string actorRole,
        string? note,
        DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7(occurredAt);
        DisputeId = disputeId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorId = actorId;
        ActorRole = actorRole;
        Note = note;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid DisputeId { get; private set; }

    public DisputeStatus? FromStatus { get; private set; }

    public DisputeStatus ToStatus { get; private set; }

    public Guid ActorId { get; private set; }

    public string ActorRole { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
