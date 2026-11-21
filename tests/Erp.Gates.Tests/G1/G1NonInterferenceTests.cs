using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 without markers (<see cref="NonInterference"/>), judged together. The check runs in
/// <see cref="NonInterferenceShards.GateCount"/> shares (<see cref="G1NonInterferenceShardTests"/>, each in an
/// environment of its own, each in a test process of its own under ./erp verify); this test is the
/// verdict on all of them (<see cref="NonInterferenceJudge"/>): every share ran, the shares together
/// are every read and write endpoint, nothing was found in any share, and the shares' numbers added up
/// reach every ratchet minimum. Run by a plain <c>dotnet test</c> (no <see cref="HttpAttackShards.DirectoryVariable"/>),
/// a share nobody has run yet runs here.
/// </summary>
public sealed class G1NonInterferenceTests
{
    /// <summary>What one tenant is answered may not depend on what the other tenant did, whether
    /// or not the answer carries anything of the other tenant's (<see cref="NonInterference"/>).</summary>
    [Fact]
    [Trait("Judge", "noninterference")]
    public async Task What_tenant_A_is_answered_never_depends_on_what_tenant_B_did()
    {
        var (results, missing) = await NonInterferenceShards.AllAsync();
        foreach (var result in results)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Share {result.ShareIndex + 1} of {result.ShareCount}: {NonInterferenceShards.Describe(result)}");
            foreach (var unstable in result.Unstable)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"unstable: {unstable}");
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine("All shares: " + NonInterferenceShards.Describe(NonInterferenceJudge.Merge(results)));
        var failures = NonInterferenceJudge.Failures(results, missing);
        Assert.True(failures.Count == 0, $"{failures.Count} non-interference checks failed:\n\n" + string.Join("\n\n", failures));
    }
}

/// <summary>One share of the non-interference check each (<see cref="NonInterference.ShareOf"/>); a
/// share's own test fails on anything its comparisons found. ./erp verify runs each in a test process
/// of its own (filter on the trait) and <see cref="G1NonInterferenceTests"/> judges them together.</summary>
public sealed class G1NonInterferenceShardTests
{
    [Fact]
    [Trait(NonInterferenceShards.Trait, "0")]
    public Task Tenant_A_is_compared_with_tenant_B_on_the_first_share_of_the_endpoints() => RunAsync(0);

    [Fact]
    [Trait(NonInterferenceShards.Trait, "1")]
    public Task Tenant_A_is_compared_with_tenant_B_on_the_second_share_of_the_endpoints() => RunAsync(1);

    [Fact]
    [Trait(NonInterferenceShards.Trait, "2")]
    public Task Tenant_A_is_compared_with_tenant_B_on_the_third_share_of_the_endpoints() => RunAsync(2);

    private static async Task RunAsync(int index)
    {
        Assert.Equal(NonInterferenceShards.GateCount, 3);
        var result = await NonInterferenceShards.RunAsync(index);
        Assert.True(result.Findings.Count == 0, $"{result.Findings.Count} answers depend on the other tenant's activity (share {index + 1} of {NonInterferenceShards.GateCount}):\n" + string.Join("\n", result.Findings.Take(30)));
        Assert.True(result.BlindSpots.Count == 0, "The non-interference check may be blind: " + string.Join("; ", result.BlindSpots));
    }
}
