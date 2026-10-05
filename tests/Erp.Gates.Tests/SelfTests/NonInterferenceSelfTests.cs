using Erp.Gates.Tests.G1;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// The non-interference check must fail meaningfully. It runs on an environment of its own: before
/// comparing, both tenants make every write the app offers, and the planted module's writes copy
/// one tenant's text into the other's records, which would blunt the marker-based self-tests that
/// share <see cref="GateSelfTests"/>'s environment.
/// </summary>
[Collection(LeakyModuleCollection.Name)]
[Trait(SelfTestProcess.Trait, SelfTestProcess.NonInterference)]
public sealed class NonInterferenceSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    /// <summary>State shared across requests that leaks only a number (lead, round 4): a count
    /// cached by search text without the tenant (bug 40) and a captured variable that answers the
    /// change since the previous caller's head count (bug 41). Neither answer carries anything of
    /// the other tenant's, so only the non-interference check can see them.</summary>
    [Fact]
    public async Task The_non_interference_check_catches_shared_state_that_leaks_no_marker()
    {
        var result = await NonInterference.RunAsync(fixture.Env);
        foreach (var finding in result.Findings)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(finding);
        }
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("GET /api/leaky/head-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("GET /api/leaky/head-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("GET /api/leaky/people-count?search=", StringComparison.Ordinal));
        // Nothing outside the planted module interferes, and the check was not blind.
        Assert.DoesNotContain(result.Findings, f => !f.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Empty(result.BlindSpots);
        Assert.True(result.Discriminating > 0);
    }

    [Fact]
    public void The_non_interference_check_ignores_only_times_and_trace_ids()
    {
        Assert.Equal(NonInterference.Normalize("""{"at":"2026-10-03T19:01:41.123Z","traceId":"00-ab","n":1}"""),
            NonInterference.Normalize("""{"at":"2026-10-04T07:00:00+04:00","traceId":"00-cd","n":1}"""));
        Assert.NotEqual(NonInterference.Normalize("""{"total":25}"""), NonInterference.Normalize("""{"total":26}"""));
        Assert.NotEqual(NonInterference.Normalize("""{"name":"a"}"""), NonInterference.Normalize("""{"name":"b"}"""));
    }
}
