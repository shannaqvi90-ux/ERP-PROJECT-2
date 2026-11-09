using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, the HTTP attack on the attachments surface alone (p11). <see cref="G1HttpIsolationTests"/> attacks
/// every endpoint of the app, but its hours-long run reaches the files last; this runs every phase and
/// judgement of the same attack (<see cref="IsolationAttack"/>, tenant B's own writes and reads before and
/// during it, every tenant B value in every route, query, header and body field, the answer shapes, the
/// SQL trace, the file probe) on the endpoints under <c>/api/attachments/</c> only. It fails when an
/// attachment endpoint leaks, tells tenant B's files apart from none, changes tenant B's rows, answers a
/// server error, or is a write the gate's own generic requests cannot make succeed (the shared gates' way
/// to reach a write): the attachments must stay inside what the shared attack can judge.
/// </summary>
public sealed class G1AttachmentsAttackTests(G1AttackFixture fixture) : IClassFixture<G1AttackFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    /// <summary>The attachments endpoints, and the contacts ones they hang on (the attack needs a list to judge, and the files are a contact's).</summary>
    private static bool Attachments(ApiEndpoint endpoint) =>
        endpoint.Pattern.StartsWith("/api/attachments/", StringComparison.Ordinal) || endpoint.Pattern.StartsWith("/api/contacts/", StringComparison.Ordinal);

    [Fact]
    public async Task Tenant_A_cannot_reach_tenant_B_s_files_through_the_attachments_endpoints()
    {
        var report = await IsolationAttack.RunAsync(Env, sortedLists: _ => false, only: Attachments);
        foreach (var phase in report.Phases)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(phase);
        }
        Assert.Equal(EndpointInventory.From(Env.Factory.Services).Count(Attachments), report.EndpointsAttacked);
        Assert.True(report.EndpointsAttacked >= 4 + 4, $"{report.EndpointsAttacked} endpoints attacked");
        var failures = new List<string>();
        if (report.Leaks.Count > 0) failures.Add($"{report.Leaks.Count} leaks:\n" + string.Join("\n", report.Leaks.Take(30)));
        if (report.Oracles.Count > 0) failures.Add($"{report.Oracles.Count} answers that tell tenant B's files apart from none:\n" + string.Join("\n", report.Oracles.Take(30)));
        if (report.ChangedTables.Count > 0) failures.Add("Tenant B rows changed during the attack in: " + string.Join(", ", report.ChangedTables));
        if (report.ServerErrors.Count > 0) failures.Add($"{report.ServerErrors.Count} server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        if (report.UncoveredSurfaces.Count > 0) failures.Add("Endpoint families without an isolation probe: " + string.Join(", ", report.UncoveredSurfaces));
        if (report.VictimUnsuccessfulWrites.Count > 0) failures.Add("Tenant B's own writes must succeed so their handlers run to the end; these did not:\n" + string.Join("\n", report.VictimUnsuccessfulWrites.Take(20)));
        if (report.AttackerUnsuccessfulWrites.Count > 0) failures.Add("Tenant A's own writes must succeed; these did not:\n" + string.Join("\n", report.AttackerUnsuccessfulWrites.Take(20)));
        if (report.BindViolations.Count > 0) failures.Add("Requests bound a tenant other than the session's:\n" + string.Join("\n", report.BindViolations.Take(20)));
        if (report.SettingViolations.Count > 0) failures.Add("Session settings changed by code other than the kernel's session:\n" + string.Join("\n", report.SettingViolations.Take(20)));
        if (report.WritableReads.Count > 0) failures.Add("Requests that only read ran in a writable transaction:\n" + string.Join("\n", report.WritableReads.Take(20)));
        if (report.TenantValueViolations.Count > 0) failures.Add("Requests whose SQL ran under another tenant:\n" + string.Join("\n", report.TenantValueViolations.Take(20)));
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }
}
