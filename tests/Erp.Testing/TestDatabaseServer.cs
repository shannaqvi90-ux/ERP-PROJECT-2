using System.Collections.Concurrent;
using System.Security.Cryptography;
using Erp.Kernel.Data;
using Erp.Kernel.Seeding;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.Testing;

/// <summary>
/// One PostgreSQL server (Testcontainers) per test process, shared by every
/// <see cref="ErpTestEnvironment"/> of the process, each in a database of its own. The roles the
/// bootstrap creates (erp_owner, erp_app, erp_auth) are server-wide, so their passwords are chosen
/// once per process. Gate templates: the first gate environment of each settings combination is
/// bootstrapped, migrated and seeded into a template database, closed to connections, and every gate
/// environment with those settings is a copy of it.
/// </summary>
public sealed class TestDatabaseServer
{
    private static readonly Lazy<Task<TestDatabaseServer>> Shared = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _databases;

    private readonly ConcurrentDictionary<string, Lazy<Task<GateTemplate>>> _templates = new(StringComparer.Ordinal);
    private readonly string _adminPassword;
    private readonly string _ownerPassword;
    private readonly string _appPassword;
    private readonly string _workerPassword;

    private TestDatabaseServer(PostgreSqlContainer container, string adminPassword)
    {
        Container = container;
        _adminPassword = adminPassword;
        _ownerPassword = Secret();
        _appPassword = Secret();
        _workerPassword = Secret();
        // ./erp verify runs the tests on the Docker host's network, where the server's own address
        // is reachable: connecting there skips Docker's port-forwarding proxy, which costs a
        // process hop on every round trip. Elsewhere (a developer's machine) the published port.
        if (System.Environment.GetEnvironmentVariable("ERP_TEST_DB_DIRECT") == "1" && !string.IsNullOrEmpty(container.IpAddress))
        {
            Host = container.IpAddress;
            Port = 5432;
        }
        else
        {
            var published = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
            Host = published.Host!;
            Port = published.Port;
        }
    }

    public PostgreSqlContainer Container { get; }

    /// <summary>Address and port the environments connect to.</summary>
    public string Host { get; }

    public int Port { get; }

    /// <summary>A gate template: its database and the plan it was seeded with.</summary>
    public sealed record GateTemplate(string Database, SeedPlan Plan);

    /// <summary>This process's server, started on first use.</summary>
    public static Task<TestDatabaseServer> GetAsync() => Shared.Value;

