using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>The planted branch change's body: the city.</summary>
public sealed record CompanyRolePlantCityRequest(string? City);

/// <summary>The planted company lookup's body: the company.</summary>
public sealed record CompanyRolePlantLookupRequest(Guid? CompanyId);

/// <summary>The planted branch edit's body (critic p02 round 13, plant P1): a branch's own fields.</summary>
public sealed record CompanyRolePlantBranchRequest(Guid? CompanyId, string? Code, string? NameEn, string? NameAr, string? City, string? Country, bool? IsActive, uint? Version);

/// <summary>
/// Self-test plants of the company-role record attack (critic p02 round 11): handlers that check
/// the permission only where the session's permissions come from, the working company, and act on
/// whatever company the record names. One changes a branch's city by its id (with a statement of
/// its own, past the kernel's write check, in the request's unit of work as the planted handlers
/// of the critic's round did); the other answers a company's legal name for the company the body
/// names, in a request that writes (its company scope is every company the user works in). Hosted
/// only by the self-test's planted host over the company-role environment's database.
/// <para>Critic p02 round 13, plant P1, twice: a branch edit that closes the branch with one
/// set-based ExecuteUpdate when isActive goes from true to false, and answers, before its
/// per-company check (the critic's live check answered 200 and the branch went inactive).
/// <c>/branch-edits</c> also lifts the database's company write limit first (a product without the
/// company_write_* policies, which the gate's one-field variants must catch on the HTTP side);
/// <c>/branch-closes</c> does not, and the database must refuse it on its own. The city plant
/// lifts the limit too: it stands for a product whose only checks are the endpoint's.</para>
/// </summary>
public sealed class CompanyRolePlantModule : ErpModule
{
    public const string Prefix = "/api/roleplant";

    public override string Name => "roleplant";

    public override void Register(ModuleBuilder module) =>
        module.Endpoints(group =>
        {
            group.MapPut("/branches/{id:guid}", async (Guid id, CompanyRolePlantCityRequest request, ErpDbSession session) =>
                {
                    await LiftWriteLimitAsync(session);
                    await using var command = new NpgsqlCommand("UPDATE tenancy.branches SET city = @city WHERE id = @id", session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("city", (object?)request.City ?? DBNull.Value);
                    command.Parameters.AddWithValue("id", id);
                    var changed = await command.ExecuteNonQueryAsync();
                    return changed == 0 ? Results.NotFound() : Results.Ok(new { id });
                })
                .WithName("roleplant.branches.city")
                .RequirePermission("tenancy.branches.update");
            group.MapPost("/company-name", async (CompanyRolePlantLookupRequest request, ErpDbSession session) =>
                {
                    await using var command = new NpgsqlCommand("SELECT legal_name_en FROM tenancy.companies WHERE id = @id", session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("id", request.CompanyId ?? Guid.Empty);
                    return await command.ExecuteScalarAsync() is string name ? Results.Ok(new { name }) : Results.NotFound();
                })
                .WithName("roleplant.companies.name")
                .RequirePermission("tenancy.companies.update");
            foreach (var (route, lift) in new[] { ("branch-edits", true), ("branch-closes", false) })
            {
                group.MapGet($"/{route}/{{id:guid}}", async (Guid id, TenancyDbContext db) =>
                        await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id) is { } b
                            ? Results.Ok(new CompanyRolePlantBranchRequest(b.CompanyId, b.Code, b.NameEn, b.NameAr, b.City, b.Country, b.IsActive, b.Version))
                            : Results.NotFound())
                    .WithName($"roleplant.{route}.get")
                    .RequirePermission("tenancy.branches.read");
                group.MapPut($"/{route}/{{id:guid}}", async (Guid id, CompanyRolePlantBranchRequest request, TenancyDbContext db, ErpDbSession session) =>
                    {
                        if (request.Country is null || request.IsActive is null || string.IsNullOrWhiteSpace(request.NameEn))
                        {
                            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] });
                        }
                        // The planted fault: a quick path that closes an open branch in one statement
                        // and answers, before the per-company check.
                        if (request.IsActive == false)
                        {
                            if (lift)
                            {
                                await LiftWriteLimitAsync(session);
                            }
                            if (await db.Branches.Where(b => b.Id == id && b.IsActive).ExecuteUpdateAsync(set => set.SetProperty(b => b.IsActive, false)) > 0)
                            {
                                return Results.Ok(new { id });
                            }
                        }
                        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id);
                        if (branch is null)
                        {
                            return Results.NotFound();
                        }
                        if (!session.PermitsCompany(branch.CompanyId))
                        {
                            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: CompanyPermissionWriteException.Code);
                        }
                        if (request.CompanyId != branch.CompanyId)
                        {
                            return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["fixed"] });
                        }
                        branch.Code = string.IsNullOrWhiteSpace(request.Code) ? branch.Code : request.Code.Trim().ToUpperInvariant();
                        branch.NameEn = request.NameEn.Trim();
                        branch.NameAr = request.NameAr?.Trim() ?? "";
                        branch.City = request.City;
                        branch.Country = request.Country.Trim().ToUpperInvariant();
                        branch.IsActive = request.IsActive.Value;
                        await db.SaveChangesAsync();
                        return Results.Ok(new { id });
                    })
                    .WithName($"roleplant.{route}.update")
                    .RequirePermission("tenancy.branches.update");
            }
        });

    /// <summary>What a product without the company_write_* policies would be: no write limit.</summary>
    private static async Task LiftWriteLimitAsync(ErpDbSession session)
    {
        await using var lift = new NpgsqlCommand("SELECT set_config('app.write_scope', '', true)", session.Connection, session.Transaction);
        await lift.ExecuteNonQueryAsync();
    }

    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(CompanyRolePlantModule).AssemblyQualifiedName };
    }
}
