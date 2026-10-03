using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Kernel.Http;

namespace Erp.Modules.Tenancy;

/// <summary>The seven emirates (for addresses in the United Arab Emirates).</summary>
public enum Emirate
{
    AbuDhabi,
    Dubai,
    Sharjah,
    Ajman,
    UmmAlQuwain,
    RasAlKhaimah,
    Fujairah,
}

/// <summary>First day of the working week shown in calendars and reports.</summary>
public enum WeekStart
{
    Monday,
    Sunday,
    Saturday,
}

/// <summary>Languages a workspace can default to.</summary>
public enum WorkspaceLanguage
{
    En,
    Ar,
}

/// <summary>Workspace-level settings and their allowed values.</summary>
public static class TenantSettings
{
    public const string DefaultTimeZone = "Asia/Dubai";

    /// <summary>Time zones offered for a workspace: the Gulf and the countries UAE trading and
    /// manufacturing companies most often work with. IANA names.</summary>
    public static readonly FrozenSet<string> TimeZones = FrozenSet.ToFrozenSet(
    [
        "Asia/Dubai", "Asia/Muscat", "Asia/Riyadh", "Asia/Qatar", "Asia/Bahrain", "Asia/Kuwait", "Asia/Baghdad", "Asia/Tehran",
        "Asia/Karachi", "Asia/Kolkata", "Asia/Dhaka", "Asia/Colombo", "Asia/Kathmandu", "Asia/Manila", "Asia/Singapore",
        "Asia/Shanghai", "Asia/Hong_Kong", "Asia/Tokyo", "Africa/Cairo", "Africa/Nairobi", "Africa/Johannesburg", "Africa/Lagos",
        "Europe/Istanbul", "Europe/Moscow", "Europe/Athens", "Europe/Berlin", "Europe/Paris", "Europe/London", "UTC",
        "America/New_York", "America/Chicago", "America/Los_Angeles", "Australia/Sydney",
    ], StringComparer.Ordinal);
}

/// <summary>Field rules shared by companies and branches. Error codes have English and Arabic
/// text in the module's resources.</summary>
internal static partial class TenancyValidation
{
    /// <summary>ISO 4217 codes of every currency .NET knows a region for.</summary>
    public static readonly FrozenSet<string> Currencies = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name).ISOCurrencySymbol; } catch (ArgumentException) { return null; } })
        .Where(code => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper))
        .Select(code => code!)
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>ISO 3166-1 alpha-2 codes of every region .NET knows.</summary>
    public static readonly FrozenSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name).TwoLetterISORegionName; } catch (ArgumentException) { return null; } })
        .Where(code => code is { Length: 2 } && code.All(char.IsAsciiLetterUpper))
        .Select(code => code!)
        .Append("AE")
        .ToFrozenSet(StringComparer.Ordinal);

    public const string CodePattern = "^[A-Z0-9][A-Z0-9-]{1,19}$";
    public const string PhonePattern = "^\\+?[0-9][0-9 ()-]{4,28}$";
    public const string TaxNumberPattern = "^[0-9]{1,20}$";
    public const string WebsitePattern = "^https?://[^\\s]{3,190}$";
    public const string PoBoxPattern = "^[A-Za-z0-9 -]{1,20}$";

    /// <summary>Upper-case, trimmed code (users may type it in lower case).</summary>
    public static string? NormalizeCode(string? code) => code?.Trim().ToUpperInvariant();

    /// <summary>Empty strings become null; others are trimmed.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Validator Code(this Validator validator, string field, string? code)
    {
        validator.Required(field, code);
        if (!string.IsNullOrWhiteSpace(code))
        {
            validator.Must(CodeRegex().IsMatch(code), field, "tenancyCode");
        }
        return validator;
    }

    public static Validator Address(this Validator validator, AddressFields address)
    {
        validator
            .MaxLength("addressLine1", address.AddressLine1, 200)
            .MaxLength("addressLine2", address.AddressLine2, 200)
            .MaxLength("city", address.City, 100)
            .MaxLength("addressAr", address.AddressAr, 400)
            .Required("country", address.Country)
            .Email("email", Clean(address.Email))
            .MaxLength("email", address.Email, 254);
        if (Clean(address.Country) is { } country)
        {
            validator.Must(Countries.Contains(country), "country", "tenancyCountry");
            validator.Must(address.Emirate is null || country == "AE", "emirate", "tenancyEmirateOutsideUae");
        }
        if (Clean(address.PoBox) is { } poBox)
        {
            validator.Must(PoBoxRegex().IsMatch(poBox), "poBox", "tenancyPoBox");
        }
        if (Clean(address.Phone) is { } phone)
        {
            validator.Must(PhoneRegex().IsMatch(phone), "phone", "tenancyPhone");
        }
        return validator;
    }

    public static Validator Website(this Validator validator, string? website)
    {
        if (Clean(website) is { } value)
        {
            validator.Must(WebsiteRegex().IsMatch(value) && Uri.TryCreate(value, UriKind.Absolute, out _), "website", "tenancyWebsite");
        }
        return validator;
    }

    public static Validator TaxNumber(this Validator validator, string? number)
    {
        if (Clean(number) is { } value)
        {
            validator.Must(TaxNumberRegex().IsMatch(value), "taxRegistrationNumber", "tenancyTaxNumber");
        }
        return validator;
    }

    public static Validator Currency(this Validator validator, string? currency)
    {
        validator.Required("baseCurrency", currency);
        if (Clean(currency) is { } value)
        {
            validator.Must(Currencies.Contains(value), "baseCurrency", "tenancyCurrency");
        }
        return validator;
    }

    /// <summary>The fiscal year starts on a day that exists in every year (29 February does not).</summary>
    public static Validator FiscalYearStart(this Validator validator, int? month, int? day)
    {
        validator.Required("fiscalYearStartMonth", month).Required("fiscalYearStartDay", day);
        if (month is { } m)
        {
            validator.Must(m is >= 1 and <= 12, "fiscalYearStartMonth", "tenancyMonth");
            if (day is { } d && m is >= 1 and <= 12)
            {
                validator.Must(d >= 1 && d <= DateTime.DaysInMonth(2025, m), "fiscalYearStartDay", "tenancyDayOfMonth");
            }
        }
        return validator;
    }

    public static string? EmirateValue(Emirate? emirate) => emirate is { } e ? JsonNamingPolicyCamel(e.ToString()) : null;

    public static Emirate? ParseEmirate(string? value) =>
        value is not null && Enum.TryParse<Emirate>(value, ignoreCase: true, out var e) ? e : null;

    private static string JsonNamingPolicyCamel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    [GeneratedRegex(CodePattern)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(PhonePattern)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(TaxNumberPattern)]
    private static partial Regex TaxNumberRegex();

    [GeneratedRegex(WebsitePattern)]
    private static partial Regex WebsiteRegex();

    [GeneratedRegex(PoBoxPattern)]
    private static partial Regex PoBoxRegex();
}

/// <summary>The address fields companies and branches share.</summary>
internal sealed record AddressFields(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    Emirate? Emirate,
    string? PoBox,
    string? Country,
    string? AddressAr,
    string? Phone,
    string? Email);
