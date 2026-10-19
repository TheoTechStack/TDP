using DisputePortal.Api.Domain;
using DisputePortal.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Transactions;

[ApiController]
[Route("api/transactions")]
[Authorize(Policy = Roles.Customer)]
[Produces("application/json")]
public sealed class TransactionsController(TransactionService transactions) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<TransactionDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<TransactionDto>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken ct) =>
        transactions.ListAsync(User.GetUserId(), page, pageSize, search, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> Get(Guid id, CancellationToken ct)
    {
        // 404 rather than 403 for other customers' transactions: don't reveal that the id exists.
        var transaction = await transactions.GetAsync(id, User.GetUserId(), ct);
        return transaction is null ? NotFound() : transaction;
    }
}
