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

    /// <summary>ISO 3166-1 alpha-2 code of the United Arab Emirates (emirates apply only there).</summary>
    public const string UnitedArabEmirates = "AE";

    public const string CodePattern = "^[A-Z0-9][A-Z0-9-]{1,19}$";
    public const string PhonePattern = "^\\+?[0-9][0-9 ()-]{4,28}$";
    /// <summary>A Tax Registration Number as typed: exactly 15 digits, 0 to 9 or the Arabic-Indic
    /// ٠ to ٩ of an Arabic keyboard (turned into 0 to 9), with any spaces and dashes (removed before
    /// it is stored). docs/compliance/vat-trn-format.md, owner 2026-10-10 and 2026-10-17.</summary>
    public const string TaxNumberPattern = "^[ -]*(?:[0-9\u0660-\u0669][ -]*){15}$";

    /// <summary>A Tax Registration Number as stored and answered: exactly 15 digits.</summary>
    public const string StoredTaxNumberPattern = "^[0-9]{15}$";

    /// <summary>Longest TRN accepted as typed (15 digits with the spaces and dashes between them), a bound on the request only.</summary>
    public const int TaxNumberTypedMaxLength = 100;
    public const string WebsitePattern = "^https?://[^\\s]{3,190}$";
    public const string PoBoxPattern = "^[A-Za-z0-9 -]{1,20}$";

    /// <summary>Upper-case, trimmed code (users may type it in lower case).</summary>
    public static string? NormalizeCode(string? code) => code?.Trim().ToUpperInvariant();

    /// <summary>Empty strings become null; others are trimmed.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A code, when given (left empty, one is made from the name).</summary>
    public static Validator Code(this Validator validator, string field, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            validator.Must(CodeRegex().IsMatch(code), field, "tenancyCode");
        }
        return validator;
    }

    /// <summary>A name in English, in Arabic or both: at least one, each at most 200 characters.</summary>
    public static Validator Names(this Validator validator, string englishField, string? english, string arabicField, string? arabic)
    {
        validator.Must(!string.IsNullOrWhiteSpace(english) || !string.IsNullOrWhiteSpace(arabic), englishField, "tenancyNameEnOrAr")
            .MaxLength(englishField, english, 200)
            .MaxLength(arabicField, arabic, 200);
        return validator;
    }

    /// <summary>
    /// A company's legal names: the English one is always required (it identifies the company in
    /// every list, code and English document, and an English document must never print an empty
    /// legal name: p06 round 3 saved a company whose English legal name was empty because an Arabic
    /// one existed) and holds at least one Latin letter; the Arabic one is optional until the owner
    /// rules on it (needs-human #9). Each is at most 200 characters.
    /// </summary>
    public static Validator LegalNames(this Validator validator, string? english, string? arabic)
    {
        validator.Must(!string.IsNullOrWhiteSpace(english), "legalNameEn", "tenancyLegalNameEn")
            // The English legal name is the one in Latin letters on the trade licence: a name with
            // no Latin letter at all (typed in Arabic, or digits only) is not it (critic p02 round 11:
            // an Arabic walk saved the English legal name "فرع ديرة 60473").
            .Must(string.IsNullOrWhiteSpace(english) || english.Any(char.IsAsciiLetter), "legalNameEn", "tenancyLegalNameEnLatin")
            .MaxLength("legalNameEn", english, 200)
            .MaxLength("legalNameAr", arabic, 200);
        return validator;
    }

    /// <summary>Words of a legal name that say nothing about which company it is.</summary>
    private static readonly FrozenSet<string> LegalWords = FrozenSet.ToFrozenSet(
        ["LLC", "L", "C", "FZE", "FZCO", "FZ", "FZC", "DMCC", "PJSC", "PSC", "LTD", "LIMITED", "INC", "CO", "COMPANY", "BRANCH", "THE", "AND", "OF"],
        StringComparer.Ordinal);

    /// <summary>
    /// What a branch's code is made from when none is typed: the branch's own part of a name that
    /// trades under its company's ("Falcon Logistics LLC - Jebel Ali Branch" gives JEBEL), and HQ for
    /// a branch named after its company alone, its head office (critic p02 round 6: every suggested
    /// branch code repeated the company code).
    /// </summary>
    public static string? BranchCodeBasis(string? branchName, string? companyName)
    {
        var name = (branchName ?? "").Trim();
        var dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0 && name[(dash + 3)..].Trim() is { Length: > 0 } own)
        {
            return own;
        }
        return name.Length > 0 && string.Equals(name, (companyName ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ? "HQ" : name;
    }

    /// <summary>
    /// A code made from an English name (its first distinctive word, joined to the next when short:
    /// "Al Noor Trading LLC" becomes AL-NOOR), unique among <paramref name="taken"/> by a numeric
    /// suffix. Names without Latin letters get <paramref name="fallback"/>-1, -2, …
    /// </summary>
    public static string SuggestCode(string? english, IReadOnlyCollection<string> taken, string fallback)
    {
        var words = WordRegex().Matches((english ?? "").ToUpperInvariant()).Select(m => m.Value)
            .Where(w => !LegalWords.Contains(w)).ToList();
        string stem;
        if (words.Count == 0)
        {
            stem = fallback;
        }
        else
        {
            stem = words[0].Length <= 3 && words.Count > 1 ? $"{words[0]}-{words[1]}" : words[0];
            stem = stem[..Math.Min(stem.Length, 14)].TrimEnd('-');
            if (stem.Length < 2)
            {
                stem = fallback;
            }
        }
        var set = taken.ToHashSet(StringComparer.Ordinal);
        if (words.Count > 0 && stem != fallback && !set.Contains(stem))
        {
            return stem;
        }
        for (var n = words.Count == 0 || stem == fallback ? 1 : 2; ; n++)
        {
            var candidate = $"{stem}-{n}";
            if (!set.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex("[A-Z0-9]+")]
    private static partial Regex WordRegex();

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
            validator.Must(address.Emirate is null || country == UnitedArabEmirates, "emirate", "tenancyEmirateOutsideUae");
        }
        if (Clean(address.PoBox) is { } poBox)
        {
            validator.Must(PoBoxRegex().IsMatch(poBox), "poBox", "tenancyPoBox");
        }
        if (Clean(address.Phone) is { } phone)
        {
            if (PhoneRegex().IsMatch(phone))
            {
                validator.Must(UaeNumberLengthFits(phone), "phone", "tenancyUaePhone");
            }
            else
            {
                validator.Add("phone", "tenancyPhone");
            }
        }
        return validator;
    }

    /// <summary>
    /// Whether a number written with the UAE's country code (+971 or 00971) has as many digits after
    /// it as the UAE's numbering plan gives its kind (ITU-T E.164 plan for country code 971 as
    /// published by the ITU for the regulator, checked 2026-10-19; decision
    /// p06-form-report-whole-documents): a fixed line, an area digit 2, 3, 4, 6, 7 or 9 and seven
    /// more, 8 digits; a mobile number (5 and eight more) or a service number 200 to 900, 9 digits;
    /// a toll-free 800 number 5 to 12 digits. A number of another country, or one written without
    /// its country code, is not judged here.
    /// </summary>
    public static bool UaeNumberLengthFits(string phone)
    {
        var text = phone.Trim();
        string rest;
        if (text.StartsWith("+971", StringComparison.Ordinal))
        {
            rest = text[4..];
        }
        else if (text.StartsWith("00971", StringComparison.Ordinal))
        {
            rest = text[5..];
        }
        else
        {
            return true;
        }
        var national = new string(rest.Where(char.IsAsciiDigit).ToArray());
        if (national.Length == 0)
        {
            return false;
        }
        return national[0] switch
        {
            '5' => national.Length == 9,
            '8' => national.StartsWith("800", StringComparison.Ordinal) && national.Length is >= 5 and <= 12,
            // Eight digits, or nine for a service number (200, 300, 400, 600, 700, 900).
            '2' or '3' or '4' or '6' or '7' or '9' => national.Length == 8 || national.Length == 9 && national[1] == '0' && national[2] == '0',
            _ => false,
        };
    }

    public static Validator Website(this Validator validator, string? website)
    {
        if (Clean(website) is { } value)
        {
            validator.Must(WebsiteRegex().IsMatch(value) && Uri.TryCreate(value, UriKind.Absolute, out _), "website", "tenancyWebsite");
        }
        return validator;
    }

    /// <summary>
    /// A Tax Registration Number, when given: exactly 15 digits 0 to 9 once the spaces and dashes a
    /// user types are removed; anything else is refused. No prefix and no check-digit rule: no
    /// official publication states one (docs/compliance/vat-trn-format.md, signed off by the owner
    /// on 2026-10-10; source: UAE Ministry of Finance, Electronic Invoicing Guidelines v1.1).
    /// </summary>
    public static Validator TaxNumber(this Validator validator, string? number)
    {
        if (Clean(number) is { } value)
        {
            validator.Must(value.Length <= TaxNumberTypedMaxLength && NormalizeTaxNumber(value) is { } digits && StoredTaxNumberRegex().IsMatch(digits),
                "taxRegistrationNumber", "tenancyTaxNumber");
        }
        return validator;
    }

    /// <summary>The TRN as stored: the typed value without its spaces and dashes, its Arabic-Indic
    /// digits ٠ to ٩ (U+0660 to U+0669) turned into 0 to 9 (owner decision 2026-10-17,
    /// docs/compliance/vat-trn-format.md); null when empty. Any other digit set (the Persian ۰ to ۹,
    /// full-width digits) is left as typed, so <see cref="TaxNumber"/> refuses it. Valid only where
    /// <see cref="TaxNumber"/> accepted it.</summary>
    public static string? NormalizeTaxNumber(string? number)
    {
        if (Clean(number) is not { } value)
        {
            return null;
        }
        var digits = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is ' ' or '-')
            {
                continue;
            }
            digits.Append(c is >= '\u0660' and <= '\u0669' ? (char)('0' + (c - '\u0660')) : c);
        }
        return digits.ToString();
    }

    /// <summary>
    /// The legal names under the company's own setting: while it makes the Arabic legal name
    /// required, a blank one is refused (docs/compliance/arabic-legal-name-on-documents.md).
    /// </summary>
    public static Validator ArabicLegalNameWhenRequired(this Validator validator, bool required, string? arabic)
    {
        validator.Must(!required || !string.IsNullOrWhiteSpace(arabic), "legalNameAr", "tenancyLegalNameArRequired");
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

    [GeneratedRegex(StoredTaxNumberPattern)]
    private static partial Regex StoredTaxNumberRegex();

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
