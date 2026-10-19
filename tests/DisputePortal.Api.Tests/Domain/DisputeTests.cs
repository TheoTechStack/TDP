using DisputePortal.Api.Domain;

namespace DisputePortal.Api.Tests.Domain;

public sealed class DisputeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();

    private static Transaction TransactionDaysAgo(double days) =>
        new(Guid.NewGuid(), CustomerId, "TXN-1", "Takealot.com", "Online shopping", 499.99m, "ZAR", Now.AddDays(-days));

    private static Dispute NewDispute() =>
        Dispute.Raise(TransactionDaysAgo(5), null, DisputeReason.Duplicate, "Charged twice", Now);

    [Fact]
    public void Raise_creates_submitted_dispute_with_first_history_entry()
    {
        var dispute = NewDispute();

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        var entry = Assert.Single(dispute.Events);
        Assert.Null(entry.FromStatus);
        Assert.Equal(DisputeStatus.Submitted, entry.ToStatus);
        Assert.Equal(Roles.Customer, entry.ActorRole);
    }

    [Fact]
    public void Raise_outside_dispute_window_is_rejected()
    {
        var old = TransactionDaysAgo(DisputePolicy.DisputeWindowDays + 1);

        var ex = Assert.Throws<DomainException>(() => Dispute.Raise(old, null, DisputeReason.Duplicate, null, Now));

        Assert.Equal("dispute_window_closed", ex.Code);
        Assert.Equal(DomainErrorKind.RuleViolation, ex.Kind);
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted)]
    [InlineData(DisputeStatus.UnderReview)]
    [InlineData(DisputeStatus.Upheld)]
    [InlineData(DisputeStatus.Rejected)]
    public void Raise_when_transaction_already_disputed_is_a_conflict(DisputeStatus existing)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Dispute.Raise(TransactionDaysAgo(1), existing, DisputeReason.Duplicate, null, Now));

        Assert.Equal("already_disputed", ex.Code);
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Raise_is_allowed_again_after_a_withdrawn_dispute()
    {
        var dispute = Dispute.Raise(TransactionDaysAgo(1), DisputeStatus.Withdrawn, DisputeReason.Duplicate, null, Now);

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
    }

    [Fact]
    public void Raise_with_reason_other_requires_a_description()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Dispute.Raise(TransactionDaysAgo(1), null, DisputeReason.Other, "   ", Now));

        Assert.Equal("description_required", ex.Code);
    }

    [Fact]
    public void Full_lifecycle_records_every_transition_in_order()
    {
        var dispute = NewDispute();

        dispute.StartReview(AgentId, Now.AddHours(1));
        dispute.Resolve(DisputeOutcome.Upheld, "Refund issued", AgentId, Now.AddHours(2));

        Assert.Equal(DisputeStatus.Upheld, dispute.Status);
        Assert.Equal("Refund issued", dispute.ResolutionNote);
        Assert.Equal(
            new[] { DisputeStatus.Submitted, DisputeStatus.UnderReview, DisputeStatus.Upheld },
            dispute.Events.Select(e => e.ToStatus));
        Assert.Equal(Now.AddHours(2), dispute.UpdatedAt);
    }

    [Fact]
    public void Resolve_before_review_is_an_invalid_transition()
    {
        var dispute = NewDispute();

        var ex = Assert.Throws<DomainException>(() => dispute.Resolve(DisputeOutcome.Rejected, "No", AgentId, Now));

        Assert.Equal("invalid_status_transition", ex.Code);
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Resolve_requires_a_note()
    {
        var dispute = NewDispute();
        dispute.StartReview(AgentId, Now);

        var ex = Assert.Throws<DomainException>(() => dispute.Resolve(DisputeOutcome.Rejected, " ", AgentId, Now));

        Assert.Equal("resolution_note_required", ex.Code);
        Assert.Equal(DisputeStatus.UnderReview, dispute.Status);
    }

    [Fact]
    public void Decided_dispute_cannot_be_withdrawn()
    {
        var dispute = NewDispute();
        dispute.StartReview(AgentId, Now);
        dispute.Resolve(DisputeOutcome.Rejected, "Merchant provided proof of delivery", AgentId, Now);

        Assert.Throws<DomainException>(() => dispute.Withdraw(CustomerId, Now));
    }

    [Theory]
    [InlineData(DisputeStatus.Submitted, Roles.Customer, new[] { Dispute.Actions.Withdraw })]
    [InlineData(DisputeStatus.UnderReview, Roles.Customer, new[] { Dispute.Actions.Withdraw })]
    [InlineData(DisputeStatus.Upheld, Roles.Customer, new string[0])]
    [InlineData(DisputeStatus.Submitted, Roles.Agent, new[] { Dispute.Actions.StartReview })]
    [InlineData(DisputeStatus.UnderReview, Roles.Agent, new[] { Dispute.Actions.Resolve })]
    [InlineData(DisputeStatus.Rejected, Roles.Agent, new string[0])]
    public void Allowed_actions_follow_the_workflow(DisputeStatus status, string role, string[] expected)
    {
        Assert.Equal(expected, Dispute.AllowedActionsFor(status, role));
    }
}
