using DisputePortal.Api.Domain;
using DisputePortal.Api.Domain.Repositories;
using DisputePortal.Api.Features;
using DisputePortal.Api.Features.Disputes;
using Moq;

namespace DisputePortal.Api.Tests.Features;

public sealed class DisputeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDisputeRepository> _disputes = new(MockBehavior.Strict);
    private readonly Mock<ITransactionRepository> _transactions = new(MockBehavior.Strict);
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Strict);

    private readonly CurrentUser _customer = new(Guid.NewGuid(), Roles.Customer);
    private readonly CurrentUser _agent = new(Guid.NewGuid(), Roles.Agent);

    private DisputeService CreateService() =>
        new(_disputes.Object, _transactions.Object, _users.Object, _unitOfWork.Object, new FixedTimeProvider(Now));

    private Transaction CustomerTransaction() =>
        new(Guid.NewGuid(), _customer.Id, "TXN-1", "Takealot.com", "Online shopping", 499.99m, "ZAR", Now.AddDays(-3));

    [Fact]
    public async Task Raise_returns_null_and_saves_nothing_when_the_transaction_is_not_the_customers()
    {
        var transactionId = Guid.NewGuid();
        _transactions
            .Setup(r => r.GetForCustomerAsync(transactionId, _customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction?)null);

        var result = await CreateService().RaiseAsync(
            transactionId, new RaiseDisputeRequest(DisputeReason.Duplicate, null), _customer, CancellationToken.None);

        Assert.Null(result);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Raise_adds_a_submitted_dispute_commits_once_and_returns_its_detail()
    {
        var transaction = CustomerTransaction();
        Dispute? added = null;

        _transactions
            .Setup(r => r.GetForCustomerAsync(transaction.Id, _customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _transactions
            .Setup(r => r.GetManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Transaction> { [transaction.Id] = transaction });
        _disputes
            .Setup(r => r.GetLatestByTransactionAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Dispute>());
        _disputes
            .Setup(r => r.Add(It.IsAny<Dispute>()))
            .Callback<Dispute>(d => added = d);
        _disputes
            .Setup(r => r.GetWithHistoryAsync(It.IsAny<Guid>(), _customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => added);
        _users
            .Setup(r => r.GetDisplayNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [_customer.Id] = "Alice" });
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateService().RaiseAsync(
            transaction.Id, new RaiseDisputeRequest(DisputeReason.Duplicate, "Charged twice"), _customer, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DisputeStatus.Submitted, result.Status);
        Assert.Equal("Alice", result.CustomerName);
        Assert.Equal(new[] { Dispute.Actions.Withdraw }, result.AllowedActions);
        Assert.Single(result.History);
        _disputes.Verify(r => r.Add(It.IsAny<Dispute>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Raise_on_a_transaction_with_an_open_dispute_is_a_conflict_and_saves_nothing()
    {
        var transaction = CustomerTransaction();
        var existing = Dispute.Raise(transaction, null, DisputeReason.Duplicate, null, Now.AddDays(-1));

        _transactions
            .Setup(r => r.GetForCustomerAsync(transaction.Id, _customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _disputes
            .Setup(r => r.GetLatestByTransactionAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Dispute> { [transaction.Id] = existing });

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateService().RaiseAsync(
            transaction.Id, new RaiseDisputeRequest(DisputeReason.Unauthorised, null), _customer, CancellationToken.None));

        Assert.Equal("already_disputed", ex.Code);
        _disputes.Verify(r => r.Add(It.IsAny<Dispute>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Withdraw_is_scoped_to_the_customer_and_returns_null_for_someone_elses_dispute()
    {
        var disputeId = Guid.NewGuid();
        _disputes
            .Setup(r => r.GetForUpdateAsync(disputeId, _customer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dispute?)null);

        var result = await CreateService().WithdrawAsync(disputeId, _customer, CancellationToken.None);

        Assert.Null(result);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Customers_list_only_their_own_disputes_and_agents_list_all()
    {
        var empty = new Page<Dispute>([], 0);
        _disputes
            .Setup(r => r.ListAsync(It.IsAny<Guid?>(), null, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);
        _transactions
            .Setup(r => r.GetManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Transaction>());
        _users
            .Setup(r => r.GetDisplayNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var service = CreateService();
        await service.ListAsync(_customer, null, null, null, CancellationToken.None);
        await service.ListAsync(_agent, null, null, null, CancellationToken.None);

        _disputes.Verify(r => r.ListAsync(_customer.Id, null, 0, 20, It.IsAny<CancellationToken>()), Times.Once);
        _disputes.Verify(r => r.ListAsync(null, null, 0, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
