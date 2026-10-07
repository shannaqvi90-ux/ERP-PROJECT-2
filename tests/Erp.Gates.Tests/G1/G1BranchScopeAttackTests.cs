using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: the attack creates a user and writes in company X.</summary>
public sealed class G1BranchFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, branch layer over HTTP (critic p02 round 3, plant C3: with tenancy's branch filter switched
/// off, an administrator limited to one branch listed and renamed the company's other branches and
/// every gate passed; only a hand-written module test noticed). Inside company X of tenant A, an
/// administrator limited to the first branch (every permission) and the read-only user (limited to
/// every branch but the last) attack the last branch Z, exactly as the company attack attacks
/// company Y: Z's ids in every route parameter, query parameter and body id field, Z's texts in
/// every query and body text field, valid bodies naming company X and branch Z, an in-company write
/// oracle on every identifying field, and direct attempts to give, switch to and open branch Z. The
/// victim is the branch's own row and every row of every tenant table that carries its
/// <c>branch_id</c>, so a later module's branch-owned table is attacked without anyone listing it.
/// </summary>
public sealed class G1BranchScopeAttackTests(G1BranchFixture fixture) : IClassFixture<G1BranchFixture>
{
    [Fact]
    public async Task A_user_limited_to_one_branch_cannot_reach_another_branch_of_the_company_through_any_endpoint()
    {
        var report = await CompanyAttack.RunAsync(fixture.Env, CompanyAttack.Layer.Branch);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.EndpointsAttacked} endpoints, {report.Requests} requests, {report.Markers} branch Z markers, {report.DifferentialChecks} differential checks, {report.WriteOracleChecks} write oracle checks");
        Assert.True(report.Leaks.Count == 0, $"{report.Leaks.Count} leaks of branch Z:\n" + string.Join("\n", report.Leaks.Take(40)));
        Assert.True(report.Oracles.Count == 0, "Answers that tell branch Z's values apart from values that exist nowhere:\n" + string.Join("\n", report.Oracles.Take(30)));
        Assert.True(report.ServerErrors.Count == 0, "Server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        Assert.True(report.ChangedTables.Count == 0, "Branch Z rows changed in: " + string.Join(", ", report.ChangedTables));
        Assert.True(report.Escalations.Count == 0, string.Join("\n", report.Escalations));
        Assert.Contains("POST /api/tenancy/branches [code] <- tenancy.branches", report.WriteOracleSources);
        Assert.Contains("PUT /api/tenancy/branches/{id:guid} [code] <- own record", report.WriteOracleSources);
        Assert.True(report.EndpointsAttacked >= Ratchet.Min("g1.branchEndpointsAttacked"),
            $"g1.branchEndpointsAttacked: {report.EndpointsAttacked}; ratchet minimum {Ratchet.Min("g1.branchEndpointsAttacked")}");
        Assert.True(report.Requests >= Ratchet.Min("g1.branchAttackRequests"),
            $"g1.branchAttackRequests: {report.Requests}; ratchet minimum {Ratchet.Min("g1.branchAttackRequests")}");
        Assert.True(report.Markers >= Ratchet.Min("g1.branchMarkers"), $"g1.branchMarkers: {report.Markers}; ratchet minimum {Ratchet.Min("g1.branchMarkers")}");

        // The records every branch of company X shares (critic p02 round 4, plant P7: a one-branch
        // administrator renamed and deactivated the whole company and every gate passed).
        var shared = report.Shared!;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"shared records: {string.Join(", ", shared.Tables)}; {shared.Writes} writes to company X by the branch-limited administrator:\n  " + string.Join("\n  ", shared.Sources));
        Assert.Contains("tenancy.companies", shared.Tables);
        Assert.True(shared.Failures.Count == 0, "Writes to company X's shared records by an administrator limited to one of its branches:\n" + string.Join("\n", shared.Failures.Take(40)));
        Assert.True(shared.ChangedTables.Count == 0, "Company X's shared rows changed (by an administrator limited to one of its branches) in: " + string.Join(", ", shared.ChangedTables));
        // Renaming, re-registering, deactivating, the logo: each proven valid by the tenant's
        // administrator and so really attacked.
        foreach (var source in new[]
                 {
                     "PUT /api/tenancy/companies/{id:guid} [legalNameEn]",
                     "PUT /api/tenancy/companies/{id:guid} [code]",
                     "PUT /api/tenancy/companies/{id:guid} [isActive]",
                     "PUT /api/tenancy/companies/{id:guid} [taxRegistrationNumber]",
                     "PUT /api/tenancy/companies/{id:guid} [tradeLicenceNumber]",
                     "PUT /api/tenancy/companies/{id:guid}/logo [-]",
                     "DELETE /api/tenancy/companies/{id:guid}/logo [-]",
                 })
        {
            Assert.Contains(source, shared.Sources);
        }
        Assert.True(shared.Writes >= Ratchet.Min("g1.sharedRecordWrites"), $"g1.sharedRecordWrites: {shared.Writes}; ratchet minimum {Ratchet.Min("g1.sharedRecordWrites")}");
    }
}
