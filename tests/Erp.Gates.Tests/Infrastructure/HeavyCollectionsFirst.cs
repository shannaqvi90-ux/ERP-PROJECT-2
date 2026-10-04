using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(Erp.Gates.Tests.Infrastructure.HeavyCollectionsFirst))]

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Starts the longest test collections first. At most four collections run at once
/// (xunit.runner.json), so a long attack that happens to start last would run alone at the end
/// while the machine idles; started first, it overlaps with all the short ones. The order changes
/// when tests start, never what they do or whether they run.
/// </summary>
public sealed class HeavyCollectionsFirst : ITestCollectionOrderer
{
    /// <summary>The longest collections, longest first (measured, round 5).</summary>
    private static readonly string[] Heaviest =
    [
        "G1HttpIsolationTests",
        "Leaky module",
        "G1CompanyIsolationTests",
        "NonInterferenceSelfTests",
        "G1NonInterferenceTests",
        "G2PermissionTests",
        "G2AccountTakeoverTests",
        "G1SignInThrottleTests",
    ];

    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : notnull, ITestCollection =>
        testCollections
            .OrderBy(c => Rank(c.TestCollectionDisplayName))
            .ThenBy(c => c.TestCollectionDisplayName, StringComparer.Ordinal)
            .ToList();

    private static int Rank(string name)
    {
        for (var i = 0; i < Heaviest.Length; i++)
        {
            if (name.Contains(Heaviest[i], StringComparison.Ordinal))
            {
                return i;
            }
        }
        return Heaviest.Length;
    }
}
