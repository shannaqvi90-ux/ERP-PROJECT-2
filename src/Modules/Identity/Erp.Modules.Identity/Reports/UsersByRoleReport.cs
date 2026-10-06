using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Roles;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Reports;

/// <summary>
/// Users by role: who holds each role, whether they are active, their language and when they last
/// signed in, grouped by role (or by language, status or company) with a count per group. A user
/// with two roles is listed under each; a user without roles under "no value". A role held in one
/// company only is listed with that company ("Only in company"); a role held in every company
/// leaves that cell empty. Filters: one role, active or inactive, language, signed in since a
/// date. Rows are read through row-level security, so roles held in companies the caller does not
/// work in are not listed (as on the user's record).
/// </summary>
internal sealed class UsersByRoleReport(IdentityDbContext db, ICompanyDirectory companies) : IReportSource
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
            new ReportColumn("company", "identity.report.onlyInCompany", ListColumnType.Text, Groupable: true),
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
        // Every holding: roles in every company (no company), roles in one company, and (without a
        // chosen role) people holding no role at all, each as one row of the same shape.
        var everywhere = from u in users
                         join h in db.UserRoles.AsNoTracking() on u.Id equals h.UserId
                         join r in db.Roles.AsNoTracking() on h.RoleId equals r.Id
                         select new HoldingRow { UserId = u.Id, RoleId = (Guid?)r.Id, RoleEn = r.NameEn, RoleAr = r.NameAr, CompanyId = null };
        var inOneCompany = from u in users
                           join h in db.UserCompanyRoles.AsNoTracking() on u.Id equals h.UserId
                           join r in db.Roles.AsNoTracking() on h.RoleId equals r.Id
                           select new HoldingRow { UserId = u.Id, RoleId = (Guid?)r.Id, RoleEn = r.NameEn, RoleAr = r.NameAr, CompanyId = (Guid?)h.CompanyId };
        var holdings = everywhere.Concat(inOneCompany);
        if (roleId is { } id)
        {
            holdings = holdings.Where(h => h.RoleId == id);
        }
        else
        {
            holdings = holdings.Concat(users
                .Where(u => !db.UserRoles.Any(h => h.UserId == u.Id) && !db.UserCompanyRoles.Any(h => h.UserId == u.Id))
                .Select(u => new HoldingRow { UserId = u.Id, RoleId = null, RoleEn = null, RoleAr = null, CompanyId = null }));
        }
        var rows = from h in holdings
                   join u in db.Users.AsNoTracking() on h.UserId equals u.Id
                   select new { User = u, h.RoleEn, h.RoleAr, h.CompanyId };
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderBy(x => x.RoleEn == null).ThenBy(x => x.RoleEn).ThenBy(x => x.User.DisplayName).ThenBy(x => x.User.Id)
            .ThenBy(x => x.CompanyId != null).ThenBy(x => x.CompanyId)
            .Take(run.MaxRows).ToListAsync(cancellationToken);
        var companyNames = page.Any(x => x.CompanyId is not null)
            ? (await companies.ListAsync(cancellationToken)).ToDictionary(c => c.Id, c => new LocalText($"{c.Code} · {c.LegalNameEn}", $"{c.Code} · {c.LegalNameAr}"))
            : [];
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
                // Row-level security returns roles in companies the caller works in (named by the
                // directory) and the caller's own; a company the directory does not name prints as
                // its id, never empty (empty means every company).
                ["company"] = x.CompanyId is { } company
                    ? companyNames.GetValueOrDefault(company) ?? new LocalText(company.ToString(), company.ToString())
                    : null,
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

/// <summary>One role a user holds (in every company when there is no company), or none.</summary>
internal sealed class HoldingRow
{
    public Guid UserId { get; init; }
    public Guid? RoleId { get; init; }
    public string? RoleEn { get; init; }
    public string? RoleAr { get; init; }
    public Guid? CompanyId { get; init; }
}
