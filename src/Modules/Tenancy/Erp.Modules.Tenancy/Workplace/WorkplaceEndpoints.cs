using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Modules.Tenancy.Workplace;

public sealed record WorkplaceBranch(Guid Id, string Code, string NameEn, string NameAr);

public sealed record WorkplaceCompany(Guid Id, string Code, string LegalNameEn, string LegalNameAr, string BaseCurrency, IReadOnlyList<WorkplaceBranch> Branches);

/// <summary>The company and branch the user works in now, and every active company and branch
/// they may switch to (what the switcher in the top bar offers).</summary>
public sealed record WorkplaceDto(Guid? CompanyId, Guid? BranchId, IReadOnlyList<WorkplaceCompany> Companies);

/// <summary>Switch to a company (and one of its branches; empty picks the first the user may
/// work in).</summary>
public sealed record SwitchWorkplaceRequest(Guid? CompanyId, Guid? BranchId);

internal static class WorkplaceEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/workplace", Get)
            .WithName("tenancy.workplace.get")
            .WithSummary("The caller's working company and branch, and the companies and branches they may switch to.")
            .RequirePermission(TenancyPermissions.WorkplaceRead);

        group.MapPut("/workplace", Switch)
            .WithName("tenancy.workplace.switch")
            .WithSummary("Switch the caller's working company and branch (kept for their next sessions).")
            .ProducesValidationProblem()
            .RequirePermission(TenancyPermissions.WorkplaceSwitch);
    }

    private static async Task<Ok<WorkplaceDto>> Get(TenancyDbContext db, ICompanyContext scope, CancellationToken cancellationToken) =>
        TypedResults.Ok(await BuildAsync(db, scope, scope.ActiveCompanyId, scope.ActiveBranchId, cancellationToken));

    private static async Task<Results<Ok<WorkplaceDto>, ProblemHttpResult>> Switch(
        SwitchWorkplaceRequest request, TenancyDbContext db, ErpDbSession session, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("companyId", request.CompanyId);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var options = await BuildAsync(db, session, null, null, cancellationToken);
        var company = options.Companies.FirstOrDefault(c => c.Id == request.CompanyId);
        validator.Must(company is not null, "companyId", "tenancyNotYourCompany");
        var branchId = request.BranchId;
        if (company is not null)
        {
            if (branchId is { } wanted)
            {
                validator.Must(company.Branches.Any(b => b.Id == wanted), "branchId", "tenancyNotYourBranch");
            }
            else
            {
                branchId = company.Branches.FirstOrDefault()?.Id;
            }
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var row = await db.Workplaces.IgnoreQueryFilters([ModuleDbContext.CompanyFilterName])
            .SingleOrDefaultAsync(w => w.UserId == caller.UserId, cancellationToken);
        if (row is not null && !session.AllowsCompany(row.CompanyId))
        {
            // A workplace left in a company the user no longer may work in (access since removed).
            db.Workplaces.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            row = null;
        }
        if (row is null)
        {
            db.Workplaces.Add(new UserWorkplace { UserId = caller.UserId, CompanyId = company!.Id, BranchId = branchId });
        }
        else if (row.CompanyId != company!.Id || row.BranchId != branchId)
        {
            row.CompanyId = company.Id;
            row.BranchId = branchId;
        }
        await db.SaveChangesAsync(cancellationToken);
        session.SetWorkplace(company.Id, branchId, session.BranchIds);
        return TypedResults.Ok(options with { CompanyId = company.Id, BranchId = branchId });
    }

    /// <summary>Active companies in scope with the active branches the user may work in.</summary>
    private static async Task<WorkplaceDto> BuildAsync(TenancyDbContext db, ICompanyContext scope, Guid? companyId, Guid? branchId, CancellationToken cancellationToken)
    {
        var companies = await db.Companies.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency })
            .ToListAsync(cancellationToken);
        // Branches in the order they were opened (time-ordered ids): the first is usually the head
        // office, which is where a switch to the company lands.
        var branches = await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.CompanyId, b.Code, b.NameEn, b.NameAr })
            .ToListAsync(cancellationToken);
        var allowed = scope.AllCompanies ? null : scope.BranchIds.ToHashSet();
        return new WorkplaceDto(companyId, branchId, companies.Select(c => new WorkplaceCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency,
            branches.Where(b => b.CompanyId == c.Id && (allowed is null || allowed.Contains(b.Id)))
                .Select(b => new WorkplaceBranch(b.Id, b.Code, b.NameEn, b.NameAr)).ToList())).ToList());
    }
}

/// <summary>
/// Binds every signed-in request to the user's companies: reads their company and branch access
/// (their own rows, readable before the scope exists), binds the company scope (row-level security
/// and query filters then hide every other company's rows), and works out their working company
/// and branch: the one they chose if they still may work there, else their first active company
/// (by code) and its first-opened branch they may work in.
/// </summary>
internal sealed class CompanyScopeBinder(ErpDbSession session, TenancyBranchScope branchScope) : ISessionScopeBinder
{
    internal const string AccessStatement = """
        SELECT a.company_id, a.all_branches FROM tenancy.user_company_access a WHERE a.tenant_id = @tenant AND a.user_id = @user
        """;

