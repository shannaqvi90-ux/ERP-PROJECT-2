using System.Reflection;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// One share of the G1 HTTP attack. Tenant A attacks the endpoints of its share (every phase, in
/// the same order as the whole attack); tenant B uses every endpoint in every share, before, between
/// and concurrently with tenant A's requests, so what the process holds when tenant A attacks is
/// the same whatever the share. The shares are disjoint and together are every endpoint in scope;
/// <see cref="HttpAttackJudge"/> refuses a set of results that are not (a share missing, an endpoint
/// in two shares or in none). Each share runs in an environment of its own (a database of its own;
/// ./erp verify runs each in a test process of its own), so the shares run side by side.
/// </summary>
public sealed class AttackShard
{
    /// <summary>How many shares the gate's HTTP attack is cut into. ./erp verify (build/verify-inside.sh)
    /// starts this many test processes; the judge refuses a different count.</summary>
    public const int GateCount = 4;

    public AttackShard(int index, int count, IReadOnlySet<string> ownedKeys)
    {
        Index = index;
        Count = count;
        OwnedKeys = ownedKeys;
    }

    public int Index { get; }

    public int Count { get; }

    public IReadOnlySet<string> OwnedKeys { get; }

    public bool Owns(ApiEndpoint endpoint) => OwnedKeys.Contains(endpoint.Key);

    /// <summary>
    /// The share <paramref name="index"/> of <paramref name="count"/>: endpoints are dealt out,
    /// heaviest first, to the share with the least weight so far (ties: the lower share), so the same
    /// inventory gives the same shares in every process. The weight is a rough guess of the requests
    /// an endpoint costs (<see cref="Weight"/>); a bad guess makes one share slower, never a request
    /// missing.
    /// </summary>
    public static AttackShard For(int index, int count, IReadOnlyList<ApiEndpoint> endpoints, Func<ApiEndpoint, int>? weight = null)
    {
        var load = new int[count];
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints.OrderByDescending(e => (weight ?? Weight)(e)).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            var target = 0;
            for (var i = 1; i < count; i++)
            {
                if (load[i] < load[target])
                {
                    target = i;
                }
            }
            load[target] += (weight ?? Weight)(endpoint);
            if (target == index)
            {
                owned.Add(endpoint.Key);
            }
        }
        return new AttackShard(index, count, owned);
    }

    /// <summary>Tenths of a second an endpoint costs, roughly. Measured on one share of the attack
    /// (round 20, 65 endpoints, 3,989 s): the write-after-write phase took 1,456 s for 33 write
    /// endpoints (44 s each: tenant A and tenant B read everything after each of its variants),
    /// the list answers 1,343 s for 3 lists (450 s each: <see cref="ListWeight"/>), everything else
    /// about 1,200 s for the other 29 endpoints and the fixed part, the parameter attacks of a GET
    /// without a route being the larger part of it.</summary>
    public static int Weight(ApiEndpoint endpoint)
    {
        var get = endpoint.Method == "GET";
        var weight = get ? (endpoint.RouteParameters.Count == 0 ? 60 : 25) : 450;
        if (endpoint.HasBody)
        {
            weight += 60;
        }
        return weight;
    }

    /// <summary>What a registered list adds to the weight of its endpoint: the list answers judged
    /// for it (four runs, both tenants), its filters (330 values by six operators for a text column,
    /// the id sample for a reference column) and sorts with forged cursors.</summary>
    public static int ListWeight(Erp.Kernel.Lists.ListDefinition list) =>
        4000 + list.Columns.Sum(c => (c.Filterable ? (c.Type == Erp.Kernel.Lists.ListColumnType.Text ? 15 : 3) : 0) + (c.Sortable ? 2 : 0) + (c.Groupable ? 1 : 0));
}

