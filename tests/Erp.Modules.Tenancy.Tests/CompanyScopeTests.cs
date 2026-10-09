using Erp.Kernel.Data;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>The company scope of a unit of work, below the HTTP layer: what a signed-in user's
/// session sees before and after its companies are bound, and what system work sees.</summary>
public sealed class CompanyScopeTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<(Guid X, Guid Y)> CompaniesAsync()
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id LIMIT 2", admin);
        command.Parameters.AddWithValue("t", Env.TenantA.Id);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return (ids[0], ids[1]);
    }

    [Fact]
    public async Task System_work_sees_every_company_and_a_user_sees_none_until_bound()
    {
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.BeginAsync(Env.TenantA.Id, null, "system");
            var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
            Assert.True(session.AllCompanies);
            Assert.Equal(2, await db.Companies.CountAsync());
        }
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.BeginAsync(Env.TenantA.Id, Guid.NewGuid(), ErpDbSession.UserActorKind);
            var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
            Assert.False(session.AllCompanies);
            Assert.Equal(0, await db.Companies.CountAsync());
            // Without the query filter, row-level security still shows nothing.
            Assert.Equal(0, await db.Companies.IgnoreQueryFilters().CountAsync());
            // The tenant itself (not company data) is visible.
            Assert.Equal(1, await db.Tenants.CountAsync());
        }
    }

    [Fact]
    public async Task Binding_companies_with_a_query_in_the_same_round_trip_runs_the_query_inside_the_bound_scope()
    {
        var (x, y) = await CompaniesAsync();
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, Guid.NewGuid(), ErpDbSession.UserActorKind);
        var seen = new List<Guid>();
        var query = new NpgsqlBatchCommand("SELECT id FROM tenancy.companies ORDER BY id");
        await session.BindCompaniesAsync([x], query, async (reader, ct) =>
        {
            while (await reader.ReadAsync(ct)) seen.Add(reader.GetGuid(0));
        });
        // The settings ran first: the query saw only company X, not Y (row-level security).
        Assert.Equal([x], seen);
        Assert.Equal([x], session.CompanyIds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.BindCompaniesAsync([x, y]));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.BindCompaniesAsync([y], new NpgsqlBatchCommand("SELECT 1"), (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task A_read_only_request_binding_is_read_only_from_the_first_statement()
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, null, "system");
        await session.MakeReadOnlyAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('transaction_read_only'), current_setting('app.tenant_id')", session.Connection, session.Transaction);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("on", reader.GetString(0));
        Assert.Equal(Env.TenantA.Id.ToString(), reader.GetString(1));
    }

    [Fact]
    public async Task A_bound_scope_shows_only_its_companies_cannot_be_widened_and_refuses_writes_elsewhere()
    {
        var (x, y) = await CompaniesAsync();
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(Env.TenantA.Id, Guid.NewGuid(), ErpDbSession.UserActorKind);
        await session.BindCompaniesAsync([x]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.BindCompaniesAsync([x, y]));

        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        Assert.Equal([x], await db.Companies.Select(c => c.Id).ToListAsync());
        Assert.Equal([x], await db.Companies.IgnoreQueryFilters().Select(c => c.Id).ToListAsync());
        Assert.All(await db.Branches.ToListAsync(), b => Assert.Equal(x, b.CompanyId));
        Assert.Contains("company_id", db.Branches.ToQueryString(), StringComparison.Ordinal);

        // A branch for company Y is refused by the application before the database sees it.
        db.Branches.Add(new Branch { CompanyId = y, Code = "NOPE", NameEn = "Nope", Country = "AE" });
        await Assert.ThrowsAsync<CrossCompanyWriteException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // A new company needs IncludeNewCompanyAsync, which makes it part of the scope.
        var company = new Company { Code = "SCOPE-NEW", LegalNameEn = "Scope New LLC", Country = "AE", BaseCurrency = "AED" };
        company.CompanyId = company.Id;
        db.Companies.Add(company);
        await Assert.ThrowsAsync<CrossCompanyWriteException>(() => db.SaveChangesAsync());
        await session.IncludeNewCompanyAsync(company.Id);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Companies.CountAsync());
        await session.RollbackAsync();
    }
    [Fact]
    public async Task The_workspace_record_is_written_only_by_a_unit_of_work_that_holds_the_whole_workspace()
    {
        // The kernel's second layer under the endpoint's own check (critic p02 round 6): a user who
        // works in only some companies, or some branches, never writes the record every company shares.
        var (x, y) = await CompaniesAsync();
        foreach (var (companies, limited, holds) in new[] { (new[] { x }, Array.Empty<Guid>(), false), (new[] { x, y }, new[] { x }, false), (new[] { x, y }, Array.Empty<Guid>(), true) })
        {
            await using var scope = Env.Factory.Services.CreateAsyncScope();
            var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.BeginAsync(Env.TenantA.Id, Guid.NewGuid(), ErpDbSession.UserActorKind);
            await session.BindCompaniesAsync(companies);
            session.SetBranchLimits(limited);
            Assert.False(session.HoldsWholeWorkspace);
            session.SetWorkspaceHolder(holds);
            Assert.Throws<InvalidOperationException>(() => session.SetWorkspaceHolder(true));
            Assert.Equal(holds, session.HoldsWholeWorkspace);
            var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
            var tenant = await db.Tenants.SingleAsync();
            tenant.NameEn += " (kernel check)";
            if (holds)
            {
                await db.SaveChangesAsync();
            }
            else
            {
                var refused = await Assert.ThrowsAsync<CrossBranchWriteException>(() => db.SaveChangesAsync());
                Assert.Equal(CrossBranchWriteException.WorkspaceCode, refused.Code);
            }
            await session.RollbackAsync();
        }
        // System work always may.
        await using (var system = Env.Factory.Services.CreateAsyncScope())
        {
            var session = system.ServiceProvider.GetRequiredService<ErpDbSession>();
            await session.BeginAsync(Env.TenantA.Id, null, "system");
            Assert.True(session.HoldsWholeWorkspace);
            await session.RollbackAsync();
        }
    }
}
