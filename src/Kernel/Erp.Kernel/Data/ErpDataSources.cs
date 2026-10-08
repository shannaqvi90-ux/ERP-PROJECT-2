using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Erp.Kernel.Data;

/// <summary>
/// Watches the application role's pools as the platform builds them: tracing enrichment, metrics or
/// diagnostics (for example OpenTelemetry, or the isolation gate's capture of the values each
/// statement sets). Registered in dependency injection; production registers none. An observer
/// configures the builder before the pool is built and may not change the role or host it
/// connects to (<see cref="ErpDataSources"/> refuses).
/// </summary>
public interface IDataSourceObserver
{
    void Configure(NpgsqlDataSourceBuilder builder);
}

/// <summary>
/// The application role's connection pools. Requests use the ordinary one (Npgsql's 30-second
/// command timeout: a request that runs longer is a fault). Bulk work (seeding, imports) gets its
/// own pool whose every command, <c>COPY</c> and EF statement inherits a long timeout from the
/// connection string, so a seeder or importer can never fall back to the 30-second default by
/// forgetting to set one on a command it creates. On a busy machine one 100,000-row statement
/// with its row-level security checks and audit rows runs for minutes.
/// </summary>
public static class ErpDataSources
{
    /// <summary>Configuration key: command timeout in seconds of bulk units of work.</summary>
    public const string BulkCommandTimeoutSetting = "Erp:Bulk:CommandTimeoutSeconds";

    /// <summary>Default bulk command timeout: one hour. Longer than any seed or import step has
    /// taken on a saturated 4-CPU machine (about 4 minutes for 100,000 users), yet finite so a
    /// statement that hangs still fails.</summary>
    public const int DefaultBulkCommandTimeoutSeconds = 3600;

    /// <summary>The smallest bulk timeout the platform accepts: lower values are configuration
    /// mistakes that bring back load-dependent failures.</summary>
    public const int MinimumBulkCommandTimeoutSeconds = 600;

    /// <summary>Statements each pooled connection keeps prepared (least recently used go first).</summary>
    public const int AutoPreparedStatements = 256;

    /// <summary>The application role's pool for requests.</summary>
    public static NpgsqlDataSource BuildApp(IConfiguration configuration) => BuildApp(configuration, []);

    /// <summary>The application role's pool for requests, configured by each observer.</summary>
    public static NpgsqlDataSource BuildApp(IConfiguration configuration, IEnumerable<IDataSourceObserver> observers) =>
        Build(AppConnectionString(configuration, "erp-app", null), observers);

    /// <summary>The application role's pool for bulk work (seeding, imports).</summary>
    public static NpgsqlDataSource BuildBulk(IConfiguration configuration) => BuildBulk(configuration, []);

    /// <summary>The application role's pool for bulk work, configured by each observer.</summary>
    public static NpgsqlDataSource BuildBulk(IConfiguration configuration, IEnumerable<IDataSourceObserver> observers) =>
        Build(AppConnectionString(configuration, "erp-bulk", BulkCommandTimeoutSeconds(configuration)), observers);

    private static NpgsqlDataSource Build(string connectionString, IEnumerable<IDataSourceObserver> observers)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        foreach (var observer in observers)
        {
            observer.Configure(builder);
        }
        var built = builder.ConnectionStringBuilder;
        if (built.Username != DatabaseRoles.App || built.Host != new NpgsqlConnectionStringBuilder(connectionString).Host)
        {
            throw new InvalidOperationException("An observer may watch the application role's pool, not change whom it connects as or where.");
        }
        return builder.Build();
    }

    /// <summary>The configured bulk command timeout (validated).</summary>
    public static int BulkCommandTimeoutSeconds(IConfiguration configuration)
    {
        var text = configuration[BulkCommandTimeoutSetting];
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultBulkCommandTimeoutSeconds;
        }
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ||
            seconds < MinimumBulkCommandTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"{BulkCommandTimeoutSetting} must be a whole number of seconds, at least {MinimumBulkCommandTimeoutSeconds}; it is '{text}'.");
        }
        return seconds;
    }

    private static string AppConnectionString(IConfiguration configuration, string applicationName, int? commandTimeout)
    {
        var connectionString = configuration.GetConnectionString(ConnectionNames.App)
                               ?? throw new InvalidOperationException("ConnectionStrings:App is required.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Username != DatabaseRoles.App)
        {
            throw new InvalidOperationException($"The application must connect as {DatabaseRoles.App}, not '{builder.Username}'.");
        }
        builder.ApplicationName ??= applicationName;
        // Every request sends the same few statements (the session lookup, the tenant and company
        // binding, the permission check, the module's queries). Npgsql prepares a statement on a
        // pooled connection once it has run there twice, so PostgreSQL plans it once per
        // connection instead of on every request: planning a statement under the row-level
        // security policies takes milliseconds, running it a fraction of that. A statement the
        // pool has not seen lately (an unusual list filter) is simply planned as before.
        if (builder.MaxAutoPrepare == 0)
        {
            builder.MaxAutoPrepare = AutoPreparedStatements;
            builder.AutoPrepareMinUsages = 2;
        }
        if (commandTimeout is { } seconds)
        {
            builder.CommandTimeout = Math.Max(builder.CommandTimeout, seconds);
        }
        return builder.ConnectionString;
    }
}
