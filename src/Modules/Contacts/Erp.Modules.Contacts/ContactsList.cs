using Erp.Kernel.Lists;
using Erp.Modules.Contacts.Contracts;

namespace Erp.Modules.Contacts;

/// <summary>
/// The contacts list, the platform's main 100,000-record list: its columns, search fields, built-in
/// views and the query binding that serves them from <c>contacts.contacts</c> (row-level security
/// limits it to the caller's workspace). A search word matches the English name, the e-mail, the
/// reference, the phone and mobile numbers and the tax registration number; a word written in
/// Arabic letters matches the English and Arabic names. All of them sit in one trigram index, and a
/// short word also matches a name's initials ("swa" finds Shamma Waleed Al Romaithi). Every sortable
/// column has a (tenant_id, column, id) index for keyset paging.
/// </summary>
internal static class ContactsList
{
    public const string Key = ContactsLists.Contacts;

    public static readonly IReadOnlyList<ListChoice> Kinds =
    [
        new ListChoice(ContactKinds.Organisation, "contacts.kind.organisation"),
        new ListChoice(ContactKinds.Person, "contacts.kind.person"),
    ];

    public static readonly IReadOnlyList<ListChoice> Emirates =
        ContactsValidation.Emirates.Select(e => new ListChoice(e, $"contacts.emirate.{e}")).ToList();

    public static ListBinding<Contact> Create() =>
        ListBinding<Contact>.For(new ListDefinition(
                Key, "contacts.contacts.title", ContactsPermissions.ContactsRead, "/api/contacts/contacts",
                [
                    new ListColumn("nameEn", "contacts.contact.name", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "nameAr", Width: ListColumnWidth.Wide),
                    new ListColumn("nameAr", "contacts.contact.nameAr", ListColumnType.Text, Hidden: true, Width: ListColumnWidth.Wide),
                    new ListColumn("kind", "contacts.contact.kind", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: Kinds),
                    new ListColumn("organisationId", "contacts.contact.organisation", ListColumnType.Reference, Filterable: true, LabelField: "organisationName"),
                    new ListColumn("email", "contacts.contact.email", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("mobile", "contacts.contact.mobile", ListColumnType.Text),
                    new ListColumn("phone", "contacts.contact.phone", ListColumnType.Text),
                    new ListColumn("city", "contacts.contact.city", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true),
                    new ListColumn("emirate", "contacts.contact.emirate", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: Emirates),
                    new ListColumn("country", "contacts.contact.country", ListColumnType.Text, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("tags", "contacts.contact.tags", ListColumnType.Text, Filterable: true),
                    new ListColumn("code", "contacts.contact.code", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("taxRegistrationNumber", "contacts.contact.taxRegistrationNumber", ListColumnType.Text, Hidden: true),
                    new ListColumn("defaultCurrency", "contacts.contact.defaultCurrency", ListColumnType.Text, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("creditLimit", "contacts.contact.creditLimit", ListColumnType.Money, Filterable: true, Hidden: true, CurrencyField: "creditLimitCurrency"),
                    new ListColumn("creditLimitCurrency", "contacts.contact.creditLimitCurrency", ListColumnType.Text, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("isActive", "contacts.contact.status", ListColumnType.Boolean, Filterable: true, Groupable: true,
                        TrueLabelKey: "contacts.status.active", FalseLabelKey: "contacts.status.archived"),
                    new ListColumn("createdAt", "contacts.contact.createdAt", ListColumnType.DateTime, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("updatedAt", "contacts.contact.updatedAt", ListColumnType.DateTime, Filterable: true, Hidden: true),
                ],
                SearchFields: ["nameEn", "email", "code", "mobile", "phone", "taxRegistrationNumber"],
                ArabicSearchFields: ["nameEn", "nameAr"],
                DefaultSort: "nameEn",
                Presets:
                [
                    new ListPreset("active", "contacts.view.active", Filter: "isActive eq true"),
                    new ListPreset("organisations", "contacts.view.organisations", Filter: "kind eq 'organisation' and isActive eq true"),
                    new ListPreset("people", "contacts.view.people", Filter: "kind eq 'person' and isActive eq true"),
                    new ListPreset("archived", "contacts.view.archived", Filter: "isActive eq false"),
                    new ListPreset("byEmirate", "contacts.view.byEmirate", Filter: "isActive eq true", GroupBy: "emirate"),
                    new ListPreset("recent", "contacts.view.recent", Sort: "-createdAt"),
                ]),
                c => c.Id)
            .Column("nameEn", c => c.NameEn)
            .Column("nameAr", c => c.NameAr)
            .Column("kind", c => c.Kind)
            .Column("organisationId", c => c.OrganisationId)
            .Column("email", c => c.Email)
            .Column("mobile", c => c.Mobile)
            .Column("phone", c => c.Phone)
            .Column("city", c => c.City)
            .Column("emirate", c => c.Emirate)
            .Column("country", c => c.Country)
            .Column("tags", c => c.Tags)
            .Column("code", c => c.Code)
            .Column("taxRegistrationNumber", c => c.TaxRegistrationNumber)
            .Column("defaultCurrency", c => c.DefaultCurrency)
            .Column("creditLimit", c => c.CreditLimitAmount)
            .Column("creditLimitCurrency", c => c.CreditLimitCurrency)
            .Column("isActive", c => c.IsActive)
            .Column("createdAt", c => c.CreatedAt)
            .Column("updatedAt", c => c.UpdatedAt)
            // "swa" finds Shamma Waleed Al Romaithi among 100,000 (the shortest name-only path).
            .Initials(c => c.NameEn);
}
