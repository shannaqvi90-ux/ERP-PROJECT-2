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
            $"{report.SwitchInputAttacks} tenant switch inputs over {report.SwitchHeaderNames} header names; {report.ResponsesHeaderJudged} responses judged on every header");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.EndpointsAttacked} endpoints, {report.Requests} requests, {report.VictimValues} tenant B values, {report.ParameterAttacks} parameter attacks, " +
            $"{report.BodyValueAttacks} body value attacks, {report.DifferentialChecks} differential checks, {report.TracedLookups} traced lookups");

        Assert.True(report.Leaks.Count == 0, $"{report.Leaks.Count} leaks:\n" + string.Join("\n", report.Leaks.Take(50)));
        Assert.True(report.Oracles.Count == 0, $"{report.Oracles.Count} answers that tell tenant B's values apart from values that exist nowhere:\n" + string.Join("\n", report.Oracles.Take(30)));
        Assert.True(report.LookupMisuse.Count == 0, "Reviewed cross-tenant lookups ran from unreviewed callers:\n" + string.Join("\n", report.LookupMisuse));
        Assert.True(report.ChangedTables.Count == 0, "Tenant B rows changed during the attack in: " + string.Join(", ", report.ChangedTables));
        Assert.True(report.ServerErrors.Count == 0, $"{report.ServerErrors.Count} server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        Assert.True(report.UncoveredSurfaces.Count == 0, "Endpoint families without an isolation probe: " + string.Join(", ", report.UncoveredSurfaces));
        Assert.True(report.UntracedFunctions.Count == 0, "The SQL trace never saw these reviewed functions run from their reviewed caller, so it may be blind: " + string.Join(", ", report.UntracedFunctions));
        Assert.True(report.BindViolations.Count == 0, "Requests bound a tenant other than the signed-in session's:\n" + string.Join("\n", report.BindViolations.Take(30)));
        Assert.True(report.SettingViolations.Count == 0, "Session settings changed by code other than the kernel's session:\n" + string.Join("\n", report.SettingViolations.Take(30)));
        Assert.True(report.TraceBlindSpots.Count == 0, "The tenant binding trace may be blind: " + string.Join("; ", report.TraceBlindSpots));
        AssertAtLeast(report.BindsJudged, "g1.tenantBindsJudged");
        AssertAtLeast(report.SwitchInputAttacks, "g1.switchInputAttacks");
        AssertAtLeast(report.SwitchHeaderNames, "g1.switchHeaderNames");
        AssertAtLeast(report.ResponsesHeaderJudged, "g1.responsesHeaderJudged");
        AssertAtLeast(report.EndpointsAttacked, "g1.endpointsAttacked");
        AssertAtLeast(report.Requests, "g1.attackRequests");
        AssertAtLeast(report.ProbesRun, "g1.isolationProbes");
        AssertAtLeast(report.VictimValues, "g1.victimValues");
        AssertAtLeast(report.ParameterAttacks, "g1.parameterAttacks");
        AssertAtLeast(report.BodyValueAttacks, "g1.bodyValueAttacks");
        AssertAtLeast(report.DifferentialChecks, "g1.differentialChecks");
        AssertAtLeast(report.TracedLookups, "g1.tracedLookups");
        Assert.True(report.VictimBlindSpots.Count == 0, "Tenant B's concurrent activity may have been blind:\n" + string.Join("\n", report.VictimBlindSpots));
        AssertAtLeast(report.VictimRequests, "g1.victimRequests");
        AssertAtLeast(report.VictimConcurrentRequests, "g1.victimConcurrentRequests");
        AssertAtLeast(report.VictimWrites, "g1.victimWrites");
        AssertAtLeast(report.ReverseChecks, "g1.reverseChecks");
        // Every endpoint that changes data (other than signing in and out) succeeded for tenant B.
        Assert.True(report.VictimUnsuccessfulWrites.Count == 0,
            "Tenant B's own writes must succeed so their handlers run to the end; these did not:\n" + string.Join("\n", report.VictimUnsuccessfulWrites));
        AssertAtLeast(report.VictimWriteEndpoints, "g1.victimWriteEndpoints");
    }

    private static void AssertAtLeast(int value, string key) =>
        Assert.True(value >= Ratchet.Min(key), $"{key}: {value}; ratchet minimum {Ratchet.Min(key)}");
}