/// <summary>What one share of the attack found, as plain numbers and lines (so it can be written to
/// a file by the process that ran it and read by the judge).</summary>
public sealed record ShardResult(int Index, int Count, Dictionary<string, int> Counters, Dictionary<string, string[]> Lists, string[] Phases)
{
    public static ShardResult From(int index, int count, IsolationReport report, IReadOnlyList<string> wholeTableReads)
    {
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var lists = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var property in typeof(IsolationReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            switch (property.GetValue(report))
            {
                case int n:
                    counters[property.Name] = n;
                    break;
                case IReadOnlyList<string> list when property.Name != nameof(IsolationReport.Phases):
                    lists[property.Name] = [.. list];
                    break;
            }
        }
        lists["WholeTableReads"] = [.. wholeTableReads];
        return new ShardResult(index, count, counters, lists, [.. report.Phases]);
    }

    public string FileName => FileNameFor(Index, Count);

    public static string FileNameFor(int index, int count) => $"g1-http-attack-shard-{index}-of-{count}.json";
}

/// <summary>Runs the shares of the HTTP attack, each once per process, and hands their results to
/// whoever asks (the share's own test and the judge).</summary>
public static class HttpAttackShards
{
    /// <summary>Where ./erp verify's test processes leave their shares' results (set by build/verify-inside.sh).
    /// When it is set the judge reads the results from there and never runs a share itself; when it
    /// is not (a plain <c>dotnet test</c>), a share nobody ran yet runs in the asking process.</summary>
    public const string DirectoryVariable = "ERP_GATE_SHARD_DIR";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<Task<ShardResult>>> Runs = new();

    public static string? Directory => Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } d ? d : null;

    public static Task<ShardResult> RunAsync(int index) =>
        Runs.GetOrAdd(index, i => new Lazy<Task<ShardResult>>(() => RunCoreAsync(i), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static async Task<ShardResult> RunCoreAsync(int index)
    {
        SqlTrace.EnsureStarted();
        await using var env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(env);
        // The shares are cut from the whole inventory (every endpoint the app exposes), the same in every process.
        var inventory = EndpointInventory.From(env.Factory.Services).ToList();
        var lists = env.Factory.Services.GetRequiredService<ModuleCatalog>().ListBindings
            .GroupBy(l => l.Definition.Endpoint).ToDictionary(g => g.Key, g => g.Sum(l => AttackShard.ListWeight(l.Definition)));
        var shard = AttackShard.For(index, AttackShard.GateCount, inventory,
            e => AttackShard.Weight(e) + (e.Method == "GET" ? lists.GetValueOrDefault(e.Pattern) : 0));
        IsolationReport report;
        try
        {
            report = await IsolationAttack.RunAsync(env, shard: shard);
        }
        catch (Exception e) when (e is not TimeoutException)
        {
            // A failure without a named request still lists the slowest requests seen before it.
            throw new InvalidOperationException($"{e.Message}\nSlowest requests seen before the failure:\n{RequestLog.Describe(20)}", e);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"Share {index + 1} of {AttackShard.GateCount}: slowest 20 requests of the attack (seconds, label, method, path):\n{RequestLog.Describe(20)}");
        foreach (var phase in report.Phases)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(phase);
        }
        // No request reads figures that describe whole tables, every tenant's rows together
        // (statistics, pg_class estimates, EXPLAIN): critic p05 round 9, plant L12.
        var result = ShardResult.From(index, AttackShard.GateCount, report, SqlTrace.WholeTableReadsFor(env));
        if (Directory is { } directory)
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, result.FileName);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result));
            File.Move(temporary, path, overwrite: true);
        }
        return result;
    }

    /// <summary>The results of every share: read from the verify's directory, or (no directory) run
    /// here. With a directory, a share that left no result is a failure, not something to run again.</summary>
    public static async Task<(IReadOnlyList<ShardResult> Results, IReadOnlyList<string> Missing)> AllAsync()
    {
        var results = new List<ShardResult>();
        var missing = new List<string>();
        for (var i = 0; i < AttackShard.GateCount; i++)
        {
            if (Directory is { } directory)
            {
                var path = Path.Combine(directory, ShardResult.FileNameFor(i, AttackShard.GateCount));
                if (!File.Exists(path))
                {
                    missing.Add($"share {i + 1} of {AttackShard.GateCount} left no result ({path}): its test did not finish");
                    continue;
                }
                results.Add(JsonSerializer.Deserialize<ShardResult>(await File.ReadAllTextAsync(path))!);
            }
            else
            {
                results.Add(await RunAsync(i));
            }
        }
        return (results, missing);
    }
}

