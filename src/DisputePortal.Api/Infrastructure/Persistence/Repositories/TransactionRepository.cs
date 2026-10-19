using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Infrastructure.Persistence.Repositories;

internal sealed class TransactionRepository(AppDbContext db) : ITransactionRepository
{
    public Task<Transaction?> GetForCustomerAsync(Guid id, Guid customerId, CancellationToken ct) =>
        db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);

    public async Task<IReadOnlyDictionary<Guid, Transaction>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Transaction>();
        }

        return await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    public async Task<Page<Transaction>> ListForCustomerAsync(
        Guid customerId, string? search, int skip, int take, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking().Where(t => t.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Merchant, pattern, "\\") ||
                EF.Functions.ILike(t.Reference, pattern, "\\") ||
                EF.Functions.ILike(t.Category, pattern, "\\"));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return new Page<Transaction>(items, total);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
