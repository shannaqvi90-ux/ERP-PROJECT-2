using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
using Erp.Modules.Contacts.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Contacts.Reports;

/// <summary>
/// A contact's card as a printed document, in English or Arabic: its names, organisation, how to
/// reach it, its address, registration numbers and commercial terms; for an organisation, its
/// people below. A contact of the caller's workspace only (row-level security); any other id
/// answers 404.
/// </summary>
internal sealed class ContactCardReport(ContactsDbContext db) : IReportSource
{
    public const string Key = "contacts.contactCard";

    public static readonly ReportDefinition Definition = new(
        Key, "contacts.report.contactCard", ContactsPermissions.ContactsRead,
        [new ReportParameter("contact", "contacts.report.contact", ReportParameterType.Reference, Required: true, Lookup: ContactsList.Key)],
        [
            new ReportColumn("name", "contacts.report.personName", ListColumnType.Text),
            new ReportColumn("jobTitle", "contacts.contact.jobTitle", ListColumnType.Text),
            new ReportColumn("email", "contacts.contact.email", ListColumnType.Text),
            new ReportColumn("mobile", "contacts.contact.mobile", ListColumnType.Text),
            new ReportColumn("phone", "contacts.contact.phone", ListColumnType.Text),
            new ReportColumn("active", "contacts.contact.status", ListColumnType.Boolean, Groupable: true),
        ],
        DescriptionKey: "contacts.report.contactCardHint",
        Facts:
        [
            new ReportColumn("code", "contacts.contact.code", ListColumnType.Text),
            new ReportColumn("kind", "contacts.contact.kind", ListColumnType.Choice, Choices: ContactsList.Kinds),
            new ReportColumn("nameEn", "contacts.contact.nameEn", ListColumnType.Text),
            new ReportColumn("nameAr", "contacts.contact.nameAr", ListColumnType.Text),
            new ReportColumn("organisation", "contacts.contact.organisation", ListColumnType.Text),
            new ReportColumn("jobTitle", "contacts.contact.jobTitle", ListColumnType.Text),
            new ReportColumn("email", "contacts.contact.email", ListColumnType.Text),
            new ReportColumn("phone", "contacts.contact.phone", ListColumnType.Text),
            new ReportColumn("mobile", "contacts.contact.mobile", ListColumnType.Text),
            new ReportColumn("website", "contacts.contact.website", ListColumnType.Text),
            new ReportColumn("address", "contacts.contact.address", ListColumnType.Text),
            new ReportColumn("emirate", "contacts.contact.emirate", ListColumnType.Choice, Choices: ContactsList.Emirates),
            new ReportColumn("poBox", "contacts.contact.poBox", ListColumnType.Text),
            new ReportColumn("country", "contacts.contact.country", ListColumnType.Text),
            new ReportColumn("tags", "contacts.contact.tags", ListColumnType.Text),
            new ReportColumn("tradeLicenceNumber", "contacts.contact.tradeLicenceNumber", ListColumnType.Text),
            new ReportColumn("taxRegistrationNumber", "contacts.contact.taxRegistrationNumber", ListColumnType.Text),
            new ReportColumn("defaultCurrency", "contacts.contact.defaultCurrency", ListColumnType.Text),
            new ReportColumn("creditLimit", "contacts.contact.creditLimit", ListColumnType.Money),
            new ReportColumn("active", "contacts.contact.status", ListColumnType.Boolean),
        ]);

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var id = run.Get<Guid>("contact");
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return null;
        }
        var organisation = contact.OrganisationId is { } o
            ? await db.Contacts.AsNoTracking().Where(c => c.Id == o).Select(c => new { c.NameEn, c.NameAr }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var people = contact.Kind != ContactKinds.Organisation || !run.PrintsRows ? [] :
            await db.Contacts.AsNoTracking().Where(c => c.OrganisationId == id).OrderBy(c => c.NameEn).ThenBy(c => c.Id).Take(run.MaxRows)
                .Select(c => new { c.NameEn, c.NameAr, c.JobTitle, c.Email, c.Mobile, c.Phone, c.IsActive })
                .ToListAsync(cancellationToken);
        var name = new LocalText(contact.NameEn, contact.NameAr);
        var heading = contact.Code is { } reference
            ? new LocalText($"{reference} · {name.For("en")}", $"{reference} · {name.For("ar")}")
            : name;
        var english = string.Join(", ", new[] { contact.AddressLine1, contact.AddressLine2, contact.Area, contact.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ReportData
        {
            Subject = heading,
            Facts = new Dictionary<string, object?>
            {
                ["code"] = contact.Code,
                ["kind"] = contact.Kind,
                ["nameEn"] = contact.NameEn,
                ["nameAr"] = contact.NameAr,
                ["organisation"] = organisation is null ? null : new LocalText(organisation.NameEn, organisation.NameAr),
                ["jobTitle"] = contact.JobTitle,
                ["email"] = contact.Email,
                ["phone"] = contact.Phone,
                ["mobile"] = contact.Mobile,
                ["website"] = contact.Website,
                ["address"] = english.Length == 0 && string.IsNullOrWhiteSpace(contact.AddressAr) ? null : new LocalText(english, contact.AddressAr),
                ["emirate"] = contact.Emirate,
                ["poBox"] = contact.PoBox,
                ["country"] = PlaceNames.Country(contact.Country),
                ["tags"] = contact.Tags,
                ["tradeLicenceNumber"] = contact.TradeLicenceNumber,
                ["taxRegistrationNumber"] = contact.TaxRegistrationNumber,
                ["defaultCurrency"] = contact.DefaultCurrency,
                ["creditLimit"] = contact.CreditLimit is { } limit ? new ReportMoney(limit.Amount, limit.Currency) : null,
                ["active"] = contact.IsActive,
            },
            Rows = people.Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["name"] = new LocalText(p.NameEn, p.NameAr),
                ["jobTitle"] = p.JobTitle,
                ["email"] = p.Email,
                ["mobile"] = p.Mobile,
                ["phone"] = p.Phone,
                ["active"] = p.IsActive,
            }).ToList(),
            ParameterTexts = new Dictionary<string, LocalText> { ["contact"] = heading },
        };
    }
}