/// <summary>
/// The verdict on the whole HTTP attack from its shares' results: every check the unsharded attack
/// made, on the shares' lines put together (nothing may be found in any share) and on the shares'
/// numbers added up (the ratchet minimums), plus the proof that the shares are the whole attack.
/// </summary>
public static class HttpAttackJudge
{
    /// <summary>Counters that describe what tenant B did in every share or what the process holds
    /// whatever the share, not what tenant A attacked: adding them up would count the same thing once
    /// per share, so every share must reach the minimum itself.</summary>
    private static readonly HashSet<string> EveryShare = new(StringComparer.Ordinal)
    {
        nameof(IsolationReport.VictimValues),
        nameof(IsolationReport.VictimWriteEndpoints),
        nameof(IsolationReport.StateLinesFingerprinted),
    };

    public static bool IsEveryShareCounter(string name) => EveryShare.Contains(name);

    public static List<string> Failures(IReadOnlyList<ShardResult> results, IReadOnlyList<string> missing)
    {
        var failures = new List<string>(missing);
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
        // The shares are the whole attack: all of them, each once, cutting the same inventory into
        // disjoint parts that together are every endpoint.
        Check(results.Count == AttackShard.GateCount && missing.Count == 0,
            $"{results.Count} of the {AttackShard.GateCount} shares of the attack reported");
        Check(results.Select(r => r.Index).Order().SequenceEqual(results.Select(r => r.Index).Distinct().Order()) &&
              results.All(r => r.Count == AttackShard.GateCount), "the shares do not agree on how many there are");
        if (results.Count == 0)
        {
            return failures;
        }
        var whole = results[0].Lists[nameof(IsolationReport.AllEndpointKeys)];
        Check(results.All(r => r.Lists[nameof(IsolationReport.AllEndpointKeys)].SequenceEqual(whole)), "the shares did not see the same endpoints");
        var owned = results.SelectMany(r => r.Lists[nameof(IsolationReport.OwnedEndpointKeys)]).ToList();
        Check(owned.Count == owned.Distinct(StringComparer.Ordinal).Count(),
            "endpoints attacked by two shares: " + string.Join(", ", owned.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).Take(10)));
        Check(owned.ToHashSet(StringComparer.Ordinal).SetEquals(whole),
            "endpoints no share attacked: " + string.Join(", ", whole.Except(owned).Take(10)));

        int Count(string name) => IsEveryShareCounter(name)
            ? results.Min(r => r.Counters.GetValueOrDefault(name))
            : results.Sum(r => r.Counters.GetValueOrDefault(name));
        void CheckAtLeast(string property, string key)
        {
            var value = Count(property);
            if (value < Ratchet.Min(key))
            {
                failures.Add($"{key}: {value}{(IsEveryShareCounter(property) ? " (the least of the shares)" : $" (the {results.Count} shares added up)")}; ratchet minimum {Ratchet.Min(key)}");
            }
        }
        var union = (string name) => results.SelectMany(r => r.Lists.GetValueOrDefault(name) ?? []).Distinct(StringComparer.Ordinal).Count();

