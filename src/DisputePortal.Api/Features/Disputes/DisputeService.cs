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

        var items = await (
                from d in pageOfDisputes
                join t in db.Transactions on d.TransactionId equals t.Id
                join c in db.Users on d.CustomerId equals c.Id
                orderby d.CreatedAt descending, d.Id descending
                select new DisputeListItemDto(
                    d.Id,
                    d.Status,
                    d.Reason,
                    d.CreatedAt,
                    d.UpdatedAt,
                    c.DisplayName,
                    new TransactionBriefDto(t.Id, t.Reference, t.Merchant, t.Amount, t.Currency, t.OccurredAt)))
            .ToListAsync(ct);

        return new PagedResult<DisputeListItemDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<DisputeDetailDto?> GetAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var header = await (
                from d in VisibleTo(user, db.Disputes.AsNoTracking())
                where d.Id == id
                join t in db.Transactions on d.TransactionId equals t.Id
                join c in db.Users on d.CustomerId equals c.Id
                select new
                {
                    d.Id,
                    d.Status,
                    d.Reason,
                    d.Description,
                    d.ResolutionNote,
                    d.CreatedAt,
                    d.UpdatedAt,
                    CustomerName = c.DisplayName,
                    Transaction = new TransactionBriefDto(t.Id, t.Reference, t.Merchant, t.Amount, t.Currency, t.OccurredAt),
                })
            .SingleOrDefaultAsync(ct);

        if (header is null)
        {
            return null;
        }

        var history = await (
                from e in db.DisputeEvents.AsNoTracking()
                where e.DisputeId == id
                join u in db.Users on e.ActorId equals u.Id
                orderby e.OccurredAt, e.Id
                select new DisputeEventDto(e.Id, e.FromStatus, e.ToStatus, u.DisplayName, e.ActorRole, e.Note, e.OccurredAt))
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

    /// <summary>Customers only ever see their own disputes; agents see all.</summary>
    private static IQueryable<Dispute> VisibleTo(CurrentUser user, IQueryable<Dispute> disputes) =>
        user.IsAgent ? disputes : disputes.Where(d => d.CustomerId == user.Id);
}
