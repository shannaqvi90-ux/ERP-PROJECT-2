using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Roles;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Reports;

/// <summary>
/// Users by role: who holds each role, whether they are active, their language and when they last
/// signed in, grouped by role (or by language or status) with a count per group. A user with two
/// roles is listed under each; a user without roles under "no value". Filters: one role, active or
/// inactive, language, signed in since a date. Rows are read through row-level security.
/// </summary>
internal sealed class UsersByRoleReport(IdentityDbContext db) : IReportSource
{
    public const string Key = "identity.usersByRole";

    private static readonly IReadOnlyList<ListChoice> Statuses =
        [new ListChoice("active", "identity.users.active"), new ListChoice("inactive", "identity.users.inactive")];

    private static readonly IReadOnlyList<ListChoice> LanguageChoices =
        [new ListChoice("en", "identity.language.en"), new ListChoice("ar", "identity.language.ar")];

    public static readonly ReportDefinition Definition = new(
        Key, "identity.report.usersByRole", IdentityPermissions.UsersRead,
        [
            new ReportParameter("role", "identity.report.role", ReportParameterType.Reference, Lookup: RolesList.Key),
            new ReportParameter("status", "identity.users.status", ReportParameterType.Choice, Choices: Statuses),
            new ReportParameter("userLanguage", "identity.users.language", ReportParameterType.Choice, Choices: LanguageChoices),
            new ReportParameter("signedInSince", "identity.report.signedInSince", ReportParameterType.Date),
        ],
        [
            new ReportColumn("role", "identity.report.role", ListColumnType.Text, Groupable: true),
            new ReportColumn("name", "identity.users.name", ListColumnType.Text),
            new ReportColumn("email", "identity.users.email", ListColumnType.Text),
            new ReportColumn("language", "identity.users.language", ListColumnType.Choice, Groupable: true, Choices: LanguageChoices),
            new ReportColumn("active", "identity.users.status", ListColumnType.Boolean, Groupable: true),
            new ReportColumn("lastSignIn", "identity.users.lastSignIn", ListColumnType.DateTime),
        ],
        DescriptionKey: "identity.report.usersByRoleHint",
        DefaultGroupBy: "role");

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var roleId = run.Get<Guid>("role");
        var users = db.Users.AsNoTracking();
        if (run.Text("status") is { } status)
        {
            var active = status == "active";
            users = users.Where(u => u.IsActive == active);
        }
        if (run.Text("userLanguage") is { } language)
        {
            users = users.Where(u => u.Language == language);
        }
        if (run.Get<DateOnly>("signedInSince") is { } since)
        {
            var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            users = users.Where(u => u.LastSignInAt >= from);
        }
        var rows = roleId is { } id
            ? from u in users
              join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
              join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
              where r.Id == id
              select new { User = u, RoleEn = (string?)r.NameEn, RoleAr = (string?)r.NameAr }
            : from u in users
              from ur in db.UserRoles.AsNoTracking().Where(x => x.UserId == u.Id).DefaultIfEmpty()
              from r in db.Roles.AsNoTracking().Where(x => ur != null && x.Id == ur.RoleId).DefaultIfEmpty()
              select new { User = u, RoleEn = r == null ? null : r.NameEn, RoleAr = r == null ? null : r.NameAr };
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderBy(x => x.RoleEn == null).ThenBy(x => x.RoleEn).ThenBy(x => x.User.DisplayName).ThenBy(x => x.User.Id)
            .Take(run.MaxRows).ToListAsync(cancellationToken);
        var texts = new Dictionary<string, LocalText>();
        if (roleId is { } chosen && await db.Roles.AsNoTracking().Where(r => r.Id == chosen).Select(r => new { r.NameEn, r.NameAr }).SingleOrDefaultAsync(cancellationToken) is { } role)
        {
            texts["role"] = new LocalText(role.NameEn, role.NameAr);
        }
        return new ReportData
        {
            Rows = page.Select(x => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["role"] = x.RoleEn is null ? null : new LocalText(x.RoleEn, x.RoleAr),
                ["name"] = new LocalText(x.User.DisplayName, x.User.DisplayNameAr),
                ["email"] = x.User.Email,
                ["language"] = x.User.Language,
                ["active"] = x.User.IsActive,
                ["lastSignIn"] = x.User.LastSignInAt,
            }).ToList(),
            Truncated = total > page.Count,
            MatchCount = total,
            ParameterTexts = texts,
        };
    }
}
