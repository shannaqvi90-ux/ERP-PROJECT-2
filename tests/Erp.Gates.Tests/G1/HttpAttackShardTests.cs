using System.Reflection;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Security;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The cut of the G1 HTTP attack into shares (<see cref="AttackShard"/>) and the judge of the shares'
/// results (<see cref="HttpAttackJudge"/>): the shares are the whole attack, and the judge fails on
/// every fault planted in a set of results (a share missing, an endpoint attacked twice or not at
/// all, a leak in one share, a number below its minimum, a number that must hold in every share held
/// by one only).
/// </summary>
public sealed class HttpAttackShardTests
{
    private static List<ApiEndpoint> Endpoints(int n) => Enumerable.Range(0, n).Select(i => (i % 5) switch
    {
        0 => new ApiEndpoint("GET", $"/api/m{i}/items", $"m{i}.list", ["m.read"], null, SurfaceKind.Data, true),
        1 => new ApiEndpoint("GET", $"/api/m{i}/items/{{id}}", $"m{i}.get", ["m.read"], null, SurfaceKind.Data, true),
        2 => new ApiEndpoint("POST", $"/api/m{i}/items", $"m{i}.create", ["m.write"], null, SurfaceKind.Data, true),
        3 => new ApiEndpoint("PUT", $"/api/m{i}/items/{{id}}", $"m{i}.update", ["m.write"], null, SurfaceKind.Data, true),
        _ => new ApiEndpoint("DELETE", $"/api/m{i}/items/{{id}}", $"m{i}.delete", ["m.write"], null, SurfaceKind.Data, true),
    }).ToList();

    [Fact]
    public void The_shares_are_disjoint_together_every_endpoint_and_the_same_in_every_process()
    {
        var endpoints = Endpoints(140);
        var shares = Enumerable.Range(0, AttackShard.GateCount).Select(i => AttackShard.For(i, AttackShard.GateCount, endpoints)).ToList();
        var again = Enumerable.Range(0, AttackShard.GateCount).Select(i => AttackShard.For(i, AttackShard.GateCount, endpoints.AsEnumerable().Reverse().ToList())).ToList();
        var owned = shares.SelectMany(s => s.OwnedKeys).ToList();
        Assert.Equal(endpoints.Count, owned.Count);
        Assert.Equal(endpoints.Select(e => e.Key).Order(StringComparer.Ordinal), owned.Order(StringComparer.Ordinal));
        for (var i = 0; i < shares.Count; i++)
        {
            Assert.True(shares[i].OwnedKeys.SetEquals(again[i].OwnedKeys), $"share {i} depends on the order the inventory came in");
            Assert.All(endpoints, e => Assert.Equal(shares[i].OwnedKeys.Contains(e.Key), shares[i].Owns(e)));
        }
        // No share carries much more than its fair part by the weights it was dealt by.
        var loads = shares.Select(s => endpoints.Where(s.Owns).Sum(AttackShard.Weight)).ToList();
        Assert.True(loads.Max() <= loads.Average() * 1.1, $"share weights {string.Join(", ", loads)}");
        Assert.All(shares, s => Assert.NotEmpty(s.OwnedKeys));
    }

