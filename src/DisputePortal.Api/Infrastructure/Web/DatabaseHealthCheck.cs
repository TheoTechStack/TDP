using DisputePortal.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisputePortal.Api.Infrastructure.Web;

internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to PostgreSQL.");
}
