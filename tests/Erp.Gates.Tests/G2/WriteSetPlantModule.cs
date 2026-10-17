using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>The planted switch's body: the company to work in and, the plant, the company's
/// Arabic-legal-name requirement.</summary>
public sealed record WritePlantSwitchRequest(Guid? CompanyId, Guid? BranchId, bool? ArabicLegalNameRequired);

/// <summary>
/// Self-test plant of the write-set check (critic p02 round 10, plant P1b): a workplace switch that
/// declares only <c>tenancy.workplace.switch</c> and, when the body carries
/// <c>arabicLegalNameRequired</c> and the caller works in every branch of the company, also sets the
/// company's Arabic-legal-name requirement, a legal setting that needs
/// <c>tenancy.companies.update</c>. It writes through the request's own unit of work (row-level
/// security, the audit trigger names the caller), as the planted handler did. Hosted only by the
/// write-set self-test's planted host over the write-set environment's database.
/// </summary>
public sealed class WriteSetPlantModule : ErpModule
{
    public const string Route = "/api/writeplant/workplace";

    public override string Name => "writeplant";

    public override void Register(ModuleBuilder module) =>
        module.Endpoints(group => group.MapPut("/workplace", async (WritePlantSwitchRequest request, ErpDbSession session) =>
            {
                if (request.CompanyId is not { } company)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["required"] });
                }
                if (request.ArabicLegalNameRequired is { } required && session.HoldsEveryBranch(company))
                {
                    await using var command = new NpgsqlCommand(
                        "UPDATE tenancy.companies SET arabic_legal_name_required = @required WHERE id = @company AND (NOT @required OR legal_name_ar <> '')",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("required", required);
                    command.Parameters.AddWithValue("company", company);
                    await command.ExecuteNonQueryAsync();
                }
                return Results.Ok(new { companyId = company });
            })
            .WithName("writeplant.workplace.switch")
            .RequirePermission("tenancy.workplace.switch"));

    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(WriteSetPlantModule).AssemblyQualifiedName };
    }
}
