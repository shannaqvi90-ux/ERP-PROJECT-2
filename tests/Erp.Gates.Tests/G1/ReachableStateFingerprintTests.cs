using System.Linq.Expressions;

namespace Erp.Gates.Tests.G1;

/// <summary>The process-state fingerprint (<see cref="ReachableState.Fingerprint"/>) reads an
/// expression tree by what it says and by the objects its constants hold: state behind a constant
/// (a captured counter, a cache object) is still seen, and the framework's own first-use caches
/// inside the tree are not reported as state a tenant changed.</summary>
public sealed class ReachableStateFingerprintTests
{
    [Fact]
    public void State_behind_an_expression_trees_constants_is_seen_and_the_frameworks_first_use_caches_are_not()
    {
        // The dictionary is reachable from the holder only through the selector's closure.
        var seen = new Dictionary<string, int>();
        var holder = new PlantedSelectors(seen);
        var product = new[] { typeof(PlantedSelectors).Assembly };
        var before = ReachableState.Fingerprint([("singleton PlantedSelectors", holder)], product);
        // The framework's first use of the lambdas (their parameter lists, compiled delegates).
        _ = holder.Plain.Parameters.Count;
        _ = holder.Capturing.Parameters.Count;
        _ = holder.Capturing.Compile()("x");
        var used = ReachableState.Differences(before, ReachableState.Fingerprint([("singleton PlantedSelectors", holder)], product));
        Assert.True(used.Count == 0, "first use reported as a change:\n" + string.Join("\n", used));

        // A count kept in an object the selector captured (a closure constant) is seen.
        seen["tenant-b-search"] = 1;
        var remembered = ReachableState.Differences(before, ReachableState.Fingerprint([("singleton PlantedSelectors", holder)], product));
        Assert.Contains(remembered, d => d.Contains(".constant[", StringComparison.Ordinal) && d.Contains("count=1", StringComparison.Ordinal));
        // A selector replaced by another is seen by its text.
        holder.Plain = r => r.Length + 1;
        var replaced = ReachableState.Differences(before, ReachableState.Fingerprint([("singleton PlantedSelectors", holder)], product));
        Assert.Contains(replaced, d => d.Contains("Plain (expression)", StringComparison.Ordinal));
    }

    private sealed class PlantedSelectors(Dictionary<string, int> seen)
    {
        public Expression<Func<string, int>> Plain { get; set; } = r => r.Length;

        public Expression<Func<string, int>> Capturing { get; } = r => seen.Count + r.Length;
    }
}
