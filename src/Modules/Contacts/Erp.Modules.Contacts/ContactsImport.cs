using System.Globalization;
using System.Text.Json;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Money;
using Erp.Modules.Contacts.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlTypes;

namespace Erp.Modules.Contacts;

/// <summary>
/// Contacts as a kind of record the import module can fill from a CSV or XLSX file (p14) and the full tenant export writes.
/// Every row is judged by the same rules as the contact form and API (<see cref="ContactEndpoints.Validate"/> and
/// <see cref="ContactEndpoints.CheckAsync"/>), in English and in Arabic, so a file can never add a contact the screen would refuse.
/// A plain header such as Name, Street or Country maps by itself (<c>name</c> fills the English name, or the Arabic one when the text is Arabic);
/// a person names the organisation they work for by its name; countries and emirates may be written as names. Custom fields are columns too.
/// </summary>
internal static class ContactsImport
{
    public const string Table = "contacts.contacts";

    private static ImportField F(string key, string? example = null, params string[] aliases) =>
        new(key, "import.field.contacts." + key, Required: false, Example: example, Aliases: aliases);

    public static readonly IReadOnlyList<ImportField> Fields =
    [
        F("name", "Al Noor Trading LLC", "company", "companyname", "contact", "contactname", "customer", "supplier", "partner", "fullname", "الاسم"),
        F("nameEn", null, "englishname", "nameinenglish"),
        F("nameAr", null, "arabicname", "nameinarabic", "الاسمبالعربية"),
        F("kind", "organisation", "type", "contacttype", "companytype", "النوع"),
        F("code", "C100234", "reference", "ref", "internalreference", "contactcode", "id", "number", "المرجع"),
        F("organisation", "Al Noor Trading LLC", "parent", "parentcompany", "worksfor", "relatedcompany", "المنشأة"),
        F("jobTitle", "Purchasing Manager", "title", "position", "job", "jobposition", "المسمىالوظيفي"),
        F("email", "accounts@example.ae", "emailaddress", "mail", "البريدالإلكتروني"),
        F("phone", "+971 4 123 4567", "telephone", "tel", "landline", "phonenumber", "الهاتف"),
        F("mobile", "+971 50 123 4567", "mobilephone", "cell", "cellphone", "mobilenumber", "الجوال"),
        F("website", "www.example.ae", "web", "url", "site", "الموقع"),
        F("addressLine1", "Office 12, Business Bay Tower", "street", "street1", "address", "address1", "addressline", "العنوان"),
        F("addressLine2", null, "street2", "address2"),
        F("area", "Business Bay", "district", "zone", "المنطقة"),
        F("city", "Dubai", "town", "المدينة"),
        F("emirate", "Dubai", "state", "province", "region", "الإمارة"),
        F("poBox", "12345", "pobox", "postbox", "postalcode", "zip", "zipcode", "postcode", "صندوقالبريد"),
        F("country", "AE", "countrycode", "nation", "الدولة"),
        F("addressAr", null, "arabicaddress", "addressinarabic"),
        F("tags", "Customer; VIP", "tag", "labels", "categories", "category", "الوسوم"),
        F("tradeLicenceNumber", null, "tradelicense", "tradelicensenumber", "licence", "license", "licencenumber", "tl"),
        F("taxRegistrationNumber", "100234567891234", "trn", "taxnumber", "taxid", "vatnumber", "vat", "vatid", "taxregistration", "الرقمالضريبي"),
        F("defaultCurrency", "AED", "currency", "currencycode", "العملة"),
        F("creditLimitAmount", "50000.00", "creditlimit", "credit", "حدالائتمان"),
        F("creditLimitCurrency", null, "creditcurrency"),
        F("creditLimitExchangeRate", null, "creditrate", "creditexchangerate"),
        F("notes", null, "note", "comment", "comments", "remarks", "description", "internalnotes", "ملاحظات"),
    ];

    public static ImportRecordType Type { get; } = new(Table, "contacts.menu.contacts.server", ContactsPermissions.ContactsCreate, Fields, ProcessAsync,
        CustomFieldRecord: ContactsModule.RecordType, ListPath: "/api/contacts/contacts", OneOfRequired: ["name", "nameEn", "nameAr"]);

    private sealed record Built(SaveContactRequest Request, Guid? OrganisationName, string? OrganisationText);

