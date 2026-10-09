using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: the attack writes to tenant A.</summary>
public sealed class G1AttackFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        SqlTrace.EnsureStarted();
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, HTTP layer. Signed in as tenant A (cookie and bearer token), as a tenant A user without
/// roles, and anonymously, the attacker calls every endpoint the running app exposes with every
/// kind of tenant B identifier in the route, tenant-switch headers, query string and body, then
/// sends every one of tenant B's real values (ids, e-mails, codes, names, hashes) through every
/// documented route, query and body parameter. No response may contain any tenant B identifier,
/// value or canary; a GET may not answer differently for a tenant B value than for a value that
/// exists nowhere; no response may be a server error; no tenant B row may change; and the
/// reviewed cross-tenant lookups may run only from their reviewed callers. Exports, jobs and
/// files are attacked by the probes their modules register. Throughout, tenant B uses the same
/// process (<see cref="TenantActivity"/>): before, between and concurrently with the attack, so
/// a leak through process-wide state (static fields, singletons, caches) is caught in both
/// directions.
/// </summary>
public sealed class G1HttpIsolationTests(G1AttackFixture fixture) : IClassFixture<G1AttackFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Tenant_A_cannot_reach_tenant_B_through_any_endpoint()
    {
        var report = await IsolationAttack.RunAsync(Env);
        foreach (var phase in report.Phases)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(phase);
        }
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.BindsJudged} tenant bindings and {report.SettingStatementsJudged} setting statements judged among {report.RequestStatementsTraced} statements; " +
            $"{report.TenantValuesJudged} tenant values judged, {report.StatementsObserved} statements observed with their parameters; " +
            $"{report.SwitchInputAttacks} tenant switch inputs over {report.SwitchHeaderNames} header names; {report.ResponsesHeaderJudged} responses judged on every header");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.VictimRouteValuesReplayed} tenant B route values replayed, {report.VictimPreTouches} tenant B opens of the routes A was about to attack");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.EnumValuesAttacked.Count} documented enumeration values sent, {report.EnumVariantPairs} enumeration-variant write pairs");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.EndpointsAttacked} endpoints, {report.Requests} requests, {report.VictimValues} tenant B values, {report.ParameterAttacks} parameter attacks, " +
            $"{report.BodyValueAttacks} body value attacks, {report.DifferentialChecks} differential checks ({report.AttackerHeldSkips} values tenant A holds itself not compared), {report.TracedLookups} traced lookups");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"in Arabic: {report.ArabicAttackRequests} tenant A requests, {report.VictimArabicRequests} tenant B requests, {report.ArabicWritePairs} write pairs");

        // Every check runs and every failing one is reported together: a plant caught by one
        // check must not hide whether the others (list answers, process state) caught it too.
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
        void CheckAtLeast(int value, string key)
        {
            if (value < Ratchet.Min(key))
            {
                failures.Add($"{key}: {value}; ratchet minimum {Ratchet.Min(key)}");
            }
        }
        Check(report.Leaks.Count == 0, $"{report.Leaks.Count} leaks:\n" + string.Join("\n", report.Leaks.Take(50)));
        Check(report.Oracles.Count == 0, $"{report.Oracles.Count} answers that tell tenant B's values apart from values that exist nowhere:\n" + string.Join("\n", report.Oracles.Take(30)));
        Check(report.LookupMisuse.Count == 0, "Reviewed cross-tenant lookups ran from unreviewed callers:\n" + string.Join("\n", report.LookupMisuse));
        Check(report.ChangedTables.Count == 0, "Tenant B rows changed during the attack in: " + string.Join(", ", report.ChangedTables));
        Check(report.ServerErrors.Count == 0, $"{report.ServerErrors.Count} server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        Check(report.UncoveredSurfaces.Count == 0, "Endpoint families without an isolation probe: " + string.Join(", ", report.UncoveredSurfaces));
        Check(report.UntracedFunctions.Count == 0, "The SQL trace never saw these reviewed functions run from their reviewed caller, so it may be blind: " + string.Join(", ", report.UntracedFunctions));
        Check(report.BindViolations.Count == 0, "Requests bound a tenant other than the signed-in session's:\n" + string.Join("\n", report.BindViolations.Take(30)));
        Check(report.SettingViolations.Count == 0, "Session settings changed by code other than the kernel's session:\n" + string.Join("\n", report.SettingViolations.Take(30)));
        Check(report.WritableReads.Count == 0, "Requests that only read ran in a writable transaction:\n" + string.Join("\n", report.WritableReads.Take(30)));
        Check(report.TraceBlindSpots.Count == 0, "The tenant binding trace may be blind: " + string.Join("; ", report.TraceBlindSpots));
        Check(report.TenantValueViolations.Count == 0, "Requests whose SQL ran under a tenant other than the signed-in session's (judged by the value each statement set):\n" +
                                                             string.Join("\n", report.TenantValueViolations.Take(30)));
        Check(report.UnobservedStatements.Count == 0, "Statements sent inside requests on a pool the platform did not build, whose tenant the gate cannot judge:\n" +
                                                            string.Join("\n", report.UnobservedStatements.Take(30)));
        Check(report.InputEnumerations.Count == 0, "Product code read request inputs by enumerating them (a name the attack cannot learn and send tenant B's id in):\n" +
                                                         string.Join("\n", report.InputEnumerations.Take(30)));
        Check(report.EnvironmentChanges.Count == 0, "Process-wide environment variables changed during the attack:\n" + string.Join("\n", report.EnvironmentChanges));
        CheckAtLeast(report.TenantValuesJudged, "g1.tenantValuesJudged");
        CheckAtLeast(report.StatementsObserved, "g1.statementsObserved");
        CheckAtLeast(report.BindsJudged, "g1.tenantBindsJudged");
        CheckAtLeast(report.SwitchInputAttacks, "g1.switchInputAttacks");
        CheckAtLeast(report.SwitchHeaderNames, "g1.switchHeaderNames");
        CheckAtLeast(report.ResponsesHeaderJudged, "g1.responsesHeaderJudged");
        CheckAtLeast(report.EndpointsAttacked, "g1.endpointsAttacked");
        CheckAtLeast(report.VictimRouteValuesReplayed, "g1.victimRouteValuesReplayed");
        CheckAtLeast(report.VictimPreTouches, "g1.victimPreTouches");
        CheckAtLeast(report.Requests, "g1.attackRequests");
        CheckAtLeast(report.ProbesRun, "g1.isolationProbes");
        CheckAtLeast(report.VictimValues, "g1.victimValues");
        CheckAtLeast(report.ParameterAttacks, "g1.parameterAttacks");
        CheckAtLeast(report.BodyValueAttacks, "g1.bodyValueAttacks");
        CheckAtLeast(report.DifferentialChecks, "g1.differentialChecks");
        CheckAtLeast(report.TracedLookups, "g1.tracedLookups");
        Check(report.VictimBlindSpots.Count == 0, "Tenant B's concurrent activity may have been blind:\n" + string.Join("\n", report.VictimBlindSpots));
        CheckAtLeast(report.VictimRequests, "g1.victimRequests");
        CheckAtLeast(report.VictimConcurrentRequests, "g1.victimConcurrentRequests");
        CheckAtLeast(report.VictimWrites, "g1.victimWrites");
        CheckAtLeast(report.ReverseChecks, "g1.reverseChecks");
        // Every endpoint that changes data (other than signing in and out) succeeded for tenant B.
        Check(report.VictimUnsuccessfulWrites.Count == 0,
            "Tenant B's own writes must succeed so their handlers run to the end; these did not:\n" + string.Join("\n", report.VictimUnsuccessfulWrites));
        CheckAtLeast(report.VictimWriteEndpoints, "g1.victimWriteEndpoints");
        // Write after write: tenant A's own valid writes, each right after tenant B's, succeeded
        // (a refused write never reaches the code that could hand on tenant B's state).
        Check(report.AttackerUnsuccessfulWrites.Count == 0,
            "Tenant A's own writes in the write-after-write phase must succeed; these did not:\n" + string.Join("\n", report.AttackerUnsuccessfulWrites));
        Check(report.WritePairBlindSpots.Count == 0, "The write-after-write phase may have been blind:\n" + string.Join("\n", report.WritePairBlindSpots));
        CheckAtLeast(report.WritePairs, "g1.writePairs");
        CheckAtLeast(report.WritePairEndpoints, "g1.writePairEndpoints");
        // Every documented value of every enumerated body field (language ar, numerals arab, ...)
        // was sent by both tenants back to back, not only the first one.
        CheckAtLeast(report.EnumValuesAttacked.Count, "g1.enumValuesAttacked");
        CheckAtLeast(report.EnumVariantPairs, "g1.enumVariantWritePairs");
        // The Arabic side of every session (critic p04 round 4): tenant A attacks and tenant B
        // uses the app in Arabic with Arabic-Indic digits too, and both stayed Arabic throughout.
        Check(report.ArabicBlindSpots.Count == 0, "The Arabic sessions may have been blind:\n" + string.Join("\n", report.ArabicBlindSpots));
        CheckAtLeast(report.ArabicAttackRequests, "g1.arabicAttackRequests");
        CheckAtLeast(report.VictimArabicRequests, "g1.victimArabicRequests");
        CheckAtLeast(report.ArabicWritePairs, "g1.arabicWritePairs");
        Check(report.ListRefusals.Count == 0, $"{report.ListRefusals.Count} list attacks were refused, so the query never ran:\n" + string.Join("\n", report.ListRefusals.Take(20)));
        CheckAtLeast(report.ListQueryAttacks, "g1.listQueryAttacks");
        Check(report.ListAnswersWrong.Count == 0, $"{report.ListAnswersWrong.Count} list answers that are not the asking tenant's own:\n" + string.Join("\n", report.ListAnswersWrong.Take(30)));
        Check(report.ListAnswersBlind.Count == 0, "The list answer check may be blind:\n" + string.Join("\n", report.ListAnswersBlind));
        CheckAtLeast(report.ListAnswerQueries, "g1.listAnswerQueries");
        CheckAtLeast(report.ListAnswersDiscriminating, "g1.listAnswersDiscriminating");
        CheckAtLeast(report.ListAnswerPagesJudged, "g1.listAnswerPagesJudged");
        CheckAtLeast(report.ListAnswerOffsetPagesJudged, "g1.listAnswerOffsetPagesJudged");
        Check(report.StateChanges.Count == 0, $"Process-wide state changed while the tenants used the app ({report.StateChanges.Count} lines):\n" + string.Join("\n", report.StateChanges.Take(30)));
        CheckAtLeast(report.StateLinesFingerprinted, "g1.stateLinesFingerprinted");
        CheckAtLeast(report.ShapeEndpoints, "g1.shapeEndpoints");
        CheckAtLeast(report.ShapeAttacks, "g1.shapeAttacks");
        CheckAtLeast(report.ShapeVictimRequests, "g1.shapeVictimRequests");
        Assert.True(failures.Count == 0, $"{failures.Count} isolation checks failed:\n\n" + string.Join("\n\n", failures));
    }
}

/// <summary>
/// The G1 HTTP attack, reusable so the gate's self-tests can prove it catches planted leaks.
/// </summary>
public static partial class IsolationAttack
{
    private const int VictimIdsPerTable = 5;

    /// <summary>Query names tried on every endpoint whether documented or not.</summary>
    private static readonly string[] GuessedQueryNames =
        ["tenantId", "tenant", "tenant_id", "companyId", "id", "userId", "search", "workspace", "email", "code", "name", "q", "filter"];

    public static async Task<IsolationReport> RunAsync(ErpTestEnvironment Env)
    {
        SqlTrace.EnsureStarted();
        // Environment variables are process-wide state outside any field (critic p00 round 4: the
        // process-state inventory reflects over fields only); nothing a request does may change them.
        var environmentBefore = EnvironmentVariables();
        var traceMark = SqlTrace.Mark;
        var traceSnapshot = SqlTrace.Snapshot();
        var a = Env.TenantA;
        var b = Env.TenantB;

        using var anonymousForDocs = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymousForDocs);
        var endpoints = EndpointInventory.From(Env.Factory.Services);

