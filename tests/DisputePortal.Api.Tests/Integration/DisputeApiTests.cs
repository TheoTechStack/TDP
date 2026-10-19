using System.Net;
using System.Net.Http.Json;
using DisputePortal.Api.Domain;
using DisputePortal.Api.Features;
using DisputePortal.Api.Features.Auth;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;
using Npgsql;

namespace DisputePortal.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class DisputeApiTests(ApiFactory api)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ApiFactory.Json;

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var response = await api.CreateClient().GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_returns_401_problem_with_code()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.Alice, "nope"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_credentials", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customers_only_see_their_own_transactions()
    {
        var alice = await api.CreateClientForAsync(ApiFactory.Alice);
        var bongani = await api.CreateClientForAsync(ApiFactory.Bongani);

        var bonganisTxn = (await ListTransactionsAsync(bongani)).Items[0];

        Assert.DoesNotContain((await ListTransactionsAsync(alice)).Items, t => t.Id == bonganisTxn.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/transactions/{bonganisTxn.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await alice.PostAsJsonAsync($"/api/transactions/{bonganisTxn.Id}/disputes", new RaiseDisputeRequest(DisputeReason.Duplicate, null), Json)).StatusCode);
    }

    [Fact]
    public async Task Dispute_lifecycle_is_recorded_in_history()
    {
        var customer = await api.CreateClientForAsync(ApiFactory.Bongani);
        var agent = await api.CreateClientForAsync(ApiFactory.Agent);
        var txn = await FirstDisputableAsync(customer);

        // Customer raises the dispute.
        var raised = await customer.PostAsJsonAsync(
            $"/api/transactions/{txn.Id}/disputes", new RaiseDisputeRequest(DisputeReason.IncorrectAmount, "Charged R500, receipt says R50."), Json);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var dispute = (await raised.Content.ReadFromJsonAsync<DisputeDetailDto>(Json))!;
        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.Equal(new[] { Dispute.Actions.Withdraw }, dispute.AllowedActions);

        // A second dispute on the same transaction is a conflict.
        var duplicate = await customer.PostAsJsonAsync(
            $"/api/transactions/{txn.Id}/disputes", new RaiseDisputeRequest(DisputeReason.Duplicate, null), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Customers cannot act as agents.
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsync($"/api/disputes/{dispute.Id}/review", null)).StatusCode);

        // Agent reviews and resolves.
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync($"/api/disputes/{dispute.Id}/review", null)).StatusCode);
        var resolved = await agent.PostAsJsonAsync(
            $"/api/disputes/{dispute.Id}/resolve", new ResolveDisputeRequest(DisputeOutcome.Upheld, "Merchant confirmed the error."), Json);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        // The customer sees the full, ordered history.
        var detail = (await customer.GetFromJsonAsync<DisputeDetailDto>($"/api/disputes/{dispute.Id}", Json))!;
        Assert.Equal(DisputeStatus.Upheld, detail.Status);
        Assert.Equal("Merchant confirmed the error.", detail.ResolutionNote);
        Assert.Equal(
            new[] { DisputeStatus.Submitted, DisputeStatus.UnderReview, DisputeStatus.Upheld },
            detail.History.Select(h => h.ToStatus));
        Assert.Empty(detail.AllowedActions);

        // And the transaction can no longer be disputed.
        var after = (await customer.GetFromJsonAsync<TransactionDto>($"/api/transactions/{txn.Id}", Json))!;
        Assert.False(after.CanDispute);
        Assert.Equal(DisputeStatus.Upheld, after.LatestDispute!.Status);
    }

    [Fact]
    public async Task Withdrawn_dispute_can_be_raised_again()
    {
        var customer = await api.CreateClientForAsync(ApiFactory.Bongani);
        var txn = await FirstDisputableAsync(customer);

        var first = await RaiseAsync(customer, txn.Id);
        var withdrawn = await customer.PostAsync($"/api/disputes/{first.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);

        var second = await customer.PostAsJsonAsync(
            $"/api/transactions/{txn.Id}/disputes", new RaiseDisputeRequest(DisputeReason.Unauthorised, "Not me."), Json);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var history = await customer.GetFromJsonAsync<PagedResult<DisputeListItemDto>>("/api/disputes?pageSize=100", Json);
        Assert.Equal(2, history!.Items.Count(d => d.Transaction.Id == txn.Id));
    }

    [Fact]
    public async Task Transaction_outside_the_window_cannot_be_disputed()
    {
        var customer = await api.CreateClientForAsync(ApiFactory.Bongani);
        var all = await customer.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/transactions?pageSize=100", Json);
        var old = all!.Items.First(t => DateTimeOffset.UtcNow - t.OccurredAt > DisputePolicy.DisputeWindow);

        Assert.False(old.CanDispute);
        var response = await customer.PostAsJsonAsync(
            $"/api/transactions/{old.Id}/disputes", new RaiseDisputeRequest(DisputeReason.Duplicate, null), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("dispute_window_closed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem()
    {
        var customer = await api.CreateClientForAsync(ApiFactory.Bongani);
        var txn = await FirstDisputableAsync(customer);

        var response = await customer.PostAsJsonAsync($"/api/transactions/{txn.Id}/disputes", new RaiseDisputeRequest(null, null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Agent_sees_disputes_from_all_customers_but_customers_only_their_own()
    {
        var agent = await api.CreateClientForAsync(ApiFactory.Agent);
        var bongani = await api.CreateClientForAsync(ApiFactory.Bongani);

        var agentView = await agent.GetFromJsonAsync<PagedResult<DisputeListItemDto>>("/api/disputes?pageSize=100", Json);
        var bonganiView = await bongani.GetFromJsonAsync<PagedResult<DisputeListItemDto>>("/api/disputes?pageSize=100", Json);

        Assert.Contains(agentView!.Items, d => d.CustomerName == "Alice Mokoena");
        Assert.All(bonganiView!.Items, d => Assert.Equal("Bongani Dlamini", d.CustomerName));
    }

    [Fact]
    public async Task Dispute_history_is_append_only_at_the_database_level()
    {
        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        _ = api.Server; // ensure the host (and therefore migrations + seed) has started

        await using var cmd = new NpgsqlCommand("UPDATE dispute_events SET note = 'tampered';", connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task Readiness_probe_checks_the_database()
    {
        var response = await api.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PagedResult<TransactionDto>> ListTransactionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<PagedResult<TransactionDto>>("/api/transactions?pageSize=100", Json))!;

    private static async Task<TransactionDto> FirstDisputableAsync(HttpClient client) =>
        (await ListTransactionsAsync(client)).Items.First(t => t.CanDispute);

    private static async Task<DisputeDetailDto> RaiseAsync(HttpClient client, Guid transactionId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/transactions/{transactionId}/disputes", new RaiseDisputeRequest(DisputeReason.Duplicate, null), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DisputeDetailDto>(Json))!;
    }
}
