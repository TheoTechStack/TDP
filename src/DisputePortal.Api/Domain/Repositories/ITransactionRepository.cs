namespace DisputePortal.Api.Domain.Repositories;

public interface ITransactionRepository
{
    /// <summary><c>null</c> if the transaction doesn't exist or belongs to another customer.</summary>
    Task<Transaction?> GetForCustomerAsync(Guid id, Guid customerId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, Transaction>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Newest first; <paramref name="search"/> matches merchant, reference or category.</summary>
    Task<Page<Transaction>> ListForCustomerAsync(Guid customerId, string? search, int skip, int take, CancellationToken ct);
}