        // Tenant B uses the product first: every write on its own records, then (below) every
        // read, so its data sits in whatever process-wide state the app keeps before A attacks.
        var own = await TenantSnapshot.TakeAsync(Env, a.Id, null, a.Code);
        var examples = openApi.ExampleValues();
        var ownValues = await VictimValues.ReadAsync(Env, own, b.Id, examples);
        var activity = await TenantActivity.StartAsync(Env, b, endpoints, openApi);
        activity.Watch(new MarkerSet(own, ownValues));
        await activity.WriteAsync(await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code));

        var victim = await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code);
        Assert.True(victim.Markers.Count > 10, "The victim tenant has too little data to attack.");
        var values = await VictimValues.ReadAsync(Env, victim, a.Id, examples);
        Assert.True(values.Strings.Count > 10, "The victim tenant has too few distinct text values to attack.");
        var victimIdTexts = values.Ids.Select(i => i.ToString()).ToList();
        await activity.ReadAsync(victim, values, "tenant B reads before the attack");
        // Tenant B keeps writing its own records during the attack (right before and after each
        // write tenant A sends); only changes between tenant B's own writes count as the attack's.
        activity.TrackChangesFrom(victim);

        // Everything reachable from the app's singletons and static fields, before tenant A
        // attacks; compared with the same after the attack (state that changes under traffic).
        var (stateRoots, productAssemblies) = ProcessState.LiveRoots(Env.Factory);
        var stateBefore = ReachableState.Fingerprint(stateRoots, productAssemblies);

        // The administrator first, its bearer token second and the anonymous caller last (the
        // phases below pick them by position). The Arabic side (critic p04 round 4): an
        // administrator and an anonymous caller whose every request runs in Arabic (with
        // Arabic-Indic digits), so the Arabic branch of every handler is attacked too.
        var attackers = new List<Attacker>
        {
            new("tenant A administrator (cookie)", () => Env.SignInAsync(Env.Email(a, "admin"))),
            new("tenant A administrator (bearer)", () => Env.SignInWithTokenAsync(Env.Email(a, "admin"))),
            new("tenant A administrator in Arabic (cookie)", () => ArabicSession.SignInAsync(Env, a), arabic: true),
            new("tenant A user without roles", () => Env.SignInAsync(Env.Email(a, "noaccess"))),
            new("anonymous in Arabic", () => Task.FromResult(ArabicSession.Anonymous(Env)), anonymous: true, arabic: true),
            new("anonymous", () => Task.FromResult(Env.CreateClient()), anonymous: true),
        };
        var arabicAdmin = attackers.Single(x => x.Arabic && !x.Anonymous);
        var arabicAnonymous = attackers.Single(x => x.Arabic && x.Anonymous);
        var arabicBlind = new List<string>();
        async Task KeepArabicAsync(string phase)
        {
            await arabicAdmin.KeepArabicAsync();
            await activity.EnsureArabicAsync(phase);
            if (!await ArabicSession.IsArabicAsync(arabicAdmin.Client))
            {
                arabicBlind.Add($"{phase}: {arabicAdmin.Name} no longer answers in Arabic with Arabic-Indic digits");
            }
        }
        foreach (var attacker in attackers)
        {
            await attacker.ConnectAsync();
        }

        var state = new AttackState(victim, values);
        var phases = new List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Phase(string name)
        {
            phases.Add($"{name}: {state.Requests} requests so far ({attackers.Where(x => x.Arabic).Sum(x => x.Requests)} by tenant A's Arabic sessions; " +
                       $"tenant B {activity.Requests}, {activity.ArabicRequests} in Arabic), {clock.Elapsed.TotalSeconds:F1} s");
        }

        var victimRouteValues = victim.IdsByTable.Values.SelectMany(ids => ids.Take(VictimIdsPerTable))
            .Append(b.Id).Distinct().Select(id => id.ToString())
            .Append(b.Code)
            .ToList();
        var ownRouteValues = own.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty)
            .Select(id => id.ToString()).ToList();

        var attacked = new HashSet<string>();
        var counter = 0;

        // Phase 1: tenant B ids in routes; tenant-switch headers; guessed query names; uuid body fields.
        // Every value tenant B itself put in a route (its own ids that answered and the records it
        // created) is replayed in every route as well, and tenant B opens each GET route with every
        // value tenant A is about to send right before A sends it: a handler that keeps answers per
        // id without the tenant (critic p03 round 1, plants T1 and T2) then hands tenant A exactly
        // the answer tenant B left there, and the body is judged for tenant B's markers.
        var replayed = 0;
        foreach (var endpoint in endpoints)
        {
            await activity.TouchAsync(endpoint, victim, "tenant B reads right before A attacks this endpoint");
            var bRouteValues = endpoint.RouteParameters.Count == 0 ? [] : activity.RouteValues.Where(v => !victimRouteValues.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
            replayed += bRouteValues.Count;
            var attackValues = victimRouteValues.Concat(bRouteValues).ToList();
            await activity.TouchEveryAsync(endpoint, attackValues, "tenant B opens the route with every value A is about to send");
            var paths = endpoint.RouteParameters.Count == 0
                ? [endpoint.Path(_ => "")]
                : attackValues.Concat(endpoint.HasBody ? ownRouteValues : [])
                    .Select(value => endpoint.Path(_ => value)).Distinct().ToList();
            var bodySchema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var signIn = endpoint.Name == "auth.signIn";

            // Every request of the endpoint is built first (in the same order and with the same
            // values as one after another), then sent four at a time, all of them after tenant B's
            // touches above and before those below. Signing in and out change the attackers' own
            // sessions, so those two endpoints send one request at a time.
            var batch = new List<(Attacker Attacker, HttpRequestMessage Request, string Label)>();
            foreach (var path in paths)
            {
                foreach (var variant in Enum.GetValues<Variant>())
                {
                    foreach (var attacker in attackers)
                    {
                        // The anonymous Arabic caller reaches a permissioned handler's Arabic
                        // refusal only, which is the same for every value: sent once per endpoint.
                        if (attacker == arabicAnonymous && !endpoint.IsAnonymous && (variant != Variant.Plain || path != paths[0]))
                        {
                            continue;
                        }
                        // The Arabic sessions send every path plainly. Tenant-switch headers and
                        // query names are about where the tenant comes from, not the language: the
                        // English sessions send them here and the switch phase (1b) sends every one
                        // the app reads, so the Arabic copies only cost processor time (the verify
                        // budget, verify.cpuSeconds).
                        if (attacker.Arabic && variant != Variant.Plain)
                        {
                            continue;
                        }
                        batch.Add((attacker, BuildRequest(endpoint, path, variant, bodySchema, openApi, victim, b, signIn, ref counter), $"{path} [{variant}]"));
                    }
                }
            }
            var together = endpoint.Name is "auth.signIn" or "auth.signOut" ? 1 : AttackParallelism.Requests;
            await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = together }, async (item, _) =>
            {
                using var request = item.Request;
                await state.SendAsync(item.Attacker, endpoint, request, item.Label);
            });
            attacked.Add(endpoint.Key);
            if (endpoint.Method != "GET")
            {
                await KeepArabicAsync($"after A attacked {endpoint.Key}");
            }
            await activity.TouchAsync(endpoint, victim, "tenant B reads right after A attacked this endpoint");
            if (endpoint.Method != "GET")
            {
                await activity.ReadRoundAsync(victim, $"tenant B reads after A's {endpoint.Key}");
            }
        }

        Phase("routes, tenant headers and guessed queries");

        // Phase 1b: the tenant can come only from the session. Tenant B's id and code go, one input
        // at a time, into every header, query parameter and cookie the running app was seen to
        // read (and every header name the source names), and into every uuid field of every body.
        var switchInputs = await TenantSwitchPhaseAsync(Env, endpoints, openApi, attackers, state, own, ownRouteValues, b, activity, victim);
        Phase($"tenant switch inputs: {switchInputs.Headers.Count} headers, {switchInputs.Queries.Count} query names, {switchInputs.Cookies.Count} cookies, {switchInputs.BodyNames.Count} body names");

        // Phase 1c: write after write. State a write leaves behind (a variable captured by the
        // endpoint's lambda, a field of a singleton, a cache filled on save) reaches whoever writes
        // next. For every endpoint that changes data, tenant B makes a valid write on its own
        // records immediately before each valid write tenant A makes on its own records (cookie and
        // bearer), and again right after; tenant A then reads everything, and so does tenant B
        // after tenant A's next write. Both tenants' writes must succeed, so every handler runs to
        // the end, and every answer (body and every header) is judged for the other tenant's markers.
        var pairs = await WritePairsPhaseAsync(Env, endpoints, openApi, activity, victim, values);
        Phase($"write after write: {pairs.Pairs} pairs over {pairs.Endpoints} endpoints, {pairs.VariantPairs} pairs with {pairs.EnumValuesAttacked?.Count ?? 0} documented values, {pairs.AttackerRequests} tenant A requests");

        // Phase 1d: every shape of every answer. A GET whose API document enumerates how it answers
        // (a format, a language, digits, a disposition, a grouping) may keep each shape apart from
        // the others: a printed PDF or an exported workbook kept by its download name (critic p06
        // round 1, plant L2), which no phase above ever fills from tenant B, because tenant B never
        // asked for a PDF. Tenant B asks for every shape on its own records first; tenant A then asks
        // for the same shapes on its own records, with and without a query of its own, while tenant
        // B keeps asking; then tenant B asks once more. Every answer is judged both ways.
        var shapes = await AnswerShapesPhaseAsync(endpoints, openApi, admin: attackers[0], state, own, victim, activity);
        Phase($"answer shapes: {shapes.Endpoints} endpoints, {shapes.Shapes} shapes, {shapes.VictimRequests} tenant B and {shapes.AttackerRequests} tenant A requests");

        // Exports, imports, jobs and files: every surface kind in use needs a probe, and every
        // probe runs with tenant B's identifiers and values.
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var probeKinds = new HashSet<SurfaceKind>();
        var probesRun = 0;
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            foreach (var probeType in catalog.Modules.SelectMany(m => m.IsolationProbes))
            {
                var probe = (IIsolationProbe)scope.ServiceProvider.GetRequiredService(probeType);
                probeKinds.Add(probe.Kind);
                // Tenant B's administrator goes with the probe: it uses the surface on its own
                // records right before tenant A does (prints and exports every report and list).
                var result = await probe.RunAsync(new IsolationProbeContext(attackers[0].Client, a.Id, b.Id, victim.AllIds.ToList(),
                    victim.Markers.Concat(values.Strings).Distinct().ToList(), activity.AdminClient), CancellationToken.None);
                state.Requests += result.Attempts;
                probesRun++;
                // Attempts the probe could not make or that got no answer fail the gate (as server
                // errors do), and what it did observe is still judged below.
                foreach (var failure in result.Failures)
                {
                    state.ServerErrors.Add($"probe {probe.Name}: no answer: {failure}");
                }
                foreach (var raw in result.Observed)
                {
                    // A probe may hand over a whole body as "body:<media type>;base64,<data>"; it is
                    // decoded like any response (PDF text, spreadsheet cells), not searched as bytes.
                    var observed = ObservedBody.Decode(raw);
                    if (state.FindMarker(observed, []) is { } marker)
                    {
                        state.Leaks.Add($"probe {probe.Name}: observed tenant B marker {marker}");
                    }
                }
            }
        }
        var uncovered = endpoints.Where(e => e.Surface != SurfaceKind.Data && !probeKinds.Contains(e.Surface))
            .Select(e => $"{e.Key} ({e.Surface})").ToList();

        // Phase 2: every tenant B value through every documented (and guessed) query parameter and
        // every route parameter. Each GET is also sent with a value that exists nowhere; the two
        // answers must not differ (no existence oracle).
        // Requests run four at a time: first every GET (pairs with their controls), then every
        // other method, so no write can change a list between a GET and its control.
        var admin = attackers[0];
        var anonymous = attackers[^1];
        // Read now, after the earlier phases' writes: tenant A's records now hold some published
        // values (an emirate), and a control must be a value no tenant holds when it is sent.
        state.UnheldPublished = await UnheldPublishedValuesAsync(Env, endpoints.SelectMany(e => openApi.Parameters(e.Method, e.Pattern)));
        // Tenant B's values tenant A now holds itself, written by the earlier phases' valid
        // requests (a branch in B's emirate): A's answer for them shows A's own records, so it is
        // judged for B's markers but not compared with a value that exists nowhere.
        state.AttackerHeld = await HeldByAsync(Env, a.Id, values.Strings);
        var work = new List<(bool Get, Func<Task> Run)>();
        foreach (var endpoint in endpoints)
        {
            // The attacker that reaches the handler: the administrator for permissioned endpoints;
            // for anonymous ones both on GET and the anonymous caller otherwise (a signed-in
            // caller would sign itself out; phase 1 already sends them signed in).
            var reachable = !endpoint.IsAnonymous ? new[] { admin } : endpoint.Method == "GET" ? [admin, anonymous] : [anonymous];
            var documented = openApi.Parameters(endpoint.Method, endpoint.Pattern);
            var catchAll = endpoint.Pattern.Contains("{*", StringComparison.Ordinal);
            var queries = documented.Where(p => p.In == "query").Select(p => (Parameter: p, Values: ValuesFor(p, values).ToList())).ToList();
            if (!catchAll)
            {
                // Names the app does not document still reach a handler that reads the raw query.
                foreach (var guess in GuessedQueryNames.Where(g => queries.All(q => q.Parameter.Name != g)))
                {
                    queries.Add((new ApiParameter(guess, "query", "string", null), values.Probe.ToList()));
                }
            }
            var ownRoute = endpoint.RouteParameters.Count == 0 ? "" : await PickOwnRouteValueAsync(admin, endpoint, ownRouteValues);
            var basePath = endpoint.Path(_ => ownRoute);
            var bodySchema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var get = endpoint.Method == "GET";
            // The Arabic side: every GET once more in Arabic (the administrator, and the anonymous
            // caller where it reaches the handler), with a cross-section of tenant B's values in
            // every parameter (TenantActivity.ArabicValuesFor), each compared with a value that
            // exists nowhere like the English attack.
            var arabicReachable = !get ? [] : !endpoint.IsAnonymous ? new[] { arabicAdmin } : [arabicAdmin, arabicAnonymous];

            foreach (var (parameter, parameterValues) in queries)
            {
                foreach (var value in parameterValues)
                {
                    foreach (var attacker in reachable)
                    {
                        var n = counter++;
                        string UriFor(string v) => basePath + "?" + Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(v);
                        work.Add((get, () => state.ParameterAttackAsync(attacker, endpoint, value, parameter, UriFor, bodySchema, openApi, b, n)));
                    }
                }
                foreach (var value in TenantActivity.ArabicValuesFor(parameter, values, published: false))
                {
                    foreach (var attacker in arabicReachable)
                    {
                        var n = counter++;
                        string UriFor(string v) => basePath + "?" + Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(v);
                        work.Add((get, () => state.ParameterAttackAsync(attacker, endpoint, value, parameter, UriFor, bodySchema, openApi, b, n)));
                    }
                }
            }

            foreach (var routeParameter in endpoint.RouteParameters)
            {
                var documentedRoute = documented.FirstOrDefault(p => p.In == "path" && p.Name == routeParameter)
                                      ?? new ApiParameter(routeParameter, "path", "string", null);
                var routeValues = catchAll
                    ? values.Probe
                    : documentedRoute.Format == "uuid"
                        ? victim.IdsByTable.Values.SelectMany(ids => ids.Take(10)).Append(b.Id).Select(i => i.ToString())
                            .Concat(activity.RouteValues.Where(v => Guid.TryParse(v, out _))).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : values.All.ToList();
                foreach (var value in routeValues)
                {
                    foreach (var attacker in reachable)
                    {
                        var n = counter++;
                        string UriFor(string v) => endpoint.Path(name => name == routeParameter ? v : ownRoute);
                        work.Add((get, () => state.ParameterAttackAsync(attacker, endpoint, value, documentedRoute, UriFor, bodySchema, openApi, b, n)));
                    }
                }
                // Every route value tenant B used itself was already sent in Arabic in every route
                // (phase 1); here the sampled ids, each with its control value.
                var arabicRouteValues = catchAll || documentedRoute.Format != "uuid"
                    ? values.Probe
                    : values.IdSample.Select(i => i.ToString()).ToList();
                foreach (var value in arabicRouteValues)
                {
                    foreach (var attacker in arabicReachable)
                    {
                        var n = counter++;
                        string UriFor(string v) => endpoint.Path(name => name == routeParameter ? v : ownRoute);
                        work.Add((get, () => state.ParameterAttackAsync(attacker, endpoint, value, documentedRoute, UriFor, bodySchema, openApi, b, n)));
                    }
                }
            }
        }
        // From here tenant B keeps reading in the background while A's requests run.
        using var concurrent = new CancellationTokenSource();
        var concurrentReader = Task.Run(() => activity.RunConcurrentlyAsync(victim, concurrent.Token));
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests };
        await Parallel.ForEachAsync(work.Where(w => w.Get), parallel, async (item, _) => await item.Run());
        await Parallel.ForEachAsync(work.Where(w => !w.Get), parallel, async (item, _) => await item.Run());

        Phase("tenant B values in route and query parameters");

        // Phase 2b: every registered list through its query contract, with tenant B's values inside
        // filter expressions (each compared with a value that exists nowhere), grouped and sorted
        // by every column that allows it, and paged from cursors forged with tenant B's ids and
        // values. The list framework builds SQL from these, so it is attacked list by list, not
        // only through the raw parameters above.
        var listWork = new List<Func<Task>>();
        var listsAttacked = 0;
        foreach (var binding in catalog.ListBindings)
        {
            var list = binding.Definition;
            var listEndpoint = endpoints.SingleOrDefault(e => e.Method == "GET" && e.Pattern == list.Endpoint);
            Assert.True(listEndpoint is not null, $"list '{list.Key}': no GET {list.Endpoint} to attack");
            listsAttacked++;
            var filterParameter = new ApiParameter("filter", "query", "string", null);
            foreach (var column in list.Columns.Where(c => c.Filterable))
            {
                var (candidates, operators) = column.Type switch
                {
                    Erp.Kernel.Lists.ListColumnType.Text => (values.Strings, new[] { "eq", "contains", "startswith", "endswith", "ne", "in" }),
                    Erp.Kernel.Lists.ListColumnType.Reference => (values.IdSample.Select(i => i.ToString()).ToList(), new[] { "eq", "ne" }),
                    _ => ((IReadOnlyList<string>)[], Array.Empty<string>()),
                };
                foreach (var value in candidates)
                {
                    foreach (var op in operators)
                    {
                        var n = counter++;
                        string UriFor(string v) => list.Endpoint + "?take=200&filter=" + Uri.EscapeDataString(op == "in"
                            ? $"{column.Key} in ({Erp.Kernel.Lists.ListFilterText.Quote(v)}, 'zz-none')"
                            : $"{column.Key} {op} {Erp.Kernel.Lists.ListFilterText.Quote(v)}");
                        listWork.Add(() => state.ParameterAttackAsync(admin, listEndpoint!, value, filterParameter, UriFor, null, openApi, b, n));
                    }
                }
                if (column.Type == Erp.Kernel.Lists.ListColumnType.Reference)
                {
                    // Every sampled id, in as few "in" filters as the filter's length and value
                    // limits allow (one table more in the victim made a single filter too long).
                    var chunk = new List<string>();
                    void Flush()
                    {
                        if (chunk.Count == 0) return;
                        var ids = string.Join(", ", chunk);
                        listWork.Add(() => SendListAsync(state, admin, listEndpoint!, $"{list.Endpoint}?take=200&filter={Uri.EscapeDataString($"{column.Key} in ({ids})")}"));
                        chunk = [];
                    }
                    foreach (var quoted in values.IdSample.Select(i => Erp.Kernel.Lists.ListFilterText.Quote(i.ToString())))
                    {
                        if (chunk.Count + 1 > Erp.Kernel.Lists.ListFilter.MaxInValues ||
                            $"{column.Key} in ({string.Join(", ", chunk.Append(quoted))})".Length > Erp.Kernel.Lists.ListFilter.MaxLength)
                        {
                            Flush();
                        }
                        chunk.Add(quoted);
                    }
                    Flush();
                }
            }
            foreach (var column in list.Columns.Where(c => c.Groupable))
            {
                listWork.Add(() => SendListAsync(state, admin, listEndpoint!, $"{list.Endpoint}?take=200&groupBy={column.Key}"));
            }
            foreach (var column in list.Columns.Where(c => c.Sortable))
            {
                foreach (var direction in new[] { "", "-" })
                {
                    var sort = direction + column.Key;
                    listWork.Add(() => SendListAsync(state, admin, listEndpoint!, $"{list.Endpoint}?take=200&sort={Uri.EscapeDataString(sort)}"));
                    var bound = binding.Columns.Single(c => c.Key == column.Key);
                    var forged = ForgedCursorValues(bound.ValueType, values).Take(12).ToList();
                    foreach (var forgedValue in forged)
                    {
                        foreach (var id in values.IdSample.Take(6))
                        {
                            var cursor = ForgeCursor(sort, forgedValue, id);
                            listWork.Add(() => SendListAsync(state, admin, listEndpoint!, $"{list.Endpoint}?take=200&sort={Uri.EscapeDataString(sort)}&after={Uri.EscapeDataString(cursor)}"));
                        }
                    }
                }
            }
        }
        Assert.True(listsAttacked >= Ratchet.Min("rules.listsChecked"), $"{listsAttacked} lists attacked through their query contract");
        var listAttacksBefore = state.Requests;
        await Parallel.ForEachAsync(listWork, new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests }, async (item, _) => await item());
        var listQueryAttacks = listWork.Count;

        Phase($"registered lists through their query contract: {listQueryAttacks} attacks, {state.Requests - listAttacksBefore} requests");

        // Phase 2c: what list answers add up to (totals, group counts, sums) must be the asking
        // tenant's own, after the other tenant sent exactly the same request first; both ways.
        var listAnswers = new List<ListAnswers.Result>();
        {
            var victimAdmin = activity.AdminClient;
            var ownIds = (await TenantSnapshot.TakeAsync(Env, a.Id, null, a.Code)).AllIds.ToHashSet();
            var victimIds = (await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code)).AllIds.ToHashSet();
            var ownStrings = (await VictimValues.ReadAsync(Env, own, b.Id)).Strings;
            listAnswers.Add(await ListAnswers.RunAsync(catalog, victimAdmin, admin.Client, ownIds, victimIds, values.Strings, "tenant B asks first, tenant A judged"));
            listAnswers.Add(await ListAnswers.RunAsync(catalog, admin.Client, victimAdmin, victimIds, ownIds, ownStrings, "tenant A asks first, tenant B judged"));
            // The same in Arabic with Arabic-Indic digits, both ways.
            listAnswers.Add(await ListAnswers.RunAsync(catalog, activity.ArabicClient, arabicAdmin.Client, ownIds, victimIds, values.Strings, "in Arabic, tenant B asks first, tenant A judged"));
            listAnswers.Add(await ListAnswers.RunAsync(catalog, arabicAdmin.Client, activity.ArabicClient, victimIds, ownIds, ownStrings, "in Arabic, tenant A asks first, tenant B judged"));
        }
        Phase($"list answers judged against each tenant's own rows: {listAnswers.Sum(r => r.Queries)} queries, {listAnswers.Sum(r => r.Discriminating)} with different true answers, {listAnswers.Sum(r => r.RowsWalked)} rows walked; process-wide state fingerprinted in {stateBefore.Count} lines from {stateRoots.Count} roots");

        // Phase 3: every tenant B text value in every string field of every request body. Values the
        // attacker managed to store in its own tenant are its own data from then on.
        foreach (var endpoint in endpoints.Where(e => e.HasBody))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
            {
                continue;
            }
            var reachable = endpoint.IsAnonymous ? new[] { anonymous } : [admin];
            var ownRoute = endpoint.RouteParameters.Count == 0 ? "" : await PickOwnRouteValueAsync(admin, endpoint, ownRouteValues);
            var path = endpoint.Path(_ => ownRoute);
            var signIn = endpoint.Name == "auth.signIn";
            foreach (var field in openApi.StringLeaves(schema))
            {
                var batch = new List<(Attacker Attacker, HttpRequestMessage Request, string Label, string Value)>();
                foreach (var value in values.Strings)
                {
                    foreach (var attacker in reachable)
                    {
                        var n = counter++;
                        // The attacked field carries tenant B's value as is; every other field conforms to
                        // its documented constraints so the request gets past validation to the handler.
                        var body = openApi.BuildBody(schema, (leaf, type, format, name) =>
                            name == field && type == "string" && format != "uuid" ? value : openApi.Conform(leaf, Leaf(type, format, name, victimIdTexts, b, signIn, n))) as JsonObject ?? [];
                        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), path)
                        {
                            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                        };
                        batch.Add((attacker, request, $"{path} [body {field}={Short(value)}]", value));
                    }
                }
                // The field's requests go four at a time (signing in and out, which change the
                // attackers' own sessions, one at a time). Every answer is judged once the field's
                // requests are done, after the values its successful writes stored in the
                // attacker's own records count as the attacker's own: the requests write the same
                // record at the same time, so an answer may already show a value a concurrent
                // request stored there.
                var answers = new (int Status, string Text, string Headers)[batch.Count];
                var together = endpoint.Name is "auth.signIn" or "auth.signOut" ? 1 : AttackParallelism.Requests;
                await Parallel.ForEachAsync(Enumerable.Range(0, batch.Count), new ParallelOptions { MaxDegreeOfParallelism = together }, async (i, _) =>
                {
                    using var request = batch[i].Request;
                    answers[i] = await state.SendUnjudgedAsync(batch[i].Attacker, endpoint, request);
                });
                for (var i = 0; i < batch.Count; i++)
                {
                    if (answers[i].Status is >= 200 and < 300)
                    {
                        state.Stored.Add(batch[i].Value);
                    }
                }
                for (var i = 0; i < batch.Count; i++)
                {
                    state.JudgeAnswer(batch[i].Attacker, endpoint, batch[i].Label, answers[i].Status, answers[i].Text, answers[i].Headers, [batch[i].Value]);
                    state.BodyValueAttacks++;
                }
            }
        }

        Phase("tenant B values in body fields");

        await concurrent.CancelAsync();
        await concurrentReader;

        // Tenant B reads everything once more, judged against everything tenant A now holds
        // (including what the attack created).
        var ownAfter = await TenantSnapshot.TakeAsync(Env, a.Id, null, a.Code);
        activity.Watch(new MarkerSet(ownAfter, await VictimValues.ReadAsync(Env, ownAfter, b.Id, examples), [.. values.Strings, .. state.Stored], b.Canary));
        await activity.ReadAsync(victim, values, "tenant B reads after the attack");
        Phase($"tenant B activity: {activity.Requests} requests ({activity.ConcurrentRequests} concurrent with the attack), " +
              $"{activity.SuccessfulWrites} successful own writes, {activity.ReverseChecks} responses judged for tenant A markers");

        var changed = await activity.ChangedByOthersAsync();
        var (rootsAfter, _) = ProcessState.LiveRoots(Env.Factory);
        var stateAfter = ReachableState.Fingerprint(rootsAfter, productAssemblies);
        var stateChanges = ReachableState.Differences(stateBefore, stateAfter);

        // The reviewed cross-tenant lookups ran only from their reviewed callers, and the trace saw
        // each of them run from that caller (a blind trace would pass anything).
        var traced = SqlTrace.For(Env, traceMark);
        var misuse = ReviewedCallers.Misuse(traced);
        var untraced = ReviewedCallers.Read().Where(e => e.Kind == "caller")
            .Where(e => !traced.Any(c => c.Function == e.Function && c.Caller == e.Value))
            .Select(e => $"{e.Function} from {e.Value}").ToList();

        // The tenant came only from the session: every binding inside a request bound the
        // principal's tenant (or is the session lookup, or a reviewed endpoint), and nothing but the
        // kernel's session changed a session setting. The trace must have seen both kinds of event
        // from the product itself, or it may be blind.
        var binds = SqlTrace.BindsFor(Env, traceSnapshot);
        var settings = SqlTrace.SettingsFor(Env, traceSnapshot);
        var traceBlindSpots = new List<string>();
        if (!binds.Any(x => x.ResolvingSession))
        {
            traceBlindSpots.Add("no tenant binding by the session lookup was traced");
        }
        if (!binds.Any(x => !x.ResolvingSession && x.PrincipalTenant is null))
        {
            traceBlindSpots.Add("no tenant binding by an anonymous request (sign-in) was traced");
        }
        if (!settings.Any(x => x.FromSession && x.Caller == typeof(Erp.Kernel.Data.ErpDbSession).FullName))
        {
            traceBlindSpots.Add("no setting statement from the kernel's session was traced with its caller");
        }
        if (SqlTrace.ReadOnlyFor(Env, traceSnapshot).Count == 0 || !binds.Any(x => x.ReadOnlyRequest))
        {
            traceBlindSpots.Add("no read-only request and its read-only transaction were traced");
        }
        // The tenant each statement runs under, by value. The trace must have read the kernel's own
        // binding values, for signed-in requests and for anonymous ones, or it may be blind.
        var changes = SqlTrace.ChangesFor(Env, traceSnapshot);
        var tenantValues = changes.Where(c => string.Equals(c.Change.Name, SqlSettings.TenantSetting, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!tenantValues.Any(c => SqlTrace.PrincipalOf(c) is not null && !c.Change.Computed && !string.IsNullOrEmpty(c.Change.Value)))
        {
            traceBlindSpots.Add("no tenant value set by a signed-in request was read from its statement's parameters");
        }
        if (!tenantValues.Any(c => SqlTrace.PrincipalOf(c) is null && !c.Change.Computed && !string.IsNullOrEmpty(c.Change.Value)))
        {
            traceBlindSpots.Add("no tenant value set by sign-in or the session lookup was read from its statement's parameters");
        }

        var environmentAfter = EnvironmentVariables();
        var environmentChanges = environmentBefore.Keys.Union(environmentAfter.Keys)
            .Where(k => environmentBefore.GetValueOrDefault(k) != environmentAfter.GetValueOrDefault(k))
            .Select(k => $"environment variable {k} changed during the attack").ToList();

        await KeepArabicAsync("after the attack");
        if (!(await arabicAnonymous.Client.GetStringAsync("/api/auth/session")).Contains("\"authenticated\":false", StringComparison.Ordinal))
        {
            arabicBlind.Add($"after the attack: {arabicAnonymous.Name} is signed in");
        }
        foreach (var attacker in attackers)
        {
            attacker.Client.Dispose();
        }
        activity.Dispose();
        return new IsolationReport([.. state.Leaks, .. activity.Leaks, .. pairs.Leaks], state.ServerErrors.Select(Env.Factory.ErrorLog.Annotate).ToList(), changed, uncovered, attacked.Count, state.Requests + pairs.AttackerRequests, probesRun)
        {
            WritePairs = pairs.Pairs,
            WritePairEndpoints = pairs.Endpoints,
            EnumVariantPairs = pairs.VariantPairs,
            EnumValuesAttacked = pairs.EnumValuesAttacked ?? [],
            ArabicAttackRequests = attackers.Where(x => x.Arabic).Sum(x => x.Requests) + pairs.ArabicRequests,
            VictimArabicRequests = activity.ArabicRequests,
            ArabicWritePairs = pairs.ArabicPairs,
            ArabicBlindSpots = arabicBlind,
            AttackerUnsuccessfulWrites = pairs.AttackerUnsuccessfulWrites,
            WritePairBlindSpots = pairs.BlindSpots,
            Oracles = state.Oracles,
            BindViolations = TenantBindingRules.BindViolations(binds),
            WritableReads = TenantBindingRules.WritableReads(binds, SqlTrace.ReadOnlyFor(Env, traceSnapshot)),
            SettingViolations = TenantBindingRules.SettingViolations(settings),
            TenantValueViolations = TenantBindingRules.TenantValueViolations(changes, binds, SqlTrace.PrincipalOf),
            TenantValuesJudged = tenantValues.Count,
            StatementsObserved = SqlTrace.ObservedFor(Env),
            UnobservedStatements = SqlTrace.UnobservedFor(Env, traceSnapshot),
            InputEnumerations = Env.Factory.Services.GetRequiredService<RequestInputRecorder>().Enumerations,
            EnvironmentChanges = environmentChanges,
            TraceBlindSpots = traceBlindSpots,
            BindsJudged = binds.Count,
            SettingStatementsJudged = settings.Count,
            RequestStatementsTraced = SqlTrace.RequestStatementsSince(traceSnapshot),
            SwitchInputAttacks = state.SwitchAttacks,
            VictimRouteValuesReplayed = replayed,
            VictimPreTouches = activity.PreTouches,
            SwitchHeaderNames = switchInputs.Headers.Count,
            ResponsesHeaderJudged = state.HeadersJudged,
            LookupMisuse = misuse,
            UntracedFunctions = untraced,
            VictimValues = values.Ids.Count + values.Strings.Count,
            ParameterAttacks = state.ParameterAttacks,
            BodyValueAttacks = state.BodyValueAttacks,
            DifferentialChecks = state.DifferentialChecks,
            AttackerHeldSkips = state.AttackerHeldSkips,
            TracedLookups = traced.Count,
            VictimRequests = activity.Requests,
            VictimConcurrentRequests = activity.ConcurrentRequests,
            VictimWrites = activity.SuccessfulWrites,
            VictimWriteEndpoints = activity.WriteEndpoints,
            ReverseChecks = activity.ReverseChecks,
            VictimBlindSpots = activity.BlindSpots,
            ShapeEndpoints = shapes.Endpoints,
            ShapeAttacks = shapes.AttackerRequests,
            ShapeVictimRequests = shapes.VictimRequests,
            VictimUnsuccessfulWrites = activity.UnsuccessfulWrites,
            ListQueryAttacks = listQueryAttacks,
            ListRefusals = state.ListRefusals,
            ListAnswersWrong = listAnswers.SelectMany(r => r.Wrong).ToList(),
            ListAnswersBlind = listAnswers.SelectMany(r => r.Blind).ToList(),
            ListAnswerQueries = listAnswers.Sum(r => r.Queries),
            ListAnswersDiscriminating = listAnswers.Sum(r => r.Discriminating),
            ListAnswerPagesJudged = listAnswers.Sum(r => r.PagesJudged),
            ListAnswerOffsetPagesJudged = listAnswers.Sum(r => r.OffsetPagesJudged),
            StateChanges = stateChanges,
            StateLinesFingerprinted = stateBefore.Count,
            Phases = [.. phases, $"tenant B: {values.Ids.Count} ids ({values.IdSample.Count} sampled), {values.Strings.Count} text values, {values.Markers.Count} extra markers, {values.Probe.Count} probe values"],
        };
    }

    private static Dictionary<string, string?> EnvironmentVariables() =>
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => e.Value as string, StringComparer.Ordinal);

    public sealed record WritePairResult(int Pairs, int Endpoints, int AttackerRequests, IReadOnlyList<string> Leaks,
        IReadOnlyList<string> AttackerUnsuccessfulWrites, IReadOnlyList<string> BlindSpots, int VariantPairs = 0, IReadOnlyList<string>? EnumValuesAttacked = null,
        int ArabicPairs = 0, int ArabicRequests = 0);

    /// <summary>
    /// Write after write, in both directions, for every endpoint that changes data: tenant B writes,
    /// tenant A writes (administrator cookie, then bearer token, each right after a tenant B
    /// write), tenant B writes again, tenant A reads every GET, tenant A writes once more and tenant
    /// B reads every GET. Tenant A's side is a <see cref="TenantActivity"/> of its own (valid bodies
    /// on its own records) whose answers are judged for tenant B's markers.
    /// Then the same pairs once per variant of the body (<see cref="TenantActivity.VariantsOf"/>):
    /// every documented value of every enumerated field, tenant B and tenant A sending the same
    /// value, so the code behind each value (language ar, numerals arab: critic p04 round 4, plant
    /// L1) runs for both tenants back to back. Every variant must succeed on both sides at least
    /// once, or the phase reports itself blind.
    /// </summary>
    private static async Task<WritePairResult> WritePairsPhaseAsync(ErpTestEnvironment env, IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi,
        TenantActivity victimActivity, TenantSnapshot victimBefore, VictimValues values)
    {
        var a = env.TenantA;
        var b = env.TenantB;
        var ownA = await TenantSnapshot.TakeAsync(env, a.Id, a.Canary, a.Code);
        var victimNow = await TenantSnapshot.TakeAsync(env, b.Id, b.Canary, b.Code);
        var attacker = await TenantActivity.StartAsync(env, a, endpoints, openApi);
        attacker.Watch(new MarkerSet(victimNow, values));
        var pairs = 0;
        var variantPairs = 0;
        var arabicPairs = 0;
        var arabicRequests = 0;
        var enumValues = new SortedSet<string>(StringComparer.Ordinal);
        var blind = new List<string>();
        var writes = victimActivity.Writes;
        try
        {
            foreach (var endpoint in writes)
            {
                // Cookie, bearer token, and the Arabic session (every request in Arabic with
                // Arabic-Indic digits) on both sides: tenant B's Arabic write right before tenant
                // A's Arabic write runs the Arabic branch of the handler for both back to back.
                foreach (var (bearer, arabic) in Writers)
                {
                    var victimStatus = await victimActivity.WriteOneAsync(endpoint, victimNow, $"tenant B writes right before A's write{(arabic ? " (in Arabic)" : "")}", arabic: arabic);
                    var attackerStatus = await attacker.WriteOneAsync(endpoint, ownA, $"tenant A writes right after B's write ({WriterName(bearer, arabic)})", bearer, arabic: arabic);
                    if (victimStatus is >= 200 and < 300 && attackerStatus is >= 200 and < 300)
                    {
                        pairs++;
                        if (arabic)
                        {
                            arabicPairs++;
                        }
                    }
                }
                // In Arabic once more, every enumerated field at its last documented value (for the
                // shell's own preferences: Arabic with Arabic-Indic digits).
                if (victimActivity.LastValues(endpoint) is { } last)
                {
                    var victimStatus = await victimActivity.WriteOneAsync(endpoint, victimNow, $"tenant B writes right before A's write ({last}, in Arabic)", variant: last, arabic: true);
                    var attackerStatus = await attacker.WriteOneAsync(endpoint, ownA, $"tenant A writes right after B's write ({last}, {WriterName(false, true)})", variant: last, arabic: true);
                    if (victimStatus is >= 200 and < 300 && attackerStatus is >= 200 and < 300)
                    {
                        pairs++;
                        arabicPairs++;
                    }
                }
                foreach (var variant in victimActivity.VariantsOf(endpoint))
                {
                    var succeeded = false;
                    foreach (var bearer in new[] { false, true })
                    {
                        var victimStatus = await victimActivity.WriteOneAsync(endpoint, victimNow, $"tenant B writes right before A's write ({variant})", variant: variant);
                        var attackerStatus = await attacker.WriteOneAsync(endpoint, ownA, $"tenant A writes right after B's write ({variant}, {(bearer ? "bearer" : "cookie")})", bearer, variant);
                        if (victimStatus is >= 200 and < 300 && attackerStatus is >= 200 and < 300)
                        {
                            variantPairs++;
                            succeeded = true;
                        }
                    }
                    // Tenant B writes once more with the same value right after tenant A: judged for A's markers.
                    await victimActivity.WriteOneAsync(endpoint, victimNow, $"tenant B writes right after A's write ({variant})", variant: variant);
                    var key = $"{endpoint.Key}|{variant.Label}";
                    if (!succeeded || !victimActivity.AppliedVariants.Contains(key) || !attacker.AppliedVariants.Contains(key))
                    {
                        blind.Add($"{endpoint.Key} with {variant}: no write pair succeeded on both sides with the value in the body");
                    }
                    else if (variant.Leaf is { } leaf)
                    {
                        enumValues.Add($"{endpoint.Key} {leaf.Name}={variant.Value!.ToJsonString()}");
                    }
                }
                // An Arabic session's own preferences write (a variant) may have left it in English.
                await victimActivity.EnsureArabicAsync($"after the write pairs of {endpoint.Key}");
                await attacker.EnsureArabicAsync($"after the write pairs of {endpoint.Key}");
                if (victimActivity.VariantsOf(endpoint).Count > 0)
                {
                    // What the variants left in either tenant's records, read by the other.
                    await attacker.ReadRoundAsync(ownA, $"tenant A reads after B's variants of {endpoint.Key}");
                    await victimActivity.ReadRoundAsync(victimNow, $"tenant B reads after A's variants of {endpoint.Key}");
                }
                // Every enumerated field at its first value last: both tenants' records go back to
                // their first documented values (an edit and save copies the rest from the record).
                await victimActivity.WriteOneAsync(endpoint, victimNow, "tenant B writes right after A's write", variant: victimActivity.FirstValues(endpoint));
                await attacker.ReadRoundAsync(ownA, $"tenant A reads after B's {endpoint.Key}");
                await attacker.WriteOneAsync(endpoint, ownA, "tenant A writes before B reads", variant: attacker.FirstValues(endpoint));
                await victimActivity.ReadRoundAsync(victimNow, $"tenant B reads after A's {endpoint.Key}");
            }
            arabicRequests = attacker.ArabicRequests;
        }
        finally
        {
            attacker.Dispose();
        }
        blind.AddRange(attacker.BlindSpots);
        if (writes.Count > 0 && pairs == 0)
        {
            blind.Add("no write pair succeeded on both sides");
        }
        if (writes.Count > 0 && arabicPairs == 0)
        {
            blind.Add("no write pair in Arabic succeeded on both sides");
        }
        return new WritePairResult(pairs, writes.Count, attacker.Requests, attacker.Leaks, attacker.UnsuccessfulWrites, blind, variantPairs, enumValues.ToList(), arabicPairs, arabicRequests);
    }

    /// <summary>Who writes in each write pair: the administrator's cookie, its bearer token, and the
    /// Arabic administrator (<see cref="ArabicSession"/>).</summary>
    private static readonly (bool Bearer, bool Arabic)[] Writers = [(false, false), (true, false), (false, true)];

    private static string WriterName(bool bearer, bool arabic) => arabic ? "in Arabic, cookie" : bearer ? "bearer" : "cookie";

    /// <summary>Header names never used as a tenant switch probe: they carry the attacker's own
    /// credentials or frame the request itself.</summary>
    private static readonly HashSet<string> FramingHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Content-Type", "Content-Length", "Transfer-Encoding", "Connection", "Expect", "Upgrade", "TE",
        "Trailer", "Keep-Alive", "Proxy-Connection", "Proxy-Authorization",
    };

    /// <summary>Body property names tried on every body whether documented or not.</summary>
    private static readonly string[] GuessedBodyNames =
        ["tenantId", "tenant_id", "tenant", "companyId", "workspace", "workspaceId", "organizationId", "orgId", "ownerReference"];

    /// <summary>Header names tried on every endpoint whether the app reads them or not.</summary>
    private static readonly string[] GuessedHeaderNames =
        ["X-Tenant-Id", "X-Tenant", "Tenant-Id", "X-Company-Id", "X-Forwarded-Host", "X-Workspace", "X-Erp-Workspace", "X-Erp-Tenant", "Host"];

    public sealed record SwitchInputSet(IReadOnlyList<string> Headers, IReadOnlyList<string> Queries, IReadOnlyList<string> Cookies, IReadOnlyList<string> BodyNames);

    /// <summary>
    /// Every input a handler could take a tenant from: header, query and cookie names the running
    /// app read during the attack so far (<see cref="RequestInputRecorder"/>), names the source
    /// reads by literal, and the usual guesses. Each gets tenant B's id and then its code, one
    /// input per request, on every endpoint (with the attacker's own route ids, so the handler is
    /// reached); every uuid field of every body gets tenant B's id at once, and so do the guessed
    /// body names. Any tenant B data in the answer is a leak; the trace then also shows any
    /// binding to tenant B.
    /// </summary>
    private static async Task<SwitchInputSet> TenantSwitchPhaseAsync(ErpTestEnvironment env, IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi,
        List<Attacker> attackers, AttackState state, TenantSnapshot own, List<string> ownRouteValues, Erp.Kernel.Seeding.SeedTenant b,
        TenantActivity activity, TenantSnapshot victim)
    {
        var recorder = env.Factory.Services.GetRequiredService<RequestInputRecorder>();
        var source = SourceInputNames.Read();
        var headers = recorder.Headers.Concat(source.Headers).Concat(GuessedHeaderNames)
            .Where(h => !FramingHeaders.Contains(h)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var queries = recorder.QueryNames.Concat(source.Queries).Concat(GuessedQueryNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var cookies = recorder.Cookies.Concat(source.Cookies).Append("erp_tenant").Append("workspace")
            .Where(c => c != Erp.Kernel.Security.SessionAuthenticationDefaults.CookieName).Distinct(StringComparer.Ordinal).ToList();
        var bodyNames = GuessedBodyNames.Concat(queries.Where(q => q.Length > 2)).Distinct(StringComparer.Ordinal).ToList();
        var admin = attackers[0];
        var bearer = attackers[1];
        var values = new[] { b.Id.ToString(), b.Code };
        var n = 0;

        foreach (var endpoint in endpoints.Where(e => !e.Pattern.Contains("{*", StringComparison.Ordinal)))
        {
            await activity.TouchAsync(endpoint, victim, "tenant B reads right before A's tenant switch inputs");
            var ownRoute = endpoint.RouteParameters.Count == 0 ? "" : await PickOwnRouteValueAsync(admin, endpoint, ownRouteValues);
            var path = endpoint.Path(_ => ownRoute);
            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var signIn = endpoint.Name == "auth.signIn";
            HttpRequestMessage Request(string uri, Func<string, string?, string?, JsonNode?>? leaf = null, IEnumerable<(string Name, string Value)>? extra = null)
            {
                var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), uri);
                if (endpoint.HasBody || endpoint.Method == "DELETE")
                {
                    var i = Interlocked.Increment(ref n);
                    var body = schema is { } s
                        ? openApi.BuildBody(s, leaf ?? ((type, format, name) => OwnLeaf(type, format, name, own, i, signIn, env, b)), useDocumentedValues: true) as JsonObject ?? []
                        : [];
                    foreach (var (name, value) in extra ?? [])
                    {
                        body[name] = value;
                    }
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                }
                return request;
            }

            var work = new List<Func<Task>>();
            foreach (var value in values)
            {
                foreach (var header in headers)
                {
                    if (header.Equals("Host", StringComparison.OrdinalIgnoreCase) && value != b.Code)
                    {
                        continue;
                    }
                    var headerValue = header.Equals("Host", StringComparison.OrdinalIgnoreCase) ? $"{b.Code}.example" : value;
                    work.Add(async () =>
                    {
                        using var request = Request(path);
                        request.Headers.TryAddWithoutValidation(header, headerValue);
                        await state.SendAsync(admin, endpoint, request, $"{path} [header {header}: {headerValue}]", sent: [headerValue]);
                        state.CountSwitchAttack();
                    });
                }
                foreach (var query in queries)
                {
                    work.Add(async () =>
                    {
                        var uri = path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + Uri.EscapeDataString(query) + "=" + Uri.EscapeDataString(value);
                        using var request = Request(uri);
                        await state.SendAsync(admin, endpoint, request, $"{uri} [query {query}]", sent: [value]);
                        state.CountSwitchAttack();
                    });
                }
                foreach (var cookie in cookies)
                {
                    work.Add(async () =>
                    {
                        using var request = Request(path);
                        request.Headers.TryAddWithoutValidation("Cookie", $"{cookie}={value}");
                        await state.SendAsync(bearer, endpoint, request, $"{path} [cookie {cookie}={value}]", sent: [value]);
                        state.CountSwitchAttack();
                    });
                }
                if (endpoint.HasBody)
                {
                    work.Add(async () =>
                    {
                        // Every uuid field holds tenant B's id (or code), and so does every guessed name.
                        var i = Interlocked.Increment(ref n);
                        using var request = Request(path,
                            (type, format, name) => type == "string" && format == "uuid" ? value : OwnLeaf(type, format, name, own, i, signIn, env, b),
                            bodyNames.Select(name => (name, value)));
                        await state.SendAsync(admin, endpoint, request, $"{path} [every uuid field and guessed body name = {value}]", sent: [value]);
                        state.CountSwitchAttack();
                    });
                }
            }
            // Sign-out ends the attacker's session (SendAsync signs it in again), so it runs alone.
            var parallel = endpoint.Name == "auth.signOut" ? 1 : AttackParallelism.Requests;
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (item, _) => await item());
            await activity.TouchAsync(endpoint, victim, "tenant B reads right after A's tenant switch inputs");
        }
        return new SwitchInputSet(headers, queries, cookies, bodyNames);
    }

    /// <summary>Leaf values from the attacker's own tenant, so a handler gets as far as it would
    /// for a real request of tenant A (sign-in uses tenant A's administrator). Fields whose values
    /// the document lists take the first listed value.</summary>
    private static JsonNode? OwnLeaf(string type, string? format, string? name, TenantSnapshot own, int n, bool signIn, ErpTestEnvironment env, Erp.Kernel.Seeding.SeedTenant b)
    {
        var lower = name?.ToLowerInvariant() ?? "";
        return type switch
        {
            "string" when format == "uuid" => TenantActivity.OwnIdFor(lower, own),
            // A sign-in that carries a new password changes the attacker's own password: once the
            // request counter made "switch-<n>" long enough to be a valid password, every later
            // reconnect failed. The switch inputs are judged on a plain, successful sign-in.
            "string" when signIn && lower.Contains("newpassword") => null,
            "string" when lower.Contains("email") => signIn ? env.Email(env.TenantA, "admin") : $"switch{n}@{env.TenantA.EmailDomain}",
            "string" when lower == "workspace" => env.TenantA.Code,
            "string" when lower == "password" => signIn ? ErpTestEnvironment.Password : "Switch-Password-2026!",
            "string" when lower == "language" => "en",
            "string" when lower.Contains("permission") => "identity.users.read",
            "string" when format == "date-time" => DateTimeOffset.UtcNow.ToString("O"),
            "string" => $"switch-{n}",
            "integer" => 0,
            "number" => "1",
            "boolean" => true,
            _ => null,
        };
    }

    /// <summary>Every value the API document enumerates for a parameter that no tenant holds in
    /// any text column of any tenant table (read with the superuser).</summary>
    private static async Task<IReadOnlySet<string>> UnheldPublishedValuesAsync(ErpTestEnvironment env, IEnumerable<ApiParameter> parameters)
    {
        var published = parameters.SelectMany(p => p.Enum ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var unheld = new HashSet<string>(published, StringComparer.Ordinal);
        if (published.Length == 0)
        {
            return unheld;
        }
        await using var admin = await env.OpenAdminAsync();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            foreach (var column in await DbCatalog.ColumnsAsync(admin, table))
            {
                if (column.Name == "tenant_id" || column.Type.EndsWith("[]", StringComparison.Ordinal) ||
                    !(column.Type.StartsWith("text", StringComparison.Ordinal) || column.Type.StartsWith("character varying", StringComparison.Ordinal) || column.Type == "citext"))
                {
                    continue;
                }
                var held = await DbCatalog.ReadAsync(admin, $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE \"{column.Name}\"::text = ANY(@values)",
                    r => r.GetString(0), ("values", published));
                unheld.ExceptWith(held);
            }
        }
        return unheld;
    }

    /// <summary>Of the given text values, those a tenant holds in any text column of any tenant table.</summary>
    private static async Task<IReadOnlySet<string>> HeldByAsync(ErpTestEnvironment env, Guid tenant, IReadOnlyList<string> candidates)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return held;
        }
        var values = candidates.ToArray();
        await using var admin = await env.OpenAdminAsync();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            foreach (var column in await DbCatalog.ColumnsAsync(admin, table))
            {
                if (column.Name == "tenant_id" || column.Type.EndsWith("[]", StringComparison.Ordinal) ||
                    !(column.Type.StartsWith("text", StringComparison.Ordinal) || column.Type.StartsWith("character varying", StringComparison.Ordinal) || column.Type == "citext"))
                {
                    continue;
                }
                held.UnionWith(await DbCatalog.ReadAsync(admin,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND \"{column.Name}\"::text = ANY(@values)",
                    r => r.GetString(0), ("t", tenant), ("values", values)));
            }
        }
        return held;
    }

    /// <summary>Values to send in one parameter: ids for uuid parameters, every value for text,
    /// nothing for numbers and flags (binding rejects text there before any handler runs).</summary>
    private static IEnumerable<string> ValuesFor(ApiParameter parameter, VictimValues values) => parameter switch
    {
        { Format: "uuid" } => values.IdSample.Select(i => i.ToString()),
        { Type: "string" } => values.All,
        _ => [],
    };

    /// <summary>One of tenant A's own ids for the endpoint's route, preferring one the attacker can
    /// open, so the query and body attacks reach the handler.</summary>
    private static async Task<string> PickOwnRouteValueAsync(Attacker attacker, ApiEndpoint endpoint, IReadOnlyList<string> ownValues)
    {
        foreach (var value in ownValues)
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, endpoint.Path(_ => value));
            using var response = await attacker.Client.SendAsync(probe);
            if (response.IsSuccessStatusCode)
            {
                return value;
            }
        }
        return ownValues.FirstOrDefault() ?? Guid.NewGuid().ToString();
    }

    private static async Task SendListAsync(AttackState state, Attacker attacker, ApiEndpoint endpoint, string uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var status = await state.SendAsync(attacker, endpoint, request, uri);
        if (status != 200)
        {
            lock (state) state.ListRefusals.Add($"{uri} → {status}");
        }
    }

    /// <summary>Tenant B's values of a sort key's type for a forged cursor.</summary>
    private static IEnumerable<object?> ForgedCursorValues(Type type, VictimValues values)
    {
        // A null key only where the column can hold one (a non-nullable key refuses it, rightly).
        var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        IEnumerable<object?> forged =
            t == typeof(string) ? values.Strings :
            t == typeof(Guid) ? values.IdSample.Cast<object?>() :
            t == typeof(DateTimeOffset) ? [DateTimeOffset.UtcNow, DateTimeOffset.UnixEpoch] :
            t == typeof(bool) ? [true, false] :
            t == typeof(int) || t == typeof(long) || t == typeof(decimal) ? [0, 1, int.MaxValue] :
            [];
        return nullable ? forged.Prepend(null) : forged;
    }

    /// <summary>A cursor as the list framework writes it (base64url JSON of the sort, the key
    /// values and the id), filled with tenant B's values: it carries no tenant, so it may only ever
    /// move within the attacker's own rows.</summary>
    private static string ForgeCursor(string sort, object? value, Guid id)
    {
        var json = new JsonObject
        {
            ["s"] = sort,
            ["k"] = new JsonArray(value switch
            {
                null => null,
                DateTimeOffset d => JsonValue.Create(d.ToString("O")),
                bool flag => JsonValue.Create(flag),
                IFormattable f => JsonValue.Create(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)),
                _ => JsonValue.Create(value.ToString()),
            }),
            ["i"] = id.ToString(),
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Short(string value) => value.Length <= 40 ? value : value[..40] + "…";

    private enum Variant
    {
        Plain,
        TenantHeaders,
        TenantQuery,
    }

    private static HttpRequestMessage BuildRequest(
        ApiEndpoint endpoint, string path, Variant variant, JsonElement? bodySchema, OpenApiDocument openApi,
        TenantSnapshot victim, Erp.Kernel.Seeding.SeedTenant b, bool signIn, ref int counter)
    {
        var victimIds = victim.AllIds.Select(i => i.ToString()).ToList();
        var uri = path;
        if (variant == Variant.TenantQuery)
        {
            var id = victimIds[counter % victimIds.Count];
            uri += $"?tenantId={b.Id}&tenant={b.Code}&tenant_id={b.Id}&companyId={b.Id}&id={id}&userId={id}&search={b.Canary}&workspace={b.Code}";
        }
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), uri);
        if (variant == Variant.TenantHeaders)
        {
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Tenant", b.Code);
            request.Headers.TryAddWithoutValidation("Tenant-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Company-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", $"{b.Code}.example");
            request.Headers.TryAddWithoutValidation("Cookie", $"erp_tenant={b.Id}");
        }
        if (endpoint.HasBody || endpoint.Method == "DELETE")
        {
            var n = counter++;
            var body = bodySchema is { } schema
                ? openApi.BuildBody(schema, (leaf, type, format, name) => openApi.Conform(leaf, Leaf(type, format, name, victimIds, b, signIn, n))) as JsonObject ?? []
                : [];
            body["tenantId"] = b.Id.ToString();
            body["tenant_id"] = b.Id.ToString();
            body["companyId"] = b.Id.ToString();
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        counter++;
        return request;
    }

    /// <summary>Leaf values: tenant B ids wherever an id is expected; tenant B e-mail and workspace
    /// for sign-in (phase 3 sends every B text value through every string field).</summary>
    private static JsonNode? Leaf(string type, string? format, string? name, List<string> victimIds, Erp.Kernel.Seeding.SeedTenant b, bool signIn, int n)
    {
        var lower = name?.ToLowerInvariant() ?? "";
        return type switch
        {
            "string" when format == "uuid" => victimIds[n % victimIds.Count],
            "string" when lower.Contains("email") => signIn ? $"admin@{b.EmailDomain}" : $"attack{n}@attack-{n}.example",
            "string" when lower == "workspace" => signIn ? b.Code : "alpha",
            "string" when lower == "password" => "Attack-Password-1",
            "string" when lower == "language" => "en",
            "string" when lower.Contains("permission") => "identity.users.read",
            "string" when format == "date-time" => DateTimeOffset.UtcNow.ToString("O"),
            "string" => $"attack-{n}",
            "integer" => 0,
            "number" => "1",
            "boolean" => true,
            _ => null,
        };
    }

    /// <summary>What the attack has seen so far and how it judges a response.</summary>
    /// <summary>The differential check's normalisation, one pass and two (for its own test).</summary>
    internal static (string OnePass, string TwoPasses) NormalizationsOf(string text, string first, string second) =>
        (AttackState.NormalizeBoth(text, first, second), AttackState.Normalize(AttackState.Normalize(text, first), second));

    private sealed class AttackState(TenantSnapshot victim, VictimValues values)
    {
        private readonly List<string> _victimIds = values.Ids.Select(i => i.ToString()).ToList();
        private readonly MarkerSearch _valueMarkers = new(values.Markers);

        private readonly Lock _lock = new();
        private int _requests;
        private int _parameterAttacks;
        private int _differentialChecks;
        private int _headersJudged;
        private int _switchAttacks;

        /// <summary>Requests that carried tenant B's id or code in one header, query, cookie or body input.</summary>
        public int SwitchAttacks => _switchAttacks;

        public void CountSwitchAttack() => Interlocked.Increment(ref _switchAttacks);

        /// <summary>Responses whose every header was judged (with the body).</summary>
        public int HeadersJudged => _headersJudged;

        public List<string> Leaks { get; } = [];
        public List<string> ServerErrors { get; } = [];
        public List<string> Oracles { get; } = [];

        /// <summary>List attacks the framework refused (each must be answered, not refused: a
        /// refusal would hide the query from the attack).</summary>
        public List<string> ListRefusals { get; } = [];
        public int Requests { get => _requests; set => _requests = value; }
        public int ParameterAttacks => _parameterAttacks;
        public int BodyValueAttacks { get; set; }
        public int DifferentialChecks => _differentialChecks;

        /// <summary>Tenant B text values the attacker wrote into its own tenant (phase 3).</summary>
        public HashSet<string> Stored { get; } = new(StringComparer.Ordinal);

        public async Task<int> SendAsync(Attacker attacker, ApiEndpoint endpoint, HttpRequestMessage request, string label, IReadOnlyCollection<string>? sent = null)
        {
            using var response = await attacker.Client.SendAsync(request);
            var text = await ResponseText.ReadAsync(response);
            Interlocked.Increment(ref _requests);
            attacker.Count();
            var status = (int)response.StatusCode;
            Judge(attacker, endpoint, label, status, text, ResponseHeaders.Text(response), sent ?? []);
            if (endpoint.Name == "auth.signOut" && !attacker.Anonymous)
            {
                await attacker.ConnectAsync();
            }
            return status;
        }

        /// <summary>Send without judging (the caller judges with <see cref="JudgeAnswer"/>).</summary>
        public async Task<(int Status, string Text, string Headers)> SendUnjudgedAsync(Attacker attacker, ApiEndpoint endpoint, HttpRequestMessage request)
        {
            using var response = await attacker.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Interlocked.Increment(ref _requests);
            attacker.Count();
            var answer = ((int)response.StatusCode, text, ResponseHeaders.Text(response));
            if (endpoint.Name == "auth.signOut" && !attacker.Anonymous)
            {
                await attacker.ConnectAsync();
            }
            return answer;
        }

        /// <summary>Judges an answer sent by <see cref="SendUnjudgedAsync"/>.</summary>
        public void JudgeAnswer(Attacker attacker, ApiEndpoint endpoint, string label, int status, string text, string headers, IReadOnlyCollection<string> sent) =>
            Judge(attacker, endpoint, label, status, text, headers, sent);

        /// <summary>One tenant B value in one parameter. For a GET, the same request with a value
        /// that exists nowhere must get the same answer.</summary>
        public async Task ParameterAttackAsync(Attacker attacker, ApiEndpoint endpoint, string value, ApiParameter parameter,
            Func<string, string> uriFor, JsonElement? bodySchema, OpenApiDocument openApi, Erp.Kernel.Seeding.SeedTenant b, int n)
        {
            Interlocked.Increment(ref _parameterAttacks);
            var uri = uriFor(value);
            var (status, text, headers) = await RawAsync(attacker, endpoint, uri, bodySchema, openApi, b, n);
            Judge(attacker, endpoint, $"{uri} [{parameter.In} {parameter.Name}]", status, text, headers, [value]);
            if (endpoint.Method != "GET")
            {
                return;
            }
            if (AttackerHeld.Contains(value))
            {
                Interlocked.Increment(ref _attackerHeldSkips);
                return;
            }
            var control = ControlFor(value, parameter);
            var (controlStatus, controlText, controlHeaders) = await RawAsync(attacker, endpoint, uriFor(control), bodySchema, openApi, b, n);
            Interlocked.Increment(ref _differentialChecks);
            // Both values are scrubbed from both answers, so a value that is also an ordinary word
            // in every answer ("user" in "Users and access") is treated the same on both sides.
            var normalized = NormalizeBoth(text, value, control);
            var controlNormalized = NormalizeBoth(controlText, control, value);
            if ((status != controlStatus || normalized != controlNormalized) && (Stamped(headers) || Stamped(controlHeaders)))
            {
                // A printed document shows the minute it was printed (in its own language and digits,
                // so not every reader can scrub it): two answers a moment apart on either side of a
                // minute differ by it alone. The pair is asked once more; an answer that tells the
                // values apart does so again.
                (status, text, var retryHeaders) = await RawAsync(attacker, endpoint, uri, bodySchema, openApi, b, n);
                Judge(attacker, endpoint, $"{uri} [{parameter.In} {parameter.Name}, asked again]", status, text, retryHeaders, [value]);
                (controlStatus, controlText, _) = await RawAsync(attacker, endpoint, uriFor(control), bodySchema, openApi, b, n);
                normalized = NormalizeBoth(text, value, control);
                controlNormalized = NormalizeBoth(controlText, control, value);
            }
            if (status != controlStatus || normalized != controlNormalized)
            {
                lock (_lock) Oracles.Add($"{attacker.Name} → GET {uri} [{parameter.In} {parameter.Name}]: {status} {Short(normalized, 160)} " +
                            $"but for a value that exists nowhere ({control}) {controlStatus} {Short(controlNormalized, 160)}; first difference: " +
                            $"{Short(normalized[FirstDifference(normalized, controlNormalized)..], 160)} | {Short(controlNormalized[FirstDifference(normalized, controlNormalized)..], 160)}");
            }
        }

        private static int FirstDifference(string a, string b)
        {
            var i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            return Math.Max(0, Math.Min(i, Math.Min(a.Length, b.Length)) - 40);
        }

        private static bool Stamped(string headers) =>
            headers.Contains(ResponseText.PrintedAtHeader + ":", StringComparison.OrdinalIgnoreCase);

        /// <summary>A value of the same shape that exists in no tenant: every letter and digit
        /// replaced at random, punctuation kept (so "a.b@c.example" stays file-like and e-mail-like).</summary>
        /// <summary>Published values (a parameter's enumeration) that no tenant holds in any text column.</summary>
        public IReadOnlySet<string> UnheldPublished { get; set; } = new HashSet<string>();

        /// <summary>Tenant B's text values tenant A holds itself when the parameter phase starts.</summary>
        public IReadOnlySet<string> AttackerHeld { get; set; } = new HashSet<string>();

        private int _attackerHeldSkips;

        /// <summary>Differentials not made because tenant A held the value itself.</summary>
        public int AttackerHeldSkips => _attackerHeldSkips;

        /// <summary>The value that exists nowhere to compare with. For a parameter whose values the
        /// document enumerates, any other text is refused by validation, so a random value would
        /// tell nothing; the control is another published value that no tenant holds (the
        /// enumeration's own "exists nowhere"). Without one, a random value as for any parameter.</summary>
        private string ControlFor(string value, ApiParameter parameter)
        {
            if (parameter.Enum is { } members && members.Contains(value, StringComparer.Ordinal) &&
                members.FirstOrDefault(m => m != value && UnheldPublished.Contains(m)) is { } other)
            {
                return other;
            }
            return ControlFor(value);
        }

        private static string ControlFor(string value)
        {
            if (Guid.TryParse(value, out _))
            {
                return Guid.NewGuid().ToString();
            }
            var random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(value.Length);
            var chars = value.Select((c, i) => char.IsLetter(c) ? (char)('a' + random[i] % 26) : char.IsDigit(c) ? (char)('0' + random[i] % 10) : c).ToArray();
            return new string(chars);
        }

        private async Task<(int Status, string Text, string Headers)> RawAsync(Attacker attacker, ApiEndpoint endpoint, string uri, JsonElement? bodySchema,
            OpenApiDocument openApi, Erp.Kernel.Seeding.SeedTenant b, int n)
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), uri);
            if (endpoint.HasBody)
            {
                var body = bodySchema is { } schema
                    ? openApi.BuildBody(schema, (leaf, type, format, name) => openApi.Conform(leaf, Leaf(type, format, name, _victimIds, b, false, n))) as JsonObject ?? []
                    : [];
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await attacker.Client.SendAsync(request);
            Interlocked.Increment(ref _requests);
            attacker.Count();
            var text = await ResponseText.ReadAsync(response);
            if (endpoint.Name == "auth.signOut" && !attacker.Anonymous)
            {
                await attacker.ConnectAsync();
            }
            return ((int)response.StatusCode, text, ResponseHeaders.Text(response));
        }

        /// <summary>Judges the body and every response header for tenant B's markers.</summary>
        private void Judge(Attacker attacker, ApiEndpoint endpoint, string label, int status, string text, string headers, IReadOnlyCollection<string> sent)
        {
            var where = $"{attacker.Name} → {endpoint.Method} {label} → {status}";
            Interlocked.Increment(ref _headersJudged);
            if (FindMarker(text, sent) is { } marker)
            {
                lock (_lock) Leaks.Add($"{where}: response contains tenant B marker {marker}");
            }
            else if (FindMarker(headers, sent) is { } headerMarker)
            {
                var line = headers.Split('\n').FirstOrDefault(h => FindMarker(h, sent) is not null) ?? "";
                lock (_lock) Leaks.Add($"{where}: response header contains tenant B marker {headerMarker} ({Short(line, 200)})");
            }
            if (status >= 500)
            {
                lock (_lock) ServerErrors.Add($"{where}: {text[..Math.Min(300, text.Length)]}");
            }
        }

        /// <summary>A tenant B marker in the text, after removing the values the attacker sent in
        /// this request (an echo is not a leak) and the B values it stored in its own tenant.</summary>
        public string? FindMarker(string text, IReadOnlyCollection<string> sent)
        {
            var scrubbed = text;
            foreach (var value in sent.Concat(Stored).Where(v => v.Length > 0).Distinct().OrderByDescending(v => v.Length))
            {
                scrubbed = Scrub(scrubbed, value);
            }
            return victim.FindMarker(scrubbed) ?? _valueMarkers.Find(scrubbed);
        }

        private static string Scrub(string text, string value)
        {
            text = text.Replace(value, "<sent>", StringComparison.OrdinalIgnoreCase);
            var escaped = JsonSerializer.Serialize(value)[1..^1];
            text = text.Replace(escaped, "<sent>", StringComparison.OrdinalIgnoreCase);
            return text.Replace(Uri.EscapeDataString(value), "<sent>", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Exactly <c>Normalize(Normalize(text, first), second)</c>, in one pass over a JSON
        /// body: the reader copies the body token by token, drops every <c>traceId</c> property and
        /// scrubs both values from each string value (never from a property name), and the writer
        /// writes it as <see cref="JsonNode.ToJsonString"/> does (compact, the default encoder,
        /// numbers as written). A body that is not JSON is scrubbed as text, as before. The
        /// differential check normalises both answers of every pair this way (twice each before:
        /// a parse, a tree, a copy of every array item and a serialisation per value).</summary>
        internal static string NormalizeBoth(string text, string first, string second)
        {
            var scrubFirst = Scrubber.For(first);
            var scrubSecond = Scrubber.For(second);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var reader = new Utf8JsonReader(bytes);
                if (!reader.Read())
                {
                    throw new JsonException("empty");
                }
                if (reader.TokenType == JsonTokenType.Null)
                {
                    // A body of JSON null: the tree is null and was written as nothing.
                    while (reader.Read())
                    {
                    }
                    return "";
                }
                var output = new ArrayBufferWriter<byte>(bytes.Length + 64);
                using (var writer = new Utf8JsonWriter(output))
                {
                    do
                    {
                        switch (reader.TokenType)
                        {
                            case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                            case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                            case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                            case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                            case JsonTokenType.PropertyName:
                                var name = reader.GetString()!;
                                if (name == "traceId")
                                {
                                    reader.Read();
                                    reader.Skip();
                                    continue;
                                }
                                writer.WritePropertyName(name);
                                break;
                            case JsonTokenType.String:
                                writer.WriteStringValue(scrubSecond.Apply(scrubFirst.Apply(reader.GetString()!)));
                                break;
                            case JsonTokenType.Number: writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true); break;
                            case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                            case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                            case JsonTokenType.Null: writer.WriteNullValue(); break;
                            default: throw new JsonException($"unexpected {reader.TokenType}");
                        }
                    }
                    while (reader.Read());
                }
                return Encoding.UTF8.GetString(output.WrittenSpan);
            }
            catch (JsonException)
            {
                return Normalize(scrubFirst.Apply(text), second);
            }
        }

        /// <summary><see cref="Scrub"/> for one value, its JSON-escaped and URI-escaped forms worked
        /// out once.</summary>
        private sealed record Scrubber(string Value, string Escaped, string UriEscaped)
        {
            public static Scrubber For(string value) => new(value, JsonSerializer.Serialize(value)[1..^1], Uri.EscapeDataString(value));

            public string Apply(string text) => text
                .Replace(Value, "<sent>", StringComparison.OrdinalIgnoreCase)
                .Replace(Escaped, "<sent>", StringComparison.OrdinalIgnoreCase)
                .Replace(UriEscaped, "<sent>", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The body with trace ids removed and the sent value replaced by a placeholder.
        /// In JSON only string values are scrubbed, never property names, so a short value such as
        /// "user" cannot hide a difference by matching a key.</summary>
        internal static string Normalize(string text, string value)
        {
            try
            {
                var node = JsonNode.Parse(text);
                node = ScrubValues(node, value);
                return node?.ToJsonString() ?? "";
            }
            catch (JsonException)
            {
                return Scrub(text, value);
            }
        }

        private static JsonNode? ScrubValues(JsonNode? node, string value)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove("traceId");
                    foreach (var (key, child) in obj.ToList())
                    {
                        obj[key] = ScrubValues(child, value);
                    }
                    return obj;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        array[i] = ScrubValues(array[i]?.DeepClone(), value);
                    }
                    return array;
                case JsonValue leaf when leaf.GetValueKind() == JsonValueKind.String:
                    return JsonValue.Create(Scrub(leaf.GetValue<string>(), value));
                default:
                    return node?.DeepClone();
            }
        }

        private static string Short(string value, int max) => value.Length <= max ? value : value[..max] + "…";
    }

    private sealed class Attacker(string name, Func<Task<HttpClient>> connect, bool anonymous = false, bool arabic = false)
    {
        public string Name { get; } = name;
        public HttpClient Client { get; private set; } = null!;

        /// <summary>Not signed in: signing out leaves it as it was.</summary>
        public bool Anonymous { get; } = anonymous;

        /// <summary>Every request runs in Arabic with Arabic-Indic digits (<see cref="ArabicSession"/>).</summary>
        public bool Arabic { get; } = arabic;

        private int _requests;

        /// <summary>Requests this attacker sent.</summary>
        public int Requests => _requests;

        public void Count() => Interlocked.Increment(ref _requests);

        public async Task ConnectAsync()
        {
            Client?.Dispose();
            Client = await connect();
        }

        /// <summary>A signed-in Arabic attacker's own write (its preferences, through the attack's
        /// valid bodies) may have changed its language or digits: they are set back.</summary>
        public async Task KeepArabicAsync()
        {
            if (Arabic && !Anonymous)
            {
                await ArabicSession.PrepareAsync(Client);
            }
        }
    }
}

