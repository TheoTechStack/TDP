using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Transactions;

public sealed class TransactionService(AppDbContext db, TimeProvider clock)
{
    public async Task<PagedResult<TransactionDto>> ListAsync(
        Guid customerId, int? page, int? pageSize, string? search, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
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
        var transactions = await query
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        var latest = await LatestDisputesAsync(transactions.Select(t => t.Id).ToList(), ct);
        var now = clock.GetUtcNow();
        var items = transactions.Select(t => ToDto(t, latest.GetValueOrDefault(t.Id), now)).ToList();

        return new PagedResult<TransactionDto>(items, paging.Page, paging.PageSize, total);
    }

    /// <returns>The transaction, or <c>null</c> if it doesn't exist or belongs to another customer.</returns>
    public async Task<TransactionDto?> GetAsync(Guid id, Guid customerId, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);
        if (transaction is null)
        {
            return null;
        }

        var latest = await LatestDisputesAsync([transaction.Id], ct);
        return ToDto(transaction, latest.GetValueOrDefault(transaction.Id), clock.GetUtcNow());
    }

    /// <summary>Latest dispute per transaction for one page of transactions, in one query.</summary>
    private async Task<Dictionary<Guid, DisputeSummaryDto>> LatestDisputesAsync(List<Guid> transactionIds, CancellationToken ct)
    {
        if (transactionIds.Count == 0)
        {
            return new Dictionary<Guid, DisputeSummaryDto>();
        }

        var disputes = await db.Disputes.AsNoTracking()
            .Where(d => transactionIds.Contains(d.TransactionId))
            .Select(d => new { d.TransactionId, d.Id, d.Status, d.CreatedAt })
            .ToListAsync(ct);

        return disputes
            .GroupBy(d => d.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(d => d.CreatedAt).Select(d => new DisputeSummaryDto(d.Id, d.Status, d.CreatedAt)).First());
    }

    private static TransactionDto ToDto(Transaction t, DisputeSummaryDto? latestDispute, DateTimeOffset now)
    {
        var blocked = DisputePolicy.CheckCanRaise(t.OccurredAt, latestDispute?.Status, now);
        return new TransactionDto(
            t.Id, t.Reference, t.Merchant, t.Category, t.Amount, t.Currency, t.OccurredAt,
            latestDispute, CanDispute: blocked is null, CannotDisputeReason: blocked?.Message);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
