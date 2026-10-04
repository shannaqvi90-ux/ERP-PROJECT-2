using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Erp.Kernel.Data;

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

    /// <summary>The application role's pool for requests.</summary>
    public static NpgsqlDataSource BuildApp(IConfiguration configuration) =>
        new NpgsqlDataSourceBuilder(AppConnectionString(configuration, "erp-app", null)).Build();

    /// <summary>The application role's pool for bulk work (seeding, imports).</summary>
    public static NpgsqlDataSource BuildBulk(IConfiguration configuration) =>
        new NpgsqlDataSourceBuilder(AppConnectionString(configuration, "erp-bulk", BulkCommandTimeoutSeconds(configuration))).Build();

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
        if (commandTimeout is { } seconds)
        {
            builder.CommandTimeout = Math.Max(builder.CommandTimeout, seconds);
        }
        return builder.ConnectionString;
    }
}
