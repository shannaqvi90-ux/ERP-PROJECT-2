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

    private readonly Lock _runs = new();
    private Task<GrantBearingRecords.Result>? _grantBearing;
    private Task<GrantEscalation.Result>? _grantEscalation;
    private Task<SetTakeover.Result>? _setTakeover;

    /// <summary>The grant-bearing record check over this environment, run once: three self-tests
    /// judge the same run, each for its own plants (three identical full runs were most of the
    /// processor time of the gate self-tests outside the attacks).</summary>
    public Task<GrantBearingRecords.Result> GrantBearingRecordsAsync()
    {
        lock (_runs) return _grantBearing ??= GrantBearingRecords.RunAsync(Env);
    }

    /// <summary>The grant escalation check over this environment, run once for the two self-tests
    /// that judge it (see <see cref="GrantBearingRecordsAsync"/>).</summary>
    public Task<GrantEscalation.Result> GrantEscalationAsync()
    {
        lock (_runs) return _grantEscalation ??= GrantEscalation.RunAsync(Env);
    }

    /// <summary>The set-based takeover check over the planted "all that match" activations, run
    /// once for the two self-tests that judge it (bugs 53 and 54, and bug 57).</summary>
    public Task<SetTakeover.Result> SetTakeoverAsync()
    {
        lock (_runs) return _setTakeover ??= SetTakeover.RunAsync(Env, userLists: ["/api/leaky/users"],
            only: e => e.Pattern.StartsWith("/api/leaky/", StringComparison.Ordinal), targets: SetTakeover.Targets.PerModule, freshAdministrator: true);
    }
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

/// <summary>The long self-tests ./erp verify runs in test processes of their own (filter on the
/// trait), so the planted static state of one never meets another's: separate processes, separate
/// statics. Run in one process (a plain <c>dotnet test</c>), they take turns in
/// <see cref="LeakyModuleCollection"/> as before.</summary>
public static class SelfTestProcess
{
    public const string Trait = "Process";
    public const string Http = "self-http";
    public const string Company = "self-company";
    public const string NonInterference = "self-noninterference";
}

