using Erp.Kernel.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Erp.Kernel.Data;

/// <summary>Connection strings the platform reads from configuration.</summary>
public static class ConnectionNames
{
    /// <summary>Application role (erp_app). The only one the running web app gets.</summary>
    public const string App = "App";

    /// <summary>Owner role (erp_owner). Migrations only.</summary>
    public const string Owner = "Owner";

    /// <summary>Superuser. Bootstrap only (create roles and the database).</summary>
    public const string Admin = "Admin";
}

/// <summary>
/// Creates the database roles and the database. Runs with a superuser connection and is
/// idempotent. Passwords are (re)set from configuration each run.
/// </summary>
public sealed class DatabaseBootstrap(IConfiguration configuration, ILogger<DatabaseBootstrap> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var admin = configuration.GetConnectionString(ConnectionNames.Admin)
                    ?? throw new InvalidOperationException("ConnectionStrings:Admin is required for bootstrap.");
        var owner = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(ConnectionNames.Owner)
                    ?? throw new InvalidOperationException("ConnectionStrings:Owner is required."));
        var app = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(ConnectionNames.App)
                  ?? throw new InvalidOperationException("ConnectionStrings:App is required."));
        if (owner.Username != DatabaseRoles.Owner || app.Username != DatabaseRoles.App)
        {
            throw new InvalidOperationException($"Owner must connect as {DatabaseRoles.Owner} and the app as {DatabaseRoles.App}.");
        }
        var database = owner.Database ?? throw new InvalidOperationException("Owner connection string needs a Database.");
        if (database != app.Database)
        {
            throw new InvalidOperationException("Owner and app must use the same database.");
        }

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync(cancellationToken);
            await Exec(connection, RoleSql(DatabaseRoles.Owner, "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT", owner.Password), cancellationToken);
            await Exec(connection, RoleSql(DatabaseRoles.App, "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT", app.Password), cancellationToken);
            await Exec(connection, RoleSql(DatabaseRoles.AuthResolver, "NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT", null), cancellationToken);
            // The owner may hand function ownership to erp_auth but does not inherit its rights.
            await Exec(connection, $"GRANT {DatabaseRoles.AuthResolver} TO {DatabaseRoles.Owner} WITH INHERIT FALSE, SET TRUE", cancellationToken);

            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", database);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
            {
                logger.LogInformation("Creating database {Database}", database);
                await Exec(connection, $"CREATE DATABASE {TenantSql.Identifier(database)} OWNER {DatabaseRoles.Owner} ENCODING 'UTF8' TEMPLATE template0", cancellationToken);
            }
            await Exec(connection, $"ALTER DATABASE {TenantSql.Identifier(database)} OWNER TO {DatabaseRoles.Owner}", cancellationToken);
            await Exec(connection, $"REVOKE ALL ON DATABASE {TenantSql.Identifier(database)} FROM PUBLIC", cancellationToken);
            await Exec(connection, $"GRANT CONNECT, TEMPORARY ON DATABASE {TenantSql.Identifier(database)} TO {DatabaseRoles.App}", cancellationToken);
        }

        var inDatabase = new NpgsqlConnectionStringBuilder(admin) { Database = database };
        await using (var connection = new NpgsqlConnection(inDatabase.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await Exec(connection, "REVOKE ALL ON SCHEMA public FROM PUBLIC", cancellationToken);
            await Exec(connection, $"GRANT USAGE ON SCHEMA public TO {DatabaseRoles.Owner}, {DatabaseRoles.App}", cancellationToken);
            await Exec(connection, KernelSql.SearchLeakproof, cancellationToken);
        }
    }

    private static string RoleSql(string role, string attributes, string? password)
    {
        var pw = password is null ? "" : $" PASSWORD {Literal(password)}";
        return $"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN CREATE ROLE {role}; END IF; END $$;" +
               $"ALTER ROLE {role} WITH {attributes}{pw};";
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task Exec(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Applies the kernel's migrations, then every module's, as the owner role, then checks
/// the security invariants and refuses to continue if any table is unprotected.</summary>
public sealed class DatabaseMigrator(ModuleCatalog catalog, IConfiguration configuration, ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var owner = configuration.GetConnectionString(ConnectionNames.Owner)
                    ?? throw new InvalidOperationException("ConnectionStrings:Owner is required for migrations.");
        var contexts = new List<(Type Type, string Schema)> { (typeof(KernelDbContext), KernelDbContext.SchemaName) };
        foreach (var module in catalog.Modules)
        {
            contexts.AddRange(module.DbContexts.Select(t => (t, module.Schema)));
        }
        foreach (var (type, schema) in contexts)
        {
            await using var context = CreateOwnerContext(type, owner, schema);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            logger.LogInformation("Migrating {Context}: {Count} pending", type.Name, pending.Count);
            await context.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync(cancellationToken);
        var problems = await SecurityInvariants.FindViolationsAsync(connection, cancellationToken);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Security invariants violated after migration:\n" + string.Join("\n", problems));
        }
    }

    internal static DbContext CreateOwnerContext(Type contextType, string connectionString, string schema)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        DataRegistration.ConfigureNpgsql(builder, connectionString, schema);
        return (DbContext)Activator.CreateInstance(contextType, builder.Options, null)!;
    }
}

/// <summary>
/// Structural checks the migrator runs after every migration (a second guard; the gate suite has
/// its own independent and stricter checks): every table with a <c>tenant_id</c> column has
/// row-level security enabled and forced, and the application role cannot bypass it.
/// </summary>
public static class SecurityInvariants
{
    public static async Task<List<string>> FindViolationsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        const string tables = """
            SELECT n.nspname, c.relname, c.relrowsecurity, c.relforcerowsecurity,
                   (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped
            WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            """;
        await using (var command = new NpgsqlCommand(tables, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = $"{reader.GetString(0)}.{reader.GetString(1)}";
                if (!reader.GetBoolean(2)) problems.Add($"{name}: row-level security not enabled");
                if (!reader.GetBoolean(3)) problems.Add($"{name}: row-level security not forced");
                if (reader.GetInt64(4) != 1) problems.Add($"{name}: no tenant_isolation policy");
            }
        }
        const string role = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = @role";
        await using (var command = new NpgsqlCommand(role, connection))
        {
            command.Parameters.AddWithValue("role", DatabaseRoles.App);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                problems.Add($"role {DatabaseRoles.App} missing");
            }
            else if (reader.GetBoolean(0) || reader.GetBoolean(1))
            {
                problems.Add($"role {DatabaseRoles.App} is superuser or has BYPASSRLS");
            }
        }
        return problems;
    }
}
