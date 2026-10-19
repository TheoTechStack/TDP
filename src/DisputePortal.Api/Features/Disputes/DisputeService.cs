using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// Dispute use cases. Business rules live on the <see cref="Dispute"/> aggregate and
/// <see cref="DisputePolicy"/>; this service loads data, scopes it to the caller, invokes the domain
/// and saves. Rule violations surface as <see cref="DomainException"/> (mapped to 409/422).
/// Methods return <c>null</c> when the resource doesn't exist or the caller may not see it (→ 404).
/// </summary>
public sealed class DisputeService(AppDbContext db, TimeProvider clock)
{
    public async Task<DisputeDetailDto?> RaiseAsync(
        Guid transactionId, RaiseDisputeRequest request, CurrentUser customer, CancellationToken ct)
    {
        if (request.Reason is not { } reason)
        {
            throw new DomainException("reason_required", "Choose a reason for the dispute.");
        }

        var transaction = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customer.Id, ct);
        if (transaction is null)
        {
            return null;
        }

        var latestStatus = await db.Disputes.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => (DisputeStatus?)d.Status)
            .FirstOrDefaultAsync(ct);

        // A concurrent duplicate that slips past this check is stopped by the partial unique index
        // (ux_disputes_one_active_per_transaction) and mapped to 409 by the exception handler.
        var dispute = Dispute.Raise(transaction, latestStatus, reason, request.Description, clock.GetUtcNow());
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync(ct);

        return await GetAsync(dispute.Id, customer, ct);
    }

    public async Task<PagedResult<DisputeListItemDto>> ListAsync(
        CurrentUser user, int? page, int? pageSize, DisputeStatus? status, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var query = VisibleTo(user, db.Disputes.AsNoTracking());

        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        var total = await query.CountAsync(ct);

        var pageOfDisputes = query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize);

        var items = await WithTransactionAndCustomer(pageOfDisputes)
            .OrderByDescending(x => x.Dispute.CreatedAt)
            .ThenByDescending(x => x.Dispute.Id)
            .Select(x => new DisputeListItemDto(
                x.Dispute.Id,
                x.Dispute.Status,
                x.Dispute.Reason,
                x.Dispute.CreatedAt,
                x.Dispute.UpdatedAt,
                x.Customer.DisplayName,
                new TransactionBriefDto(
                    x.Transaction.Id,
                    x.Transaction.Reference,
                    x.Transaction.Merchant,
                    x.Transaction.Amount,
                    x.Transaction.Currency,
                    x.Transaction.OccurredAt)))
            .ToListAsync(ct);

        return new PagedResult<DisputeListItemDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<DisputeDetailDto?> GetAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var header = await WithTransactionAndCustomer(VisibleTo(user, db.Disputes.AsNoTracking()).Where(d => d.Id == id))
            .Select(x => new
            {
                x.Dispute.Id,
                x.Dispute.Status,
                x.Dispute.Reason,
                x.Dispute.Description,
                x.Dispute.ResolutionNote,
                x.Dispute.CreatedAt,
                x.Dispute.UpdatedAt,
                CustomerName = x.Customer.DisplayName,
                Transaction = new TransactionBriefDto(
                    x.Transaction.Id,
                    x.Transaction.Reference,
                    x.Transaction.Merchant,
                    x.Transaction.Amount,
                    x.Transaction.Currency,
                    x.Transaction.OccurredAt),
            })
            .SingleOrDefaultAsync(ct);

        if (header is null)
        {
            return null;
        }

        var history = await db.DisputeEvents.AsNoTracking()
            .Where(e => e.DisputeId == id)
            .Join(db.Users, e => e.ActorId, u => u.Id, (e, u) => new { Event = e, Actor = u })
            .OrderBy(x => x.Event.OccurredAt)
            .ThenBy(x => x.Event.Id)
            .Select(x => new DisputeEventDto(
                x.Event.Id,
                x.Event.FromStatus,
                x.Event.ToStatus,
                x.Actor.DisplayName,
                x.Event.ActorRole,
                x.Event.Note,
                x.Event.OccurredAt))
            .ToListAsync(ct);

        return new DisputeDetailDto(
            header.Id,
            header.Status,
            header.Reason,
            header.Description,
            header.ResolutionNote,
            header.CreatedAt,
            header.UpdatedAt,
            header.CustomerName,
            header.Transaction,
            history,
            Dispute.AllowedActionsFor(header.Status, user.Role));
    }

    public Task<DisputeDetailDto?> WithdrawAsync(Guid id, CurrentUser customer, CancellationToken ct) =>
        ChangeStatusAsync(id, customer, d => d.Withdraw(customer.Id, clock.GetUtcNow()), ct);

    public Task<DisputeDetailDto?> StartReviewAsync(Guid id, CurrentUser agent, CancellationToken ct) =>
        ChangeStatusAsync(id, agent, d => d.StartReview(agent.Id, clock.GetUtcNow()), ct);

    public Task<DisputeDetailDto?> ResolveAsync(Guid id, ResolveDisputeRequest request, CurrentUser agent, CancellationToken ct)
    {
        if (request.Outcome is not { } outcome)
        {
            throw new DomainException("outcome_required", "Choose whether the dispute is upheld or rejected.");
        }

        return ChangeStatusAsync(id, agent, d => d.Resolve(outcome, request.Note, agent.Id, clock.GetUtcNow()), ct);
    }

    /// <summary>
    /// Loads the dispute (scoped to the caller), applies the domain operation and saves. The xmin row
    /// version turns a lost update into a 409 instead of silently overwriting.
    /// </summary>
    private async Task<DisputeDetailDto?> ChangeStatusAsync(
        Guid id, CurrentUser user, Action<Dispute> change, CancellationToken ct)
    {
        var dispute = await VisibleTo(user, db.Disputes).SingleOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null)
        {
            return null;
        }

        change(dispute);
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, user, ct);
    }

    /// <summary>Pairs each dispute with its transaction and customer (two inner joins, one query).</summary>
    private IQueryable<DisputeRow> WithTransactionAndCustomer(IQueryable<Dispute> disputes) =>
        disputes
            .Join(db.Transactions, d => d.TransactionId, t => t.Id, (d, t) => new { Dispute = d, Transaction = t })
            .Join(
                db.Users,
                x => x.Dispute.CustomerId,
                c => c.Id,
                (x, c) => new DisputeRow { Dispute = x.Dispute, Transaction = x.Transaction, Customer = c });

    // An object initializer (not a constructor) so EF Core can translate OrderBy/Select over its members.
    private sealed class DisputeRow
    {
        public required Dispute Dispute { get; init; }

        public required Transaction Transaction { get; init; }

        public required User Customer { get; init; }
    }

    /// <summary>Customers only ever see their own disputes; agents see all.</summary>
    private static IQueryable<Dispute> VisibleTo(CurrentUser user, IQueryable<Dispute> disputes) =>
        user.IsAgent ? disputes : disputes.Where(d => d.CustomerId == user.Id);
}
