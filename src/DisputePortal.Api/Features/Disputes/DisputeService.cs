using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>
/// Dispute use cases. Business rules live on the <see cref="Dispute"/> aggregate and
/// <see cref="DisputePolicy"/>; this service loads through repositories, scopes data to the caller,
/// invokes the domain and commits through <see cref="IUnitOfWork"/>.
/// Rule violations surface as <see cref="DomainException"/> (mapped to 409/422).
/// Methods return <c>null</c> when the resource doesn't exist or the caller may not see it (→ 404).
/// </summary>
public sealed class DisputeService(
    IDisputeRepository disputes,
    ITransactionRepository transactions,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<DisputeDetailDto?> RaiseAsync(
        Guid transactionId, RaiseDisputeRequest request, CurrentUser customer, CancellationToken ct)
    {
        if (request.Reason is not { } reason)
        {
            throw new DomainException("reason_required", "Choose a reason for the dispute.");
        }

        var transaction = await transactions.GetForCustomerAsync(transactionId, customer.Id, ct);
        if (transaction is null)
        {
            return null;
        }

        var latest = await disputes.GetLatestByTransactionAsync([transactionId], ct);
        var latestStatus = latest.GetValueOrDefault(transactionId)?.Status;

        // A concurrent duplicate that slips past this check is stopped by the partial unique index
        // (ux_disputes_one_active_per_transaction) and mapped to 409 by the exception handler.
        var dispute = Dispute.Raise(transaction, latestStatus, reason, request.Description, clock.GetUtcNow());
        disputes.Add(dispute);
        await unitOfWork.SaveChangesAsync(ct);

        return await GetAsync(dispute.Id, customer, ct);
    }

    public async Task<PagedResult<DisputeListItemDto>> ListAsync(
        CurrentUser user, int? page, int? pageSize, DisputeStatus? status, CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var result = await disputes.ListAsync(CustomerScope(user), status, paging.Skip, paging.PageSize, ct);

        var transactionsById = await transactions.GetManyAsync(
            result.Items.Select(d => d.TransactionId).Distinct().ToList(), ct);
        var names = await users.GetDisplayNamesAsync(
            result.Items.Select(d => d.CustomerId).Distinct().ToList(), ct);

        var items = result.Items
            .Select(d => new DisputeListItemDto(
                d.Id,
                d.Status,
                d.Reason,
                d.CreatedAt,
                d.UpdatedAt,
                NameOf(names, d.CustomerId),
                ToBrief(transactionsById[d.TransactionId])))
            .ToList();

        return new PagedResult<DisputeListItemDto>(items, paging.Page, paging.PageSize, result.TotalCount);
    }

    public async Task<DisputeDetailDto?> GetAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var dispute = await disputes.GetWithHistoryAsync(id, CustomerScope(user), ct);
        if (dispute is null)
        {
            return null;
        }

        var transactionsById = await transactions.GetManyAsync([dispute.TransactionId], ct);
        var names = await users.GetDisplayNamesAsync(
            dispute.Events.Select(e => e.ActorId).Append(dispute.CustomerId).Distinct().ToList(), ct);

        var history = dispute.Events
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new DisputeEventDto(
                e.Id, e.FromStatus, e.ToStatus, NameOf(names, e.ActorId), e.ActorRole, e.Note, e.OccurredAt))
            .ToList();

        return new DisputeDetailDto(
            dispute.Id,
            dispute.Status,
            dispute.Reason,
            dispute.Description,
            dispute.ResolutionNote,
            dispute.CreatedAt,
            dispute.UpdatedAt,
            NameOf(names, dispute.CustomerId),
            ToBrief(transactionsById[dispute.TransactionId]),
            history,
            Dispute.AllowedActionsFor(dispute.Status, user.Role));
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
    /// Loads the dispute (scoped to the caller), applies the domain operation and commits. The xmin row
    /// version turns a lost update into a 409 instead of silently overwriting.
    /// </summary>
    private async Task<DisputeDetailDto?> ChangeStatusAsync(
        Guid id, CurrentUser user, Action<Dispute> change, CancellationToken ct)
    {
        var dispute = await disputes.GetForUpdateAsync(id, CustomerScope(user), ct);
        if (dispute is null)
        {
            return null;
        }

        change(dispute);
        await unitOfWork.SaveChangesAsync(ct);

        return await GetAsync(id, user, ct);
    }

    /// <summary>Customers only ever see their own disputes; agents see all.</summary>
    private static Guid? CustomerScope(CurrentUser user) => user.IsAgent ? null : user.Id;

    private static string NameOf(IReadOnlyDictionary<Guid, string> names, Guid userId) =>
        names.GetValueOrDefault(userId) ?? "Unknown user";

    private static TransactionBriefDto ToBrief(Transaction t) =>
        new(t.Id, t.Reference, t.Merchant, t.Amount, t.Currency, t.OccurredAt);
}