    [Fact]
    public void Every_counter_of_the_report_is_added_up_or_named_as_holding_in_every_share()
    {
        // A new counter on the report is added up across the shares unless it is named here; one
        // that every share counts the same way (tenant B's own work, the process's own state) must
        // be named, or it would be counted once per share.
        var counters = typeof(IsolationReport).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int)).Select(p => p.Name).ToHashSet();
        Assert.All(new[] { nameof(IsolationReport.VictimValues), nameof(IsolationReport.VictimWriteEndpoints), nameof(IsolationReport.StateLinesFingerprinted) },
            name => { Assert.Contains(name, counters); Assert.True(HttpAttackJudge.IsEveryShareCounter(name)); });
        Assert.False(HttpAttackJudge.IsEveryShareCounter(nameof(IsolationReport.Requests)));
        Assert.False(HttpAttackJudge.IsEveryShareCounter(nameof(IsolationReport.ParameterAttacks)));
    }

    private static List<ShardResult> CleanResults(int count = AttackShard.GateCount)
    {
        var endpoints = Endpoints(40);
        var shares = Enumerable.Range(0, count).Select(i => AttackShard.For(i, count, endpoints)).ToList();
        var report = typeof(IsolationReport).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(int)).Select(p => p.Name).ToList();
        return Enumerable.Range(0, count).Select(i =>
        {
            var counters = report.ToDictionary(name => name, _ => 100_000_000);
            counters[nameof(IsolationReport.ArabicWorkspaceWrites)] = 7;
            counters[nameof(IsolationReport.ArabicWorkspaceRefusals)] = 7;
            var lists = typeof(IsolationReport).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(IReadOnlyList<string>) && p.Name != nameof(IsolationReport.Phases)).Select(p => p.Name)
                .ToDictionary(name => name, _ => Array.Empty<string>());
            lists["WholeTableReads"] = [];
            lists[nameof(IsolationReport.AllEndpointKeys)] = [.. endpoints.Select(e => e.Key).Order(StringComparer.Ordinal)];
            lists[nameof(IsolationReport.OwnedEndpointKeys)] = [.. shares[i].OwnedKeys.Order(StringComparer.Ordinal)];
            lists[nameof(IsolationReport.SwitchHeaderNameList)] = [.. Enumerable.Range(0, 1000).Select(n => $"X-Header-{n}")];
            lists[nameof(IsolationReport.EnumValuesAttacked)] = [.. Enumerable.Range(0, 100_000).Select(n => $"value {n}")];
            return new ShardResult(i, count, counters, lists, []);
        }).ToList();
    }

    private static List<string> Judge(IReadOnlyList<ShardResult> results, IReadOnlyList<string>? missing = null) => HttpAttackJudge.Failures(results, missing ?? []);

    [Fact]
    public void The_judge_passes_shares_that_are_the_whole_attack_and_found_nothing()
    {
        Assert.Empty(Judge(CleanResults()));
    }

    [Fact]
    public void The_judge_catches_a_share_that_is_missing_or_a_count_that_differs()
    {
        var results = CleanResults();
        Assert.Contains(Judge(results.Take(3).ToList(), ["share 4 of 4 left no result"]), f => f.Contains("left no result", StringComparison.Ordinal));
        Assert.NotEmpty(Judge(results.Take(3).ToList()));
        Assert.NotEmpty(Judge([.. results.Take(3), results[2]]));
        Assert.Contains(Judge(CleanResults(3)), f => f.Contains("shares", StringComparison.Ordinal));
    }

    [Fact]
    public void The_judge_catches_an_endpoint_attacked_twice_or_by_no_share_or_different_inventories()
    {
        var twice = CleanResults();
        var first = twice[0].Lists[nameof(IsolationReport.OwnedEndpointKeys)];
        twice[1].Lists[nameof(IsolationReport.OwnedEndpointKeys)] = [.. twice[1].Lists[nameof(IsolationReport.OwnedEndpointKeys)], first[0]];
        Assert.Contains(Judge(twice), f => f.Contains("attacked by two shares", StringComparison.Ordinal));

        var none = CleanResults();
        none[2].Lists[nameof(IsolationReport.OwnedEndpointKeys)] = [.. none[2].Lists[nameof(IsolationReport.OwnedEndpointKeys)].Skip(1)];
        Assert.Contains(Judge(none), f => f.Contains("no share attacked", StringComparison.Ordinal));

        var different = CleanResults();
        different[3].Lists[nameof(IsolationReport.AllEndpointKeys)] = [.. different[3].Lists[nameof(IsolationReport.AllEndpointKeys)].Skip(1)];
        Assert.Contains(Judge(different), f => f.Contains("did not see the same endpoints", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(IsolationReport.Leaks))]
    [InlineData(nameof(IsolationReport.Oracles))]
    [InlineData(nameof(IsolationReport.ServerErrors))]
    [InlineData(nameof(IsolationReport.ChangedTables))]
    [InlineData(nameof(IsolationReport.UncoveredSurfaces))]
    [InlineData(nameof(IsolationReport.StateChanges))]
    [InlineData(nameof(IsolationReport.ListAnswersWrong))]
    [InlineData(nameof(IsolationReport.VictimBlindSpots))]
    [InlineData("WholeTableReads")]
    public void The_judge_catches_a_finding_in_any_one_share(string list)
    {
        var results = CleanResults();
        results[2].Lists[list] = ["a finding"];
        Assert.NotEmpty(Judge(results));
    }

    [Fact]
    public void The_judge_catches_numbers_below_their_minimum_added_up_or_in_one_share()
    {
        var added = CleanResults();
        foreach (var result in added)
        {
            result.Counters[nameof(IsolationReport.Requests)] = 1;
        }
        Assert.Contains(Judge(added), f => f.Contains("g1.attackRequests", StringComparison.Ordinal));

        // A number every share counts the same way must reach its minimum in each share.
        var one = CleanResults();
        one[1].Counters[nameof(IsolationReport.StateLinesFingerprinted)] = 1;
        Assert.Contains(Judge(one), f => f.Contains("g1.stateLinesFingerprinted", StringComparison.Ordinal));

        var names = CleanResults();
        foreach (var result in names)
        {
            result.Lists[nameof(IsolationReport.SwitchHeaderNameList)] = ["X-Only"];
        }
        Assert.Contains(Judge(names), f => f.Contains("g1.switchHeaderNames", StringComparison.Ordinal));

        var workspace = CleanResults();
        workspace[0].Counters[nameof(IsolationReport.ArabicWorkspaceWrites)] = 6;
        Assert.Contains(Judge(workspace), f => f.Contains("workspace-wide Arabic administrator", StringComparison.Ordinal));
    }
}
