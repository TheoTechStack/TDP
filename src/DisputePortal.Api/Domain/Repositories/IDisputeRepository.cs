namespace DisputePortal.Api.Domain.Repositories;

/// <summary>
/// Persistence for the <see cref="Dispute"/> aggregate. <c>customerId</c> scopes a lookup to one
/// customer's disputes; <c>null</c> means unscoped (agents).
/// </summary>
public interface IDisputeRepository
{
    void Add(Dispute dispute);

    /// <summary>Tracked for change; status changes are saved by <see cref="IUnitOfWork"/>.</summary>
    Task<Dispute?> GetForUpdateAsync(Guid id, Guid? customerId, CancellationToken ct);

    /// <summary>Read-only, with the full event history loaded.</summary>
    Task<Dispute?> GetWithHistoryAsync(Guid id, Guid? customerId, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<Page<Dispute>> ListAsync(Guid? customerId, DisputeStatus? status, int skip, int take, CancellationToken ct);

    /// <summary>The most recent dispute for each transaction that has one.</summary>
    Task<IReadOnlyDictionary<Guid, Dispute>> GetLatestByTransactionAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken ct);
}
