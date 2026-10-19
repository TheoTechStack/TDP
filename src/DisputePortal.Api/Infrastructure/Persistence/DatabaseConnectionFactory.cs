using Npgsql;

namespace DisputePortal.Api.Infrastructure.Persistence;

/// <summary>
/// Resolves the PostgreSQL connection string. Locally a full <c>ConnectionStrings:Default</c> is used;
/// in AWS the parts arrive separately (<c>Database:Host</c>, <c>Database:Password</c> from Secrets
/// Manager, ...) so no secret is ever baked into a connection string in configuration.
/// </summary>
public sealed class DatabaseConnectionFactory(IConfiguration configuration)
{
    private readonly Lazy<string> _connectionString = new(() => Build(configuration));

    public string ConnectionString => _connectionString.Value;

    public NpgsqlConnection Create() => new(ConnectionString);

    private static string Build(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var db = configuration.GetSection("Database");
        var host = db["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "Database is not configured. Set ConnectionStrings:Default or Database:Host/Name/Username/Password.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(db["Port"], out var port) ? port : 5432,
            Database = db["Name"] ?? "disputes",
            Username = db["Username"],
            Password = db["Password"],
            SslMode = Enum.TryParse<SslMode>(db["SslMode"], ignoreCase: true, out var ssl) ? ssl : SslMode.Prefer,
        };

        return builder.ConnectionString;
    }
}