    private static async Task<TestDatabaseServer> StartAsync()
    {
        var adminPassword = Secret();
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithUsername("postgres")
            .WithPassword(adminPassword)
            .WithDatabase("postgres")
            // Durability is irrelevant for throw-away test data, so the data directory is in memory
            // (tmpfs: round 22, owner decision #59) and fsync, synchronous commit and full-page
            // writes are off. The server and everything on it go with the test process. Only the
            // test servers: the product's own settings and the demo database are untouched.
            // Every environment of the process has its own database here, each with its own pools.
            .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=1500",
                "-c", "shared_buffers=256MB")
            .WithTmpfsMount("/var/lib/postgresql/data")
            // Docker gives a container 64 MB of /dev/shm; a parallel query at demo volume needs more
            // ("could not resize shared memory segment ... No space left on device", G1ListVolumeTests).
            // It is only a ceiling on a memory-backed mount, not memory taken up front.
            .WithCreateParameterModifier(parameters => parameters.HostConfig!.ShmSize = 1024L * 1024 * 1024)
            .Build();
        await container.StartAsync();
        return new TestDatabaseServer(container, adminPassword);
    }

    private readonly SemaphoreSlim _bootstrap = new(1, 1);

    /// <summary>
    /// The product's bootstrap for one environment's database. It creates or alters the
    /// server-wide roles, which PostgreSQL does not let two sessions do at once (a role created by
    /// both, or "tuple concurrently updated"): environments of one server take turns. Production
    /// runs it once per deployment.
    /// </summary>
    public async Task BootstrapAsync(ErpAppFactory factory)
    {
        await _bootstrap.WaitAsync();
        try
        {
            await ((DatabaseBootstrap)factory.Services.GetService(typeof(DatabaseBootstrap))!).RunAsync();
        }
        finally
        {
            _bootstrap.Release();
        }
    }

    /// <summary>A database name not used before in this process.</summary>
    public string NewDatabaseName() => $"erp_{Interlocked.Increment(ref _databases)}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";

    private string ConnectionString(string user, string password, string database) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Username = user,
            Password = password,
            Database = database,
            Pooling = true,
            MaxPoolSize = 100,
            // Inspection and set-up statements of the tests themselves (ANALYZE of 100,000 rows,
            // checksums of every table) on a saturated machine. The application role keeps
            // Npgsql's default: the product sets its own timeouts (ErpDataSources).
            CommandTimeout = user == DatabaseRoles.App ? 30 : 600,
            // Opening a connection of the tests' own set-up (bootstrap, seeding, inspection) on a
            // machine shared by several suites: Npgsql's 15 s default ran out in fixture set-up
            // while the load average was above 100, failing whole test classes before any test ran.
            Timeout = user == DatabaseRoles.App ? 15 : 120,
        }.ConnectionString;

    /// <summary>The app's configuration for <paramref name="database"/>, then the test's settings.</summary>
    public Dictionary<string, string?> Configuration(string database, IDictionary<string, string?>? settings)
    {
        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Admin"] = ConnectionString("postgres", _adminPassword, "postgres"),
            ["ConnectionStrings:Owner"] = ConnectionString(DatabaseRoles.Owner, _ownerPassword, database),
            ["ConnectionStrings:App"] = ConnectionString(DatabaseRoles.App, _appPassword, database),
            ["ConnectionStrings:Worker"] = ConnectionString(DatabaseRoles.Worker, _workerPassword, database),
            ["Erp:RateLimits:SignInPerMinute"] = "100000",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Erp"] = "Warning",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            config[key] = value;
        }
        return config;
    }

    internal ErpTestEnvironment Environment(string database, ErpAppFactory factory, SeedPlan plan) => ErpTestEnvironment.Create(this, database, factory, plan,
        ConnectionString("postgres", _adminPassword, database),
        ConnectionString(DatabaseRoles.Owner, _ownerPassword, database),
        ConnectionString(DatabaseRoles.App, _appPassword, database));

    /// <summary>
    /// The gate template for <paramref name="settings"/> (made once per process by
    /// <paramref name="seed"/>, which bootstraps, migrates and seeds the database it is given and
    /// returns the host it used).
    /// </summary>
    public async Task<GateTemplate> GateTemplateAsync(IDictionary<string, string?>? settings,
        Func<string, Task<(ErpAppFactory Host, SeedPlan Plan)>> seed)
    {
        var key = string.Join("\n", (settings ?? new Dictionary<string, string?>())
            .OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={s.Value}"));
        var lazy = _templates.GetOrAdd(key, _ => new Lazy<Task<GateTemplate>>(async () =>
        {
            var database = "tpl" + NewDatabaseName()[3..];
            var (host, plan) = await seed(database);
            await host.DisposeAsync();
            await CloseAsync(database);
            return new GateTemplate(database, plan);
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value;
    }

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _migrated = new(StringComparer.Ordinal);

    /// <summary>
    /// A template database that is bootstrapped and migrated but holds no seed (made once per process
    /// and settings combination by <paramref name="prepare"/>, which bootstraps and migrates the
    /// database it is given). Environments with their own seed plan copy it instead of running every
    /// migration again (round 24, owner decision #59 step 3); each still gets a database of its own,
    /// and seeds it itself. Environments with other settings get another template.
    /// </summary>
    public async Task<string> MigratedTemplateAsync(IDictionary<string, string?>? settings, Func<string, Task<ErpAppFactory>> prepare)
    {
        var key = string.Join("\n", (settings ?? new Dictionary<string, string?>())
            .OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={s.Value}"));
        var lazy = _migrated.GetOrAdd(key, _ => new Lazy<Task<string>>(async () =>
        {
            var database = "mig" + NewDatabaseName()[3..];
            try
            {
                var host = await prepare(database);
                await host.DisposeAsync();
            }
            catch
            {
                await DropAsync(database);
                throw;
            }
            await CloseAsync(database);
            return database;
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value;
    }

    /// <summary>No connection may reach a template while PostgreSQL copies it.</summary>
    private async Task CloseAsync(string database)
    {
        await using var admin = new NpgsqlConnection(ConnectionString("postgres", _adminPassword, "postgres"));
        await admin.OpenAsync();
        await Exec(admin, $"ALTER DATABASE {TenantSql.Identifier(database)} WITH ALLOW_CONNECTIONS false IS_TEMPLATE true");
        await using var terminate = new NpgsqlCommand("SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = @d AND pid <> pg_backend_pid()", admin);
        terminate.Parameters.AddWithValue("d", database);
        await terminate.ExecuteScalarAsync();
    }

    /// <summary>A new database <paramref name="target"/> as a copy of <paramref name="template"/>.</summary>
    public async Task CopyAsync(string template, string target)
    {
        await using var admin = new NpgsqlConnection(ConnectionString("postgres", _adminPassword, "postgres"));
        await admin.OpenAsync();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await Exec(admin, $"CREATE DATABASE {TenantSql.Identifier(target)} TEMPLATE {TenantSql.Identifier(template)} OWNER {DatabaseRoles.Owner}");
                return;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 20)
            {
                // A connection to the template that was closing (terminated above) has not gone yet.
                await Task.Delay(250 * attempt);
            }
        }
    }

    /// <summary>Drop an environment's database (its host is already disposed).</summary>
    public async Task DropAsync(string database)
    {
        try
        {
            await using var admin = new NpgsqlConnection(ConnectionString("postgres", _adminPassword, "postgres"));
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {TenantSql.Identifier(database)} WITH (FORCE)");
        }
        catch (NpgsqlException)
        {
            // The server goes with the process; a database left behind costs nothing.
        }
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync();
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
