using System.Security.Claims;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Api.Features.Disputes;

public sealed record RaiseDisputeRequest(DisputeReason? Reason, string? Description);

public sealed record ResolveDisputeRequest(DisputeOutcome? Outcome, string? Note);

public sealed record TransactionBriefDto(
    Guid Id, string Reference, string Merchant, decimal Amount, string Currency, DateTimeOffset OccurredAt);

public sealed record DisputeListItemDto(
    Guid Id,
    DisputeStatus Status,
    DisputeReason Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CustomerName,
    TransactionBriefDto Transaction);

public sealed record DisputeEventDto(
    Guid Id,
    DisputeStatus? FromStatus,
    DisputeStatus ToStatus,
    string ActorName,
    string ActorRole,
    string? Note,
    DateTimeOffset OccurredAt);

public sealed record DisputeDetailDto(
    Guid Id,
    DisputeStatus Status,
    DisputeReason Reason,
    string Description,
    string? ResolutionNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CustomerName,
    TransactionBriefDto Transaction,
    IReadOnlyList<DisputeEventDto> History,
    IReadOnlyList<string> AllowedActions);

public static class DisputeEndpoints
{
    public static IEndpointRouteBuilder MapDisputeEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/transactions/{transactionId:guid}/disputes", RaiseAsync)
            .WithTags("Disputes")
            .RequireAuthorization(Roles.Customer)
            .Produces<DisputeDetailDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var group = routes.MapGroup("/api/disputes").WithTags("Disputes").RequireAuthorization();

        group.MapGet("/", ListAsync).Produces<PagedResult<DisputeListItemDto>>();
        group.MapGet("/{id:guid}", GetAsync).Produces<DisputeDetailDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/withdraw", WithdrawAsync)
            .RequireAuthorization(Roles.Customer)
            .Produces<DisputeDetailDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/review", StartReviewAsync)
            .RequireAuthorization(Roles.Agent)
            .Produces<DisputeDetailDto>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/resolve", ResolveAsync)
            .RequireAuthorization(Roles.Agent)
            .Produces<DisputeDetailDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> RaiseAsync(
        Guid transactionId,
        RaiseDisputeRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Reason is null)
        {
            errors["reason"] = ["Choose a reason for the dispute."];
        }

        if (request.Description is { Length: > DisputePolicy.MaxDescriptionLength })
        {
            errors["description"] = [$"Keep the description under {DisputePolicy.MaxDescriptionLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var customerId = principal.GetUserId();
        var transaction = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customerId, ct);
        if (transaction is null)
        {
            return TypedResults.NotFound();
        }

        var latestStatus = await db.Disputes.AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => (DisputeStatus?)d.Status)
            .FirstOrDefaultAsync(ct);

        // Domain rules throw DomainException (mapped to 409/422). A concurrent duplicate that slips past
        // this check is stopped by the partial unique index and mapped to 409 as well.
        var dispute = Dispute.Raise(transaction, latestStatus, request.Reason!.Value, request.Description, clock.GetUtcNow());
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync(ct);

        var detail = await LoadDetailAsync(db, dispute.Id, principal, ct);
        return TypedResults.Created($"/api/disputes/{dispute.Id}", detail);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        int? page,
        int? pageSize,
        DisputeStatus? status,
        CancellationToken ct)
    {
        var paging = Paging.From(page, pageSize);
        var query = Scope(db.Disputes.AsNoTracking(), principal);

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

        return TypedResults.Ok(new PagedResult<DisputeListItemDto>(items, paging.Page, paging.PageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var detail = await LoadDetailAsync(db, id, principal, ct);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    private static Task<IResult> WithdrawAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        ChangeStatusAsync(id, principal, db, ct, d => d.Withdraw(principal.GetUserId(), clock.GetUtcNow()));

    private static Task<IResult> StartReviewAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        ChangeStatusAsync(id, principal, db, ct, d => d.StartReview(principal.GetUserId(), clock.GetUtcNow()));

    private static async Task<IResult> ResolveAsync(
        Guid id,
        ResolveDisputeRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Outcome is null)
        {
            errors["outcome"] = ["Choose whether the dispute is upheld or rejected."];
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            errors["note"] = ["Explain the decision to the customer."];
        }
        else if (request.Note.Length > DisputePolicy.MaxDescriptionLength)
        {
            errors["note"] = [$"Keep the note under {DisputePolicy.MaxDescriptionLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        return await ChangeStatusAsync(
            id, principal, db, ct, d => d.Resolve(request.Outcome!.Value, request.Note, principal.GetUserId(), clock.GetUtcNow()));
    }

    /// <summary>
    /// Loads the dispute (scoped to what the caller may see), applies the domain operation and saves.
    /// The xmin row version turns a lost update into a 409 instead of silently overwriting.
    /// </summary>
    private static async Task<IResult> ChangeStatusAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct, Action<Dispute> change)
    {
        var dispute = await Scope(db.Disputes, principal).SingleOrDefaultAsync(d => d.Id == id, ct);
        if (dispute is null)
        {
            return TypedResults.NotFound();
        }

        change(dispute);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await LoadDetailAsync(db, id, principal, ct));
    }

    /// <summary>Customers only ever see their own disputes; agents see all.</summary>
    private static IQueryable<Dispute> Scope(IQueryable<Dispute> disputes, ClaimsPrincipal principal)
    {
        if (principal.IsAgent())
        {
            return disputes;
        }

        var customerId = principal.GetUserId();
        return disputes.Where(d => d.CustomerId == customerId);
    }

    private static async Task<DisputeDetailDto?> LoadDetailAsync(AppDbContext db, Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var header = await (
                from d in Scope(db.Disputes.AsNoTracking(), principal)
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
            Dispute.AllowedActionsFor(header.Status, principal.GetRole()));
    }
}
