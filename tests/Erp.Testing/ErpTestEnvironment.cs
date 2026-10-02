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
/// </summary>
public sealed class ErpTestEnvironment : IAsyncDisposable
{
    public const string Password = "Gate-Pass-2026!";

    private ErpTestEnvironment(PostgreSqlContainer container, ErpAppFactory factory, SeedPlan plan, string admin, string owner, string app)
    {
        Container = container;
        Factory = factory;
        Plan = plan;
        AdminConnectionString = admin;
        OwnerConnectionString = owner;
        AppConnectionString = app;
    }

    public PostgreSqlContainer Container { get; }
    public ErpAppFactory Factory { get; }
    public SeedPlan Plan { get; }

    /// <summary>Superuser on the ERP database (bypasses row-level security). For inspection only.</summary>
    public string AdminConnectionString { get; }
    public string OwnerConnectionString { get; }
    public string AppConnectionString { get; }

    public SeedTenant TenantA => Plan.Tenants[0];
    public SeedTenant TenantB => Plan.Tenants[1];

    /// <summary>The gate profile: tenant A (attacker) and tenant B (victim, full of canaries).</summary>
    public static Task<ErpTestEnvironment> StartGateAsync(IDictionary<string, string?>? settings = null) =>
        StartAsync(SeedPlan.Gate(NewCanary(), Password), settings);

    public static string NewCanary() => "CNRY" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5));

    public static async Task<ErpTestEnvironment> StartAsync(SeedPlan plan, IDictionary<string, string?>? settings = null)
    {
        var adminPassword = Secret();
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithUsername("postgres")
            .WithPassword(adminPassword)
            .WithDatabase("postgres")
            .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=300")
            .Build();
        await container.StartAsync();

        var server = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
        string For(string user, string password, string database) => new NpgsqlConnectionStringBuilder
        {
            Host = server.Host,
            Port = server.Port,
            Username = user,
            Password = password,
            Database = database,
            Pooling = true,
            MaxPoolSize = 100,
        }.ConnectionString;

        var adminServer = For("postgres", adminPassword, "postgres");
        var admin = For("postgres", adminPassword, "erp");
        var owner = For(DatabaseRoles.Owner, Secret(), "erp");
        var app = For(DatabaseRoles.App, Secret(), "erp");

        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Admin"] = adminServer,
            ["ConnectionStrings:Owner"] = owner,
            ["ConnectionStrings:App"] = app,
            ["Erp:RateLimits:SignInPerMinute"] = "100000",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Erp"] = "Warning",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            config[key] = value;
        }
        var factory = new ErpAppFactory(config);
        var services = factory.Services;
        await services.GetRequiredService<DatabaseBootstrap>().RunAsync();
        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        await services.GetRequiredService<SeedRunner>().RunAsync(plan);
        return new ErpTestEnvironment(container, factory, plan, admin, owner, app);
    }

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
        await Container.DisposeAsync();
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

/// <summary>Hosts the real <c>Program</c> with test connection strings.</summary>
public sealed class ErpAppFactory(IDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(_ => { });
    }
}
