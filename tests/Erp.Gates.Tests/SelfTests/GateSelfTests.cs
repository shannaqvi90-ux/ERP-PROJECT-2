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
/// <summary>Every test class whose environment loads <see cref="LeakyModule"/>: its planted static
/// state is shared by the whole test process, so these classes run one after another, never side
/// by side (one environment's tenant A filling the static cache first would decide another's
/// self-test).</summary>
[CollectionDefinition(Name)]
public sealed class LeakyModuleCollection
{
    public const string Name = "Leaky module (process-wide planted state)";
}

[Collection(LeakyModuleCollection.Name)]
public sealed class GateSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    [Fact]
    public async Task The_HTTP_attack_catches_planted_header_route_and_body_leaks()
    {
        // Tenant B's warm-up must be the first to fill the planted static cache (bug 9), whatever
        // another self-test environment of this process left in it.
        LeakyModule.ResetProcessState();
        var report = await IsolationAttack.RunAsync(fixture.Env);
        foreach (var leak in report.Leaks.Where(l => !l.Contains("/api/leaky/", StringComparison.Ordinal)))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unexpected: {leak}");
        }
        foreach (var group in report.Leaks.GroupBy(l => System.Text.RegularExpressions.Regex.Match(l, @"/api/leaky/[a-z-]+").Value).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"leaks on {group.Key}: {group.Count()}");
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

        // The tenant each statement runs under, judged by the value it sets (critic p00 round 4):
        // the X-Acting-For switch, and the units of work bound to a tenant the client chose, ran SQL
        // under tenant B in requests signed in as tenant A.
        foreach (var name in new[] { "leaky.acting", "leaky.byHeader", "leaky.byRoute", "leaky.report", "leaky.silent" })
        {
            Assert.Contains(report.TenantValueViolations, v => v.Contains($"endpoint:{name})", StringComparison.Ordinal) && v.Contains("SQL ran under tenant", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.TenantValueViolations, v => !v.Contains("/api/leaky/", StringComparison.Ordinal));
        // The silent switch shows no tenant B data at all: only the value it set gives it away.
        Assert.DoesNotContain(report.Leaks, l => l.Contains("/api/leaky/silent", StringComparison.Ordinal));
        // Plant T1d: a header found by enumerating the headers. Its name is never learnt, so the
        // attack never sends it; the enumeration itself is reported, with the code that did it.
        Assert.Contains(report.InputEnumerations, e => e == $"headers by {typeof(LeakyModule).FullName}");
        Assert.DoesNotContain(report.InputEnumerations, e => !e.EndsWith(typeof(LeakyModule).FullName!, StringComparison.Ordinal));
        // A pool built outside the platform: its statements cannot be judged and are reported.
        Assert.Contains(report.UnobservedStatements, u => u.StartsWith("GET /api/leaky/own-pool", StringComparison.Ordinal));
        Assert.DoesNotContain(report.UnobservedStatements, u => !u.Contains("/api/leaky/", StringComparison.Ordinal));

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

        // Write after write (critic p04 round 1, plants P1b and P1c): state a valid write leaves in
        // a captured array reaches the next valid writer of the other tenant, in a header with a
        // correct body, or inside the body. Judged in both directions.
        var a = fixture.Env.TenantA.Code;
        var b = fixture.Env.TenantB.Code;
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {a} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/theme", StringComparison.Ordinal) &&
                                           l.Contains("a response header to tenant", StringComparison.Ordinal) && l.Contains("X-Erp-Previous-Editor", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {b} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/theme", StringComparison.Ordinal) &&
                                           l.Contains("X-Erp-Previous-Editor", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {a} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/density", StringComparison.Ordinal) &&
                                           l.Contains($"response to tenant {a} contains", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {b} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/density", StringComparison.Ordinal) &&
                                           l.Contains($"response to tenant {b} contains", StringComparison.Ordinal));
        Assert.True(report.WritePairs > 0, "no write-after-write pair succeeded on both sides");
        Assert.Empty(report.WritePairBlindSpots);
        Assert.DoesNotContain(report.AttackerUnsuccessfulWrites, w => w.Contains("/api/leaky/me/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_company_attack_catches_an_endpoint_that_widens_the_company_scope()
    {
        var report = await CompanyAttack.RunAsync(fixture.Env);
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/company-names", StringComparison.Ordinal) && l.StartsWith("company X administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Leaks, l => !l.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Empty(report.Escalations);
        // Critic p02 round 2, plant C2: a create that adds the body's company to the scope before
        // writing. Only a body that passes validation reaches the write; company Y's branches change.
        Assert.Contains("tenancy.branches", report.ChangedTables);
        // The in-tenant write oracle: a company create that answers 409 for company Y's code.
        Assert.Contains(report.Oracles, o => o.Contains("POST /api/leaky/companies [code]", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Oracles, o => !o.Contains("/api/leaky/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_company_grant_check_catches_access_given_without_checking_the_caller_or_the_user()
    {
        // Critic p02 round 2: company access changed by a user holding only the access permission
        // on the Administrator, by a one-branch manager giving every branch, and on oneself.
        var result = await CompanyGrants.RunAsync(fixture.Env);
        const string planted = "PUT /api/leaky/company-access/{userId:guid}";
        Assert.Contains(planted, result.Endpoints);
        Assert.Contains("PUT /api/tenancy/access/{userId:guid}", result.Endpoints);
        Assert.Contains(result.Problems, p => p.StartsWith(planted, StringComparison.Ordinal) && p.Contains("removing the Administrator from company X: answered 200", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith(planted, StringComparison.Ordinal) && p.Contains("giving every branch of X: answered 200", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith(planted, StringComparison.Ordinal) && p.Contains("on themselves (unchanged access): answered 200", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith(planted, StringComparison.Ordinal) && p.Contains("rows changed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal) && !p.StartsWith("the tenant Administrator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_company_policy_check_catches_a_company_table_without_the_company_scope()
    {
        await using (var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "CREATE TABLE tenancy.selftest_company_unscoped (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, company_id uuid NOT NULL)");
            await DbCatalog.ExecuteAsync(owner, "CREATE TABLE tenancy.selftest_company_permissive (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, company_id uuid NOT NULL)");
            await DbCatalog.ExecuteAsync(owner, "CREATE POLICY company_scope ON tenancy.selftest_company_permissive AS PERMISSIVE FOR ALL TO PUBLIC USING (true)");
        }
        try
        {
            var (problems, _) = await G1CompanyScopeTests.PolicyProblemsAsync(fixture.Env);
            Assert.Contains(problems, p => p.StartsWith("tenancy.selftest_company_unscoped: needs exactly one company_scope policy", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("tenancy.selftest_company_permissive: company_scope must be RESTRICTIVE", StringComparison.Ordinal));
            var (tenantProblems, _) = await G1DatabaseIsolationTests.RowLevelSecurityProblemsAsync(fixture.Env);
            Assert.Contains(tenantProblems, p => p.Contains("tenancy.selftest_company_permissive: unreviewed policy", StringComparison.Ordinal));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString);
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "DROP TABLE tenancy.selftest_company_unscoped; DROP TABLE tenancy.selftest_company_permissive");
        }
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
    public async Task The_grant_bearing_record_check_catches_an_edit_that_skips_the_access_check_when_only_the_email_changes()
    {
        // Critic p03 round 2, plant P5: an e-mail-only edit of a stronger account skips the access
        // check. The edit that changes the name (the one request earlier gates sent) is refused;
        // only the single-field request finds the bypass.
        var result = await GrantBearingRecords.RunAsync(fixture.Env);
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [email changed]", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [email changed]", StringComparison.Ordinal) && p.Contains("changed: before", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid}: ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [displayName changed]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [roleIds changed]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("PUT /api/identity/users/{id:guid} [email changed]", result.FieldVariants ?? []);
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

        // A captured array whose element the lambda replaces (critic p04 round 1, plants P1b and
        // P1c): the variable itself is never reassigned, the array it holds is mutable.
        foreach (var name in new[] { "previousEditor", "previousSaver" })
        {
            Assert.Contains(running.Findings, f => f.Key == $"closure {typeof(LeakyModule).FullName}.Register.{name}" && f.Why.Contains("shared by every request of every tenant", StringComparison.Ordinal));
        }
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

/// <summary>Its own environment with the leaky module: the write-oracle check leaves tenant B's
/// addresses on tenant A's records, which the HTTP attack self-test would then read as leaks.</summary>
public sealed class LeakyWriteOracleFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = typeof(LeakyModule).AssemblyQualifiedName,
        });
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

[Collection(LeakyModuleCollection.Name)]
public sealed class WriteOracleSelfTests(LeakyWriteOracleFixture fixture) : IClassFixture<LeakyWriteOracleFixture>
{
    [Fact]
    public async Task The_write_oracle_check_catches_a_create_that_refuses_another_tenants_address()
    {
        // Critic p03 round 2, plant L4: a registry on disk answers 409 for tenant B's addresses.
        var result = await G1WriteOracle.RunAsync(fixture.Env);
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/accounts [email]: tenant A sending a value written by tenant B", StringComparison.Ordinal) && p.Contains("answered 409", StringComparison.Ordinal));
        // No product endpoint is reported (the leaky module's other plants may be: its company
        // create answers 409 for a code its own tenant already used).
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/users", result.Endpoints);
        Assert.Contains("PUT /api/leaky/members/{id:guid}", result.Endpoints);
    }
}
