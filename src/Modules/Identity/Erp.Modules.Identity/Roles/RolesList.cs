using Erp.Kernel.Lists;
using Erp.Modules.Identity.Contracts;

namespace Erp.Modules.Identity.Roles;

/// <summary>
/// The roles list. A workspace holds a handful of roles, so the endpoint loads them whole with
/// their user counts and the binding serves the list contract in memory over the rows it returns.
/// </summary>
internal static class RolesList
{
    public const string Key = "identity.roles";

    public static ListBinding<RoleDto> Create() =>
        ListBinding<RoleDto>.For(new ListDefinition(
                Key, "identity.roles.title", IdentityPermissions.RolesRead, "/api/identity/roles",
                [
                    new ListColumn("nameEn", "identity.roles.name", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "nameAr"),
                    new ListColumn("nameAr", "identity.roles.nameAr", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("isSystem", "identity.roles.kind", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        Choices: [new ListChoice("true", "identity.roles.system"), new ListChoice("false", "identity.roles.custom")]),
                    new ListColumn("userCount", "identity.roles.users", ListColumnType.Number, Sortable: true, Filterable: true, Aggregate: true),
                    new ListColumn("permissions", "identity.roles.permissions", ListColumnType.Choice),
                ],
                SearchFields: ["nameEn", "nameAr"],
                DefaultSort: "-isSystem,nameEn"),
                r => r.Id)
            .Column("nameEn", r => r.NameEn)
            .Column("nameAr", r => r.NameAr)
            .Column("isSystem", r => r.IsSystem)
            .Column("userCount", r => r.UserCount)
            .InMemory("A workspace holds tens of roles: the endpoint loads them whole with their user counts (one grouped query) and the list is searched, filtered and sorted in memory.");
}
