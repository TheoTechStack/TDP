using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;

namespace DisputePortal.Api.Features.Transactions;

public sealed class TransactionService(ITransactionRepository transactions, IDisputeRepository disputes, TimeProvider clock)
{
    public async Task<PagedResult<TransactionDto>> ListAsync(
        Guid customerId, int? page, int? pageSize, string? search, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var result = await transactions.ListForCustomerAsync(customerId, search, paging.Skip, paging.PageSize, ct);

        var latest = await disputes.GetLatestByTransactionAsync(result.Items.Select(t => t.Id).ToList(), ct);
        var now = clock.GetUtcNow();
        var items = result.Items
            .Select(t => ToDto(t, latest.GetValueOrDefault(t.Id), now))
            .ToList();

        return new PagedResult<TransactionDto>(items, paging.Page, paging.PageSize, result.TotalCount);
    }

    /// <returns>The transaction, or <c>null</c> if it doesn't exist or belongs to another customer.</returns>
    public async Task<TransactionDto?> GetAsync(Guid id, Guid customerId, CancellationToken ct)
    {
        var transaction = await transactions.GetForCustomerAsync(id, customerId, ct);
        if (transaction is null)
        {
            return null;
        }

        var latest = await disputes.GetLatestByTransactionAsync([transaction.Id], ct);
        return ToDto(transaction, latest.GetValueOrDefault(transaction.Id), clock.GetUtcNow());
    }

    private static TransactionDto ToDto(Transaction t, Dispute? latestDispute, DateTimeOffset now)
    {
        var blocked = DisputePolicy.CheckCanRaise(t.OccurredAt, latestDispute?.Status, now);
        var summary = latestDispute is null
            ? null
            : new DisputeSummaryDto(latestDispute.Id, latestDispute.Status, latestDispute.CreatedAt);

        return new TransactionDto(
            t.Id, t.Reference, t.Merchant, t.Category, t.Amount, t.Currency, t.OccurredAt,
            summary, CanDispute: blocked is null, CannotDisputeReason: blocked?.Message);
    }
}
