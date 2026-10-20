using System.Net;
using System.Net.Http.Json;
using DisputePortal.Api.Features.Auth;
using DisputePortal.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class DemoAccountsApiTests(ApiFactory api)
{
    [Fact]
    public async Task Demo_accounts_are_listed_without_the_password_by_default()
    {
        var response = await api.CreateClient().GetAsync("/api/auth/demo-accounts");
        var raw = await response.Content.ReadAsStringAsync();
        var body = await response.Content.ReadFromJsonAsync<DemoAccountsResponse>(ApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(body!.Accounts, a => a.Email == ApiFactory.Alice);
        Assert.Contains(body.Accounts, a => a.Email == ApiFactory.Agent);
        Assert.Null(body.Password);
        Assert.DoesNotContain(ApiFactory.Password, raw);
    }

    [Fact]
    public async Task Demo_password_is_included_only_when_the_environment_opts_in()
    {
        using var factory = api.WithWebHostBuilder(b => b.UseSetting("Seed:RevealDemoPassword", "true"));

        var body = await factory.CreateClient().GetFromJsonAsync<DemoAccountsResponse>("/api/auth/demo-accounts", ApiFactory.Json);

        Assert.Equal(ApiFactory.Password, body!.Password);
    }

    [Fact]
    public async Task Demo_accounts_are_hidden_when_seeding_is_off()
    {
        using var factory = api.WithWebHostBuilder(b => b.UseSetting("Seed:Enabled", "false"));

        var response = await factory.CreateClient().GetAsync("/api/auth/demo-accounts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_configured_password_rotates_the_demo_accounts()
    {
        const string rotated = "rotated-demo-password-2026";

        try
        {
            await SeedWithPasswordAsync(rotated);

            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(ApiFactory.Agent, rotated)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(ApiFactory.Agent, ApiFactory.Password)).StatusCode);
        }
        finally
        {
            // The collection shares one database: put the original password back for the other tests.
            await SeedWithPasswordAsync(ApiFactory.Password);
        }
    }

    private async Task SeedWithPasswordAsync(string password)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var seeder = new DemoDataSeeder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            TimeProvider.System,
            Options.Create(new SeedOptions { Enabled = true, DemoPassword = password }),
            NullLogger<DemoDataSeeder>.Instance);

        await seeder.SeedAsync(CancellationToken.None);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), ApiFactory.Json);
}
