namespace DisputePortal.Api.Domain.Repositories;

/// <summary>
/// Commits everything the repositories changed in the current request as one database transaction.
/// Repositories never save on their own, so a use case decides when its work is complete.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>One page of results plus the total number of matches.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int TotalCount);
