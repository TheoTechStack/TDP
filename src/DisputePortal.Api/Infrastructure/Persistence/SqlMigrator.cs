using System.Reflection;
using Npgsql;

namespace DisputePortal.Api.Infrastructure.Persistence;

/// <summary>
/// Applies the embedded <c>Migrations/*.sql</c> scripts in name order, once each, inside a single
/// transaction guarded by an advisory lock, so several instances starting together cannot race.
/// </summary>
public sealed class SqlMigrator(DatabaseConnectionFactory connections, ILogger<SqlMigrator> logger)
{
    private const long AdvisoryLockKey = 7_420_001;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var scripts = LoadScripts();

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, tx, $"SELECT pg_advisory_xact_lock({AdvisoryLockKey});", cancellationToken);
        await ExecuteAsync(
            connection,
            tx,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    text        PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """,
            cancellationToken);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations;", connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        foreach (var (version, sql) in scripts)
        {
            if (applied.Contains(version))
            {
                continue;
            }

            logger.LogInformation("Applying migration {Version}", version);
            await ExecuteAsync(connection, tx, sql, cancellationToken);

            await using var record = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@v);", connection, tx);
            record.Parameters.AddWithValue("v", version);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static List<(string Version, string Sql)> LoadScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string marker = ".Migrations.";

        return assembly.GetManifestResourceNames()
            .Where(n => n.Contains(marker, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var file = name[(name.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
                return (Version: Path.GetFileNameWithoutExtension(file), Sql: reader.ReadToEnd());
            })
            .ToList();
    }
}
