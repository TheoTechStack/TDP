using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Disputes;

[ApiController]
[Route("api/disputes")]
[Authorize]
[Produces("application/json")]
public sealed class DisputesController(DisputeService disputes) : ControllerBase
{
    [HttpPost("/api/transactions/{transactionId:guid}/disputes")]
    [Authorize(Policy = Roles.Customer)]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DisputeDetailDto>> Raise(Guid transactionId, RaiseDisputeRequest request, CancellationToken ct)
    {
        var dispute = await disputes.RaiseAsync(transactionId, request, User.ToCurrentUser(), ct);
        return dispute is null ? NotFound() : CreatedAtAction(nameof(Get), new { id = dispute.Id }, dispute);
    }

    /// <summary>Customers get their own disputes (their history); agents get the whole queue.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DisputeListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<DisputeListItemDto>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] DisputeStatus? status, CancellationToken ct) =>
        disputes.ListAsync(User.ToCurrentUser(), page, pageSize, status, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeDetailDto>> Get(Guid id, CancellationToken ct) =>
        OkOrNotFound(await disputes.GetAsync(id, User.ToCurrentUser(), ct));

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Policy = Roles.Customer)]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeDetailDto>> Withdraw(Guid id, CancellationToken ct) =>
        OkOrNotFound(await disputes.WithdrawAsync(id, User.ToCurrentUser(), ct));

    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = Roles.Agent)]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeDetailDto>> StartReview(Guid id, CancellationToken ct) =>
        OkOrNotFound(await disputes.StartReviewAsync(id, User.ToCurrentUser(), ct));

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = Roles.Agent)]
    [ProducesResponseType<DisputeDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisputeDetailDto>> Resolve(Guid id, ResolveDisputeRequest request, CancellationToken ct) =>
        OkOrNotFound(await disputes.ResolveAsync(id, request, User.ToCurrentUser(), ct));

    private ActionResult<DisputeDetailDto> OkOrNotFound(DisputeDetailDto? dispute) =>
        dispute is null ? NotFound() : dispute;
}