/// <summary>
/// The G1 HTTP attack, reusable so the gate's self-tests can prove it catches planted leaks.
/// </summary>
public static class IsolationAttack
{
    private const int VictimIdsPerTable = 5;

    /// <summary>Query names tried on every endpoint whether documented or not.</summary>
    private static readonly string[] GuessedQueryNames =
        ["tenantId", "tenant", "tenant_id", "companyId", "id", "userId", "search", "workspace", "email", "code", "name", "q", "filter"];

    public static async Task<IsolationReport> RunAsync(ErpTestEnvironment Env)
    {
        SqlTrace.EnsureStarted();
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
        var ownValues = await VictimValues.ReadAsync(Env, own, b.Id);
        var activity = await TenantActivity.StartAsync(Env, b, endpoints, openApi);
        activity.Watch(new MarkerSet(own, ownValues));
        await activity.WriteAsync(await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code));

        var victim = await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code);
        Assert.True(victim.Markers.Count > 10, "The victim tenant has too little data to attack.");
        var values = await VictimValues.ReadAsync(Env, victim, a.Id);
        Assert.True(values.Strings.Count > 10, "The victim tenant has too few distinct text values to attack.");
        var victimIdTexts = values.Ids.Select(i => i.ToString()).ToList();
        await activity.ReadAsync(victim, values, "tenant B reads before the attack");

        var attackers = new List<Attacker>
        {
            new("tenant A administrator (cookie)", () => Env.SignInAsync(Env.Email(a, "admin"))),
            new("tenant A administrator (bearer)", () => Env.SignInWithTokenAsync(Env.Email(a, "admin"))),
            new("tenant A user without roles", () => Env.SignInAsync(Env.Email(a, "noaccess"))),
            new("anonymous", () => Task.FromResult(Env.CreateClient())),
        };
        foreach (var attacker in attackers)
        {
            await attacker.ConnectAsync();
        }

        var state = new AttackState(victim, values);
        var phases = new List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Phase(string name)
        {
            phases.Add($"{name}: {state.Requests} requests so far, {clock.Elapsed.TotalSeconds:F1} s");
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
        foreach (var endpoint in endpoints)
        {
            await activity.TouchAsync(endpoint, victim, "tenant B reads right before A attacks this endpoint");
            var paths = endpoint.RouteParameters.Count == 0
                ? [endpoint.Path(_ => "")]
                : victimRouteValues.Concat(endpoint.HasBody ? ownRouteValues : [])
                    .Select(value => endpoint.Path(_ => value)).Distinct().ToList();
            var bodySchema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var signIn = endpoint.Name == "auth.signIn";

            foreach (var path in paths)
            {
                foreach (var variant in Enum.GetValues<Variant>())
                {
                    foreach (var attacker in attackers)
                    {
                        using var request = BuildRequest(endpoint, path, variant, bodySchema, openApi, victim, b, signIn, ref counter);
                        await state.SendAsync(attacker, endpoint, request, $"{path} [{variant}]");
                    }
                }
            }
            attacked.Add(endpoint.Key);
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
                var result = await probe.RunAsync(new IsolationProbeContext(attackers[0].Client, a.Id, b.Id, victim.AllIds.ToList(),
                    victim.Markers.Concat(values.Strings).Distinct().ToList()), CancellationToken.None);
                state.Requests += result.Attempts;
                probesRun++;
                foreach (var observed in result.Observed)
                {
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
            }

            foreach (var routeParameter in endpoint.RouteParameters)
            {
                var documentedRoute = documented.FirstOrDefault(p => p.In == "path" && p.Name == routeParameter)
                                      ?? new ApiParameter(routeParameter, "path", "string", null);
                var routeValues = catchAll
                    ? values.Probe
                    : documentedRoute.Format == "uuid"
                        ? victim.IdsByTable.Values.SelectMany(ids => ids.Take(10)).Append(b.Id).Distinct().Select(i => i.ToString()).ToList()
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
            }
        }
        // From here tenant B keeps reading in the background while A's requests run.
        using var concurrent = new CancellationTokenSource();
        var concurrentReader = Task.Run(() => activity.RunConcurrentlyAsync(victim, concurrent.Token));
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        await Parallel.ForEachAsync(work.Where(w => w.Get), parallel, async (item, _) => await item.Run());
        await Parallel.ForEachAsync(work.Where(w => !w.Get), parallel, async (item, _) => await item.Run());

        Phase("tenant B values in route and query parameters");

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
                foreach (var value in values.Strings)
                {
                    foreach (var attacker in reachable)
                    {
                        var n = counter++;
                        var body = openApi.BuildBody(schema, (type, format, name) =>
                            name == field && type == "string" && format != "uuid" ? value : Leaf(type, format, name, victimIdTexts, b, signIn, n)) as JsonObject ?? [];
                        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), path)
                        {
                            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                        };
                        var status = await state.SendAsync(attacker, endpoint, request, $"{path} [body {field}={Short(value)}]", sent: [value]);
                        state.BodyValueAttacks++;
                        if (status is >= 200 and < 300)
                        {
                            state.Stored.Add(value);
                        }
                    }
                }
            }
        }

        Phase("tenant B values in body fields");

        await concurrent.CancelAsync();
        await concurrentReader;

        // Tenant B reads everything once more, judged against everything tenant A now holds
        // (including what the attack created).
        var ownAfter = await TenantSnapshot.TakeAsync(Env, a.Id, null, a.Code);
        activity.Watch(new MarkerSet(ownAfter, await VictimValues.ReadAsync(Env, ownAfter, b.Id), [.. values.Strings, .. state.Stored], b.Canary));
        await activity.ReadAsync(victim, values, "tenant B reads after the attack");
        Phase($"tenant B activity: {activity.Requests} requests ({activity.ConcurrentRequests} concurrent with the attack), " +
              $"{activity.SuccessfulWrites} successful own writes, {activity.ReverseChecks} responses judged for tenant A markers");

        var after = await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code);
        var changed = TenantSnapshot.Differences(victim, after);

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

        foreach (var attacker in attackers)
        {
            attacker.Client.Dispose();
        }
        activity.Dispose();
        return new IsolationReport([.. state.Leaks, .. activity.Leaks], state.ServerErrors, changed, uncovered, attacked.Count, state.Requests, probesRun)
        {
            Oracles = state.Oracles,
            BindViolations = TenantBindingRules.BindViolations(binds),
            SettingViolations = TenantBindingRules.SettingViolations(settings),
            TraceBlindSpots = traceBlindSpots,
            BindsJudged = binds.Count,
            SettingStatementsJudged = settings.Count,
            RequestStatementsTraced = SqlTrace.RequestStatementsSince(traceSnapshot),
            SwitchInputAttacks = state.SwitchAttacks,
            SwitchHeaderNames = switchInputs.Headers.Count,
            ResponsesHeaderJudged = state.HeadersJudged,
            LookupMisuse = misuse,
            UntracedFunctions = untraced,
            VictimValues = values.Ids.Count + values.Strings.Count,
            ParameterAttacks = state.ParameterAttacks,
            BodyValueAttacks = state.BodyValueAttacks,
            DifferentialChecks = state.DifferentialChecks,
            TracedLookups = traced.Count,
            VictimRequests = activity.Requests,
            VictimConcurrentRequests = activity.ConcurrentRequests,
            VictimWrites = activity.SuccessfulWrites,
            VictimWriteEndpoints = activity.WriteEndpoints,
            ReverseChecks = activity.ReverseChecks,
            VictimBlindSpots = activity.BlindSpots,
            VictimUnsuccessfulWrites = activity.UnsuccessfulWrites,
            Phases = [.. phases, $"tenant B: {values.Ids.Count} ids ({values.IdSample.Count} sampled), {values.Strings.Count} text values, {values.Markers.Count} extra markers, {values.Probe.Count} probe values"],
        };
    }

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
                        ? openApi.BuildBody(s, leaf ?? ((type, format, name) => OwnLeaf(type, format, name, own, i, signIn, env, b))) as JsonObject ?? []
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
            var parallel = endpoint.Name == "auth.signOut" ? 1 : 4;
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (item, _) => await item());
            await activity.TouchAsync(endpoint, victim, "tenant B reads right after A's tenant switch inputs");
        }
        return new SwitchInputSet(headers, queries, cookies, bodyNames);
    }

    /// <summary>Leaf values from the attacker's own tenant, so a handler gets as far as it would
    /// for a real request of tenant A (sign-in uses tenant A's administrator).</summary>
    private static JsonNode? OwnLeaf(string type, string? format, string? name, TenantSnapshot own, int n, bool signIn, ErpTestEnvironment env, Erp.Kernel.Seeding.SeedTenant b)
    {
        var lower = name?.ToLowerInvariant() ?? "";
        return type switch
        {
            "string" when format == "uuid" => TenantActivity.OwnIdFor(lower, own),
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
                ? openApi.BuildBody(schema, (type, format, name) => Leaf(type, format, name, victimIds, b, signIn, n)) as JsonObject ?? []
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
    private sealed class AttackState(TenantSnapshot victim, VictimValues values)
    {
        private readonly List<string> _victimIds = values.Ids.Select(i => i.ToString()).ToList();

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
        public int Requests { get => _requests; set => _requests = value; }
        public int ParameterAttacks => _parameterAttacks;
        public int BodyValueAttacks { get; set; }
        public int DifferentialChecks => _differentialChecks;

        /// <summary>Tenant B text values the attacker wrote into its own tenant (phase 3).</summary>
        public HashSet<string> Stored { get; } = new(StringComparer.Ordinal);

        public async Task<int> SendAsync(Attacker attacker, ApiEndpoint endpoint, HttpRequestMessage request, string label, IReadOnlyCollection<string>? sent = null)
        {
            using var response = await attacker.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Interlocked.Increment(ref _requests);
            var status = (int)response.StatusCode;
            Judge(attacker, endpoint, label, status, text, ResponseHeaders.Text(response), sent ?? []);
            if (endpoint.Name == "auth.signOut" && attacker.Name != "anonymous")
            {
                await attacker.ConnectAsync();
            }
            return status;
        }

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
            var control = ControlFor(value);
            var (controlStatus, controlText, _) = await RawAsync(attacker, endpoint, uriFor(control), bodySchema, openApi, b, n);
            Interlocked.Increment(ref _differentialChecks);
            // Both values are scrubbed from both answers, so a value that is also an ordinary word
            // in every answer ("user" in "Users and access") is treated the same on both sides.
            var normalized = Normalize(Normalize(text, value), control);
            var controlNormalized = Normalize(Normalize(controlText, control), value);
            if (status != controlStatus || normalized != controlNormalized)
            {
                lock (_lock) Oracles.Add($"{attacker.Name} → GET {uri} [{parameter.In} {parameter.Name}]: {status} {Short(normalized, 160)} " +
                            $"but for a value that exists nowhere {controlStatus} {Short(controlNormalized, 160)}");
            }
        }

        /// <summary>A value of the same shape that exists in no tenant: every letter and digit
        /// replaced at random, punctuation kept (so "a.b@c.example" stays file-like and e-mail-like).</summary>
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
                    ? openApi.BuildBody(schema, (type, format, name) => Leaf(type, format, name, _victimIds, b, false, n)) as JsonObject ?? []
                    : [];
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await attacker.Client.SendAsync(request);
            Interlocked.Increment(ref _requests);
            var text = await response.Content.ReadAsStringAsync();
            if (endpoint.Name == "auth.signOut" && attacker.Name != "anonymous")
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
            return victim.FindMarker(scrubbed) ?? values.Markers.FirstOrDefault(m => scrubbed.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        private static string Scrub(string text, string value)
        {
            text = text.Replace(value, "<sent>", StringComparison.OrdinalIgnoreCase);
            var escaped = JsonSerializer.Serialize(value)[1..^1];
            text = text.Replace(escaped, "<sent>", StringComparison.OrdinalIgnoreCase);
            return text.Replace(Uri.EscapeDataString(value), "<sent>", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The body with trace ids removed and the sent value replaced by a placeholder.
        /// In JSON only string values are scrubbed, never property names, so a short value such as
        /// "user" cannot hide a difference by matching a key.</summary>
        private static string Normalize(string text, string value)
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

    private sealed class Attacker(string name, Func<Task<HttpClient>> connect)
    {
        public string Name { get; } = name;
        public HttpClient Client { get; private set; } = null!;

        public async Task ConnectAsync()
        {
            Client?.Dispose();
            Client = await connect();
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

    /// <summary>Tenant B writes on its own records that did not succeed.</summary>
    public IReadOnlyList<string> VictimUnsuccessfulWrites { get; init; } = [];

    /// <summary>Requests and elapsed time after each phase.</summary>
    public IReadOnlyList<string> Phases { get; init; } = [];
}
