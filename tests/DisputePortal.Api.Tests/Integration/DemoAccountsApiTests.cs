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
    public async Task Demo_accounts_are_listed_without_any_password()
    {
        var response = await api.CreateClient().GetAsync("/api/auth/demo-accounts");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ApiFactory.Alice, body);
        Assert.Contains(ApiFactory.Agent, body);
        Assert.DoesNotContain(ApiFactory.Password, body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
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
