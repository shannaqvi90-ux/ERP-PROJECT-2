using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
using Erp.Modules.Tenancy.Companies;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Reports;

/// <summary>
/// A company's profile as a printed document: its legal names, licence, tax registration,
/// accounting settings, address and contact details, then its branches. One of the caller's
/// companies only (row-level security and the company scope); any other id answers 404.
/// </summary>
internal sealed class CompanyProfileReport(TenancyDbContext db) : IReportSource
{
    public const string Key = "tenancy.companyProfile";

    private static readonly IReadOnlyList<ListChoice> Months =
        Enumerable.Range(1, 12).Select(m => new ListChoice(m.ToString(System.Globalization.CultureInfo.InvariantCulture), $"tenancy.month.{m}")).ToList();

    public static readonly ReportDefinition Definition = new(
        Key, "tenancy.report.companyProfile", TenancyPermissions.CompaniesRead,
        [new ReportParameter("company", "tenancy.report.company", ReportParameterType.Reference, Required: true, Lookup: CompaniesList.Key)],
        [
            new ReportColumn("code", "tenancy.branch.code", ListColumnType.Text),
            new ReportColumn("name", "tenancy.report.branchName", ListColumnType.Text),
            new ReportColumn("city", "tenancy.address.city", ListColumnType.Text, Groupable: true),
            new ReportColumn("emirate", "tenancy.address.emirate", ListColumnType.Choice, Groupable: true, Choices: TenancyLists.Emirates),
            new ReportColumn("phone", "tenancy.address.phone", ListColumnType.Text),
            new ReportColumn("active", "tenancy.common.active", ListColumnType.Boolean, Groupable: true),
        ],
        DescriptionKey: "tenancy.report.companyProfileHint",
        Facts:
        [
            new ReportColumn("code", "tenancy.company.code", ListColumnType.Text),
            new ReportColumn("legalNameEn", "tenancy.company.legalNameEn", ListColumnType.Text),
            new ReportColumn("legalNameAr", "tenancy.company.legalNameAr", ListColumnType.Text),
            new ReportColumn("tradeLicenceNumber", "tenancy.company.tradeLicenceNumber", ListColumnType.Text),
            new ReportColumn("tradeLicenceAuthority", "tenancy.company.tradeLicenceAuthority", ListColumnType.Text),
            new ReportColumn("taxRegistrationNumber", "tenancy.company.taxRegistrationNumber", ListColumnType.Text),
            new ReportColumn("baseCurrency", "tenancy.company.baseCurrency", ListColumnType.Text),
            new ReportColumn("fiscalYearStartMonth", "tenancy.company.fiscalYearStartMonth", ListColumnType.Choice, Choices: Months),
            new ReportColumn("fiscalYearStartDay", "tenancy.company.fiscalYearStartDay", ListColumnType.Number),
            new ReportColumn("address", "tenancy.address.title", ListColumnType.Text),
            new ReportColumn("emirate", "tenancy.address.emirate", ListColumnType.Choice, Choices: TenancyLists.Emirates),
            new ReportColumn("poBox", "tenancy.address.poBox", ListColumnType.Text),
            new ReportColumn("country", "tenancy.address.country", ListColumnType.Text),
            new ReportColumn("phone", "tenancy.address.phone", ListColumnType.Text),
            new ReportColumn("email", "tenancy.address.email", ListColumnType.Text),
            new ReportColumn("website", "tenancy.company.website", ListColumnType.Text),
            new ReportColumn("active", "tenancy.common.active", ListColumnType.Boolean),
        ]);

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var id = run.Get<Guid>("company");
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new
            {
                c.Code, c.LegalNameEn, c.LegalNameAr, c.TradeLicenceNumber, c.TradeLicenceAuthority, c.TaxRegistrationNumber, c.BaseCurrency,
                c.FiscalYearStartMonth, c.FiscalYearStartDay, c.AddressLine1, c.AddressLine2, c.City, c.Emirate, c.PoBox, c.Country, c.AddressAr,
                c.Phone, c.Email, c.Website, c.IsActive,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (company is null)
        {
            return null;
        }
        var branches = await db.Branches.AsNoTracking().Where(b => b.CompanyId == id).OrderBy(b => b.Code).Take(run.MaxRows)
            .Select(b => new { b.Code, b.NameEn, b.NameAr, b.City, b.Emirate, b.Phone, b.IsActive })
            .ToListAsync(cancellationToken);
        var english = string.Join(", ", new[] { company.AddressLine1, company.AddressLine2, company.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ReportData
        {
            Subject = new LocalText($"{company.Code} \u00B7 {company.LegalNameEn}", $"{company.Code} \u00B7 {company.LegalNameAr}"),
            Facts = new Dictionary<string, object?>
            {
                ["code"] = company.Code,
                ["legalNameEn"] = company.LegalNameEn,
                ["legalNameAr"] = company.LegalNameAr,
                ["tradeLicenceNumber"] = company.TradeLicenceNumber,
                ["tradeLicenceAuthority"] = company.TradeLicenceAuthority,
                ["taxRegistrationNumber"] = company.TaxRegistrationNumber,
                ["baseCurrency"] = company.BaseCurrency,
                ["fiscalYearStartMonth"] = company.FiscalYearStartMonth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["fiscalYearStartDay"] = company.FiscalYearStartDay,
                ["address"] = english.Length == 0 && string.IsNullOrWhiteSpace(company.AddressAr) ? null : new LocalText(english, company.AddressAr),
                ["emirate"] = company.Emirate,
                ["poBox"] = company.PoBox,
                ["country"] = company.Country,
                ["phone"] = company.Phone,
                ["email"] = company.Email,
                ["website"] = company.Website,
                ["active"] = company.IsActive,
            },
            Rows = branches.Select(b => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["code"] = b.Code,
                ["name"] = new LocalText(b.NameEn, b.NameAr),
                ["city"] = b.City,
                ["emirate"] = b.Emirate,
                ["phone"] = b.Phone,
                ["active"] = b.IsActive,
            }).ToList(),
            ParameterTexts = new Dictionary<string, LocalText> { ["company"] = new($"{company.Code} \u00B7 {company.LegalNameEn}", $"{company.Code} \u00B7 {company.LegalNameAr}") },
        };
    }
}

/// <summary>
/// The branch directory: every branch of the caller's companies with its city, emirate, phone and
/// status, grouped by company (or by city, emirate or status) with a count per group. Filters: one
/// company, one emirate, inactive branches included or not.
/// </summary>
internal sealed class BranchDirectoryReport(TenancyDbContext db) : IReportSource
{
    public const string Key = "tenancy.branchDirectory";

