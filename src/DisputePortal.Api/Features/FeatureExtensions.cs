using DisputePortal.Api.Features.Auth;
using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Features.Transactions;

namespace DisputePortal.Api.Features;

internal static class FeatureExtensions
{
    /// <summary>Feature services (vertical slices: each feature owns its controller, service and contracts).</summary>
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<DisputeService>();

        return services;
    }
}
