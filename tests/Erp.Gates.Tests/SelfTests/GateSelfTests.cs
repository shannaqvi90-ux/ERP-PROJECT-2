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

        // A list whose totals and group counts are remembered across tenants (critic p05 round 1,
        // plant L3): no tenant B id or text reaches tenant A, only B's numbers. The answers are
        // judged against each tenant's own rows, in both directions, and only that list is wrong.
        Assert.Contains(report.ListAnswersWrong, w => w.StartsWith("tenant B asks first, tenant A judged", StringComparison.Ordinal) &&
                                                      w.Contains("/api/leaky/people", StringComparison.Ordinal) && w.Contains("answered total", StringComparison.Ordinal));
        Assert.Contains(report.ListAnswersWrong, w => w.Contains("/api/leaky/people", StringComparison.Ordinal) && w.Contains("groupBy=language", StringComparison.Ordinal));
        Assert.Contains(report.ListAnswersWrong, w => w.StartsWith("tenant A asks first, tenant B judged", StringComparison.Ordinal) &&
                                                      w.Contains("/api/leaky/people", StringComparison.Ordinal));
        Assert.DoesNotContain(report.ListAnswersWrong, w => !w.Contains("/api/leaky/people", StringComparison.Ordinal));
        Assert.Empty(report.ListAnswersBlind);

        // The planted state changed while the tenants used the app (the list memory on the module
        // instance, reached through the module catalogue; the stateful singleton); the product's
        // did not. (The static workspace cache is filled by tenant B before the snapshot and kept,
        // so it does not change; the marker check above catches it.)
        var changes = "\n" + string.Join("\n", report.StateChanges);
        Assert.True(report.StateChanges.Any(c => c.StartsWith("singleton Erp.Kernel.Modules.ModuleCatalog._modules[", StringComparison.Ordinal) &&
                                                 c.Contains($"({typeof(LeakyModule).FullName})._totals", StringComparison.Ordinal)), "no change to the planted totals:" + changes);
        Assert.True(report.StateChanges.Any(c => c.Contains($"({typeof(LeakyModule).FullName})._groups", StringComparison.Ordinal)), "no change to the planted groups:" + changes);
        Assert.True(report.StateChanges.Any(c => c.StartsWith($"singleton {typeof(LeakyModule).FullName}.LastListHolder.Last", StringComparison.Ordinal)), "no change to the stateful singleton:" + changes);
        Assert.True(report.StateChanges.All(c => c.Contains("Leaky", StringComparison.Ordinal)), "product state changed:" + changes);
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

        // A reviewed root is not trusted for what it holds (critic p05 round 1): the planted list's
        // memory sits on the module instance, reached from the module catalogue.
        Assert.Contains(running.Findings, f => f.Key == $"reachable {typeof(LeakyModule).FullName}._totals" && f.Why.Contains("ModuleCatalog._modules", StringComparison.Ordinal));
        Assert.Contains(running.Findings, f => f.Key == $"reachable {typeof(LeakyModule).FullName}._groups");
    }

    [Fact]
    public void The_reachable_state_walk_judges_every_field_of_registration_objects()
    {
        // The shape of plant L3: a catalogue (reviewed) holding modules holding a dictionary of
        // bindings, one of which keeps a count cache; and a registration lambda that captures a
        // counter it writes.
        var planted = new PlantedCatalog();
        var result = ReachableState.Inspect([("singleton PlantedCatalog", planted)], [typeof(PlantedCatalog).Assembly], new HashSet<Type>(), new HashSet<Type> { typeof(PlantedCatalog) });
        Assert.Contains(result.Findings, f => f.Key == $"reachable {ReachableState.TypeName(typeof(PlantedBinding<>))}._counts" && f.Why.Contains("_modules[0].Bindings[", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Key == $"reachable {ReachableState.TypeName(typeof(PlantedBinding<>))}.Calls" && f.Why.Contains("reassigned", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Key.StartsWith("reachable closure ", StringComparison.Ordinal) && f.Key.EndsWith(".seen", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Key.Contains("ImmutableBinding", StringComparison.Ordinal));

        // The fingerprint sees the cache fill.
        var before = ReachableState.Fingerprint([("singleton PlantedCatalog", planted)], [typeof(PlantedCatalog).Assembly]);
        planted.Use("tenant-b-search");
        var after = ReachableState.Fingerprint([("singleton PlantedCatalog", planted)], [typeof(PlantedCatalog).Assembly]);
        var differences = ReachableState.Differences(before, after);
        // Named by path, with the type of each product object below the root (a catalogue's
        // modules are found by type, not only by position).
        Assert.Contains(differences, d => d.StartsWith($"singleton PlantedCatalog._modules[0]({ReachableState.TypeName(typeof(PlantedModule))}).Bindings[users]({ReachableState.TypeName(typeof(PlantedBinding<>))})._counts", StringComparison.Ordinal));
        // The planted lambda's captured counter and a framework dictionary's internals are not
        // reported as changes of the catalogue (only what the walk is meant to see).
        Assert.DoesNotContain(differences, d => d.Contains("_version", StringComparison.Ordinal));
    }

    private sealed class PlantedCatalog
    {
        private readonly List<PlantedModule> _modules = [new PlantedModule()];

        public void Use(string key) => _modules[0].Bindings["users"].Remember(key);
    }

    private sealed class PlantedModule
    {
        public Dictionary<string, PlantedBinding<string>> Bindings { get; } = new() { ["users"] = new PlantedBinding<string>() };

        public ImmutableBinding Clean { get; } = new("roles");

        public Action Register { get; } = Registration();

        private static Action Registration()
        {
            var seen = 0;
            return () => seen++;
        }
    }

    private sealed class PlantedBinding<T>
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _counts = new();
        public int Calls;

        public void Remember(string key)
        {
            _counts[key] = 1004;
            Calls++;
        }
    }

    private sealed record ImmutableBinding(string Key);

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
        // The planted list's own saved-view endpoints (a reader saving their own view) are the
        // planted module's too: they are reviewed for product lists, not for this one.
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal) && !p.Contains($"/api/lists/{LeakyModule.PeopleList}/views", StringComparison.Ordinal));

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
