using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Erp.Kernel.Data;
using Erp.Kernel.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.Testing;

/// <summary>
/// A real PostgreSQL (Testcontainers) bootstrapped, migrated and seeded exactly like production:
/// roles created by the bootstrap, migrations applied as the owner role, seed data written by the
/// application role through row-level security. The web app runs in memory and connects only as
/// the application role.
/// <para>
/// Each environment has a database of its own on one PostgreSQL server per test process
/// (<see cref="TestDatabaseServer"/>), not a server of its own: starting a container per test
/// class made the suite spend most of its time starting servers and migrating. Gate environments
/// (<see cref="StartGateAsync"/>) are copied from a template database that the same bootstrap,
/// migrations and seeding produced once per process and settings (PostgreSQL
/// <c>CREATE DATABASE … TEMPLATE</c>); every copy then runs the bootstrap and the migrator again
/// (database privileges, no pending migration, the security invariants) before it is used.
/// Environments of an explicit <see cref="SeedPlan"/> (<see cref="StartAsync"/>) are seeded in
/// their own database as before.
/// </para>
/// </summary>
public sealed class ErpTestEnvironment : IAsyncDisposable
{
    public const string Password = "Gate-Pass-2026!";

    private ErpTestEnvironment(TestDatabaseServer server, string database, ErpAppFactory factory, SeedPlan plan, string admin, string owner, string app)
    {
        Server = server;
        DatabaseName = database;
        Factory = factory;
        Plan = plan;
        AdminConnectionString = admin;
        OwnerConnectionString = owner;
        AppConnectionString = app;
    }

    private TestDatabaseServer Server { get; }

    /// <summary>The PostgreSQL server (shared by every environment of this test process).</summary>
    public PostgreSqlContainer Container => Server.Container;

    /// <summary>The port the environment connects to the server on.</summary>
    public int DatabasePort => Server.Port;

    /// <summary>This environment's database on <see cref="Container"/>.</summary>
    public string DatabaseName { get; }

    public ErpAppFactory Factory { get; }
    public SeedPlan Plan { get; }

    /// <summary>The configuration the app was started with (connection strings, test settings).</summary>
    public IReadOnlyDictionary<string, string?> Settings => Factory.Settings;

    /// <summary>
    /// A second, independent app process over the same database: its own service provider,
    /// singletons, static-free caches and endpoint closures, none of which any other caller has
    /// touched. (Static fields are shared with <see cref="Factory"/>, as both run in this test
    /// process.) The caller disposes it.
    /// </summary>
    public ErpAppFactory StartFreshProcess() => new(new Dictionary<string, string?>(Factory.Settings));

    /// <summary>Superuser on the ERP database (bypasses row-level security). For inspection only.</summary>
    public string AdminConnectionString { get; }
    public string OwnerConnectionString { get; }
    public string AppConnectionString { get; }

    public SeedTenant TenantA => Plan.Tenants[0];
    public SeedTenant TenantB => Plan.Tenants[1];

    /// <summary>The gate profile: tenant A (attacker) and tenant B (victim, full of canaries),
    /// copied from this process's gate template for <paramref name="settings"/>.</summary>
    public static async Task<ErpTestEnvironment> StartGateAsync(IDictionary<string, string?>? settings = null)
    {
        var server = await TestDatabaseServer.GetAsync();
        var template = await server.GateTemplateAsync(settings, async templateDatabase =>
        {
            var plan = SeedPlan.Gate(NewCanary(), Password);
            return (await SeedAsync(server, templateDatabase, plan, settings), plan);
        });
        var database = server.NewDatabaseName();
        await server.CopyAsync(template.Database, database);
        var factory = new ErpAppFactory(server.Configuration(database, settings));
        try
        {
            // The copy is checked like a fresh database: privileges on the database itself (not
            // copied by PostgreSQL), no pending migration and the security invariants.
            await factory.Services.GetRequiredService<DatabaseBootstrap>().RunAsync();
            await factory.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        }
        catch
        {
            await factory.DisposeAsync();
            await server.DropAsync(database);
            throw;
        }
        return server.Environment(database, factory, template.Plan);
    }

    public static string NewCanary() => "CNRY" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    /// <summary>A database of its own, bootstrapped, migrated and seeded with <paramref name="plan"/>.</summary>
    public static async Task<ErpTestEnvironment> StartAsync(SeedPlan plan, IDictionary<string, string?>? settings = null)
    {
        var server = await TestDatabaseServer.GetAsync();
        var database = server.NewDatabaseName();
        var factory = await SeedAsync(server, database, plan, settings);
        return server.Environment(database, factory, plan);
    }

    /// <summary>Bootstrap, migrate and seed <paramref name="database"/> through a new app host.</summary>
    private static async Task<ErpAppFactory> SeedAsync(TestDatabaseServer server, string database, SeedPlan plan, IDictionary<string, string?>? settings)
    {
        var factory = new ErpAppFactory(server.Configuration(database, settings));
        try
        {
            var services = factory.Services;
            await services.GetRequiredService<DatabaseBootstrap>().RunAsync();
            await services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
            await services.GetRequiredService<SeedRunner>().RunAsync(plan);
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            await server.DropAsync(database);
            throw;
        }
    }

    internal static ErpTestEnvironment Create(TestDatabaseServer server, string database, ErpAppFactory factory, SeedPlan plan, string admin, string owner, string app) =>
        new(server, database, factory, plan, admin, owner, app);

    /// <summary>An unauthenticated client that sends the X-Erp-Request header.</summary>
    public HttpClient CreateClient(bool requestHeader = true)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        if (requestHeader)
        {
            client.DefaultRequestHeaders.Add("X-Erp-Request", "1");
        }
        return client;
    }

    /// <summary>A client signed in with the session cookie.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password = Password, string? workspace = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password, workspace });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Sign-in as {email} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        return client;
    }

    /// <summary>A client authenticated with a bearer token (no cookie, no request header).</summary>
    public async Task<HttpClient> SignInWithTokenAsync(string email, string password = Password)
    {
        using var anonymous = CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new { email, password, issueToken = true });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private sealed record TokenBody(string Token);

    public string Email(SeedTenant tenant, string local) => $"{local}@{tenant.EmailDomain}";

    public async Task<NpgsqlConnection> OpenAdminAsync()
    {
        var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task<NpgsqlConnection> OpenAppAsync()
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Server.DropAsync(DatabaseName);
    }
}

/// <summary>Hosts the real <c>Program</c> with test connection strings.</summary>
public sealed class ErpAppFactory(IDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    /// <summary>The settings this host was started with.</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(settings);

    /// <summary>Errors the app logged (to explain a 5xx answer by its trace id).</summary>
    public ServerErrorLog ErrorLog { get; } = new();

    private IReadOnlyList<ServiceDescriptor>? _descriptors;

    /// <summary>Every service registration of the running app (for the process-wide state gate).</summary>
    public IReadOnlyList<ServiceDescriptor> ServiceDescriptors
    {
        get
        {
            _ = Services;
            return _descriptors ?? [];
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning).AddProvider(ErrorLog));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<RequestInputRecorder>();
            services.AddSingleton<IDataSourceObserver, StatementCapture.Observer>();
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, RequestInputRecorder.StartupFilter>();
            _descriptors = services.ToList();
        });
    }
}
