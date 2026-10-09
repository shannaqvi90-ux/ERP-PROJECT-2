using Erp.Kernel.Lists;
using Erp.Modules.Tenancy.Companies;
using Erp.Modules.Tenancy.Contracts;

namespace Erp.Modules.Tenancy.Branches;

/// <summary>
/// The branches list over <c>tenancy.branches</c> (row-level security limits it to branches of the
/// companies the caller may work in): filter or group by company, emirate, city or status; search
/// on trigram indexes over the code and both names; every sortable column has a (tenant_id,
/// column, id) index.
/// </summary>
internal static class BranchesList
{
    public const string Key = "tenancy.branches";

    public static ListBinding<Branch> Create() =>
        ListBinding<Branch>.For(new ListDefinition(
                Key, "tenancy.branches.title", TenancyPermissions.BranchesRead, "/api/tenancy/branches",
                [
                    new ListColumn("code", "tenancy.branch.code", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("nameEn", "tenancy.branch.nameEn", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("nameAr", "tenancy.branch.nameAr", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("companyId", "tenancy.branch.company", ListColumnType.Reference, Filterable: true, Groupable: true, LabelField: "companyCode"),
                    new ListColumn("city", "tenancy.company.city", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true),
                    new ListColumn("emirate", "tenancy.company.emirate", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: TenancyLists.Emirates),
                    new ListColumn("isActive", "tenancy.common.status", ListColumnType.Boolean, Filterable: true, Groupable: true,
                        TrueLabelKey: "tenancy.common.active", FalseLabelKey: "tenancy.common.inactive"),
                ],
                SearchFields: ["code", "nameEn", "nameAr"],
                DefaultSort: "code",
                Presets:
                [
                    new ListPreset("active", "tenancy.view.active", Filter: "isActive eq true"),
                    new ListPreset("inactive", "tenancy.view.inactive", Filter: "isActive eq false"),
                    new ListPreset("byCompany", "tenancy.view.byCompany", GroupBy: "companyId"),
                    new ListPreset("byEmirate", "tenancy.view.byEmirate", GroupBy: "emirate"),
                ]),
                b => b.Id)
            .Column("code", b => b.Code)
            .Column("nameEn", b => b.NameEn)
            .Column("nameAr", b => b.NameAr)
            .Column("companyId", b => b.CompanyId)
            .Column("city", b => b.City)
            .Column("emirate", b => b.Emirate)
            .Column("isActive", b => b.IsActive);
}
