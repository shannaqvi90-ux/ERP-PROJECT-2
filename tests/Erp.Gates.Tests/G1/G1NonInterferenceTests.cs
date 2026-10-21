using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: before comparing, both tenants make every write the app offers,
/// and the records those writes create would collide with the ones the HTTP attack's tenant B
/// activity creates in the same environment.</summary>
public sealed class G1NonInterferenceFixture : IAsyncLifetime
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

/// <summary>G1 without markers (<see cref="NonInterference"/>).</summary>
public sealed class G1NonInterferenceTests(G1NonInterferenceFixture fixture) : IClassFixture<G1NonInterferenceFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    /// <summary>What one tenant is answered may not depend on what the other tenant did, whether
    /// or not the answer carries anything of the other tenant's (<see cref="NonInterference"/>).</summary>
    [Fact]
    public async Task What_tenant_A_is_answered_never_depends_on_what_tenant_B_did()
    {
        var result = await NonInterference.RunAsync(Env);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{result.Comparisons} comparisons over {result.Endpoints} endpoints ({result.Discriminating} with different answers per tenant), " +
            $"{result.WriteComparisons} write comparisons over {result.WriteEndpoints} endpoints and {result.WriteVariants} body variants, {result.Requests} requests, {result.Unstable.Count} unstable; " +
            $"in Arabic: {result.ArabicComparisons} comparisons ({result.ArabicAnswers} answered in Arabic), {result.ArabicWriteComparisons} write comparisons " +
            $"({result.ArabicWorkspaceWriteComparisons} between the workspace-wide Arabic administrators); " +
            $"{result.HeadersCompared} response header lines compared, {result.ChangeProbes} changing answers judged by how they change, " +
            $"{result.ConfigurationKeysWatched} configuration keys watched; " +
            $"{result.TimesAttributed} times attributed to a request ({result.TimesAmbiguous} ambiguous, {result.PacedRuns} paced re-runs), {result.CookieValuesCompared} cookie values compared; " +
            $"{result.PartialWriteComparisons} partial-body write comparisons, {result.PartialChoiceChecks} partial bodies judged against the other tenant's choice");
        foreach (var unstable in result.Unstable)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unstable: {unstable}");
        }
        Assert.True(result.Findings.Count == 0, $"{result.Findings.Count} answers depend on the other tenant's activity:\n" + string.Join("\n", result.Findings.Take(30)));
        Assert.True(result.BlindSpots.Count == 0, "The non-interference check may be blind: " + string.Join("; ", result.BlindSpots));
        AssertAtLeast(result.Comparisons, "g1.nonInterferenceComparisons");
        AssertAtLeast(result.Endpoints, "g1.nonInterferenceEndpoints");
        AssertAtLeast(result.Discriminating, "g1.nonInterferenceDiscriminating");
        AssertAtLeast(result.WriteComparisons, "g1.writeNonInterferenceComparisons");
        AssertAtLeast(result.WriteEndpoints, "g1.writeNonInterferenceEndpoints");
        AssertAtLeast(result.WriteVariants, "g1.writeNonInterferenceVariants");
        // The Arabic side of every session (critic p04 round 4).
        AssertAtLeast(result.ArabicComparisons, "g1.nonInterferenceArabicComparisons");
        AssertAtLeast(result.ArabicAnswers, "g1.nonInterferenceArabicAnswers");
        AssertAtLeast(result.ArabicWriteComparisons, "g1.writeNonInterferenceArabicComparisons");
        // Writes every company shares, refused to the branch-limited Arabic administrators, compared
        // between administrators who work in every company and branch (p04 round 7).
        AssertAtLeast(result.ArabicWorkspaceWriteComparisons, "g1.writeNonInterferenceArabicWorkspaceComparisons");
        // Response headers are part of every answer compared, and the configuration of every host
        // is watched for writes while serving (critic p04 round 8, plant L3: a process-wide count
        // kept in the configuration and sent in a header only).
        AssertAtLeast(result.HeadersCompared, "g1.nonInterferenceHeadersCompared");
        AssertAtLeast(result.ConfigurationKeysWatched, "g1.nonInterferenceConfigurationKeysWatched");
        // Times are attributed to the request they fall in, not blanked (critic p04 round 9): a
        // check that attributes none would be blind to the time of the other tenant's activity.
        AssertAtLeast(result.TimesAttributed, "g1.nonInterferenceTimesAttributed");
        // Partial bodies (critic p04 round 11, plant SL1): every field a client may leave out was
        // left out and compared, and each partial body that leaves out an enumerated field was
        // judged against the other tenant's choice of it.
        AssertAtLeast(result.PartialWriteComparisons, "g1.nonInterferencePartialWriteComparisons");
        AssertAtLeast(result.PartialChoiceChecks, "g1.nonInterferencePartialChoiceChecks");
    }

    private static void AssertAtLeast(int value, string key) =>
        Assert.True(value >= Ratchet.Min(key), $"{key}: {value}; ratchet minimum {Ratchet.Min(key)}");
}