        // Every check runs and every failing one is reported together: a plant caught by one
        // check must not hide whether the others (list answers, process state) caught it too.
        failures.AddRange(ShareFailures(results));
        CheckAtLeast(nameof(IsolationReport.TenantValuesJudged), "g1.tenantValuesJudged");
        CheckAtLeast(nameof(IsolationReport.StatementsObserved), "g1.statementsObserved");
        CheckAtLeast(nameof(IsolationReport.BindsJudged), "g1.tenantBindsJudged");
        CheckAtLeast(nameof(IsolationReport.SwitchInputAttacks), "g1.switchInputAttacks");
        var headerNames = union(nameof(IsolationReport.SwitchHeaderNameList));
        Check(headerNames >= Ratchet.Min("g1.switchHeaderNames"), $"g1.switchHeaderNames: {headerNames} (the names of all shares); ratchet minimum {Ratchet.Min("g1.switchHeaderNames")}");
        CheckAtLeast(nameof(IsolationReport.ResponsesHeaderJudged), "g1.responsesHeaderJudged");
        CheckAtLeast(nameof(IsolationReport.EndpointsAttacked), "g1.endpointsAttacked");
        CheckAtLeast(nameof(IsolationReport.VictimRouteValuesReplayed), "g1.victimRouteValuesReplayed");
        CheckAtLeast(nameof(IsolationReport.VictimPreTouches), "g1.victimPreTouches");
        CheckAtLeast(nameof(IsolationReport.Requests), "g1.attackRequests");
        CheckAtLeast(nameof(IsolationReport.ProbesRun), "g1.isolationProbes");
        CheckAtLeast(nameof(IsolationReport.VictimValues), "g1.victimValues");
        CheckAtLeast(nameof(IsolationReport.ParameterAttacks), "g1.parameterAttacks");
        CheckAtLeast(nameof(IsolationReport.BodyValueAttacks), "g1.bodyValueAttacks");
        CheckAtLeast(nameof(IsolationReport.DifferentialChecks), "g1.differentialChecks");
        CheckAtLeast(nameof(IsolationReport.TracedLookups), "g1.tracedLookups");
        CheckAtLeast(nameof(IsolationReport.VictimRequests), "g1.victimRequests");
        CheckAtLeast(nameof(IsolationReport.VictimConcurrentRequests), "g1.victimConcurrentRequests");
        CheckAtLeast(nameof(IsolationReport.VictimWrites), "g1.victimWrites");
        CheckAtLeast(nameof(IsolationReport.ReverseChecks), "g1.reverseChecks");
        CheckAtLeast(nameof(IsolationReport.VictimWriteEndpoints), "g1.victimWriteEndpoints");
        // Write after write: tenant A's own valid writes, each right after tenant B's, succeeded
        // (a refused write never reaches the code that could hand on tenant B's state).
        CheckAtLeast(nameof(IsolationReport.WritePairs), "g1.writePairs");
        CheckAtLeast(nameof(IsolationReport.WritePairEndpoints), "g1.writePairEndpoints");
        // Every documented value of every enumerated body field (language ar, numerals arab, ...)
        // was sent by both tenants back to back, not only the first one.
        var enumValues = union(nameof(IsolationReport.EnumValuesAttacked));
        Check(enumValues >= Ratchet.Min("g1.enumValuesAttacked"), $"g1.enumValuesAttacked: {enumValues}; ratchet minimum {Ratchet.Min("g1.enumValuesAttacked")}");
        CheckAtLeast(nameof(IsolationReport.EnumVariantPairs), "g1.enumVariantWritePairs");
        // Partial bodies (critic p04 round 11): every field a client may leave out was left out by
        // both tenants, right after the other wrote the whole body.
        CheckAtLeast(nameof(IsolationReport.PartialWriteVariants), "g1.partialWriteVariants");
        CheckAtLeast(nameof(IsolationReport.PartialWritePairs), "g1.partialWritePairs");
        CheckAtLeast(nameof(IsolationReport.PartialWriteEndpoints), "g1.partialWriteEndpoints");
        // The Arabic side of every session (critic p04 round 4): tenant A attacks and tenant B
        // uses the app in Arabic with Arabic-Indic digits too, and both stayed Arabic throughout.
        CheckAtLeast(nameof(IsolationReport.ArabicAttackRequests), "g1.arabicAttackRequests");
        CheckAtLeast(nameof(IsolationReport.VictimArabicRequests), "g1.victimArabicRequests");
        CheckAtLeast(nameof(IsolationReport.ArabicWritePairs), "g1.arabicWritePairs");
        CheckAtLeast(nameof(IsolationReport.ArabicWorkspaceWrites), "g1.arabicWorkspaceWideWrites");
        CheckAtLeast(nameof(IsolationReport.ListQueryAttacks), "g1.listQueryAttacks");
        var listsAttacked = Count(nameof(IsolationReport.ListsAttacked));
        Check(listsAttacked >= Ratchet.Min("rules.listsChecked"), $"{listsAttacked} lists attacked through their query contract (the shares added up); ratchet minimum {Ratchet.Min("rules.listsChecked")}");
        CheckAtLeast(nameof(IsolationReport.ListAnswerQueries), "g1.listAnswerQueries");
        CheckAtLeast(nameof(IsolationReport.ListAnswersDiscriminating), "g1.listAnswersDiscriminating");
        CheckAtLeast(nameof(IsolationReport.ListAnswerPagesJudged), "g1.listAnswerPagesJudged");
        CheckAtLeast(nameof(IsolationReport.ListAnswerOffsetPagesJudged), "g1.listAnswerOffsetPagesJudged");
        CheckAtLeast(nameof(IsolationReport.ListAnswerSortedQueries), "g1.listAnswerSortedQueries");
        CheckAtLeast(nameof(IsolationReport.ListAnswerSortedDiscriminating), "g1.listAnswerSortedDiscriminating");
        CheckAtLeast(nameof(IsolationReport.StateLinesFingerprinted), "g1.stateLinesFingerprinted");
        CheckAtLeast(nameof(IsolationReport.ShapeEndpoints), "g1.shapeEndpoints");
        CheckAtLeast(nameof(IsolationReport.ShapeAttacks), "g1.shapeAttacks");
        CheckAtLeast(nameof(IsolationReport.ShapeVictimRequests), "g1.shapeVictimRequests");
        CheckAtLeast(nameof(IsolationReport.ShapeDocumentedValues), "g1.shapeDocumentedValues");
        CheckAtLeast(nameof(IsolationReport.ShapeReplays), "g1.shapeReplays");
        CheckAtLeast(nameof(IsolationReport.ShapeCombinedRows), "g1.shapeCombinedRows");
        return failures;
    }

    /// <summary>The checks that need no other share: nothing found, nothing blind, in each share's own lines and numbers.</summary>
    public static List<string> ShareFailures(IReadOnlyList<ShardResult> results)
    {
        var failures = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
        string[] Lines(string name) => [.. results.SelectMany(r => r.Lists.GetValueOrDefault(name) ?? [])];
        void Empty(string name, string what, int take)
        {
            var lines = Lines(name);
            Check(lines.Length == 0, $"{lines.Length} {what}:\n" + string.Join("\n", lines.Take(take)));
        }
        Empty(nameof(IsolationReport.Leaks), "leaks", 50);
        Empty(nameof(IsolationReport.Oracles), "answers that tell tenant B's values apart from values that exist nowhere", 30);
        Empty(nameof(IsolationReport.LookupMisuse), "reviewed cross-tenant lookups that ran from unreviewed callers", 50);
        Check(Lines(nameof(IsolationReport.ChangedTables)).Length == 0, "Tenant B rows changed during the attack in: " + string.Join(", ", Lines(nameof(IsolationReport.ChangedTables)).Distinct()));
        Empty(nameof(IsolationReport.ServerErrors), "server errors", 20);
        Check(Lines(nameof(IsolationReport.UncoveredSurfaces)).Length == 0, "Endpoint families without an isolation probe: " + string.Join(", ", Lines(nameof(IsolationReport.UncoveredSurfaces)).Distinct()));
        Check(Lines(nameof(IsolationReport.UntracedFunctions)).Length == 0, "The SQL trace never saw these reviewed functions run from their reviewed caller, so it may be blind: " + string.Join(", ", Lines(nameof(IsolationReport.UntracedFunctions)).Distinct()));
        Empty(nameof(IsolationReport.BindViolations), "requests that bound a tenant other than the signed-in session's", 30);
        Empty(nameof(IsolationReport.SettingViolations), "session settings changed by code other than the kernel's session", 30);
        Empty(nameof(IsolationReport.WritableReads), "requests that only read ran in a writable transaction", 30);
        Check(Lines(nameof(IsolationReport.TraceBlindSpots)).Length == 0, "The tenant binding trace may be blind: " + string.Join("; ", Lines(nameof(IsolationReport.TraceBlindSpots))));
        Empty(nameof(IsolationReport.TenantValueViolations), "requests whose SQL ran under a tenant other than the signed-in session's (judged by the value each statement set)", 30);
        Empty(nameof(IsolationReport.UnobservedStatements), "statements sent inside requests on a pool the platform did not build, whose tenant the gate cannot judge", 30);
        Empty(nameof(IsolationReport.InputEnumerations), "places where product code read request inputs by enumerating them (a name the attack cannot learn and send tenant B's id in)", 30);
        Check(Lines(nameof(IsolationReport.EnvironmentChanges)).Length == 0, "Process-wide environment variables changed during the attack:\n" + string.Join("\n", Lines(nameof(IsolationReport.EnvironmentChanges))));
        Empty("WholeTableReads", "requests that read figures that describe whole tables (every tenant's rows)", 20);
        Check(Lines(nameof(IsolationReport.VictimBlindSpots)).Length == 0, "Tenant B's concurrent activity may have been blind:\n" + string.Join("\n", Lines(nameof(IsolationReport.VictimBlindSpots))));
        // Every endpoint that changes data (other than signing in and out) succeeded for tenant B.
        Check(Lines(nameof(IsolationReport.VictimUnsuccessfulWrites)).Length == 0,
            "Tenant B's own writes must succeed so their handlers run to the end; these did not:\n" + string.Join("\n", Lines(nameof(IsolationReport.VictimUnsuccessfulWrites)).Distinct()));
        Check(Lines(nameof(IsolationReport.AttackerUnsuccessfulWrites)).Length == 0,
            "Tenant A's own writes in the write-after-write phase must succeed; these did not:\n" + string.Join("\n", Lines(nameof(IsolationReport.AttackerUnsuccessfulWrites))));
        Check(Lines(nameof(IsolationReport.WritePairBlindSpots)).Length == 0, "The write-after-write phase may have been blind:\n" + string.Join("\n", Lines(nameof(IsolationReport.WritePairBlindSpots))));
        Check(Lines(nameof(IsolationReport.ArabicBlindSpots)).Length == 0, "The Arabic sessions may have been blind:\n" + string.Join("\n", Lines(nameof(IsolationReport.ArabicBlindSpots))));
        // A write of a record every company shares, refused by design to the Arabic administrator
        // (limited to one branch in the gate seed), is made in Arabic by an administrator who works
        // in every company and branch, and every such write succeeded (p04 round 7, after p02
        // round 7's workspace-wide guard): its success path ran in Arabic for both tenants.
        foreach (var r in results)
        {
            var writes = r.Counters.GetValueOrDefault(nameof(IsolationReport.ArabicWorkspaceWrites));
            var refusals = r.Counters.GetValueOrDefault(nameof(IsolationReport.ArabicWorkspaceRefusals));
            Check(writes == refusals,
                $"Of {refusals} writes refused to the branch-limited Arabic administrator because they need the whole workspace, only {writes} succeeded for the workspace-wide Arabic administrator (share {r.Index + 1})");
        }
        Empty(nameof(IsolationReport.ListRefusals), "list attacks were refused, so the query never ran", 20);
        Empty(nameof(IsolationReport.ListAnswersWrong), "list answers that are not the asking tenant's own", 30);
        Check(Lines(nameof(IsolationReport.ListAnswersBlind)).Length == 0, "The list answer check may be blind:\n" + string.Join("\n", Lines(nameof(IsolationReport.ListAnswersBlind))));
        Empty(nameof(IsolationReport.StateChanges), "lines of process-wide state that changed while the tenants used the app", 30);
        return failures;
    }
}
