using System.Collections.Concurrent;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The G1 non-interference check in shares (round 22, owner decision #59: the check was the longest
/// test of the gates, 55 to 80 minutes in one process). Each share runs in an environment of its own:
/// both tenants make every write the app offers (the state every comparison starts from), then the
/// share compares the read and write endpoints it owns. Every endpoint is in exactly one share
/// (<see cref="NonInterference.ShareOf"/>); <see cref="NonInterferenceJudge"/> refuses a set of
/// results that are not the whole check. ./erp verify runs each share in a test process of its own
/// (trait <see cref="Trait"/>) and the judge test (trait Judge=noninterference) after them.
/// </summary>
public static class NonInterferenceShards
{
    /// <summary>How many shares ./erp verify (build/verify-inside.sh) starts; the judge refuses a different count.</summary>
    public const int GateCount = 3;

    public const string Trait = "NiShard";

    private static readonly ConcurrentDictionary<int, Lazy<Task<NonInterference.Result>>> Runs = new();

    public static string FileNameFor(int index, int count) => $"g1-noninterference-shard-{index}-of-{count}.json";

    public static Task<NonInterference.Result> RunAsync(int index) =>
        Runs.GetOrAdd(index, i => new Lazy<Task<NonInterference.Result>>(() => RunCoreAsync(i), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static async Task<NonInterference.Result> RunCoreAsync(int index)
    {
        SqlTrace.EnsureStarted();
        await using var env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(env);
        var result = await NonInterference.RunAsync(env, shardIndex: index, shardCount: GateCount);
        TestContext.Current.TestOutputHelper?.WriteLine(Describe(result));
        if (HttpAttackShards.Directory is { } directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileNameFor(index, GateCount));
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result));
            File.Move(temporary, path, overwrite: true);
        }
        return result;
    }

    /// <summary>The results of every share: read from the verify's directory, or (no directory) run here.</summary>
    public static async Task<(IReadOnlyList<NonInterference.Result> Results, IReadOnlyList<string> Missing)> AllAsync()
    {
        var results = new List<NonInterference.Result>();
        var missing = new List<string>();
        for (var i = 0; i < GateCount; i++)
        {
            if (HttpAttackShards.Directory is { } directory)
            {
                var path = Path.Combine(directory, FileNameFor(i, GateCount));
                if (!File.Exists(path))
                {
                    missing.Add($"share {i + 1} of {GateCount} left no result ({path}): its test did not finish");
                    continue;
                }
                results.Add(JsonSerializer.Deserialize<NonInterference.Result>(await File.ReadAllTextAsync(path))!);
            }
            else
            {
                results.Add(await RunAsync(i));
            }
        }
        return (results, missing);
    }

    public static string Describe(NonInterference.Result result) =>
        $"{result.Comparisons} comparisons over {result.Endpoints} endpoints ({result.Discriminating} with different answers per tenant), " +
        $"{result.WriteComparisons} write comparisons over {result.WriteEndpoints} endpoints and {result.WriteVariants} body variants, {result.Requests} requests, {result.Unstable.Count} unstable; " +
        $"in Arabic: {result.ArabicComparisons} comparisons ({result.ArabicAnswers} answered in Arabic), {result.ArabicWriteComparisons} write comparisons " +
        $"({result.ArabicWorkspaceWriteComparisons} between the workspace-wide Arabic administrators); " +
        $"{result.HeadersCompared} response header lines compared, {result.ChangeProbes} changing answers judged by how they change, " +
        $"{result.ConfigurationKeysWatched} configuration keys watched; " +
        $"{result.TimesAttributed} times attributed to a request ({result.TimesAmbiguous} ambiguous, {result.PacedRuns} paced re-runs), {result.CookieValuesCompared} cookie values compared; " +
        $"{result.PartialWriteComparisons} partial-body write comparisons, {result.PartialChoiceChecks} partial bodies judged against the other tenant's choice";
}

/// <summary>The verdict on the whole non-interference check from its shares' results.</summary>
public static class NonInterferenceJudge
{
    /// <summary>The check's counters added up over shares (a counter every share holds alike, the
    /// configuration keys watched, takes the least of the shares).</summary>
    public static NonInterference.Result Merge(IReadOnlyList<NonInterference.Result> results)
    {
        int Sum(Func<NonInterference.Result, int> f) => results.Sum(f);
        IReadOnlyList<string> Union(Func<NonInterference.Result, IReadOnlyList<string>> f) => results.SelectMany(f).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new NonInterference.Result(
            results.SelectMany(r => r.Findings).Distinct(StringComparer.Ordinal).ToList(),
            results.SelectMany(r => r.Unstable).ToList(),
            results.SelectMany(r => r.BlindSpots).ToList(),
            Sum(r => r.Requests), Sum(r => r.Comparisons), Sum(r => r.Discriminating), Sum(r => r.Endpoints),
            Sum(r => r.WriteComparisons), Sum(r => r.WriteEndpoints), Sum(r => r.WriteVariants), Sum(r => r.ArabicComparisons), Sum(r => r.ArabicAnswers),
            Sum(r => r.ArabicWriteComparisons), Sum(r => r.ArabicWorkspaceWriteComparisons))
        {
            HeadersCompared = Sum(r => r.HeadersCompared),
            ChangeProbes = Sum(r => r.ChangeProbes),
            ConfigurationKeysWatched = results.Count == 0 ? 0 : results.Min(r => r.ConfigurationKeysWatched),
            TimesAttributed = Sum(r => r.TimesAttributed),
            TimesAmbiguous = Sum(r => r.TimesAmbiguous),
            PacedRuns = Sum(r => r.PacedRuns),
            CookieValuesCompared = Sum(r => r.CookieValuesCompared),
            PartialWriteComparisons = Sum(r => r.PartialWriteComparisons),
            PartialChoiceChecks = Sum(r => r.PartialChoiceChecks),
            ShareCount = results.Count == 0 ? 1 : results[0].ShareCount,
            AllReadKeys = Union(r => r.AllReadKeys),
            OwnedReadKeys = Union(r => r.OwnedReadKeys),
            AllWriteKeys = Union(r => r.AllWriteKeys),
            OwnedWriteKeys = Union(r => r.OwnedWriteKeys),
            ComparedReadKeys = Union(r => r.ComparedReadKeys),
            ComparedWriteKeys = Union(r => r.ComparedWriteKeys),
        };
    }