public sealed record IsolationReport(
    IReadOnlyList<string> Leaks,
    IReadOnlyList<string> ServerErrors,
    IReadOnlyList<string> ChangedTables,
    IReadOnlyList<string> UncoveredSurfaces,
    int EndpointsAttacked,
    int Requests,
    int ProbesRun)
{
    /// <summary>GETs that answered differently for a tenant B value than for a value that exists nowhere.</summary>
    public IReadOnlyList<string> Oracles { get; init; } = [];

    /// <summary>Reviewed security-definer functions run from an unreviewed caller.</summary>
    public IReadOnlyList<string> LookupMisuse { get; init; } = [];

    /// <summary>Bindings inside a request to a tenant other than the request principal's.</summary>
    public IReadOnlyList<string> BindViolations { get; init; } = [];

    /// <summary>Requests that only read but bound a writable transaction.</summary>
    public IReadOnlyList<string> WritableReads { get; init; } = [];

    /// <summary>Setting statements inside a request sent by code other than the kernel's session.</summary>
    public IReadOnlyList<string> SettingViolations { get; init; } = [];

    /// <summary>Reasons the binding trace may have been blind.</summary>
    public IReadOnlyList<string> TraceBlindSpots { get; init; } = [];

    /// <summary>Tenant bindings inside requests that the trace judged.</summary>
    public int BindsJudged { get; init; }

    /// <summary>Setting statements inside requests that the trace judged.</summary>
    public int SettingStatementsJudged { get; init; }

    /// <summary>SQL statements sent inside requests while the attack ran (all environments).</summary>
    public int RequestStatementsTraced { get; init; }

    /// <summary>Requests carrying tenant B's id or code in one header, query, cookie or body input.</summary>
    public int SwitchInputAttacks { get; init; }

    /// <summary>Route attacks that replayed a value tenant B itself had put in a route (beyond the
    /// sampled tenant B ids).</summary>
    public int VictimRouteValuesReplayed { get; init; }

    /// <summary>Requests in which tenant B opened a GET route with the value tenant A was about to send.</summary>
    public int VictimPreTouches { get; init; }

    /// <summary>Header names that carried tenant B's id and code.</summary>
    public int SwitchHeaderNames { get; init; }

    /// <summary>Attack responses judged on body and every header.</summary>
    public int ResponsesHeaderJudged { get; init; }

    /// <summary>Reviewed caller entries the trace never saw (a blind trace).</summary>
    public IReadOnlyList<string> UntracedFunctions { get; init; } = [];

    public int VictimValues { get; init; }
    public int ParameterAttacks { get; init; }
    public int BodyValueAttacks { get; init; }
    public int DifferentialChecks { get; init; }

    /// <summary>Tenant B values not compared because tenant A held them itself by then.</summary>
    public int AttackerHeldSkips { get; init; }
    public int TracedLookups { get; init; }

    /// <summary>Requests tenant B sent while sharing the process with the attack.</summary>
    public int VictimRequests { get; init; }

    /// <summary>Tenant B requests sent concurrently with tenant A's parameter and body attacks.</summary>
    public int VictimConcurrentRequests { get; init; }

    /// <summary>Tenant B writes on its own records that succeeded.</summary>
    public int VictimWrites { get; init; }

    /// <summary>Endpoints that change data tenant B called on its own records.</summary>
    public int VictimWriteEndpoints { get; init; }

    /// <summary>Responses to tenant B judged for tenant A's markers.</summary>
    public int ReverseChecks { get; init; }

    /// <summary>Reasons tenant B's activity may have been blind.</summary>
    public IReadOnlyList<string> VictimBlindSpots { get; init; } = [];

    /// <summary>GETs whose enumerated answer shapes (format, language, …) were asked for by both tenants.</summary>
    public int ShapeEndpoints { get; init; }

    /// <summary>Tenant A's requests for an answer shape tenant B had asked for first.</summary>
    public int ShapeAttacks { get; init; }

    /// <summary>Tenant B's requests for answer shapes on its own records (before, during and after).</summary>
    public int ShapeVictimRequests { get; init; }

    /// <summary>Tenant B writes on its own records that did not succeed.</summary>
    public IReadOnlyList<string> VictimUnsuccessfulWrites { get; init; } = [];

    /// <summary>Attacks sent through the registered lists' query contract (filters with tenant B's
    /// values, grouping, sorting and forged cursors).</summary>
    public int ListQueryAttacks { get; init; }

    /// <summary>List attacks (grouping, sorting, forged cursors, id filters) that were refused
    /// instead of answered.</summary>
    public IReadOnlyList<string> ListRefusals { get; init; } = [];

    /// <summary>Write-after-write pairs (tenant B's valid write, then tenant A's) where both succeeded.</summary>
    public int WritePairs { get; init; }

    /// <summary>Endpoints that change data run through the write-after-write phase.</summary>
    public int WritePairEndpoints { get; init; }

    /// <summary>Write pairs (both tenants succeeding) sent with a variant of the body: one
    /// documented value of an enumerated field, the same in both tenants' bodies.</summary>
    public int EnumVariantPairs { get; init; }

    /// <summary>Requests tenant A's Arabic sessions (administrator and anonymous) sent, every one
    /// of them in Arabic with Arabic-Indic digits.</summary>
    public int ArabicAttackRequests { get; init; }

    /// <summary>Requests tenant B's Arabic administrator sent.</summary>
    public int VictimArabicRequests { get; init; }

    /// <summary>Write pairs in which both tenants wrote in Arabic.</summary>
    public int ArabicWritePairs { get; init; }

    /// <summary>Moments an Arabic session no longer answered in Arabic with Arabic-Indic digits.</summary>
    public IReadOnlyList<string> ArabicBlindSpots { get; init; } = [];

    /// <summary>Endpoint, field and documented value, for every value both tenants sent back to back.</summary>
    public IReadOnlyList<string> EnumValuesAttacked { get; init; } = [];

    /// <summary>Tenant A's own valid writes in the write-after-write phase that did not succeed.</summary>
    public IReadOnlyList<string> AttackerUnsuccessfulWrites { get; init; } = [];

    /// <summary>Reasons the write-after-write phase may have been blind.</summary>
    public IReadOnlyList<string> WritePairBlindSpots { get; init; } = [];

    /// <summary>List answers whose total, groups or sums disagree with the asking tenant's own
    /// rows (or whose rows are not its own), after the other tenant sent the same request.</summary>
    public IReadOnlyList<string> ListAnswersWrong { get; init; } = [];

    /// <summary>Lists where no query had different true answers in the two tenants.</summary>
    public IReadOnlyList<string> ListAnswersBlind { get; init; } = [];

    /// <summary>List queries whose answers were judged against the asking tenant's own rows.</summary>
    public int ListAnswerQueries { get; init; }

    /// <summary>Judged list queries whose true answers differ between the tenants.</summary>
    public int ListAnswersDiscriminating { get; init; }

    /// <summary>Keyset pages (first and following, both tenants) whose total and groups were judged.</summary>
    public int ListAnswerPagesJudged { get; init; }

    /// <summary>Pages of offset (skip) walks whose total, groups and rows were judged.</summary>
    public int ListAnswerOffsetPagesJudged { get; init; }

    /// <summary>Process-wide state (reachable from singletons and static fields) that changed
    /// while the tenants used the app.</summary>
    public IReadOnlyList<string> StateChanges { get; init; } = [];

    /// <summary>Lines of reachable process-wide state fingerprinted before the attack.</summary>
    public int StateLinesFingerprinted { get; init; }

    /// <summary>Requests whose SQL ran under a tenant other than the one they may run under,
    /// judged by the value each statement gave <c>app.tenant_id</c> (<see cref="TenantBindingRules.TenantValueViolations"/>).</summary>
    public IReadOnlyList<string> TenantValueViolations { get; init; } = [];

    /// <summary>Changes of <c>app.tenant_id</c> inside requests whose value the trace judged.</summary>
    public int TenantValuesJudged { get; init; }

    /// <summary>Statements inside requests whose parameters the trace captured.</summary>
    public int StatementsObserved { get; init; }

    /// <summary>Statements inside requests sent on a pool the platform did not build (the trace
    /// cannot read their values).</summary>
    public IReadOnlyList<string> UnobservedStatements { get; init; } = [];

    /// <summary>Product code that enumerated a request's headers, query or cookies, or read the raw
    /// query string: a name the attack cannot learn (<see cref="RequestInputRecorder"/>).</summary>
    public IReadOnlyList<string> InputEnumerations { get; init; } = [];

    /// <summary>Environment variables of the process that changed while the attack ran.</summary>
    public IReadOnlyList<string> EnvironmentChanges { get; init; } = [];

    /// <summary>Requests and elapsed time after each phase.</summary>
    public IReadOnlyList<string> Phases { get; init; } = [];
}