[Collection(LeakyModuleCollection.Name)]
public sealed class GateSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    /// <summary>A finding about one of the leaky module's planted endpoints or planted reports.</summary>
    private static bool Planted(string finding) =>
        finding.Contains("/api/leaky/", StringComparison.Ordinal) || finding.Contains("/api/reports/run/leaky.", StringComparison.Ordinal);

    // ./erp verify runs this test, the company attack's self-test and the non-interference
    // self-test each in a test process of its own (trait Process), side by side: the planted state
    // is static, so within one process the leaky module's tests take turns.
    [Fact]
    [Trait(SelfTestProcess.Trait, SelfTestProcess.Http)]
    public async Task The_HTTP_attack_catches_planted_header_route_and_body_leaks()
    {
        // Tenant B's warm-up must be the first to fill the planted static cache (bug 9), whatever
        // another self-test environment of this process left in it.
        LeakyModule.ResetProcessState();
        // The planted lists are also judged sorted; the product's lists are judged sorted by the gate.
        var report = await IsolationAttack.RunAsync(fixture.Env, sortedLists: l => l.Key.StartsWith("leaky.", StringComparison.Ordinal));
        foreach (var leak in report.Leaks.Where(l => !l.Contains("/api/leaky/", StringComparison.Ordinal)))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unexpected: {leak}");
        }
        foreach (var oracle in report.Oracles.Where(o => !o.Contains("/api/leaky/", StringComparison.Ordinal)))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unexpected oracle: {oracle}");
        }
        foreach (var group in report.Leaks.GroupBy(l => System.Text.RegularExpressions.Regex.Match(l, @"/api/leaky/[a-z-]+").Value).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"leaks on {group.Key}: {group.Count()}");
        }
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/by-header", StringComparison.Ordinal) && l.Contains("[TenantHeaders]", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/tenants/", StringComparison.Ordinal));
        // Bug 50: a printed file kept by its download name reaches tenant A only once tenant B has
        // printed it, so the answer-shape phase (tenant B asks for every format and language
        // first) must find it.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/printed?", StringComparison.Ordinal) && l.Contains("an answer shape tenant B asked for first", StringComparison.Ordinal));
        // Bug 60 (critic p06 round 4, plant L6): a spool keyed on the columns printed reaches
        // tenant A only once tenant B printed with columns other than the default.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/spooled?", StringComparison.Ordinal) && l.Contains("columns=", StringComparison.Ordinal) &&
                                          l.Contains("an answer shape tenant B asked for first", StringComparison.Ordinal));
        Assert.Contains("tenancy.tenants", report.ChangedTables);
        Assert.DoesNotContain(report.Leaks, l => !l.Contains("/api/leaky/", StringComparison.Ordinal));

        // Lookups by e-mail, by an undocumented-name query parameter and by a body text field.
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/lookup?email=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/report?ownerReference=", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.Contains("/api/leaky/find [body reference=", StringComparison.Ordinal));

        // Exports (p06): tenant B's workspace name inside a compressed PDF content stream and a
        // zipped workbook cell, both filled by tenant B's warm-up through a static cache.
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A", StringComparison.Ordinal) && l.Contains("GET /api/leaky/export.pdf", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A", StringComparison.Ordinal) && l.Contains("GET /api/leaky/export.xlsx", StringComparison.Ordinal));

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
        // A parameter picked out of the raw query string is reported; the framework's parse of the
        // query that every report endpoint triggers is not (only the planted module may appear).
        Assert.Contains(report.InputEnumerations, e => e == $"raw query string by {typeof(LeakyModule).FullName}");
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
        // The Arabic side (critic p04 round 4, plant L1): a write that leaks the previous caller's
        // e-mail only for Arabic-Indic digits. Both tenants send every documented value back to
        // back, so the leak shows in both directions, and only with "arab".
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {a} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/digits", StringComparison.Ordinal) &&
                                           l.Contains("numerals=\"arab\"", StringComparison.Ordinal) && l.Contains($"response to tenant {a} contains", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {b} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/digits", StringComparison.Ordinal) &&
                                           l.Contains("numerals=\"arab\"", StringComparison.Ordinal));
        // Latin digits never leak (the plant's state is only read for "arab"); the attack's own
        // body values may send "arab" too and are judged as any other leak.
        Assert.DoesNotContain(report.Leaks, l => l.Contains("/api/leaky/me/digits", StringComparison.Ordinal) && l.Contains("numerals=\"latn\"", StringComparison.Ordinal));
        foreach (var leak in report.Leaks.Where(l => l.Contains("/api/leaky/me/digits", StringComparison.Ordinal)).Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"digits: {leak}");
        }
        Assert.Contains(report.EnumValuesAttacked, v => v.Contains("/api/leaky/me/digits", StringComparison.Ordinal) && v.EndsWith("numerals=\"arab\"", StringComparison.Ordinal));
        Assert.True(report.EnumVariantPairs > 0, "no write pair was sent with a documented value other than the default");
        // The Arabic side of the session (critic p04 round 4, "language=ar"): a read with no body
        // and no parameter that leaks the previous caller's e-mail only when the request runs in
        // Arabic (bug 47). Only sessions whose language is Arabic reach it: caught in both
        // directions by the Arabic administrators, and by an English administrator only in the
        // reads of the preferences write pairs (PUT /api/identity/me/preferences): from its own
        // variant with language "ar" until its own default-body write puts English back, its
        // requests run in Arabic too. That window is the reads after either tenant's variants,
        // and tenant A's reads after B's default-body write (B writes its default first, then
        // A reads, then A writes its own default).
        foreach (var leak in report.Leaks.Where(l => l.Contains("/api/leaky/me/greeting", StringComparison.Ordinal) && !l.Contains("in Arabic", StringComparison.Ordinal)).Take(20))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"greeting outside an Arabic session: {leak}");
        }
        Assert.Contains(report.Leaks, l => l.StartsWith("tenant A administrator in Arabic", StringComparison.Ordinal) &&
                                           l.Contains("GET /api/leaky/me/greeting", StringComparison.Ordinal) && l.Contains("response contains tenant B marker", StringComparison.Ordinal));
        Assert.Contains(report.Leaks, l => l.StartsWith($"tenant {b} administrator in Arabic", StringComparison.Ordinal) &&
                                           l.Contains("GET /api/leaky/me/greeting", StringComparison.Ordinal) && l.Contains($"response to tenant {b} contains", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Leaks, l => l.Contains("/api/leaky/me/greeting", StringComparison.Ordinal) && !l.Contains("in Arabic", StringComparison.Ordinal) &&
                                                 !l.Contains("variants of PUT /api/identity/me/preferences]", StringComparison.Ordinal) &&
                                                 !l.Contains("[tenant A reads after B's PUT /api/identity/me/preferences]", StringComparison.Ordinal));
        Assert.True(report.ArabicAttackRequests > 0, "tenant A sent nothing in Arabic");
        Assert.True(report.VictimArabicRequests > 0, "tenant B sent nothing in Arabic");
        Assert.True(report.ArabicWritePairs > 0, "no write pair in Arabic succeeded on both sides");
        Assert.Empty(report.ArabicBlindSpots);
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
        // A list whose pages after the first reuse the total the last first page counted, whatever
        // its tenant (critic p05 round 4, plant L6): every first page is right, so only judging the
        // keyset pages that follow, walked in lock step with the other tenant, catches it.
        foreach (var direction in new[] { "tenant B asks first, tenant A judged", "tenant A asks first, tenant B judged" })
        {
            Assert.Contains(report.ListAnswersWrong, w => w.StartsWith(direction, StringComparison.Ordinal) && w.Contains("/api/leaky/scroll", StringComparison.Ordinal) &&
                                                          w.Contains("the judged page 2 of ", StringComparison.Ordinal) && w.Contains("answered total", StringComparison.Ordinal));
        }
        // (The scroll list's sorted searches carry the planted sorted-search memo below; every other
        // first page of it is right.)
        static bool SortedSearch(string w) => w.Contains("&sort=", StringComparison.Ordinal) && w.Contains("&search=", StringComparison.Ordinal);
        Assert.DoesNotContain(report.ListAnswersWrong, w => w.Contains("/api/leaky/scroll", StringComparison.Ordinal) && w.Contains(" page 1 of ", StringComparison.Ordinal) && !SortedSearch(w));
        Assert.DoesNotContain(report.ListAnswersWrong, w => w.Contains("GET /api/leaky/scroll?take=50", StringComparison.Ordinal) && !SortedSearch(w));
        // A list whose offset (skip) pages reuse the total the last first page counted, whatever
        // its tenant, kept in a pooled scratch object (critic p05 round 5, plant L10): every first
        // page and every keyset page is right, so only judging offset pages, walked in lock step
        // with the other tenant, catches it.
        foreach (var direction in new[] { "tenant B asks first, tenant A judged", "tenant A asks first, tenant B judged" })
        {
            Assert.Contains(report.ListAnswersWrong, w => w.StartsWith(direction, StringComparison.Ordinal) && w.Contains("/api/leaky/jump", StringComparison.Ordinal) &&
                                                          w.Contains("the judged offset page 2 of ", StringComparison.Ordinal) && w.Contains("answered total", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.ListAnswersWrong, w => w.Contains("/api/leaky/jump", StringComparison.Ordinal) && !w.Contains(" offset page ", StringComparison.Ordinal));
        Assert.DoesNotContain(report.ListAnswersWrong, w => w.Contains("/api/leaky/jump", StringComparison.Ordinal) && w.Contains(" offset page 1 of ", StringComparison.Ordinal));
        // The scroll list's sorted searches reuse the total first counted for the same search,
        // filter and sort, whatever its tenant (critic p05 round 7, plant L11's behaviour): every
        // unsorted first page and every sorted first page without a search is right, so only judging
        // the queries the client sends after a header click with a search typed catches it, on the
        // first page itself, in both directions.
        foreach (var direction in new[] { "tenant B asks first, tenant A judged", "tenant A asks first, tenant B judged" })
        {
            Assert.Contains(report.ListAnswersWrong, w => w.StartsWith(direction, StringComparison.Ordinal) && w.Contains("GET /api/leaky/scroll?take=50", StringComparison.Ordinal) &&
                                                          SortedSearch(w) && w.Contains("answered total", StringComparison.Ordinal));
        }
        Assert.True(report.ListAnswerSortedQueries > 0 && report.ListAnswerSortedDiscriminating > 0, "no sorted list query was judged with different true answers");
        Assert.DoesNotContain(report.ListAnswersWrong, w => !w.Contains("/api/leaky/people", StringComparison.Ordinal) && !w.Contains("/api/leaky/scroll", StringComparison.Ordinal) &&
                                                            !w.Contains("/api/leaky/jump", StringComparison.Ordinal));
        Assert.True(report.ListAnswerOffsetPagesJudged > 0, "no offset page was judged");
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
        // The static memo of a closed generic type (plant L6's shape) is a root of its own.
        Assert.True(report.StateChanges.Any(c => c.StartsWith($"static {typeof(LeakyModule).FullName}.ScrollTotals`1[", StringComparison.Ordinal)), "no change to the generic type's static memo:" + changes);
        // The pooled scratch (plant L10's shape) sits in a framework singleton built over a planted
        // type: the holder is a root of its own and its framework internals are walked to it.
        Assert.True(report.StateChanges.Any(c => c.StartsWith($"singleton Microsoft.Extensions.ObjectPool.ObjectPool<{typeof(LeakyModule).FullName}.JumpScratch>", StringComparison.Ordinal)),
            "no change to the pooled scratch:" + changes);
        Assert.True(report.StateChanges.All(c => c.Contains("Leaky", StringComparison.Ordinal)), "product state changed:" + changes);
    }

    [Fact]
    [Trait(SelfTestProcess.Trait, SelfTestProcess.Company)]
    public async Task The_company_attack_catches_an_endpoint_that_widens_the_company_scope()
    {
        var report = await CompanyAttack.RunAsync(fixture.Env);
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/company-names", StringComparison.Ordinal) && l.StartsWith("company X administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Leaks, l => !Planted(l));
        // Critic p02 round 6: the workspace every company shares, renamed by an administrator of one company.
        var shared = report.Shared!;
        Assert.Contains("PUT /api/leaky/workspace-name [nameEn]", shared.Sources);
        Assert.Contains(shared.Failures, f => f.StartsWith("company X administrator → PUT /api/leaky/workspace-name [nameEn] on the workspace every company shares", StringComparison.Ordinal) &&
                                              f.Contains("answered 200", StringComparison.Ordinal));
        Assert.Contains("tenancy.tenants", shared.ChangedTables);
        Assert.DoesNotContain(shared.Failures, f => !f.Contains("/api/leaky/", StringComparison.Ordinal));
        // The same planted read, made with nothing but the attacker's own parameters.
        Assert.Contains(report.Leaks, l => l.StartsWith("company X administrator → GET /api/leaky/company-names", StringComparison.Ordinal) &&
                                           l.Contains("(own parameters only)", StringComparison.Ordinal));
        Assert.Empty(report.Escalations);
        // Critic p02 round 2, plant C2: a create that adds the body's company to the scope before
        // writing. Only a body that passes validation reaches the write; company Y's branches change.
        Assert.Contains("tenancy.branches", report.ChangedTables);
        // The in-tenant write oracle: a company create that answers 409 for company Y's code.
        Assert.Contains(report.Oracles, o => o.Contains("POST /api/leaky/companies [code]", StringComparison.Ordinal));
        Assert.Contains("POST /api/leaky/companies [code] <- tenancy.companies", report.WriteOracleSources);
        Assert.DoesNotContain(report.Oracles, o => !o.Contains("/api/leaky/", StringComparison.Ordinal));
    }

    [Fact]
    [Trait(SelfTestProcess.Trait, SelfTestProcess.Company)]
    public async Task The_branch_attack_catches_reads_and_writes_that_ignore_the_branch_limits()
    {
        // Critic p02 round 3, plant C3: a user limited to one branch reads and renames the
        // company's other branches through code that relies on row-level security alone.
        var report = await CompanyAttack.RunAsync(fixture.Env, CompanyAttack.Layer.Branch);
        Assert.Contains(report.Leaks, l => l.Contains("GET /api/leaky/branch-names", StringComparison.Ordinal) && l.StartsWith("branch-limited administrator", StringComparison.Ordinal));
        Assert.Contains("tenancy.branches", report.ChangedTables);
        Assert.DoesNotContain(report.Leaks, l => !Planted(l));
        // Critic p02 round 6, plants B3b, B3 and B1: reads made with nothing but the attacker's own
        // parameters (no parameters; its own company; a user of its own workspace by id) that read
        // branches past the branch limits. The marker check after the attack's writes was blind to
        // them (the attacker had stored branch Z's texts in its own records by then).
        static bool Own(string leak, string path) =>
            leak.StartsWith("branch-limited administrator → GET " + path, StringComparison.Ordinal) && leak.Contains("(own parameters only)", StringComparison.Ordinal);
        Assert.Contains(report.Leaks, l => Own(l, "/api/reports/run/leaky.branchDirectory"));
        Assert.Contains(report.Leaks, l => Own(l, "/api/reports/run/leaky.branchDirectory?format=csv"));
        Assert.Contains(report.Leaks, l => Own(l, "/api/reports/run/leaky.branchDirectory?format=pdf"));
        Assert.Contains(report.Leaks, l => Own(l, "/api/reports/run/leaky.companyBranches?company="));
        Assert.Contains(report.Leaks, l => Own(l, "/api/leaky/people/") && l.Contains("/branch-options", StringComparison.Ordinal));
        Assert.Empty(report.Escalations);
        Assert.DoesNotContain(report.Oracles, o => !o.Contains("/api/leaky/", StringComparison.Ordinal));
        // Critic p02 round 4, plant C5: a read by id that ignores the branch limits answers for
        // branch Z's id and not for an id that exists nowhere (whether or not its texts count as markers).
        Assert.Contains(report.Oracles, o => o.Contains("GET /api/leaky/branch-by-id/", StringComparison.Ordinal) && o.StartsWith("branch-limited administrator", StringComparison.Ordinal));
        // Critic p02 round 4, plant P7: a one-branch administrator renames the company every branch
        // shares; the write is refused nowhere and company X's own record changes.
        var shared = report.Shared!;
        Assert.Contains("PUT /api/leaky/company-profile/{id:guid} [legalNameEn]", shared.Sources);
        Assert.Contains(shared.Failures, f => f.Contains("PUT /api/leaky/company-profile/{id:guid} [legalNameEn]", StringComparison.Ordinal) && f.Contains("answered 200", StringComparison.Ordinal));
        Assert.Contains("tenancy.companies", shared.ChangedTables);
        Assert.DoesNotContain(shared.Failures, f => !f.Contains("/api/leaky/", StringComparison.Ordinal));
        // Critic p02 round 6: the workspace every company shares, renamed by a one-branch administrator.
        Assert.Contains(shared.Failures, f => f.Contains("PUT /api/leaky/workspace-name [nameEn] on the workspace every company shares", StringComparison.Ordinal) &&
                                              f.Contains("answered 200", StringComparison.Ordinal));
        Assert.Contains("tenancy.tenants", shared.ChangedTables);
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

        // Critic p02 round 3, plant P3: only the permission rule missing. The clerk of company X
        // alone is still refused (by the company rule), which is why the gate once passed; the
        // clerk who works in every company is not.
        const string partly = "PUT /api/leaky/company-access-partly-checked/{userId:guid}";
        Assert.Contains(partly, result.Endpoints);
        Assert.Contains(result.Problems, p => p.StartsWith(partly, StringComparison.Ordinal) &&
                                              p.Contains("who works in every company, removing the Administrator from company X: answered 200", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(partly, StringComparison.Ordinal) &&
                                                    p.Contains("by a user holding only the access permission, removing the Administrator from company X", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(partly, StringComparison.Ordinal) && p.Contains("on themselves", StringComparison.Ordinal));
        // The product's endpoint carries a version and refuses a stale one.
        Assert.DoesNotContain(result.Problems, p => p.Contains("concurrency token", StringComparison.Ordinal) || p.Contains("stale version", StringComparison.Ordinal));
    }

    /// <summary>Critic p03 round 6: a table whose own rows stay readable outside the company scope
    /// (the own-rows variant) without the RESTRICTIVE update and delete policies lets a session
    /// delete its own rows of other companies or move one into its scope; a permissive policy by
    /// one of those names widens access and is unreviewed.</summary>
    [Fact]
    public async Task The_company_policy_check_catches_own_rows_writable_outside_the_scope()
    {
        await using (var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString))
        {
            await owner.OpenAsync();
            foreach (var table in new[] { "selftest_own_rows_writable", "selftest_own_rows_permissive" })
            {
                await DbCatalog.ExecuteAsync(owner, $"CREATE TABLE tenancy.{table} (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, company_id uuid NOT NULL, user_id uuid NOT NULL)");
                await DbCatalog.ExecuteAsync(owner, $"CREATE POLICY company_scope ON tenancy.{table} AS RESTRICTIVE FOR ALL TO PUBLIC " +
                                                    "USING (erp.company_allowed(company_id) OR user_id = erp.current_actor_id()) WITH CHECK (erp.company_allowed(company_id))");
            }
            await DbCatalog.ExecuteAsync(owner, "CREATE POLICY company_scope_update ON tenancy.selftest_own_rows_permissive AS RESTRICTIVE FOR UPDATE TO PUBLIC USING (erp.company_allowed(company_id))");
            await DbCatalog.ExecuteAsync(owner, "CREATE POLICY company_scope_delete ON tenancy.selftest_own_rows_permissive AS PERMISSIVE FOR DELETE TO PUBLIC USING (true)");
        }
        try
        {
            var (problems, _) = await G1CompanyScopeTests.PolicyProblemsAsync(fixture.Env);
            Assert.Contains(problems, p => p.StartsWith("tenancy.selftest_own_rows_writable: its own rows are readable outside the company scope, so it needs the RESTRICTIVE company_scope_update", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("tenancy.selftest_own_rows_writable: its own rows are readable outside the company scope, so it needs the RESTRICTIVE company_scope_delete", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("tenancy.selftest_own_rows_permissive: its own rows are readable outside the company scope, so it needs the RESTRICTIVE company_scope_delete", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, p => p.StartsWith("tenancy.selftest_own_rows_permissive: its own rows are readable outside the company scope, so it needs the RESTRICTIVE company_scope_update", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, p => !p.Contains("selftest_own_rows_", StringComparison.Ordinal));
            var (tenantProblems, _) = await G1DatabaseIsolationTests.RowLevelSecurityProblemsAsync(fixture.Env);
            Assert.Contains(tenantProblems, p => p.Contains("tenancy.selftest_own_rows_permissive: unreviewed policy 'tenancy.selftest_own_rows_permissive company_scope_delete", StringComparison.Ordinal));
            Assert.DoesNotContain(tenantProblems, p => p.Contains("company_scope_update", StringComparison.Ordinal));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fixture.Env.OwnerConnectionString);
            await owner.OpenAsync();
            await DbCatalog.ExecuteAsync(owner, "DROP TABLE tenancy.selftest_own_rows_writable; DROP TABLE tenancy.selftest_own_rows_permissive");
        }
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
    public async Task The_subject_check_catches_me_endpoints_that_act_on_the_user_a_request_names()
    {
        // Critic p04 round 3, plant N1: PUT /me/preferences with an optional userId changed the
        // Administrator's language for a viewer. Here in the body (documented) and in a header.
        var result = await G2.SubjectInjection.RunAsync(fixture.Env, e => e.Name is "leaky.languageFor" or "leaky.nicknameFor");
        Assert.Equal(["PUT /api/leaky/me/language", "PUT /api/leaky/me/nickname"], result.Checked.Order(StringComparer.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/me/language [Body]", StringComparison.Ordinal) && p.Contains("identity.users changed", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/me/language [Body]", StringComparison.Ordinal) && p.Contains("own session changed", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/me/nickname [Header]", StringComparison.Ordinal) && p.Contains("identity.users changed", StringComparison.Ordinal));
        // Only the carrier each plant honours: the check names what it found, nothing else.
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/me/language [Query]", StringComparison.Ordinal) || p.StartsWith("PUT /api/leaky/me/language [Header]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/me/nickname [Body]", StringComparison.Ordinal) || p.StartsWith("PUT /api/leaky/me/nickname [Query]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.Contains("cannot tell", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_grant_bearing_record_check_catches_a_role_delete_without_the_grant_check()
    {
        // Critic p03 round 1, plant P2: deleting a role never checks what it grants.
        var result = await fixture.GrantBearingRecordsAsync();
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
        var result = await fixture.GrantBearingRecordsAsync();
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [email changed]", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [email changed]", StringComparison.Ordinal) && p.Contains("changed: before", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid}: ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [displayName changed]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith("PUT /api/leaky/members/{id:guid} [roleIds changed]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("PUT /api/identity/users/{id:guid} [email changed]", result.FieldVariants ?? []);
    }

    [Fact]
    public async Task The_grant_bearing_record_check_catches_access_checks_that_compare_only_identity_permissions()
    {
        // Critic p03 round 3, plants P14 and P16: acting on a role or a member checks only the
        // identity part of what it grants. A record granting everything still answers 403 (it
        // also grants identity permissions the caller lacks); a record granting another module's
        // permissions alone finds the narrowed checks.
        var result = await fixture.GrantBearingRecordsAsync();
        foreach (var planted in new[]
                 {
                     "PUT /api/leaky/narrow-roles/{id:guid}", "DELETE /api/leaky/narrow-roles/{id:guid}", "POST /api/leaky/narrow-roles/{id:guid}/copy",
                     "PUT /api/leaky/narrow-members/{id:guid}", "POST /api/leaky/narrow-members/{id:guid}/password",
                 })
        {
            Assert.Contains(planted, result.Checked);
            Assert.Contains(result.Problems, p => p.StartsWith($"{planted}: aimed at a record granting only tenancy.", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
            Assert.Contains(result.Problems, p => p.StartsWith($"{planted}: aimed at a record granting every tenancy permission", StringComparison.Ordinal));
            Assert.Contains(result.Problems, p => p.StartsWith($"{planted}: aimed at a record granting the caller's own permissions plus tenancy.", StringComparison.Ordinal));
            // The probe aimed only at the record granting everything is blind to this plant.
            Assert.DoesNotContain(result.Problems, p => p.StartsWith($"{planted}: aimed at a record granting everything", StringComparison.Ordinal));
        }
        // The changes are read back: the deleted role and the renamed member.
        Assert.Contains(result.Problems, p => p.StartsWith("DELETE /api/leaky/narrow-roles/{id:guid}: a record granting only tenancy.", StringComparison.Ordinal) && p.Contains("changed", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/narrow-members/{id:guid}: a record granting only tenancy.", StringComparison.Ordinal) && p.Contains("changed", StringComparison.Ordinal));
        // Single fields aimed at a record granting one other module find the narrowing too.
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/narrow-members/{id:guid} [email changed]: aimed at a record granting every tenancy permission", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.True(result.PartialTargets > 0 && result.ModuleFieldVariants > 0);
    }

    /// <summary>Bug 64 (critic p03 round 7, plant Pf): changing, deleting and copying a role accept
    /// grants the caller holds in any one company as held in every company. Only a caller whose
    /// grants come from a role in one company reaches it: the grant-bearing record check must report
    /// each of the three acting on a role held across the workspace and on one held in the other
    /// company, read the change back, and report nothing else of this family (its other rules work,
    /// and a record granting nothing is meant to change).</summary>
    [Fact]
    public async Task The_grant_bearing_record_check_catches_role_checks_that_take_grants_held_in_one_company_as_held_everywhere()
    {
        const string who = "only through a role in one company";
        var result = await fixture.GrantBearingRecordsAsync();
        foreach (var problem in result.Problems.Where(p => p.Contains("/api/leaky/company-roles", StringComparison.Ordinal)).Take(8))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        foreach (var planted in new[] { "PUT /api/leaky/company-roles/{id:guid}", "DELETE /api/leaky/company-roles/{id:guid}", "POST /api/leaky/company-roles/{id:guid}/copy" })
        {
            Assert.Contains(planted, result.Checked);
            Assert.Contains(planted, result.CompanyCallerChecked ?? []);
            foreach (var place in new[] { "held across the workspace", "held in one company only, the other one than where the caller holds it" })
            {
                Assert.Contains(result.Problems, p => p.StartsWith($"{planted}: aimed at ", StringComparison.Ordinal) && p.Contains(place, StringComparison.Ordinal) &&
                                                      p.Contains(who, StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
            }
        }
        // The changes are read back: the renamed and the deleted role, and the role emptied of a permission.
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/company-roles/{id:guid}: ", StringComparison.Ordinal) && p.Contains("changed when", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/company-roles/{id:guid} [permissions changed]: ", StringComparison.Ordinal) && p.Contains("changed when", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("DELETE /api/leaky/company-roles/{id:guid}: ", StringComparison.Ordinal) && p.Contains("changed when", StringComparison.Ordinal));
        // Workspace-wide callers meet correct checks, and the control is accepted.
        Assert.DoesNotContain(result.Problems, p => p.Contains("/api/leaky/company-roles", StringComparison.Ordinal) && !p.Contains(who, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.Contains("/api/leaky/company-roles", StringComparison.Ordinal) && p.Contains("cannot tell", StringComparison.Ordinal));
        // The product's role endpoints are judged the same way, and hold.
        Assert.Contains("PUT /api/identity/roles/{id:guid}", result.CompanyCallerChecked ?? []);
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.True(result.CompanyCallerTargets > 0);
    }

    [Fact]
    public async Task The_grant_escalation_check_catches_grant_checks_that_compare_only_identity_permissions()
    {
        // Critic p03 round 3, plant P16 (and P15's shape on members): creating a role, or a member
        // with roles, checks only the identity part of the grants. Asking for everything is refused;
        // asking for another module's permission alone, or a role granting it, is not.
        var result = await fixture.GrantEscalationAsync();
        foreach (var planted in new[] { "POST /api/leaky/narrow-roles", "POST /api/leaky/narrow-members", "PUT /api/leaky/narrow-roles/{id:guid}", "PUT /api/leaky/narrow-members/{id:guid}" })
        {
            Assert.Contains(planted, result.Checked);
            Assert.Contains(result.Problems, p => p.StartsWith($"{planted}: asking for a record granting only tenancy.", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Problems, p => p.StartsWith($"{planted}: granting the Administrator role", StringComparison.Ordinal));
        }
        Assert.Contains(result.Problems, p => p.StartsWith("PUT /api/leaky/narrow-roles/{id:guid}: asking for a record granting only tenancy.", StringComparison.Ordinal) && p.Contains("target grants changed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/roles", result.Checked);
        Assert.True(result.PartialTargets > 0);
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
        // Every root is walked to the end, whatever the tests before this one left in the process
        // (a full run once filled the walk's object budget before the generic statics were reached).
        Assert.False(running.ReachableWalkCut, $"the reachable-state walk stopped at its object budget after {running.ReachableObjectsWalked} objects");
        Assert.Contains(running.Findings, f => f.Key == $"static {typeof(LeakyModule).FullName}.cachedTenant");
        Assert.Contains(running.Findings, f => f.Key == $"singleton {typeof(LeakyModule).FullName}.LastListHolder.Last");

        // A file-local class's static memo (critic p05 round 7, plant L11): a type a person wrote,
        // although its compiled name holds '<'.
        var fileLocal = typeof(LeakyModule).Assembly.GetTypes().Single(t => t.Name.EndsWith("__SortedTotals", StringComparison.Ordinal));
        Assert.True(CompilerGenerated.IsFileLocal(fileLocal) && !CompilerGenerated.Is(fileLocal), $"{fileLocal.Name} is judged as compiler-generated");
        Assert.Contains(ProcessState.InspectTypes([fileLocal], [], new HashSet<Type>()).Findings, f => f.Key.EndsWith("SortedTotals.Totals", StringComparison.Ordinal));
        Assert.Contains(running.Findings, f => f.Key.EndsWith("SortedTotals.Totals", StringComparison.Ordinal));

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

        // A framework singleton built over a planted type (critic p05 round 5, plant L10: an
        // ObjectPool<T> served by the framework's DefaultObjectPool<T>) is reported on its own, and
        // the walk goes through its framework internals to the planted scratch objects it pools,
        // whose fields are judged like any product object's.
        Assert.Contains(running.Findings, f => f.Key == $"framework-singleton Microsoft.Extensions.ObjectPool.ObjectPool<{typeof(LeakyModule).FullName}.JumpScratch>" &&
                                               f.Why.Contains("JumpScratch", StringComparison.Ordinal));
        Assert.True(running.FrameworkSingletonsInspected > 0, "no framework singleton built over a product type was found");

        // A static delegate in a generic type (critic p05 round 4, plant L6): the field is flagged
        // by type, and the closure it holds is reached through the closed instantiation the
        // product's code uses and judged on its own.
        Assert.Contains(inventory.Findings, f => f.Key == $"static {typeof(LeakyModule).FullName}.ScrollTotals`1.Remembered" && f.Why.Contains("static delegate", StringComparison.Ordinal));
        Assert.Contains(running.Findings, f => f.Key.StartsWith("reachable closure ", StringComparison.Ordinal) && f.Key.EndsWith(".memo", StringComparison.Ordinal) &&
                                               f.Why.Contains("ScrollTotals`1[", StringComparison.Ordinal));
    }

    [Fact]
    public void The_reachable_state_walk_says_when_it_stopped_at_its_object_budget()
    {
        // A root holding more objects than the walk's budget (the shape of the gate's SQL trace in
        // a full run) leaves the roots after it unwalked: the walk must say so, never stop quietly.
        var crowd = Enumerable.Range(0, 7).Select(_ => Enumerable.Range(0, 100_000).Select(_ => new object()).ToArray()).ToArray();
        var planted = new PlantedCatalog();
        var crowded = ReachableState.Inspect([("static Crowd", crowd), ("singleton PlantedCatalog", planted)], [typeof(PlantedCatalog).Assembly], new HashSet<Type>(), new HashSet<Type> { typeof(PlantedCatalog) });
        Assert.True(crowded.Cut, $"a walk of {crowded.ObjectsWalked} objects did not report that it stopped");
        var plain = ReachableState.Inspect([("singleton PlantedCatalog", planted)], [typeof(PlantedCatalog).Assembly], new HashSet<Type>(), new HashSet<Type> { typeof(PlantedCatalog) });
        Assert.False(plain.Cut);
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
        var result = await fixture.GrantEscalationAsync();
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("expected 403", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/grants", StringComparison.Ordinal) && p.Contains("Administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/users", result.Checked);
    }

    /// <summary>Bugs 53 and 54 (critic p03 round 5, plant P2 and finding R1): "all that match"
    /// activation that judges nothing the chosen users hold, and one that judges only roles held
    /// in every company. The set-based takeover check must report the first changing the
    /// Administrator, and the second changing users whose roles are held in one company and a user
    /// holding a role in a company the caller does not work in, while leaving the Administrator
    /// and users holding workspace-wide grants alone (the second's own check works).</summary>
    [Fact]
    public async Task The_set_takeover_check_catches_all_that_match_judging_nothing_or_only_roles_held_everywhere()
    {
        const string anyone = "POST /api/leaky/users/matching/active-anyone";
        const string workspaceOnly = "POST /api/leaky/users/matching/active-workspace-roles";
        var result = await fixture.SetTakeoverAsync();
        foreach (var problem in result.Problems.Take(12))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(anyone, result.Checked);
        Assert.Contains(workspaceOnly, result.Checked);
        Assert.Contains(result.Problems, p => p.StartsWith(anyone + " by search", StringComparison.Ordinal) && p.Contains("aimed at the Administrator", StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith(anyone + " by filter", StringComparison.Ordinal) && p.Contains("aimed at the Administrator", StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
        foreach (var selector in new[] { " by search", " by filter" })
        {
            Assert.Contains(result.Problems, p => p.StartsWith(workspaceOnly + selector, StringComparison.Ordinal) && p.Contains("in one company only", StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
            Assert.Contains(result.Problems, p => p.StartsWith(workspaceOnly + selector, StringComparison.Ordinal) && p.Contains("a company the caller does not work in", StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(workspaceOnly, StringComparison.Ordinal) && p.Contains("aimed at the Administrator", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(workspaceOnly, StringComparison.Ordinal) && p.Contains("aimed at a user holding", StringComparison.Ordinal) &&
                                                    !p.Contains("in one company only", StringComparison.Ordinal) && !p.Contains("a company the caller does not work in", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.True(result.CompanyAimed > 0);
    }

    /// <summary>Bug 57 (critic p03 round 6, plant Pc): "all that match" activation that takes a role
    /// the caller's grants cover in one company as covered in every company. Only a caller whose
    /// grants come from a role in one company reaches it: the set-based takeover check must report
    /// it changing a user holding a module's grants in the other company, by search and by filter,
    /// and nothing else of that endpoint (its other rules work, and the same role held in the
    /// caller's company is meant to change).</summary>
    [Fact]
    public async Task The_set_takeover_check_catches_all_that_match_letting_a_grant_held_in_one_company_cover_another()
    {
        const string roleAnywhere = "POST /api/leaky/users/matching/active-role-anywhere";
        const string otherCompany = "in one company only, the other one than where the caller holds it";
        var result = await fixture.SetTakeoverAsync();
        foreach (var problem in result.Problems.Where(p => p.StartsWith(roleAnywhere, StringComparison.Ordinal)).Take(6))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(roleAnywhere, result.Checked);
        foreach (var selector in new[] { " by search", " by filter" })
        {
            Assert.Contains(result.Problems, p => p.StartsWith(roleAnywhere + selector, StringComparison.Ordinal) && p.Contains(otherCompany, StringComparison.Ordinal) &&
                                                  p.Contains("only through a role in one company", StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(roleAnywhere, StringComparison.Ordinal) && !p.Contains(otherCompany, StringComparison.Ordinal));
        Assert.True(result.CompanyCallerAimed > 0);
    }

    /// <summary>Bug 65 (critic p02 round 8): "all that match" activation with every role rule right
    /// that leaves out where the chosen users work. The set-based takeover check must report it
    /// changing a user who also works in a company the caller does not work in, by search and by
    /// filter, and nothing else of that endpoint.</summary>
    [Fact]
    public async Task The_set_takeover_check_catches_all_that_match_leaving_out_where_users_work()
    {
        const string ignoring = "POST /api/leaky/users/matching/active-ignoring-workplaces";
        const string worksElsewhere = "a user who also works in a company the caller does not work in";
        var result = await fixture.SetTakeoverAsync();
        foreach (var problem in result.Problems.Where(p => p.StartsWith(ignoring, StringComparison.Ordinal)).Take(6))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(ignoring, result.Checked);
        foreach (var selector in new[] { " by search", " by filter" })
        {
            Assert.Contains(result.Problems, p => p.StartsWith(ignoring + selector, StringComparison.Ordinal) && p.Contains(worksElsewhere, StringComparison.Ordinal) && p.Contains("changed them", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(ignoring, StringComparison.Ordinal) && !p.Contains(worksElsewhere, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_permission_checks_catch_a_write_guarded_by_a_read_permission_and_a_read_that_writes()
    {
        // Critic p00 round 2, plant P2: a POST that reactivates users while declaring a read permission.
        var result = ReadPermissionWrites.Check(EndpointInventory.From(fixture.Env.Factory.Services));
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/users/{id:guid}/reactivate ", StringComparison.Ordinal));
        // The planted list's own saved-view endpoints (a reader saving their own view) are the
        // planted module's too: they are reviewed for product lists, not for this one.
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal) && !p.Contains($"/api/lists/{LeakyModule.PeopleList}/views", StringComparison.Ordinal) &&
                                                     !p.Contains($"/api/lists/{LeakyModule.ScrollList}/views", StringComparison.Ordinal) &&
                                                     !p.Contains($"/api/lists/{LeakyModule.JumpList}/views", StringComparison.Ordinal));

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

    /// <summary>Bug 51 (critic p06 round 1, plant P1): a report printing the companies' tax
    /// numbers under a permission that grants no company data must fail the report data check;
    /// the catalogue check must pass (the planted report is listed exactly to its permission).</summary>
    [Fact]
    public async Task The_report_data_check_catches_a_report_printing_data_its_permission_does_not_grant()
    {
        var result = await G2.ReportDataCheck.RunAsync(fixture.Env, onlyReport: "leaky.taxNumbers");
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(result.Problems, p => p.Contains("leaky.taxNumbers", StringComparison.Ordinal) &&
                                              p.Contains("as a user holding exactly [leaky.data.read]", StringComparison.Ordinal) &&
                                              p.Contains("which no other endpoint those permissions open shows", StringComparison.Ordinal));
        Assert.True(result.ValuesJudged > 0);
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
        // Critic p03 round 3, plant L7: the same registry kept for role names.
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/registered-roles [nameEn]: tenant A sending a value written by tenant B", StringComparison.Ordinal) && p.Contains("answered 409", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/roles", result.Endpoints);
    }

    /// <summary>Bug 58 (critic p03 rounds 5 and 6, plant L3): a membership naming another
    /// workspace's company inside a list of objects answers "not their company", an id that exists
    /// nowhere "unknown ids". The id differential must report it, and no product endpoint.</summary>
    [Fact]
    public async Task The_write_oracle_check_catches_a_write_telling_another_tenants_id_inside_a_list_of_objects_from_an_unknown_one()
    {
        var result = await G1WriteOracle.RunIdsAsync(fixture.Env);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(result.Problems, p => p.StartsWith("POST /api/leaky/company-memberships [memberships[].companyId]: tenant A naming tenant B's record", StringComparison.Ordinal) &&
                                              p.Contains("notTheirCompany", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Contains("POST /api/identity/users [companyRoles[].companyId]", result.Judged);
    }
}