    public static async Task<IReadOnlyList<ImportIssue>> ProcessAsync(IServiceProvider services, ImportBatch batch, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ContactsDbContext>();
        var companies = services.GetRequiredService<ICompanyDirectory>();
        var customFields = services.GetService<ICustomFields>();
        var definitions = customFields is null ? [] : await customFields.FieldsAsync(ContactsModule.RecordType, cancellationToken);
        var issues = new List<ImportIssue>();
        var state = batch.State;
        var codes = Kept(state, "codes", () => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        // Organisations of this file by their name key: the id they will have (null while only checking, they are not written).
        var created = Kept(state, "created", () => new Dictionary<string, Guid>(StringComparer.Ordinal));
        var found = Kept(state, "found", () => new Dictionary<string, Guid?>(StringComparer.Ordinal));
        var baseCurrency = state.TryGetValue("currency", out var kept) && kept is string currency ? currency : (string)(state["currency"] = (await ContactEndpoints.BaseCurrencyAsync(companies, cancellationToken)).Code);
        var pending = new List<(Contact Contact, IReadOnlyDictionary<string, JsonElement> Custom)>();

        async Task FlushAsync()
        {
            if (batch.DryRun || pending.Count == 0)
            {
                return;
            }
            // One COPY and one INSERT for the batch (the contacts form's own validation has judged every row): the audit trigger and
            // row-level security still see every row, as for any write.
            var session = services.GetRequiredService<ErpDbSession>();
            var tenant = services.GetRequiredService<ITenantContext>();
            var now = DateTimeOffset.UtcNow;
            await BulkInsert.InsertAsync(session, ContactsDbContext.SchemaName, "contacts", BulkColumns, pending.Select(p => BulkRow(p.Contact, tenant, now)), cancellationToken);
            if (customFields is not null && definitions.Count > 0)
            {
                // After the contacts themselves, so the values' history follows their creation: all of the batch's values in one go.
                // The values were checked with their rows; a refusal now means the fields changed under the import, which stops it.
                var refused = await customFields.SaveNewAsync(ContactsModule.RecordType, [.. pending.Select((p, i) => (i, p.Contact.Id, p.Custom))], cancellationToken);
                if (refused.Count > 0)
                {
                    throw new InvalidOperationException("A custom field value that passed the check was refused on saving: " + string.Join("; ", refused.Values.First().Select(e => e.Key + " " + e.Value)));
                }
            }
            pending.Clear();
        }

        foreach (var row in batch.Rows)
        {
            var values = row.Values;
            var organisationText = Clean(values, "organisation");
            Guid? organisationId = null;
            var wantsOrganisation = organisationText is not null && KindOf(values) == ContactKinds.Person;
            string? organisationProblem = null;
            Guid? fileOrganisation = null;
            if (wantsOrganisation)
            {
                var key = ContactsValidation.NameKey(organisationText);
                if (created.TryGetValue(key, out var createdId))
                {
                    // An organisation of this file: it is written with the people who point at it, in one statement.
                    fileOrganisation = createdId;
                }
                else
                {
                    if (!found.TryGetValue(key, out var existing))
                    {
                        existing = await db.Contacts.AsNoTracking().Where(c => c.Kind == ContactKinds.Organisation && c.IsActive && (c.NameKey == key || c.NameArKey == key))
                            .OrderBy(c => c.CreatedAt).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
                        found[key] = existing;
                    }
                    organisationId = existing;
                    organisationProblem = existing is null ? "contactsOrganisationUnknown" : null;
                }
            }
            var code = ContactsValidation.Clean(Clean(values, "code"));
            var codeProblem = code is not null && !codes.Add(code) ? "contactsCodeTaken" : null;
            var customProblems = new List<(string Language, Validator Validator)>();

            var (englishValidator, contact, custom) = await CheckAsync("en");
            var rowIssues = new List<(string Field, string Code, string En, string Ar)>();
            if (!englishValidator.IsValid)
            {
                var (arabicValidator, _, _) = await CheckAsync("ar");
                var arabic = arabicValidator.Errors;
                foreach (var (field, errors) in englishValidator.Errors)
                {
                    for (var i = 0; i < errors.Length; i++)
                    {
                        var ar = arabic.TryGetValue(field, out var list) && i < list.Length ? list[i].Message : errors[i].Message;
                        rowIssues.Add((FieldOf(field), errors[i].Code, errors[i].Message, ar));
                    }
                }
            }
            foreach (var (field, issue) in rowIssues.Select(i => (i.Field, i)))
            {
                issues.Add(new ImportIssue(row.Number, issue.Field, issue.Code, issue.En, issue.Ar));
            }
            if (rowIssues.Count > 0)
            {
                // A row that is refused does not keep its reference or its name for the rows after it.
                if (code is not null && codeProblem is null)
                {
                    codes.Remove(code);
                }
                continue;
            }
            if (fileOrganisation is { } fileParent && contact.Kind == ContactKinds.Person)
            {
                contact.OrganisationId = fileParent;
            }
            if (contact.Kind == ContactKinds.Organisation)
            {
                created[ContactsValidation.NameKey(string.IsNullOrWhiteSpace(contact.NameEn) ? contact.NameAr : contact.NameEn)] = contact.Id;
            }
            if (!batch.DryRun)
            {
                pending.Add((contact, custom));
            }

            async Task<(Validator Validator, Contact Contact, IReadOnlyDictionary<string, JsonElement> Custom)> CheckAsync(string language)
            {
                var http = Context(services, language);
                var request = Build(values, baseCurrency, organisationId);
                var validator = ContactEndpoints.Validate(request, http, requireVersion: false);
                var contact = new Contact();
                var custom = (IReadOnlyDictionary<string, JsonElement>)new Dictionary<string, JsonElement>();
                if (organisationProblem is not null)
                {
                    validator.Add("organisation", organisationProblem);
                }
                if (codeProblem is not null)
                {
                    validator.Add("code", codeProblem);
                }
                if (definitions.Count > 0)
                {
                    custom = ImportCustomValues.Convert(values, definitions, validator);
                    foreach (var field in definitions.Where(d => d.Required && d.Default is null && !custom.ContainsKey(d.Key)))
                    {
                        validator.Add("customFields." + field.Key, "required");
                    }
                }
                if (validator.IsValid)
                {
                    await ContactEndpoints.CheckAsync(db, companies, contact, request with { CustomFields = custom }, validator, cancellationToken);
                }
                return (validator, contact, custom);
            }
        }
        await FlushAsync();
        return issues;
    }

    private static readonly System.Collections.Immutable.ImmutableArray<BulkColumn> BulkColumns =
    [
        new("id", NpgsqlDbType.Uuid), new("tenant_id", NpgsqlDbType.Uuid), new("kind", NpgsqlDbType.Varchar), new("organisation_id", NpgsqlDbType.Uuid),
        new("code", NpgsqlDbType.Varchar), new("name_en", NpgsqlDbType.Varchar), new("name_ar", NpgsqlDbType.Varchar), new("job_title", NpgsqlDbType.Varchar),
        new("email", NpgsqlDbType.Varchar), new("phone", NpgsqlDbType.Varchar), new("mobile", NpgsqlDbType.Varchar), new("website", NpgsqlDbType.Varchar),
        new("address_line1", NpgsqlDbType.Varchar), new("address_line2", NpgsqlDbType.Varchar), new("area", NpgsqlDbType.Varchar), new("city", NpgsqlDbType.Varchar),
        new("emirate", NpgsqlDbType.Varchar), new("po_box", NpgsqlDbType.Varchar), new("country", NpgsqlDbType.Varchar), new("address_ar", NpgsqlDbType.Varchar),
        new("tags", NpgsqlDbType.Varchar), new("trade_licence_number", NpgsqlDbType.Varchar), new("tax_registration_number", NpgsqlDbType.Varchar),
        new("default_currency", NpgsqlDbType.Char), new("credit_limit_amount", NpgsqlDbType.Numeric), new("credit_limit_currency", NpgsqlDbType.Char),
        new("credit_limit_exchange_rate", NpgsqlDbType.Numeric), new("credit_limit_base_amount", NpgsqlDbType.Numeric), new("credit_limit_base_currency", NpgsqlDbType.Char),
        new("credit_limit_stated", NpgsqlDbType.Boolean), new("notes", NpgsqlDbType.Varchar), new("is_active", NpgsqlDbType.Boolean),
        new("name_key", NpgsqlDbType.Varchar), new("name_ar_key", NpgsqlDbType.Varchar), new("email_key", NpgsqlDbType.Varchar),
        new("phone_key", NpgsqlDbType.Varchar), new("mobile_key", NpgsqlDbType.Varchar),
        new("created_at", NpgsqlDbType.TimestampTz), new("created_by", NpgsqlDbType.Uuid), new("updated_at", NpgsqlDbType.TimestampTz), new("updated_by", NpgsqlDbType.Uuid),
    ];

    private static object?[] BulkRow(Contact c, ITenantContext tenant, DateTimeOffset now) =>
    [
        c.Id, tenant.TenantId, c.Kind, c.OrganisationId,
        c.Code, c.NameEn, c.NameAr, c.JobTitle,
        c.Email, c.Phone, c.Mobile, c.Website,
        c.AddressLine1, c.AddressLine2, c.Area, c.City,
        c.Emirate, c.PoBox, c.Country, c.AddressAr,
        c.Tags, c.TradeLicenceNumber, c.TaxRegistrationNumber,
        c.DefaultCurrency, c.CreditLimitAmount, c.CreditLimitCurrency,
        c.CreditLimitExchangeRate, c.CreditLimitBaseAmount, c.CreditLimitBaseCurrency,
        c.CreditLimitStated, c.Notes, c.IsActive,
        c.NameKey, c.NameArKey, c.EmailKey,
        c.PhoneKey, c.MobileKey,
        now, tenant.ActorId, now, tenant.ActorId,
    ];

    private static T Kept<T>(IDictionary<string, object?> state, string key, Func<T> create) where T : class
    {
        if (state.TryGetValue(key, out var value) && value is T existing)
        {
            return existing;
        }
        var made = create();
        state[key] = made;
        return made;
    }

    private static string FieldOf(string validatorField) =>
        validatorField.StartsWith("customFields.", StringComparison.Ordinal) ? ImportCustomValues.Prefix + validatorField["customFields.".Length..] : validatorField;

    private static DefaultHttpContext Context(IServiceProvider services, string language)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Headers.AcceptLanguage = language;
        return http;
    }

