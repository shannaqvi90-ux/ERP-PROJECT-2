using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// The HTTP attack runs in shares (<see cref="AttackShard"/>), each in an environment of its own
/// (round 20). A leak that tenant B's use of the app fills through one endpoint and tenant A reads
/// through another must still show when the two endpoints are in different shares: tenant B uses
/// every endpoint in every share. Here the planted module's endpoints are cut into two shares, each
/// run in an environment of its own, one after the other, and the leaks planted in them (a captured
/// array handed to the next writer, an Arabic-only greeting naming the previous caller, a static
/// cache, a singleton holding the last list) are all found by the two shares together, from the
/// share that attacks the endpoint and in both directions. The shares' own checks (disjoint, every
/// scoped endpoint attacked once) are asserted too. Process-wide planted state is static, so this runs
/// in a test process of its own (trait Process), like the other long self-tests.
/// </summary>
[Collection(LeakyModuleCollection.Name)]
[Trait(SelfTestProcess.Trait, SelfTestProcess.Shards)]
public sealed class ShardedAttackSelfTests
{
    // Method, then the path (a path ending in a slash stands for every route under it).
    private static readonly (string Method, string Path)[] FirstShare =
    [
        ("PUT", "/api/leaky/me/theme"), ("PUT", "/api/leaky/me/density"), ("GET", "/api/leaky/cached-tenant"), ("GET", "/api/leaky/by-header"),
    ];

    private static readonly (string Method, string Path)[] SecondShare =
    [
        ("GET", "/api/leaky/me/greeting"), ("GET", "/api/leaky/recent"), ("GET", "/api/leaky/tenants/"), ("GET", "/api/leaky/people"),
    ];

    private static bool In(ApiEndpoint e, (string Method, string Path)[] share) =>
        share.Any(s => e.Method == s.Method && (s.Path.EndsWith('/') ? e.Pattern.StartsWith(s.Path, StringComparison.Ordinal) : e.Pattern == s.Path));

    [Fact]
    public async Task The_sharded_HTTP_attack_finds_planted_leaks_filled_through_endpoints_of_the_other_share()
    {
        bool InScope(ApiEndpoint e) => In(e, FirstShare) || In(e, SecondShare);
        var reports = new List<IsolationReport>();
        var ownedBy = new List<List<string>>();
        string a = "", b = "";
        for (var i = 0; i < 2; i++)
        {
            // Each share in an environment of its own, as in the verify's test processes; the planted
            // state is static, so it starts empty for each (tenant B's warm-up fills it first).
            LeakyModule.ResetProcessState();
            var fixture = new LeakyFixture();
            await fixture.InitializeAsync();
            try
            {
                a = fixture.Env.TenantA.Code;
                b = fixture.Env.TenantB.Code;
                var mine = EndpointInventory.From(fixture.Env.Factory.Services).Where(e => In(e, i == 0 ? FirstShare : SecondShare)).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
                ownedBy.Add([.. mine]);
                reports.Add(await IsolationAttack.RunAsync(fixture.Env, sortedLists: l => l.Key.StartsWith("leaky.", StringComparison.Ordinal),
                    only: InScope, shard: new AttackShard(i, 2, mine),
                    beforeEachListRun: () => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<LeakyModule.SortedSearchMemo>(fixture.Env.Factory.Services).Totals.Clear()));
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }

        // The shares together attack every scoped endpoint, each once.
        var owned = reports.SelectMany(r => r.OwnedEndpointKeys).ToList();
        var scope = reports[0].AllEndpointKeys.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(8, scope.Count);
        Assert.Equal(scope, owned.Order(StringComparer.Ordinal));
        Assert.All(reports, r => Assert.Equal(scope, r.AllEndpointKeys.Order(StringComparer.Ordinal)));
        Assert.Equal(ownedBy[0].Order(StringComparer.Ordinal), reports[0].OwnedEndpointKeys.Order(StringComparer.Ordinal));
        var leaks = reports.SelectMany(r => r.Leaks).ToList();
        foreach (var leak in leaks.Where(l => !l.Contains("/api/leaky/", StringComparison.Ordinal)).Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"unexpected: {leak}");
        }
        Assert.DoesNotContain(leaks, l => !l.Contains("/api/leaky/", StringComparison.Ordinal));

        // Write after write: a captured array hands the previous writer's e-mail to the next writer
        // (both directions), found by the share that owns the write endpoint.
        Assert.Contains(reports[0].Leaks, l => l.StartsWith($"tenant {a} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/theme", StringComparison.Ordinal) &&
                                               l.Contains("a response header to tenant", StringComparison.Ordinal) && l.Contains("X-Erp-Previous-Editor", StringComparison.Ordinal));
        Assert.Contains(reports[0].Leaks, l => l.StartsWith($"tenant {b} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/theme", StringComparison.Ordinal) &&
                                               l.Contains("X-Erp-Previous-Editor", StringComparison.Ordinal));
        Assert.Contains(reports[0].Leaks, l => l.StartsWith($"tenant {a} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/density", StringComparison.Ordinal) &&
                                               l.Contains($"response to tenant {a} contains", StringComparison.Ordinal));
        Assert.Contains(reports[0].Leaks, l => l.StartsWith($"tenant {b} ", StringComparison.Ordinal) && l.Contains("PUT /api/leaky/me/density", StringComparison.Ordinal) &&
                                               l.Contains($"response to tenant {b} contains", StringComparison.Ordinal));
        // A static cache tenant B filled (in a share whose attack is elsewhere) shows in tenant A's answer.
        Assert.Contains(reports[0].Leaks, l => l.Contains("GET /api/leaky/cached-tenant", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(reports[0].Leaks, l => l.Contains("/api/leaky/by-header", StringComparison.Ordinal) && l.Contains("[TenantHeaders]", StringComparison.Ordinal));
        // The Arabic-only greeting and a singleton holding the last list, in the second share: tenant
        // B's use of both endpoints in this share fills them before tenant A attacks.
        Assert.Contains(reports[1].Leaks, l => l.StartsWith("tenant A administrator in Arabic", StringComparison.Ordinal) &&
                                               l.Contains("GET /api/leaky/me/greeting", StringComparison.Ordinal) && l.Contains("response contains tenant B marker", StringComparison.Ordinal));
        Assert.Contains(reports[1].Leaks, l => l.StartsWith($"tenant {b} administrator in Arabic", StringComparison.Ordinal) &&
                                               l.Contains("GET /api/leaky/me/greeting", StringComparison.Ordinal) && l.Contains($"response to tenant {b} contains", StringComparison.Ordinal));
        Assert.Contains(reports[1].Leaks, l => l.Contains("GET /api/leaky/recent", StringComparison.Ordinal) && l.StartsWith("tenant A", StringComparison.Ordinal));
        Assert.Contains(reports[1].Leaks, l => l.Contains("/api/leaky/tenants/", StringComparison.Ordinal));
        // Each share's tenant B wrote through both write endpoints, the second share's attack owns neither.
        Assert.All(reports, r => Assert.True(r.VictimWriteEndpoints >= 2, $"tenant B wrote through {r.VictimWriteEndpoints} write endpoints in a share"));
    }
}
