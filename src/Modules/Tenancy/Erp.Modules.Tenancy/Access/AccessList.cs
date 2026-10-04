using Erp.Kernel.Lists;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Contracts;

namespace Erp.Modules.Tenancy.Access;

/// <summary>
/// The access list: the workspace's users with the companies (of the caller's) each may work in.
/// Its rows are identity's users, so identity's users list serves its query (search on name and
/// e-mail, filters, sort, keyset paging) through <see cref="IUserDirectory.QueryListAsync"/>; the
/// companies column is added from tenancy's own access rows for the page shown.
/// </summary>
internal static class AccessList
{
    public const string Key = "tenancy.access";

    public static readonly ListDefinition Definition = new(
        Key, "tenancy.access.title", TenancyPermissions.AccessRead, "/api/tenancy/access",
        [
            new ListColumn("displayName", "tenancy.access.user", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "tenancy.access.email", ListColumnType.Text, Sortable: true, Filterable: true),
            // Shown from tenancy's access rows for the page; not sorted or filtered.
            new ListColumn("companies", "tenancy.access.companies", ListColumnType.Reference),
        ],
        SearchFields: ["displayName", "email"],
        DefaultSort: "displayName");
}
