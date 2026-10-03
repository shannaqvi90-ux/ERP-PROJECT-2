using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.G2;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

public sealed class LeakyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        SqlTrace.EnsureStarted();
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = typeof(LeakyModule).AssemblyQualifiedName,
        });
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The gates must fail meaningfully. These tests plant isolation bugs and require the gate
/// machinery to report each one, so a gate that silently stopped looking would be caught.
/// </summary>
public sealed class GateSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    [Fact]
    public async Task The_HTTP_attack_catches_planted_header_route_and_body_leaks()
    {
        var report = await IsolationAttack.RunAsync(fixture.Env);
        foreach (var leak in report.Leaks.Where(l => !l.Contains("/api/leaky/", StringComparison.Ordinal)))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unexpected: {leak}");
        }
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/by-header", StringComparison.Ordinal) && l.Contains("[TenantHeaders]", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/tenants/", StringComparison.Ordinal));
        Assert.Contains("tenancy.tenants", report.ChangedTables);
        Assert.DoesNotContain(report.Leaks, l => !l.Contains("/api/leaky/", StringComparison.Ordinal));

        // Lookups by e-mail, by an undocumented-name query parameter and by a body text field.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/lookup?email=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/report?ownerReference=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/find [body reference=", StringComparison.Ordinal));

        // An endpoint that only says whether a tenant B e-mail exists.
        Assert.Contains(report.Oracles, o => o.Contains("/api/leaky/exists?email=", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Oracles, o => !o.Contains("/api/leaky/", StringComparison.Ordinal));

        // The reviewed sign-in lookup reused by other endpoints.
        foreach (var name in new[] { "leaky.lookup", "leaky.exists", "leaky.find" })
        {
            Assert.Contains(report.LookupMisuse, m => m.Contains($"endpoint:{name}", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.LookupMisuse, m => !m.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Empty(report.UntracedFunctions);

        // Process-wide state: tenant B's activity fills a static cache and a singleton, and the
        // attack sees tenant B's data in tenant A's answers; the singleton also hands tenant A's
        // data back to tenant B.
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/cached-tenant", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/recent", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/recent", StringComparison.Ordinal) && l.Contains("attacking tenant's marker", StringComparison.Ordinal));
        Assert.True(report.VictimConcurrentRequests > 0, "tenant B never read concurrently with the attack");
        Assert.Empty(report.VictimBlindSpots);

        // The tenant comes only from the session. A header with a name nobody guesses, read by the
        // handler and used with set_config on the request's own connection (critic p00 round 2,
        // plant A-hdr): the attack sends tenant B's id in every header the app reads and the
        // trace sees the setting change from code other than the kernel's session.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/acting", StringComparison.Ordinal) && l.Contains("[header X-Acting-For: ", StringComparison.Ordinal));
        Assert.Contains(report.SettingViolations, v => v.Contains("endpoint:leaky.acting", StringComparison.Ordinal) && v.Contains(typeof(LeakyModule).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(report.SettingViolations, v => !v.Contains("/api/leaky/", StringComparison.Ordinal));

        // Units of work bound to a tenant the client chose (built outside dependency injection, or
        // the request's own session, which the kernel refuses): every binding is traced.
        foreach (var name in new[] { "leaky.byHeader", "leaky.byRoute", "leaky.byBody", "leaky.report", "leaky.guarded" })
        {
            Assert.Contains(report.BindViolations, v => v.Contains($"endpoint:{name})", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.BindViolations, v => !v.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Leaks, l => l.Contains("/api/leaky/guarded/", StringComparison.Ordinal));
        Assert.Empty(report.TraceBlindSpots);

        // A unit of work built outside dependency injection in a GET (bound to whatever tenant) is
        // not read-only: the trace reports it. The request's own session always is.
        Assert.Contains(report.WritableReads, w => w.Contains("endpoint:leaky.byRoute)", StringComparison.Ordinal));
        Assert.DoesNotContain(report.WritableReads, w => !w.Contains("/api/leaky/", StringComparison.Ordinal));

        // State captured by an endpoint lambda, handed to the next caller in a response header
        // (critic p01 round 2, plant B): judged in both directions.
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A", StringComparison.Ordinal) && l.Contains("GET /api/leaky/previous", StringComparison.Ordinal) &&
                                           l.Contains("response header contains tenant B marker", StringComparison.Ordinal) && l.Contains("X-Previous-Workspace", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/previous", StringComparison.Ordinal) && l.Contains("a response header to tenant", StringComparison.Ordinal));

        // A per-id cache on a route with an id (critic p03 round 1, plants T2 and T1): tenant B opens
        // the route with the person it created, an id the attack's sample of tenant B ids does not
        // hold. The attack replays tenant B's exact route values and has tenant B open every id it
        // is about to send, and finds tenant B's person in tenant A's answer, in a captured
        // dictionary and in a static one alike.
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A", StringComparison.Ordinal) && l.Contains("GET /api/leaky/people/", StringComparison.Ordinal) &&
                                           l.Contains("/access", StringComparison.Ordinal) && l.Contains("response contains tenant B marker", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A", StringComparison.Ordinal) && l.Contains("GET /api/leaky/people/", StringComparison.Ordinal) &&
                                           l.Contains("/card", StringComparison.Ordinal) && l.Contains("response contains tenant B marker", StringComparison.Ordinal));
        Assert.True(report.VictimRouteValuesReplayed > 0, "no route value of tenant B's own activity was replayed by the attack");
        Assert.True(report.VictimPreTouches > 0, "tenant B never opened a route with the value tenant A was about to send");
    }

    [Fact]
    public async Task The_grant_bearing_record_check_catches_a_role_delete_without_the_grant_check()
    {
        // Critic p03 round 1, plant P2: deleting a role never checks what it grants.
        var result = await GrantBearingRecords.RunAsync(fixture.Env);
        Assert.Contains(result.Problems, p => p.StartsWith("DELETE /api/leaky/roles/{id:guid}", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("DELETE /api/leaky/roles/{id:guid}", StringComparison.Ordinal) && p.Contains("changed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("DELETE /api/identity/roles/{id:guid}", result.Checked);
    }

    [Fact]
    public void The_process_state_check_catches_a_static_cache_and_a_stateful_singleton()
    {
        var inventory = ProcessState.InspectTypes(typeof(LeakyModule).GetNestedTypes().Append(typeof(LeakyModule)),
            [typeof(LeakyModule.LastListHolder)], new HashSet<Type>());
        Assert.Contains(inventory.Findings, f => f.Key == $"static {typeof(LeakyModule).FullName}.cachedTenant" && f.Why.Contains("reassigned", StringComparison.Ordinal));
        Assert.Contains(inventory.Findings, f => f.Key == $"singleton {typeof(LeakyModule).FullName}.LastListHolder.Last");

        // The running self-test app loads the planted module and registers its singleton; the
        // inventory of the running app finds both.
        var running = ProcessState.Inspect(fixture.Env.Factory);
        Assert.Contains(running.Findings, f => f.Key == $"static {typeof(LeakyModule).FullName}.cachedTenant");
        Assert.Contains(running.Findings, f => f.Key == $"singleton {typeof(LeakyModule).FullName}.LastListHolder.Last");

        // A variable captured by an endpoint lambda lives as long as the endpoint (critic p01
        // round 2, plant B): the inventory walks every endpoint's delegate to the closures it holds.
        Assert.Contains(running.Findings, f => f.Key == $"closure {typeof(LeakyModule).FullName}.Register.previousCaller" && f.Why.Contains("written inside", StringComparison.Ordinal));
        Assert.True(running.ClosuresInspected > 0, "no endpoint closure was inspected");
        Assert.True(running.DelegateObjectsWalked > running.EndpointsWalked, "the endpoint delegate walk reached nothing beyond the delegates");
    }

    [Fact]
    public async Task The_grant_escalation_check_catches_an_endpoint_that_grants_any_role()
    {
        var result = await GrantEscalation.RunAsync(fixture.Env);
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("Administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/users", result.Checked);
    }

    [Fact]
    public async Task The_permission_checks_catch_a_write_guarded_by_a_read_permission_and_a_read_that_writes()
    {
        // Critic p00 round 2, plant P2: a POST that reactivates users while declaring a read permission.
        var result = ReadPermissionWrites.Check(EndpointInventory.From(fixture.Env.Factory.Services));
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/users/{id:guid}/reactivate ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));

        // A GET that writes: the database refuses inside the read-only transaction, nothing changes.
        await using var owner = new NpgsqlConnection(fixture.Env.AdminConnectionString);
        await owner.OpenAsync();
        const string name = "SELECT name_en FROM tenancy.tenants WHERE id = @t";
        var before = await DbCatalog.ScalarAsync<string>(owner, name, ("t", fixture.Env.TenantA.Id));
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        using var response = await admin.GetAsync("/api/leaky/touch");
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, await DbCatalog.ScalarAsync<string>(owner, name, ("t", fixture.Env.TenantA.Id)));
    }

    [Fact]
    public async Task The_database_check_catches_a_unique_index_across_tenants()
    {
        // Critic p03 round 1, plant U: e-mail unique across the platform.
        await using (var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "CREATE UNIQUE INDEX selftest_users_email_global ON identity.users (email_normalized)");
        }
        try
        {
            var (problems, _) = await G1UniqueIndexTests.ProblemsAsync(fixture.Env);
            Assert.Contains(problems, p => p.StartsWith("identity.users.selftest_users_email_global is unique across every tenant", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, p => !p.Contains("selftest_", StringComparison.Ordinal));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString);
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "DROP INDEX identity.selftest_users_email_global");
        }
    }

    [Fact]
    public async Task The_database_check_catches_a_table_without_row_level_security()
    {
        await using (var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "CREATE TABLE tenancy.selftest_unprotected (id uuid PRIMARY KEY, tenant_id uuid NOT NULL)");
        }
        try
        {
            var (problems, _) = await G1DatabaseIsolationTests.RowLevelSecurityProblemsAsync(fixture.Env);
            Assert.Contains(problems, p => p.Contains("tenancy.selftest_unprotected: row-level security is not enabled", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.Contains("tenancy.selftest_unprotected: needs exactly one tenant_isolation policy", StringComparison.Ordinal));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString);
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "DROP TABLE tenancy.selftest_unprotected");
        }
    }
}
