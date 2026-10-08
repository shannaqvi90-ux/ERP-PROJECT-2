using Erp.Kernel.Lists;
using Erp.Modules.Tenancy.Contracts;

namespace Erp.Modules.Tenancy.Companies;

/// <summary>
/// The companies list: its columns, search fields, built-in views and the query binding that
/// serves them from <c>tenancy.companies</c> (row-level security limits it to the companies the
/// caller may work in). Search runs on trigram indexes over the code and both legal names; every
/// sortable column has a (tenant_id, column, id) index. The branch count is shown, not sorted.
/// </summary>
internal static class CompaniesList
{
    public const string Key = "tenancy.companies";

    public static ListBinding<Company> Create() =>
        ListBinding<Company>.For(new ListDefinition(
                Key, "tenancy.companies.title", TenancyPermissions.CompaniesRead, "/api/tenancy/companies",
                [
                    new ListColumn("code", "tenancy.company.code", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("legalNameEn", "tenancy.company.legalNameEn", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("legalNameAr", "tenancy.company.legalNameAr", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("baseCurrency", "tenancy.company.baseCurrency", ListColumnType.Text, Filterable: true, Groupable: true),
                    new ListColumn("city", "tenancy.company.city", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true),
                    new ListColumn("emirate", "tenancy.company.emirate", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: TenancyLists.Emirates),
                    new ListColumn("branchCount", "tenancy.company.branchCount", ListColumnType.Number),
                    new ListColumn("isActive", "tenancy.common.status", ListColumnType.Boolean, Filterable: true, Groupable: true),
                ],
                SearchFields: ["code", "legalNameEn", "legalNameAr"],
                DefaultSort: "code",
                Presets:
                [
                    new ListPreset("active", "tenancy.view.active", Filter: "isActive eq true"),
                    new ListPreset("inactive", "tenancy.view.inactive", Filter: "isActive eq false"),
                    new ListPreset("byEmirate", "tenancy.view.byEmirate", GroupBy: "emirate"),
                ]),
                c => c.Id)
            .Column("code", c => c.Code)
            .Column("legalNameEn", c => c.LegalNameEn)
            .Column("legalNameAr", c => c.LegalNameAr)
            .Column("baseCurrency", c => c.BaseCurrency)
            .Column("city", c => c.City)
            .Column("emirate", c => c.Emirate)
            .Column("isActive", c => c.IsActive);
}

/// <summary>Choices the tenancy lists share.</summary>
internal static class TenancyLists
{
    /// <summary>The seven emirates as stored (camel case) with their label keys.</summary>
    public static readonly IReadOnlyList<ListChoice> Emirates =
        Enum.GetValues<Emirate>().Select(e => new ListChoice(TenancyValidation.EmirateValue(e)!, $"tenancy.emirate.{TenancyValidation.EmirateValue(e)}")).ToList();
}
