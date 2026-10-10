using Erp.Kernel.Lists;
using Erp.Modules.Identity.Contracts;

namespace Erp.Modules.Identity.Users;

/// <summary>
/// The users list: its columns, search fields, built-in views and the query binding that serves
/// them from <c>identity.users</c>. Search runs on trigram indexes over the display name and the
/// normalised e-mail; a word written in Arabic letters searches the display name and the Arabic name
/// (its own trigram index) instead; every sortable column has a (tenant_id, column, id) index.
/// </summary>
internal static class UsersList
{
    public const string Key = IdentityLists.Users;

    public static ListBinding<User> Create() =>
        ListBinding<User>.For(new ListDefinition(
                Key, "identity.users.title", IdentityPermissions.UsersRead, "/api/identity/users",
                [
                    new ListColumn("displayName", "identity.users.name", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "displayNameAr"),
                    // Shown in place of the name on Arabic screens when given; filtered ("contains") on its own
                    // trigram index, and searched by words written in Arabic letters (with the name, not
                    // the address): still two fields per word, so the search over 100,000 users costs the same.
                    new ListColumn("displayNameAr", "identity.users.nameAr", ListColumnType.Text, Filterable: true, Hidden: true),
                    new ListColumn("email", "identity.users.email", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("language", "identity.users.language", ListColumnType.Choice, Filterable: true, Groupable: true,
                        Choices: [new ListChoice("en", "identity.language.en"), new ListChoice("ar", "identity.language.ar")]),
                    new ListColumn("isActive", "identity.users.status", ListColumnType.Boolean, Filterable: true, Groupable: true,
                        TrueLabelKey: "identity.users.active", FalseLabelKey: "identity.users.inactive"),
                    new ListColumn("lastSignInAt", "identity.users.lastSignIn", ListColumnType.DateTime, Sortable: true, Filterable: true),
                    // Shown from each row's role ids (names come from the roles list); not sorted or filtered.
                    // Printed as the screen shows them: roles in every company, then each role held in
                    // one company with that company's code, then a note when the user holds roles in
                    // companies the reader does not work in (critic p04 round 7: the PDF printed only
                    // the roles held everywhere).
                    new ListColumn("roleIds", "identity.users.roles", ListColumnType.Choice, ValuesFrom: Roles.RolesList.Key,
                        InCompany: new ListCompanyValues("companyRoles", "roleId", "companyId", "rolesElsewhere", "identity.users.rolesElsewhereShort")),
                    new ListColumn("createdAt", "identity.users.created", ListColumnType.DateTime, Sortable: true, Filterable: true, Hidden: true),
                ],
                SearchFields: ["displayName", "email"],
                ArabicSearchFields: ["displayName", "displayNameAr"],
                DefaultSort: "-createdAt",
                Presets:
                [
                    new ListPreset("active", "identity.users.view.active", Filter: "isActive eq true"),
                    new ListPreset("inactive", "identity.users.view.inactive", Filter: "isActive eq false"),
                    new ListPreset("neverSignedIn", "identity.users.view.neverSignedIn", Filter: "lastSignInAt is null"),
                    new ListPreset("byLanguage", "identity.users.view.byLanguage", GroupBy: "language"),
                ]),
                u => u.Id)
            .Column("displayName", u => u.DisplayName)
            .Column("displayNameAr", u => u.DisplayNameAr)
            // Sorted, filtered and searched by the normalised (lower-case, trimmed) address, which
            // carries the unique and trigram indexes.
            .Column("email", u => u.EmailNormalized)
            .Column("language", u => u.Language)
            .Column("isActive", u => u.IsActive)
            .Column("lastSignInAt", u => u.LastSignInAt)
            .Column("createdAt", u => u.CreatedAt)
            // "map" finds Majid Anil Pillai (critic p05 round 7: the shortest name-only path).
            .Initials(u => u.DisplayName);
}
