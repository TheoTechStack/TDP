using System.Security.Claims;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Transactions;

public sealed record DisputeSummaryDto(Guid Id, DisputeStatus Status, DateTimeOffset CreatedAt);

public sealed record TransactionDto(
    Guid Id,
    string Reference,
    string Merchant,
    string Category,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    DisputeSummaryDto? LatestDispute,
    bool CanDispute,
    string? CannotDisputeReason);

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/transactions")
            .WithTags("Transactions")
            .RequireAuthorization(Roles.Customer);

        group.MapGet("/", ListAsync).Produces<PagedResult<TransactionDto>>();
        group.MapGet("/{id:guid}", GetAsync).Produces<TransactionDto>().ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        TimeProvider clock,
        int? page,
        int? pageSize,
        string? search,
        CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var customerId = principal.GetUserId();

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

        var latest = await LatestDisputesAsync(db, transactions.Select(t => t.Id).ToList(), ct);
        var now = clock.GetUtcNow();
        var items = transactions.Select(t => ToDto(t, latest.GetValueOrDefault(t.Id), now)).ToList();

        return TypedResults.Ok(new PagedResult<TransactionDto>(items, paging.Page, paging.PageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var transaction = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId, ct);

        // 404 rather than 403 for other customers' transactions: don't reveal that the id exists.
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }

        var latest = await LatestDisputesAsync(db, [transaction.Id], ct);
        return TypedResults.Ok(ToDto(transaction, latest.GetValueOrDefault(transaction.Id), clock.GetUtcNow()));
    }

    /// <summary>Latest dispute per transaction for one page of transactions (one round trip).</summary>
    private static async Task<Dictionary<Guid, DisputeSummaryDto>> LatestDisputesAsync(
        AppDbContext db, List<Guid> transactionIds, CancellationToken ct)
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
