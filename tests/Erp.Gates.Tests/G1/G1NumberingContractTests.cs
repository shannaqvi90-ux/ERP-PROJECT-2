using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 for the document numbering contract (<see cref="IDocumentNumbers"/>), the surface later modules
/// take numbers through inside their own unit of work; no HTTP endpoint reaches it, so the HTTP gates
/// never do. Bound to tenant A, as system work (a job) and as a signed-in administrator, the gate asks
/// for and previews a number of every registered document type in every company of tenant B, and in
/// every company of tenant A with every branch of tenant B; a user limited to one company asks in the
/// other, and a user whose permission is held in one company asks in the other. Every request must be
/// refused by the module's own check (the company scope, the permission's companies, the branch's
/// company) and never answered with a number, and tenant B's counters and issued numbers must not
/// change. A refusal that only the database gave (no sequence visible) counts for system work, which
/// sees every company of its own tenant by design.
/// </summary>
public sealed class G1NumberingContractTests(NumberingContractFixture fixture) : IClassFixture<NumberingContractFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string Fingerprint =
        "SELECT coalesce((SELECT string_agg(id::text || ':' || next_value || ':' || coalesce(last_issued, 0) || ':' || xmin::text, ',' ORDER BY id) FROM numbering.counters WHERE tenant_id = @t), '') || '|' || " +
        "coalesce((SELECT string_agg(id::text || ':' || number, ',' ORDER BY id) FROM numbering.issued_numbers WHERE tenant_id = @t), '') || '|' || " +
        "coalesce((SELECT string_agg(id::text || ':' || prefix || ':' || xmin::text, ',' ORDER BY id) FROM numbering.sequences WHERE tenant_id = @t), '')";

    [Fact]
    public async Task Tenant_A_never_takes_or_previews_a_number_of_tenant_B_or_of_a_company_outside_its_scope()
    {
        List<Guid> victimCompanies, victimBranches, ownCompanies;
        string before;
        await using (var owner = await Env.OpenAdminAsync())
        {
            victimCompanies = await DbCatalog.ReadAsync(owner, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY code", r => r.GetGuid(0), ("t", Env.TenantB.Id));
            victimBranches = await DbCatalog.ReadAsync(owner, "SELECT id FROM tenancy.branches WHERE tenant_id = @t ORDER BY code", r => r.GetGuid(0), ("t", Env.TenantB.Id));
            ownCompanies = await DbCatalog.ReadAsync(owner, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY code", r => r.GetGuid(0), ("t", Env.TenantA.Id));
            before = await DbCatalog.ScalarAsync<string>(owner, Fingerprint, ("t", Env.TenantB.Id));
        }
        Assert.True(victimCompanies.Count >= 2 && victimBranches.Count >= 2 && ownCompanies.Count >= 2, "the gate workspaces have two companies with branches each");
        var admin = await UserIdAsync(Env.Email(Env.TenantA, "admin"));
        var types = Env.Factory.Services.GetServices<NumberedDocumentType>().Select(t => t.Key).ToList();
        Assert.True(types.Count >= 11, $"{types.Count} document types registered");
        var problems = new List<string>();
        var attempts = 0;
        var date = new DateOnly(2040, 6, 15);

        async Task Attack(string who, Func<Task<UnitScope>> open, Guid company, Guid? branch, string type, IReadOnlySet<string> refusals)
        {
            foreach (var preview in new[] { false, true })
            {
                attempts++;
                await using var unit = await open();
                try
                {
                    var request = new NumberRequest(type, company, date, branch, Guid.NewGuid());
                    var answer = preview
                        ? (await unit.Numbers.PreviewAsync(request, CancellationToken.None)).Number
                        : (await unit.Numbers.TakeAsync(request, CancellationToken.None)).Number;
                    problems.Add($"{who} {(preview ? "previewed" : "took")} {answer} for {type} in company {company} branch {branch}");
                }
                catch (NumberingException refused) when (refusals.Contains(refused.Code))
                {
                }
                catch (Exception other)
                {
                    problems.Add($"{who} asking {type} in company {company} branch {branch}: refused by {other.GetType().Name} ({(other as NumberingException)?.Code ?? other.Message}), not by the module's own check");
                }
                await unit.Session.RollbackAsync();
            }
        }

        var bySystem = new HashSet<string>(StringComparer.Ordinal) { NumberingException.SequenceMissing, NumberingException.CompanyNotAllowed };
        var byScope = new HashSet<string>(StringComparer.Ordinal) { NumberingException.CompanyNotAllowed };
        var byBranch = new HashSet<string>(StringComparer.Ordinal) { NumberingException.BranchNotOfCompany };
        foreach (var type in types)
        {
            foreach (var company in victimCompanies)
            {
                await Attack("tenant A's job", () => UnitScope.JobAsync(Env, Env.TenantA.Id), company, null, type, bySystem);
                await Attack("tenant A's administrator", () => UnitScope.UserAsync(Env, Env.TenantA.Id, admin, ownCompanies, null), company, null, type, byScope);
            }
            foreach (var branch in victimBranches)
            {
                await Attack("tenant A's job with tenant B's branch", () => UnitScope.JobAsync(Env, Env.TenantA.Id), ownCompanies[0], branch, type, byBranch);
            }
            // Inside tenant A: a user of the first company only, and a user whose permission is held in the first company only.
            await Attack("a user of the first company", () => UnitScope.UserAsync(Env, Env.TenantA.Id, admin, [ownCompanies[0]], null), ownCompanies[1], null, type, byScope);
            await Attack("a user whose permission is held in the first company", () => UnitScope.UserAsync(Env, Env.TenantA.Id, admin, ownCompanies, [ownCompanies[0]]), ownCompanies[1], null, type, byScope);
        }

        await using (var owner = await Env.OpenAdminAsync())
        {
            var after = await DbCatalog.ScalarAsync<string>(owner, Fingerprint, ("t", Env.TenantB.Id));
            if (after != before)
            {
                problems.Add("tenant B's sequences, counters or issued numbers changed during the attack");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
        Assert.True(attempts >= Ratchet.Min("g1.numberingContractAttacks"), $"g1.numberingContractAttacks: {attempts}; ratchet minimum {Ratchet.Min("g1.numberingContractAttacks")}");
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<Guid>(owner, "SELECT id FROM identity.users WHERE email = @e", ("e", email));
    }

    /// <summary>A unit of work as a later module's request or job holds one.</summary>
    private sealed class UnitScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;

        private UnitScope(AsyncServiceScope scope)
        {
            _scope = scope;
            Session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            Numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumbers>();
        }

        public ErpDbSession Session { get; }

        public IDocumentNumbers Numbers { get; }

        public static async Task<UnitScope> JobAsync(ErpTestEnvironment env, Guid tenant)
        {
            var unit = new UnitScope(env.Factory.Services.CreateAsyncScope());
            await unit.Session.BeginAsync(tenant, null, "job");
            return unit;
        }

        /// <param name="permitted">The companies where the request's permission is held (null: every company of the scope).</param>
        public static async Task<UnitScope> UserAsync(ErpTestEnvironment env, Guid tenant, Guid user, IReadOnlyCollection<Guid> companies, IReadOnlyCollection<Guid>? permitted)
        {
            var unit = new UnitScope(env.Factory.Services.CreateAsyncScope());
            await unit.Session.BeginAsync(tenant, user, ErpDbSession.UserActorKind);
            await unit.Session.BindCompaniesAsync(companies);
            unit.Session.SetBranchLimits([]);
            await unit.Session.LimitToPermissionAsync(permitted, narrowScope: false);
            return unit;
        }

        public async ValueTask DisposeAsync() => await _scope.DisposeAsync();
    }
}

/// <summary>Its own environment: the attack binds units of work of tenant A directly.</summary>
public sealed class NumberingContractFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
