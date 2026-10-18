using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>The planted branch change's body: the city.</summary>
public sealed record CompanyRolePlantCityRequest(string? City);

/// <summary>The planted company lookup's body: the company.</summary>
public sealed record CompanyRolePlantLookupRequest(Guid? CompanyId);

/// <summary>
/// Self-test plants of the company-role record attack (critic p02 round 11): handlers that check
/// the permission only where the session's permissions come from, the working company, and act on
/// whatever company the record names. One changes a branch's city by its id (with a statement of
/// its own, past the kernel's write check, in the request's unit of work as the planted handlers
/// of the critic's round did); the other answers a company's legal name for the company the body
/// names, in a request that writes (its company scope is every company the user works in). Hosted
/// only by the self-test's planted host over the company-role environment's database.
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
        });

    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(CompanyRolePlantModule).AssemblyQualifiedName };
    }
}
