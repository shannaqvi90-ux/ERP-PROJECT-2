using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Money;
using Erp.Kernel.Security;
using Erp.Modules.Contacts.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Contacts;

/// <summary>A contact as its form shows it. <c>organisationName</c> and <c>organisationNameAr</c>
/// name a person's organisation; <c>peopleCount</c> is how many people belong to an organisation.
/// <c>creditLimit</c> is null when no limit is set: otherwise the amount in its currency, the
/// exchange rate used and the amount in the base currency of the company it was set in (decimals
/// as strings). <c>tags</c> in the order typed. <c>version</c> is sent back to save a change.</summary>
public sealed record ContactDto(
    Guid Id,
    string Kind,
    string? Code,
    string NameEn,
    string NameAr,
    Guid? OrganisationId,
    string? OrganisationName,
    string? OrganisationNameAr,
    string? JobTitle,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Website,
    string? AddressLine1,
    string? AddressLine2,
    string? Area,
    string? City,
    string? Emirate,
    string? PoBox,
    string Country,
    string? AddressAr,
    IReadOnlyList<string> Tags,
    string? TradeLicenceNumber,
    [property: ApiExample("100234567891234"), RegularExpression(ContactsValidation.StoredTaxNumberPattern)] string? TaxRegistrationNumber,
    string DefaultCurrency,
    Money? CreditLimit,
    string? Notes,
    bool IsActive,
    int PeopleCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>A row of the contacts list (the list query contract). <c>creditLimit</c> is the
/// limit's amount in <c>creditLimitCurrency</c> (a decimal string), null when none is set.</summary>
public sealed record ContactRow(
    Guid Id,
    string Kind,
    string? Code,
    string NameEn,
    string NameAr,
    Guid? OrganisationId,
    string? OrganisationName,
    string? OrganisationNameAr,
    string? JobTitle,
    string? Email,
    string? Mobile,
    string? Phone,
    string? City,
    string? Emirate,
    string Country,
    string? Tags,
    string? TaxRegistrationNumber,
    string DefaultCurrency,
    decimal? CreditLimit,
    string? CreditLimitCurrency,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>
/// Create or change a contact. <c>kind</c> is organisation or person; a person may name the
/// organisation they work for (<c>organisationId</c>, an active organisation of the workspace) and
/// their job title (both are a person's only: an organisation's are left empty). At
/// least one of the English and Arabic names. Phone numbers in international form (+971 50 123
/// 4567); a UAE number with its country code has the length its kind has. The tax registration number is
/// 15 digits (spaces and dashes are removed, Arabic-Indic digits become 0 to 9). The credit limit is
/// an amount (a decimal string, at least 0) in <c>creditLimitCurrency</c> (the default currency when
/// left out); in a currency other than the working company's base currency it needs
/// <c>creditLimitExchangeRate</c> (base-currency units per unit). Leave the amount out for no limit.
/// On change, <c>version</c> is the version that was read. Archiving is its own action.
/// </summary>
public sealed record SaveContactRequest(
    [property: ApiExample("organisation"), RegularExpression("^(organisation|person)$")] string? Kind,
    [property: ApiExample("C123456"), StringLength(30), RegularExpression(ContactsValidation.CodePattern)] string? Code,
    [property: ApiExample("Al Noor Trading LLC"), StringLength(ContactsValidation.NameMaxLength)] string? NameEn,
    [property: ApiExample("شركة النور للتجارة ذ.م.م"), StringLength(ContactsValidation.NameMaxLength)] string? NameAr,
    Guid? OrganisationId,
    [property: StringLength(100)] string? JobTitle,
    [property: ApiExample("accounts@example.ae"), StringLength(254)] string? Email,
    [property: ApiExample("+971 4 123 4567"), RegularExpression(ContactsValidation.PhoneShapePattern)] string? Phone,
    [property: ApiExample("+971 50 123 4567"), RegularExpression(ContactsValidation.PhoneShapePattern)] string? Mobile,
    [property: ApiExample("https://www.example.ae"), RegularExpression(ContactsValidation.WebsitePattern)] string? Website,
    [property: StringLength(200)] string? AddressLine1,
    [property: StringLength(200)] string? AddressLine2,
    [property: StringLength(100)] string? Area,
    [property: ApiExample("Dubai"), StringLength(100)] string? City,
    [property: ApiExample("dubai"), RegularExpression("^(abuDhabi|dubai|sharjah|ajman|ummAlQuwain|rasAlKhaimah|fujairah)$")] string? Emirate,
    [property: ApiExample("12345"), RegularExpression(ContactsValidation.PoBoxPattern)] string? PoBox,
    [property: ApiExample("AE"), RegularExpression("^[A-Za-z]{2}$")] string? Country,
    [property: StringLength(400)] string? AddressAr,
    IReadOnlyList<string>? Tags,
    [property: StringLength(50)] string? TradeLicenceNumber,
    [property: ApiExample("100234567891234"), StringLength(ContactsValidation.TaxNumberTypedMaxLength), RegularExpression(ContactsValidation.TaxNumberPattern)] string? TaxRegistrationNumber,
    [property: ApiExample("AED"), RegularExpression("^[A-Za-z]{3}$")] string? DefaultCurrency,
    [property: ApiExample("50000.00"), RegularExpression(ContactsValidation.AmountPattern)] string? CreditLimitAmount,
    [property: ApiExample("USD"), RegularExpression("^[A-Za-z]{3}$")] string? CreditLimitCurrency,
    [property: ApiExample("3.6725"), RegularExpression("^[0-9]{1,10}(\\.[0-9]{1,8})?$")] string? CreditLimitExchangeRate,
    [property: StringLength(ContactsValidation.NotesMaxLength)] string? Notes,
    uint? Version);

/// <summary>A contact that may be the same as the one being entered, and what it shares with it:
/// <c>name</c>, <c>email</c>, <c>phone</c> (either number) or <c>taxRegistrationNumber</c>.</summary>
public sealed record ContactDuplicate(
    Guid Id,
    string Kind,
    string? Code,
    string NameEn,
    string NameAr,
    string? Email,
    string? Phone,
    string? Mobile,
    string? TaxRegistrationNumber,
    bool IsActive,
    IReadOnlyList<string> Matches);

/// <summary>What a new contact starts with for the caller: the country, and the working
/// company's base currency as the default currency and as the currency a credit limit's base
/// amount is stated in.</summary>
public sealed record ContactDefaults(string Country, string DefaultCurrency, string BaseCurrency);

/// <summary>What a contact being entered is compared on. <c>except</c> leaves out the contact
/// being changed.</summary>
public sealed class DuplicateQuery
{
    [FromQuery(Name = "nameEn"), Description("The English name typed."), StringLength(ContactsValidation.NameMaxLength)]
    public string? NameEn { get; init; }

    [FromQuery(Name = "nameAr"), Description("The Arabic name typed."), StringLength(ContactsValidation.NameMaxLength)]
    public string? NameAr { get; init; }

    [FromQuery(Name = "email"), Description("The e-mail typed."), StringLength(254)]
    public string? Email { get; init; }

    [FromQuery(Name = "phone"), Description("The phone number typed."), StringLength(30)]
    public string? Phone { get; init; }

    [FromQuery(Name = "mobile"), Description("The mobile number typed."), StringLength(30)]
    public string? Mobile { get; init; }

    [FromQuery(Name = "taxRegistrationNumber"), Description("The tax registration number typed (15 digits; spaces and dashes are ignored)."), StringLength(ContactsValidation.TaxNumberTypedMaxLength)]
    public string? TaxRegistrationNumber { get; init; }

    [FromQuery(Name = "except"), Description("The id of the contact being changed, left out of the answer.")]
    public Guid? Except { get; init; }
}

internal static class ContactEndpoints
{
    /// <summary>Most possible duplicates one check answers.</summary>
    public const int MaxDuplicates = 10;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/contacts", List)
            .WithName("contacts.contacts.list")
            .WithSummary("Contacts of the workspace, a page at a time: word search on names, e-mail, reference, phone and mobile numbers and tax registration number (Arabic words on the English and Arabic names, short words also on a name's initials), filters, sort, keyset or offset paging and grouping (the list query contract); by name by default.")
            .RequirePermission(ContactsPermissions.ContactsRead);

        group.MapGet("/contacts/duplicates", Duplicates)
            .WithName("contacts.contacts.duplicates")
            .WithSummary("Contacts that may be the same as one being entered: the same name in English or Arabic (case, punctuation and the usual Arabic spelling variants ignored), the same e-mail, the same phone or mobile number (however it was written) or the same tax registration number. At most 10, active ones first. A warning only: saving is not refused.")
            .RequirePermission(ContactsPermissions.ContactsRead);

        group.MapGet("/contacts/defaults", Defaults)
            .WithName("contacts.contacts.defaults")
            .WithSummary("What a new contact starts with for the caller: the country (AE) and the working company's base currency, which is also the currency a credit limit's base amount is stated in.")
            .RequirePermission(ContactsPermissions.ContactsRead);

        group.MapGet("/contacts/{id:guid}", Get)
            .WithName("contacts.contacts.get")
            .WithSummary("One contact of the workspace.")
            .RequirePermission(ContactsPermissions.ContactsRead);

        group.MapPost("/contacts", Create)
            .WithName("contacts.contacts.create")
            .WithSummary("Create a contact (an organisation or a person). Possible duplicates are not refused: ask /api/contacts/contacts/duplicates first to warn the user.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(ContactsPermissions.ContactsCreate);

        group.MapPut("/contacts/{id:guid}", Update)
            .WithName("contacts.contacts.update")
            .WithSummary("Change a contact. An organisation that people belong to stays an organisation. Archiving and restoring are their own actions.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(ContactsPermissions.ContactsUpdate);

        group.MapPost("/contacts/{id:guid}/archive", Archive)
            .WithName("contacts.contacts.archive")
            .WithSummary("Archive a contact: it is kept (documents still name it, the list shows it under Archived) but no longer offered for new work, and no new person may join an archived organisation. Contacts are never deleted.")
            .RequirePermission(ContactsPermissions.ContactsArchive);

        group.MapPost("/contacts/{id:guid}/restore", Restore)
            .WithName("contacts.contacts.restore")
            .WithSummary("Restore an archived contact to use.")
            .RequirePermission(ContactsPermissions.ContactsArchive);
    }

    private static async Task<Results<Ok<ListPage<ContactRow>>, ProblemHttpResult>> List(
        ContactsDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the contacts list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<ContactRow>> PageAsync(ContactsDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await catalog.ListBinding<Contact>(ContactsList.Key).QueryAsync(ListRows(db.Contacts), request, http, cancellationToken);
        if (result.Problem is not null)
        {
            return result.Map(_ => (ContactRow)null!);
        }
        var organisationIds = result.Rows.Where(c => c.OrganisationId is not null).Select(c => c.OrganisationId!.Value).Distinct().ToList();
        var organisations = organisationIds.Count == 0
            ? []
            : await db.Contacts.AsNoTracking().Where(c => organisationIds.Contains(c.Id))
                .Select(c => new { c.Id, c.NameEn, c.NameAr }).ToDictionaryAsync(c => c.Id, cancellationToken);
        return result.Map(c =>
        {
            var organisation = c.OrganisationId is { } o && organisations.TryGetValue(o, out var found) ? found : null;
            return new ContactRow(c.Id, c.Kind, c.Code, c.NameEn, c.NameAr, c.OrganisationId, organisation?.NameEn, organisation?.NameAr, c.JobTitle,
                c.Email, c.Mobile, c.Phone, c.City, c.Emirate, c.Country, c.Tags, c.TaxRegistrationNumber, c.DefaultCurrency, c.CreditLimitAmount,
                c.CreditLimitCurrency, c.IsActive, c.CreatedAt, c.UpdatedAt, c.Version);
        });
    }

    /// <summary>The columns a list row needs (not the notes or the address lines): a list page
    /// reads only these, and filters, sort and paging compose over the projection.</summary>
    internal static IQueryable<Contact> ListRows(IQueryable<Contact> contacts) =>
        contacts.AsNoTracking().Select(c => new Contact
        {
            Id = c.Id,
            TenantId = c.TenantId,
            Kind = c.Kind,
            Code = c.Code,
            NameEn = c.NameEn,
            NameAr = c.NameAr,
            OrganisationId = c.OrganisationId,
            JobTitle = c.JobTitle,
            Email = c.Email,
            Mobile = c.Mobile,
            Phone = c.Phone,
            City = c.City,
            Emirate = c.Emirate,
            Country = c.Country,
            Tags = c.Tags,
            TaxRegistrationNumber = c.TaxRegistrationNumber,
            DefaultCurrency = c.DefaultCurrency,
            CreditLimitAmount = c.CreditLimitAmount,
            CreditLimitCurrency = c.CreditLimitCurrency,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Version = c.Version,
        });

    private static async Task<Results<Ok<ContactDto>, ProblemHttpResult>> Get(Guid id, ContactsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return contact is null ? Problems.NotFound(http) : TypedResults.Ok(await ToDtoAsync(db, contact, cancellationToken));
    }

    private static async Task<Ok<ContactDefaults>> Defaults(ICompanyDirectory companies, CancellationToken cancellationToken)
    {
        var baseCurrency = await BaseCurrencyAsync(companies, cancellationToken);
        return TypedResults.Ok(new ContactDefaults(ContactsValidation.UnitedArabEmirates, baseCurrency.Code, baseCurrency.Code));
    }

    /// <summary>The base currency a credit limit set now is stated in: the working company's (AED
    /// when the caller works in no company, as for system work).</summary>
    internal static async Task<CurrencyCode> BaseCurrencyAsync(ICompanyDirectory companies, CancellationToken cancellationToken)
    {
        var working = await companies.GetWorkingAsync(cancellationToken);
        return new CurrencyCode(working?.BaseCurrency is { } code && CurrencyCode.IsKnown(code) ? code : CurrencyCode.Aed.Code);
    }

    private static async Task<Ok<IReadOnlyList<ContactDuplicate>>> Duplicates([AsParameters] DuplicateQuery query, ContactsDbContext db, CancellationToken cancellationToken)
    {
        var found = await FindDuplicatesAsync(db, query.NameEn, query.NameAr, query.Email, query.Phone, query.Mobile, query.TaxRegistrationNumber, query.Except, cancellationToken);
        return TypedResults.Ok(found);
    }

    /// <summary>Contacts of the workspace that share a name, e-mail, number or tax registration
    /// number with what was typed (row-level security keeps the search in the caller's workspace).</summary>
    internal static async Task<IReadOnlyList<ContactDuplicate>> FindDuplicatesAsync(ContactsDbContext db, string? nameEn, string? nameAr, string? email,
        string? phone, string? mobile, string? taxNumber, Guid? except, CancellationToken cancellationToken)
    {
        var nameKey = ContactsValidation.NameKey(nameEn);
        var nameArKey = ContactsValidation.NameKey(nameAr);
        var emailKey = ContactsValidation.EmailKey(email);
        var phones = new[] { ContactsValidation.PhoneKey(phone), ContactsValidation.PhoneKey(mobile) }.Where(p => p is { Length: >= 7 }).Distinct().ToList();
        var trn = ContactsValidation.NormalizeTaxNumber(taxNumber) is { Length: 15 } digits && digits.All(char.IsAsciiDigit) ? digits : null;
        // A name of one or two letters says nothing about who it is.
        var byName = nameKey.Length >= 3;
        var byNameAr = nameArKey.Length >= 3;
        if (!byName && !byNameAr && emailKey is null && phones.Count == 0 && trn is null)
        {
            return [];
        }
        var candidates = await db.Contacts.AsNoTracking()
            .Where(c => except == null || c.Id != except)
            .Where(c => (byName && (c.NameKey == nameKey || c.NameArKey == nameKey)) || (byNameAr && (c.NameArKey == nameArKey || c.NameKey == nameArKey)) ||
                        (emailKey != null && c.EmailKey == emailKey) ||
                        (c.PhoneKey != null && phones.Contains(c.PhoneKey)) || (c.MobileKey != null && phones.Contains(c.MobileKey)) ||
                        (trn != null && c.TaxRegistrationNumber == trn))
            .OrderByDescending(c => c.IsActive).ThenBy(c => c.NameEn).ThenBy(c => c.Id)
            .Take(MaxDuplicates)
            .Select(c => new { c.Id, c.Kind, c.Code, c.NameEn, c.NameAr, c.Email, c.Phone, c.Mobile, c.TaxRegistrationNumber, c.IsActive, c.NameKey, c.NameArKey, c.EmailKey, c.PhoneKey, c.MobileKey })
            .ToListAsync(cancellationToken);
        return candidates.Select(c =>
        {
            var matches = new List<string>();
            if ((byName && (c.NameKey == nameKey || c.NameArKey == nameKey)) || (byNameAr && (c.NameArKey == nameArKey || c.NameKey == nameArKey)))
            {
                matches.Add("name");
            }
            if (emailKey is not null && c.EmailKey == emailKey)
            {
                matches.Add("email");
            }
            if ((c.PhoneKey is not null && phones.Contains(c.PhoneKey)) || (c.MobileKey is not null && phones.Contains(c.MobileKey)))
            {
                matches.Add("phone");
            }
            if (trn is not null && c.TaxRegistrationNumber == trn)
            {
                matches.Add("taxRegistrationNumber");
            }
            return new ContactDuplicate(c.Id, c.Kind, c.Code, c.NameEn, c.NameAr, c.Email, c.Phone, c.Mobile, c.TaxRegistrationNumber, c.IsActive, matches);
        }).ToList();
    }

    private static async Task<Results<Created<ContactDto>, ProblemHttpResult>> Create(
        SaveContactRequest request, ContactsDbContext db, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: false);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var contact = new Contact();
        var checks = await CheckAsync(db, companies, contact, request, validator, cancellationToken);
        if (checks is not null)
        {
            return checks;
        }
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/contacts/contacts/{contact.Id}", await ToDtoAsync(db, contact, cancellationToken));
    }

    private static async Task<Results<Ok<ContactDto>, ProblemHttpResult>> Update(
        Guid id, SaveContactRequest request, ContactsDbContext db, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: true);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Problems.NotFound(http);
        }
        db.Entry(contact).Property(c => c.Version).OriginalValue = request.Version!.Value;
        var checks = await CheckAsync(db, companies, contact, request, validator, cancellationToken);
        if (checks is not null)
        {
            return checks;
        }
        db.Entry(contact).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(db, contact, cancellationToken));
    }

    private static Task<Results<Ok<ContactDto>, ProblemHttpResult>> Archive(Guid id, ContactsDbContext db, HttpContext http, CancellationToken cancellationToken) =>
        SetActiveAsync(id, false, db, http, cancellationToken);

    private static Task<Results<Ok<ContactDto>, ProblemHttpResult>> Restore(Guid id, ContactsDbContext db, HttpContext http, CancellationToken cancellationToken) =>
        SetActiveAsync(id, true, db, http, cancellationToken);

    private static async Task<Results<Ok<ContactDto>, ProblemHttpResult>> SetActiveAsync(Guid id, bool active, ContactsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Problems.NotFound(http);
        }
        if (contact.IsActive != active)
        {
            contact.IsActive = active;
            await db.SaveChangesAsync(cancellationToken);
        }
        return TypedResults.Ok(await ToDtoAsync(db, contact, cancellationToken));
    }

    private static Validator Validate(SaveContactRequest request, HttpContext http, bool requireVersion)
    {
        var validator = new Validator(http)
            .Required("kind", request.Kind)
            .OneOf("kind", ContactsValidation.Clean(request.Kind), ContactKinds.All.ToList())
            .Code(request.Code)
            .Names(request.NameEn, request.NameAr)
            .MaxLength("jobTitle", request.JobTitle?.Trim(), 100)
            .Email("email", ContactsValidation.Clean(request.Email))
            .MaxLength("email", request.Email?.Trim(), 254)
            .Phone("phone", request.Phone)
            .Phone("mobile", request.Mobile)
            .Website(request.Website)
            .Address(request.AddressLine1, request.AddressLine2, request.Area, request.City, request.Emirate, request.PoBox, request.Country, request.AddressAr)
            .Tags(request.Tags)
            .MaxLength("tradeLicenceNumber", request.TradeLicenceNumber?.Trim(), 50)
            .TaxNumber(request.TaxRegistrationNumber)
            .Currency("defaultCurrency", request.DefaultCurrency, required: true)
            .Currency("creditLimitCurrency", request.CreditLimitCurrency, required: false)
            .MaxLength("notes", request.Notes?.Trim(), ContactsValidation.NotesMaxLength);
        if (ContactsValidation.Clean(request.CreditLimitAmount) is { } amount)
        {
            validator.Must(System.Text.RegularExpressions.Regex.IsMatch(amount, ContactsValidation.AmountPattern), "creditLimitAmount", "contactsAmount");
        }
        if (ContactsValidation.Clean(request.CreditLimitExchangeRate) is { } rate)
        {
            validator.Must(decimal.TryParse(rate, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0
                && decimal.Round(r, Money.RateScale) == r, "creditLimitExchangeRate", "contactsExchangeRate");
        }
        if (requireVersion)
        {
            validator.Required("version", request.Version);
        }
        return validator;
    }

    /// <summary>The checks that need the database, then the request applied to the contact: the
    /// organisation exists in the workspace, is an organisation and is active (or is the one the
    /// person already had); the reference is free; an organisation with people stays one; the credit
    /// limit is made into money against the working company's base currency.</summary>
    private static async Task<ProblemHttpResult?> CheckAsync(ContactsDbContext db, ICompanyDirectory companies, Contact contact, SaveContactRequest request,
        Validator validator, CancellationToken cancellationToken)
    {
        var kind = ContactsValidation.Clean(request.Kind)!;
        // The organisation and the job title are a person's: an organisation's are left empty.
        if (kind == ContactKinds.Person && request.OrganisationId is { } organisationId)
        {
            var organisation = await db.Contacts.AsNoTracking().Where(c => c.Id == organisationId).Select(c => new { c.Kind, c.IsActive }).SingleOrDefaultAsync(cancellationToken);
            if (organisation is null)
            {
                validator.Add("organisationId", "contactsOrganisationUnknown");
            }
            else if (organisation.Kind != ContactKinds.Organisation || organisationId == contact.Id)
            {
                validator.Add("organisationId", "contactsOrganisationNotOrganisation");
            }
            else if (!organisation.IsActive && contact.OrganisationId != organisationId)
            {
                validator.Add("organisationId", "contactsOrganisationArchived");
            }
        }
        var reference = ContactsValidation.Clean(request.Code);
        if (reference is not null && reference != contact.Code &&
            await db.Contacts.AnyAsync(c => c.Code == reference && c.Id != contact.Id, cancellationToken))
        {
            validator.Add("code", "contactsCodeTaken");
        }
        if (contact.Kind == ContactKinds.Organisation && kind == ContactKinds.Person && contact.CreatedAt != default &&
            await db.Contacts.AnyAsync(c => c.OrganisationId == contact.Id, cancellationToken))
        {
            validator.Add("kind", "contactsOrganisationHasPeople");
        }
        var defaultCurrency = ContactsValidation.Clean(request.DefaultCurrency)!.ToUpperInvariant();
        Money? creditLimit = null;
        if (ContactsValidation.Clean(request.CreditLimitAmount) is { } typedAmount)
        {
            var amount = decimal.Parse(typedAmount, System.Globalization.CultureInfo.InvariantCulture);
            var currency = new CurrencyCode(ContactsValidation.Clean(request.CreditLimitCurrency)?.ToUpperInvariant() ?? defaultCurrency);
            decimal? rate = ContactsValidation.Clean(request.CreditLimitExchangeRate) is { } typedRate
                ? decimal.Parse(typedRate, System.Globalization.CultureInfo.InvariantCulture)
                : null;
            if (Money.RoundToMinorUnits(amount, currency) != amount)
            {
                validator.Add("creditLimitAmount", "contactsAmountDecimals", currency.MinorUnits, currency.Code);
            }
            else if (contact.CreditLimit is { } kept && kept.Amount == amount && kept.Currency == currency.Code && (rate is null || rate == kept.ExchangeRate))
            {
                // Unchanged: the limit keeps the rate and base currency it was set with.
                creditLimit = kept;
            }
            else
            {
                var baseCurrency = await BaseCurrencyAsync(companies, cancellationToken);
                if (currency == baseCurrency)
                {
                    if (rate is not null && rate != 1m)
                    {
                        validator.Add("creditLimitExchangeRate", "contactsRateInBase", baseCurrency.Code);
                    }
                    else
                    {
                        creditLimit = Money.Create(amount, currency, 1m, baseCurrency);
                    }
                }
                else if (rate is null)
                {
                    validator.Add("creditLimitExchangeRate", "contactsRateRequired", currency.Code, baseCurrency.Code);
                }
                else
                {
                    try
                    {
                        creditLimit = Money.Create(amount, currency, rate.Value, baseCurrency);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        validator.Add("creditLimitAmount", "contactsAmountTooLarge");
                    }
                }
            }
        }
        else if (ContactsValidation.Clean(request.CreditLimitExchangeRate) is not null)
        {
            validator.Add("creditLimitAmount", "contactsRateWithoutAmount");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        Apply(contact, request, kind, reference, defaultCurrency, creditLimit);
        return null;
    }

    private static void Apply(Contact contact, SaveContactRequest r, string kind, string? reference, string defaultCurrency, Money? creditLimit)
    {
        contact.Kind = kind;
        contact.Code = reference;
        contact.NameEn = r.NameEn?.Trim() ?? "";
        contact.NameAr = r.NameAr?.Trim() ?? "";
        contact.OrganisationId = kind == ContactKinds.Person ? r.OrganisationId : null;
        contact.JobTitle = kind == ContactKinds.Person ? ContactsValidation.Clean(r.JobTitle) : null;
        contact.Email = ContactsValidation.Clean(r.Email);
        contact.Phone = ContactsValidation.Clean(r.Phone);
        contact.Mobile = ContactsValidation.Clean(r.Mobile);
        contact.Website = ContactsValidation.NormalizeWebsite(r.Website);
        contact.AddressLine1 = ContactsValidation.Clean(r.AddressLine1);
        contact.AddressLine2 = ContactsValidation.Clean(r.AddressLine2);
        contact.Area = ContactsValidation.Clean(r.Area);
        contact.City = ContactsValidation.Clean(r.City);
        contact.Emirate = ContactsValidation.Clean(r.Emirate);
        contact.PoBox = ContactsValidation.Clean(r.PoBox);
        contact.Country = r.Country!.Trim().ToUpperInvariant();
        contact.AddressAr = ContactsValidation.Clean(r.AddressAr);
        contact.Tags = ContactsValidation.JoinTags(r.Tags);
        contact.TradeLicenceNumber = ContactsValidation.Clean(r.TradeLicenceNumber);
        contact.TaxRegistrationNumber = ContactsValidation.NormalizeTaxNumber(r.TaxRegistrationNumber);
        contact.DefaultCurrency = defaultCurrency;
        contact.CreditLimit = creditLimit;
        contact.Notes = ContactsValidation.Clean(r.Notes);
        Keys(contact);
    }

    /// <summary>The values duplicates are compared on, kept with the record.</summary>
    internal static void Keys(Contact contact)
    {
        contact.NameKey = ContactsValidation.NameKey(contact.NameEn);
        contact.NameArKey = ContactsValidation.NameKey(contact.NameAr);
        contact.EmailKey = ContactsValidation.EmailKey(contact.Email);
        contact.PhoneKey = ContactsValidation.PhoneKey(contact.Phone);
        contact.MobileKey = ContactsValidation.PhoneKey(contact.Mobile);
    }

    internal static async Task<ContactDto> ToDtoAsync(ContactsDbContext db, Contact c, CancellationToken cancellationToken)
    {
        var organisation = c.OrganisationId is { } o
            ? await db.Contacts.AsNoTracking().Where(x => x.Id == o).Select(x => new { x.NameEn, x.NameAr }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var people = c.Kind == ContactKinds.Organisation ? await db.Contacts.CountAsync(x => x.OrganisationId == c.Id, cancellationToken) : 0;
        return new ContactDto(c.Id, c.Kind, c.Code, c.NameEn, c.NameAr, c.OrganisationId, organisation?.NameEn, organisation?.NameAr, c.JobTitle,
            c.Email, c.Phone, c.Mobile, c.Website, c.AddressLine1, c.AddressLine2, c.Area, c.City, c.Emirate, c.PoBox, c.Country, c.AddressAr,
            ContactsValidation.TagsOf(c.Tags), c.TradeLicenceNumber, c.TaxRegistrationNumber, c.DefaultCurrency, c.CreditLimit, c.Notes, c.IsActive, people,
            c.CreatedAt, c.UpdatedAt, c.Version);
    }
}
