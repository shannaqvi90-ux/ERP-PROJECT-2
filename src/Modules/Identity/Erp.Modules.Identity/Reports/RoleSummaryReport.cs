using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Reports;

/// <summary>
/// Roles and access: every role with how many users hold it and how many permissions it grants,
/// grouped by kind (system or custom) with a total of each per group and for the workspace. A
/// review of who can do what starts here. Rows are read through row-level security.
/// </summary>
internal sealed class RoleSummaryReport(IdentityDbContext db) : IReportSource
{
    public const string Key = "identity.roleSummary";

    private static readonly IReadOnlyList<ListChoice> Kinds =
        [new ListChoice("system", "identity.roles.system"), new ListChoice("custom", "identity.roles.custom")];

    public static readonly ReportDefinition Definition = new(
        Key, "identity.report.roleSummary", IdentityPermissions.RolesRead,
        [new ReportParameter("kind", "identity.roles.kind", ReportParameterType.Choice, Choices: Kinds)],
        [
            new ReportColumn("name", "identity.roles.name", ListColumnType.Text),
            new ReportColumn("kind", "identity.roles.kind", ListColumnType.Choice, Groupable: true, Choices: Kinds),
            new ReportColumn("users", "identity.roles.users", ListColumnType.Number, Total: true),
            new ReportColumn("permissions", "identity.roles.permissions", ListColumnType.Number, Total: true),
        ],
        DescriptionKey: "identity.report.roleSummaryHint",
        DefaultGroupBy: "kind");

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var roles = db.Roles.AsNoTracking();
        if (run.Text("kind") is { } kind)
        {
            var system = kind == "system";
            roles = roles.Where(r => r.IsSystem == system);
        }
        var total = await roles.CountAsync(cancellationToken);
        var page = await roles
            .OrderBy(r => r.NameEn).ThenBy(r => r.Id)
            .Take(run.MaxRows)
            .Select(r => new { r.NameEn, r.NameAr, r.IsSystem, r.Permissions, Users = db.UserRoles.Count(ur => ur.RoleId == r.Id) })
            .ToListAsync(cancellationToken);
        return new ReportData
        {
            Rows = page.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["name"] = new LocalText(r.NameEn, r.NameAr),
                ["kind"] = r.IsSystem ? "system" : "custom",
                ["users"] = r.Users,
                ["permissions"] = r.Permissions.Count,
            }).ToList(),
            Truncated = total > page.Count,
            MatchCount = total,
        };
    }
}
