using DisputePortal.Api.Domain.Repositories;
using DisputePortal.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DisputePortal.Api.Infrastructure.Persistence;

internal static class PersistenceExtensions
{
    /// <summary>PostgreSQL via EF Core, the unit of work, repositories, migrations and demo seeding.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DatabaseConnectionFactory>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<DatabaseConnectionFactory>().ConnectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<SqlMigrator>();
        services.AddScoped<DemoDataSeeder>();
        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Applies pending migrations and, when enabled, seeds demo data. Runs before the app takes traffic;
    /// both steps take a PostgreSQL advisory lock, so several instances starting together are safe.
    /// </summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();

        if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            await scope.ServiceProvider.GetRequiredService<SqlMigrator>().MigrateAsync(cancellationToken);
        }

        if (scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value.Enabled)
        {
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
        }
    }
}