    public async Task<bool> BindAsync(ResolvedSession resolved, CancellationToken cancellationToken)
    {
        var userId = resolved.UserId;
        // The companies the user may work in, on every authenticated request: one direct statement
        // in the bound tenant (row-level security, with the tenant named as the second layer), not
        // an EF query, whose per-query work and context start-up cost more than the statement.
        var access = new List<(Guid CompanyId, bool AllBranches)>();
        await using (var command = new NpgsqlCommand(AccessStatement, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("tenant", session.TenantId);
            command.Parameters.AddWithValue("user", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                access.Add((reader.GetGuid(0), reader.GetBoolean(1)));
            }
        }
        if (access.Count == 0)
        {
            await session.BindCompaniesAsync([], cancellationToken);
            session.SetBranchLimits([]);
            session.SetWorkspaceHolder(false);
            session.SetWorkplace(null, null, []);
            return true;
        }
        // The same round trip as the company binding, inside the bound scope: active companies and
        // branches, the user's branch limits and the working company and branch they chose.
        var companies = new List<(Guid Id, string Code)>();
        var branches = new List<(Guid Id, Guid CompanyId)>();
        var limited = new HashSet<Guid>();
        (Guid CompanyId, Guid? BranchId)? chosen = null;
        var workspaceCompanies = int.MaxValue;
        var query = new NpgsqlBatchCommand("""
            SELECT 1, c.id, c.id, c.code FROM tenancy.companies c WHERE c.is_active
            UNION ALL SELECT 2, b.id, b.company_id, NULL FROM tenancy.branches b WHERE b.is_active
            UNION ALL SELECT 3, a.branch_id, a.company_id, NULL FROM tenancy.user_branch_access a WHERE a.user_id = @user
            UNION ALL SELECT 4, w.branch_id, w.company_id, NULL FROM tenancy.user_workplaces w
                       WHERE w.user_id = @user AND erp.company_allowed(w.company_id)
            UNION ALL SELECT 5, t.id, t.id, t.company_count::text FROM tenancy.tenants t
            """);
        query.Parameters.AddWithValue("user", userId);
        await session.BindCompaniesAsync(access.Select(a => a.CompanyId).ToList(), query, async (reader, ct) =>
        {
            while (await reader.ReadAsync(ct))
            {
                var companyOfRow = reader.GetGuid(2);
                switch (reader.GetInt32(0))
                {
                    case 1: companies.Add((reader.GetGuid(1), reader.GetString(3))); break;
                    case 2: branches.Add((reader.GetGuid(1), companyOfRow)); break;
                    case 3: limited.Add(reader.GetGuid(1)); break;
                    case 5: workspaceCompanies = int.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture); break;
                    default: chosen = (companyOfRow, reader.IsDBNull(1) ? null : reader.GetGuid(1)); break;
                }
            }
        }, cancellationToken);
        // Records every branch of a company shares are written only by users who hold every branch
        // of it (the kernel refuses the rest, whatever an endpoint checked).
        session.SetBranchLimits(access.Where(a => !a.AllBranches).Select(a => a.CompanyId));
        // Records every company shares (the workspace's own record) are written only by users who
        // work in every company and every branch of each (the kernel refuses the rest).
        session.SetWorkspaceHolder(access.Count >= workspaceCompanies && access.All(a => a.AllBranches));
        var activeCompanies = companies.OrderBy(c => c.Code, StringComparer.Ordinal).Select(c => c.Id).ToList();
        var allBranches = access.Where(a => a.AllBranches).Select(a => a.CompanyId).ToHashSet();
        // Branch limits hold for the rest of the request: only the branches the user may work in
        // exist for them in a company where they hold only some.
        branchScope.Set(access.Where(a => !a.AllBranches).Select(a => a.CompanyId), limited);
        // Branches in opening order (time-ordered ids), as the switcher lists them.
        var allowedBranches = branches.Where(b => allBranches.Contains(b.CompanyId) || limited.Contains(b.Id))
            .OrderBy(b => b.Id).Select(b => new { b.Id, b.CompanyId }).ToList();
        var companiesInOrder = activeCompanies;

        Guid? companyId = chosen is { } picked && companiesInOrder.Contains(picked.CompanyId) ? picked.CompanyId : companiesInOrder.Cast<Guid?>().FirstOrDefault();
        Guid? branchId = null;
        if (companyId is { } working)
        {
            var mine = allowedBranches.Where(b => b.CompanyId == working).Select(b => b.Id).ToList();
            branchId = chosen is { BranchId: { } wanted } choice && choice.CompanyId == working && mine.Contains(wanted) ? wanted : mine.Cast<Guid?>().FirstOrDefault();
        }
        session.SetWorkplace(companyId, branchId, allowedBranches.Select(b => b.Id).ToList());
        return true;
    }
}