    private static string? Clean(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string KindOf(IReadOnlyDictionary<string, string?> values) => Clean(values, "kind") is { } text
        ? Normal(text) switch
        {
            "person" or "individual" or "people" or "contactperson" or "فرد" or "شخص" => ContactKinds.Person,
            _ => ContactKinds.Organisation,
        }
        : ContactKinds.Organisation;

    private static string Normal(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool HasArabic(string text) => text.Any(c => c is >= '؀' and <= 'ۿ');

    /// <summary>The request the contact form would have sent for the row (the text of the cells, tidied).</summary>
    internal static SaveContactRequest Build(IReadOnlyDictionary<string, string?> values, string baseCurrency, Guid? organisationId)
    {
        string? nameEn = Clean(values, "nameEn");
        string? nameAr = Clean(values, "nameAr");
        if (Clean(values, "name") is { } name)
        {
            if (HasArabic(name))
            {
                nameAr ??= name;
            }
            else
            {
                nameEn ??= name;
            }
        }
        var kind = KindOf(values);
        var country = Clean(values, "country");
        var amount = Clean(values, "creditLimitAmount");
        return new SaveContactRequest(
            Kind: kind,
            Code: Clean(values, "code"),
            NameEn: nameEn,
            NameAr: nameAr ?? "",
            OrganisationId: kind == ContactKinds.Person ? organisationId : null,
            JobTitle: Clean(values, "jobTitle"),
            Email: Clean(values, "email"),
            Phone: Phone(Clean(values, "phone")),
            Mobile: Phone(Clean(values, "mobile")),
            Website: Clean(values, "website"),
            AddressLine1: Clean(values, "addressLine1"),
            AddressLine2: Clean(values, "addressLine2"),
            Area: Clean(values, "area"),
            City: Clean(values, "city"),
            Emirate: Emirate(Clean(values, "emirate")),
            PoBox: Clean(values, "poBox"),
            Country: country is null ? ContactsValidation.UnitedArabEmirates : CountryCode(country),
            AddressAr: Clean(values, "addressAr"),
            Tags: Clean(values, "tags") is { } tags ? tags.Split([';', ',', '|', '،', '؛'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [],
            TradeLicenceNumber: Clean(values, "tradeLicenceNumber"),
            TaxRegistrationNumber: Clean(values, "taxRegistrationNumber") is { } trn ? ImportCustomValues.Digits(trn) : null,
            DefaultCurrency: Clean(values, "defaultCurrency")?.ToUpperInvariant() ?? baseCurrency,
            CreditLimitAmount: amount is null ? null : ImportCustomValues.Digits(amount).Replace(",", "").Replace(" ", ""),
            CreditLimitCurrency: Clean(values, "creditLimitCurrency")?.ToUpperInvariant(),
            CreditLimitExchangeRate: Clean(values, "creditLimitExchangeRate") is { } rate ? ImportCustomValues.Digits(rate) : null,
            Notes: Clean(values, "notes"),
            Version: null);
    }

    /// <summary>A phone number from a spreadsheet: Arabic-Indic digits as 0 to 9, and a number a spreadsheet stored as a bare number kept as it is.</summary>
    private static string? Phone(string? phone) => phone is null ? null : ImportCustomValues.Digits(phone);

    /// <summary>The emirate as the API stores it, from its English or Arabic name; an unknown text is kept so the check can refuse it.</summary>
    private static string? Emirate(string? text)
    {
        if (text is null)
        {
            return null;
        }
        return Normal(text) switch
        {
            "abudhabi" or "abudhabiemirate" or "أبوظبي" or "ابوظبي" => "abuDhabi",
            "dubai" or "دبي" => "dubai",
            "sharjah" or "الشارقة" => "sharjah",
            "ajman" or "عجمان" => "ajman",
            "ummalquwain" or "ummalqaiwain" or "uaq" or "أمالقيوين" or "امالقيوين" => "ummAlQuwain",
            "rasalkhaimah" or "rasalkhaimah" or "ras alkhaimah" or "rak" or "رأسالخيمة" or "راسالخيمة" => "rasAlKhaimah",
            "fujairah" or "alfujairah" or "الفجيرة" => "fujairah",
            _ => text,
        };
    }

    private static readonly System.Collections.Frozen.FrozenDictionary<string, string> CountryNames = BuildCountryNames();

    private static System.Collections.Frozen.FrozenDictionary<string, string> BuildCountryNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName;
                if (code.Length == 2 && code.All(char.IsAsciiLetterUpper))
                {
                    foreach (var name in new[] { region.EnglishName, region.NativeName, region.DisplayName, region.ThreeLetterISORegionName })
                    {
                        names.TryAdd(Normal(name), code);
                    }
                }
            }
            catch (ArgumentException)
            {
            }
        }
        foreach (var (name, code) in new[] { ("uae", "AE"), ("emirates", "AE"), ("unitedarabemirates", "AE"), ("الإماراتالعربيةالمتحدة", "AE"), ("الامارات", "AE"), ("ksa", "SA"), ("saudi", "SA"),
            ("uk", "GB"), ("unitedkingdom", "GB"), ("greatbritain", "GB"), ("england", "GB"), ("usa", "US"), ("unitedstates", "US"), ("america", "US"), ("korea", "KR"), ("southkorea", "KR"),
            ("russia", "RU"), ("vietnam", "VN"), ("turkey", "TR"), ("türkiye", "TR"), ("iran", "IR"), ("syria", "SY"), ("egypt", "EG"), ("india", "IN"), ("pakistan", "PK") })
        {
            names[name] = code;
        }
        return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(names, StringComparer.Ordinal);
    }

    /// <summary>The two-letter code of a country written as a code or as a name; an unknown text is kept so the check can refuse it.</summary>
    private static string CountryCode(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 2)
        {
            return trimmed.ToUpperInvariant();
        }
        return CountryNames.TryGetValue(Normal(trimmed), out var code) ? code : trimmed;
    }

