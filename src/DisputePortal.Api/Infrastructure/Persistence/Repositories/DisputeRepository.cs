using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Infrastructure.Persistence.Repositories;

internal sealed class DisputeRepository(AppDbContext db) : IDisputeRepository
{
    public void Add(Dispute dispute) => db.Disputes.Add(dispute);

    public Task<Dispute?> GetForUpdateAsync(Guid id, Guid? customerId, CancellationToken ct) =>
        Scoped(db.Disputes, customerId)
            .SingleOrDefaultAsync(d => d.Id == id, ct);

    public Task<Dispute?> GetWithHistoryAsync(Guid id, Guid? customerId, CancellationToken ct) =>
        Scoped(db.Disputes.AsNoTracking(), customerId)
            .Include(d => d.Events)
            .SingleOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Page<Dispute>> ListAsync(
        Guid? customerId, DisputeStatus? status, int skip, int take, CancellationToken ct)
    {
        var query = Scoped(db.Disputes.AsNoTracking(), customerId);

        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return new Page<Dispute>(items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, Dispute>> GetLatestByTransactionAsync(
        IReadOnlyCollection<Guid> transactionIds, CancellationToken ct)
    {
        if (transactionIds.Count == 0)
        {
            return new Dictionary<Guid, Dispute>();
        }

        var disputes = await db.Disputes.AsNoTracking()
            .Where(d => transactionIds.Contains(d.TransactionId))
            .ToListAsync(ct);

        return disputes
            .GroupBy(d => d.TransactionId)
            .ToDictionary(g => g.Key, g => g.MaxBy(d => d.CreatedAt)!);
    }

    private static IQueryable<Dispute> Scoped(IQueryable<Dispute> disputes, Guid? customerId) =>
        customerId is { } id ? disputes.Where(d => d.CustomerId == id) : disputes;
}
