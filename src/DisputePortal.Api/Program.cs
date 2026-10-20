using DisputePortal.Api.Features;
using DisputePortal.Api.Infrastructure.Persistence;
using DisputePortal.Api.Infrastructure.Security;
using DisputePortal.Api.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddPersistence(builder.Configuration)
    .AddFeatures()
    .AddWebApi()
    .AddJwtAuthentication(builder.Configuration)
    .AddLoginRateLimiting();

var app = builder.Build();

await app.MigrateAndSeedAsync();

app.UseWebApi();
app.MapWebApi();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