    /// <summary>What is wrong with the shares as a whole: a share missing or twice, an endpoint in two
    /// shares or in none, a share's own findings or blind spots, and the counters added up against the
    /// ratchet minimums (the same ones the unsharded check had).</summary>
    public static List<string> Failures(IReadOnlyList<NonInterference.Result> results, IReadOnlyList<string> missing, int expectedShares = NonInterferenceShards.GateCount, bool ratchet = true)
    {
        var failures = new List<string>(missing);
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }
        Check(results.Count == expectedShares && missing.Count == 0, $"{results.Count} of the {expectedShares} shares of the non-interference check reported");
        Check(results.Select(r => r.ShareIndex).Order().SequenceEqual(Enumerable.Range(0, results.Count)) && results.All(r => r.ShareCount == expectedShares),
            "the shares do not agree on how many there are, or one reported twice");
        if (results.Count == 0)
        {
            return failures;
        }
        foreach (var (name, all, owned) in new[]
                 {
                     ("read", (Func<NonInterference.Result, IReadOnlyList<string>>)(r => r.AllReadKeys), (Func<NonInterference.Result, IReadOnlyList<string>>)(r => r.OwnedReadKeys)),
                     ("write", r => r.AllWriteKeys, r => r.OwnedWriteKeys),
                 })
        {
            var whole = all(results[0]);
            Check(results.All(r => all(r).SequenceEqual(whole)), $"the shares did not see the same {name} endpoints");
            var ownedKeys = results.SelectMany(owned).ToList();
            Check(ownedKeys.Count == ownedKeys.Distinct(StringComparer.Ordinal).Count(),
                $"{name} endpoints compared by two shares: " + string.Join(", ", ownedKeys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).Take(10)));
            Check(ownedKeys.ToHashSet(StringComparer.Ordinal).SetEquals(whole), $"{name} endpoints no share compared: " + string.Join(", ", whole.Except(ownedKeys).Take(10)));
        }
        foreach (var r in results)
        {
            Check(r.ComparedReadKeys.All(r.OwnedReadKeys.Contains) && r.ComparedWriteKeys.All(r.OwnedWriteKeys.Contains), $"share {r.ShareIndex + 1} compared an endpoint it does not own");
            Check(r.Findings.Count == 0, $"share {r.ShareIndex + 1}: {r.Findings.Count} answers depend on the other tenant's activity:\n" + string.Join("\n", r.Findings.Take(30)));
            Check(r.BlindSpots.Count == 0, $"share {r.ShareIndex + 1}: the non-interference check may be blind: " + string.Join("; ", r.BlindSpots));
        }
        var total = Merge(results);
        // The blind-spot rules that need the whole check's numbers, not a share's.
        Check(total.Comparisons > 0, "no request was compared");
        Check(total.WriteComparisons > 0, "no write was compared");
        Check(total.Discriminating > 0, "no request had different true answers in the two tenants, so no interference could show");
        Check(total.ArabicComparisons > 0, "no request was compared in Arabic");
        Check(total.ArabicAnswers > 0, "no answer to an Arabic session was more Arabic than the English one, so the Arabic branch may never have run");
        Check(total.ArabicWriteComparisons > 0, "no write was compared in Arabic");
        Check(total.HeadersCompared > 0, "no response header was compared");
        if (ratchet)
        {
            void AtLeast(int value, string key) => Check(value >= Ratchet.Min(key), $"{key}: {value} (the {results.Count} shares added up); ratchet minimum {Ratchet.Min(key)}");
            AtLeast(total.Comparisons, "g1.nonInterferenceComparisons");
            AtLeast(total.Endpoints, "g1.nonInterferenceEndpoints");
            AtLeast(total.Discriminating, "g1.nonInterferenceDiscriminating");
            AtLeast(total.WriteComparisons, "g1.writeNonInterferenceComparisons");
            AtLeast(total.WriteEndpoints, "g1.writeNonInterferenceEndpoints");
            AtLeast(total.WriteVariants, "g1.writeNonInterferenceVariants");
            AtLeast(total.ArabicComparisons, "g1.nonInterferenceArabicComparisons");
            AtLeast(total.ArabicAnswers, "g1.nonInterferenceArabicAnswers");
            AtLeast(total.ArabicWriteComparisons, "g1.writeNonInterferenceArabicComparisons");
            AtLeast(total.ArabicWorkspaceWriteComparisons, "g1.writeNonInterferenceArabicWorkspaceComparisons");
            AtLeast(total.HeadersCompared, "g1.nonInterferenceHeadersCompared");
            AtLeast(total.ConfigurationKeysWatched, "g1.nonInterferenceConfigurationKeysWatched");
            AtLeast(total.TimesAttributed, "g1.nonInterferenceTimesAttributed");
            AtLeast(total.PartialWriteComparisons, "g1.nonInterferencePartialWriteComparisons");
            AtLeast(total.PartialChoiceChecks, "g1.nonInterferencePartialChoiceChecks");
        }
        return failures;
    }
}
