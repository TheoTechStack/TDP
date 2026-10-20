using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisputePortal.Api.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace DisputePortal.Api.Tests.Integration;

/// <summary>
/// Hosts the real API against a throwaway PostgreSQL container, with migrations and demo seed applied
/// exactly as in production.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Alice = "alice@demo.test";
    public const string Bongani = "bongani@demo.test";
    public const string Agent = "agent@demo.test";
    /// <summary>Test-only value fed in as configuration, exactly as production gets it from Secrets Manager.</summary>
    public const string Password = "integration-test-demo-password";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef"); // gitleaks:allow (test-only key)
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:DemoPassword", Password);
        builder.UseSetting("RateLimiting:LoginPermitPerMinute", "1000");
    }

    public async Task<HttpClient> CreateClientForAsync(string email)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password), Json);
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