    public static readonly ReportDefinition Definition = new(
        Key, "tenancy.report.branchDirectory", TenancyPermissions.BranchesRead,
        [
            new ReportParameter("company", "tenancy.report.company", ReportParameterType.Reference, Lookup: CompaniesList.Key),
            new ReportParameter("emirate", "tenancy.address.emirate", ReportParameterType.Choice, Choices: TenancyLists.Emirates),
            new ReportParameter("includeInactive", "tenancy.report.includeInactive", ReportParameterType.Boolean),
        ],
        [
            new ReportColumn("company", "tenancy.report.company", ListColumnType.Text, Groupable: true),
            new ReportColumn("code", "tenancy.branch.code", ListColumnType.Text),
            new ReportColumn("name", "tenancy.report.branchName", ListColumnType.Text),
            new ReportColumn("city", "tenancy.address.city", ListColumnType.Text, Groupable: true),
            new ReportColumn("emirate", "tenancy.address.emirate", ListColumnType.Choice, Groupable: true, Choices: TenancyLists.Emirates),
            new ReportColumn("phone", "tenancy.address.phone", ListColumnType.Text),
            new ReportColumn("active", "tenancy.common.active", ListColumnType.Boolean, Groupable: true),
        ],
        DescriptionKey: "tenancy.report.branchDirectoryHint",
        DefaultGroupBy: "company");

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var branches = db.Branches.AsNoTracking();
        var texts = new Dictionary<string, LocalText>();
        if (run.Get<Guid>("company") is { } companyId)
        {
            branches = branches.Where(b => b.CompanyId == companyId);
            if (await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => new { c.Code, c.LegalNameEn, c.LegalNameAr }).SingleOrDefaultAsync(cancellationToken) is { } chosen)
            {
                texts["company"] = new LocalText($"{chosen.Code} \u00B7 {chosen.LegalNameEn}", $"{chosen.Code} \u00B7 {chosen.LegalNameAr}");
            }
        }
        if (run.Text("emirate") is { } emirate)
        {
            branches = branches.Where(b => b.Emirate == emirate);
        }
        if (run.Get<bool>("includeInactive") is not true)
        {
            branches = branches.Where(b => b.IsActive);
        }
        var rows = from b in branches
                   join c in db.Companies.AsNoTracking() on b.CompanyId equals c.Id
                   select new { b.Code, b.NameEn, b.NameAr, b.City, b.Emirate, b.Phone, b.IsActive, CompanyCode = c.Code, c.LegalNameEn, c.LegalNameAr };
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderBy(r => r.CompanyCode).ThenBy(r => r.Code).Take(run.MaxRows).ToListAsync(cancellationToken);
        return new ReportData
        {
            Rows = page.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["company"] = new LocalText($"{r.CompanyCode} \u00B7 {r.LegalNameEn}", $"{r.CompanyCode} \u00B7 {r.LegalNameAr}"),
                ["code"] = r.Code,
                ["name"] = new LocalText(r.NameEn, r.NameAr),
                ["city"] = r.City,
                ["emirate"] = r.Emirate,
                ["phone"] = r.Phone,
                ["active"] = r.IsActive,
            }).ToList(),
            Truncated = total > page.Count,
            MatchCount = total,
            ParameterTexts = texts,
        };
    }
}