    // ------------------------------------------------------------------ the full tenant export

    public static readonly IReadOnlyList<string> ExportColumns =
    [
        "id", "kind", "organisationId", "code", "nameEn", "nameAr", "jobTitle", "email", "phone", "mobile", "website",
        "addressLine1", "addressLine2", "area", "city", "emirate", "poBox", "country", "addressAr", "tags", "tradeLicenceNumber", "taxRegistrationNumber",
        "defaultCurrency", "creditLimitAmount", "creditLimitCurrency", "creditLimitExchangeRate", "creditLimitBaseAmount", "creditLimitBaseCurrency", "notes", "isActive",
        "createdAt", "createdBy", "updatedAt", "updatedBy",
    ];

    public static ExportRecordType ExportType { get; } = new(Table, "contacts.menu.contacts.server", ExportColumns, ExportRowsAsync, CustomFieldRecord: ContactsModule.RecordType);

    private static async IAsyncEnumerable<ExportRecord> ExportRowsAsync(IServiceProvider services, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ContactsDbContext>();
        Guid last = Guid.Empty;
        while (true)
        {
            var page = await db.Contacts.AsNoTracking().Where(c => c.Id.CompareTo(last) > 0).OrderBy(c => c.Id).Take(1000).ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                yield break;
            }
            foreach (var c in page)
            {
                yield return new ExportRecord(c.Id,
                [
                    c.Id, c.Kind, c.OrganisationId, c.Code, c.NameEn, c.NameAr, c.JobTitle, c.Email, c.Phone, c.Mobile, c.Website,
                    c.AddressLine1, c.AddressLine2, c.Area, c.City, c.Emirate, c.PoBox, c.Country, c.AddressAr, c.Tags, c.TradeLicenceNumber, c.TaxRegistrationNumber,
                    c.DefaultCurrency, c.CreditLimitAmount, c.CreditLimitCurrency, c.CreditLimitExchangeRate, c.CreditLimitBaseAmount, c.CreditLimitBaseCurrency, c.Notes, c.IsActive,
                    c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy,
                ]);
            }
            last = page[^1].Id;
        }
    }
}
