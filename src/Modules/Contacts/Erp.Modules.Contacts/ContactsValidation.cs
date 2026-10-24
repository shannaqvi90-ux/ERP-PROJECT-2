using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Erp.Kernel.Http;
using Erp.Kernel.Money;

namespace Erp.Modules.Contacts;

/// <summary>
/// Field rules of a contact. Error codes have English and Arabic text in the module's resources.
/// The only number-format rule taken from a statutory source is the tax registration number's
/// (docs/compliance/vat-trn-format.md, signed off by the owner); the trade licence number is a
/// plain text field, and phone numbers are judged only by their shape and, for a number with the
/// UAE's code, by the lengths of the UAE numbering plan the record form already applies
/// (docs/decisions/p16-contacts-fields-and-rules.md).
/// </summary>
internal static partial class ContactsValidation
{
    public const string UnitedArabEmirates = "AE";

    /// <summary>The seven emirates as stored (camel case).</summary>
    public static readonly IReadOnlyList<string> Emirates = ["abuDhabi", "dubai", "sharjah", "ajman", "ummAlQuwain", "rasAlKhaimah", "fujairah"];

    private static readonly FrozenSet<string> EmirateSet = Emirates.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Current ISO 4217 currency codes with minor units (the kernel's money type accepts exactly these).</summary>
    public static readonly FrozenSet<string> Currencies = CurrencyCode.All.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>ISO 3166-1 alpha-2 codes of every region .NET knows.</summary>
    public static readonly FrozenSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name).TwoLetterISORegionName; } catch (ArgumentException) { return null; } })
        .Where(code => code is { Length: 2 } && code.All(char.IsAsciiLetterUpper))
        .Select(code => code!)
        .Append(UnitedArabEmirates)
        .ToFrozenSet(StringComparer.Ordinal);

    public const int NameMaxLength = 200;
    public const int TagMaxLength = 40;
    public const int MaxTags = 10;
    public const int NotesMaxLength = 2000;

    /// <summary>A reference: a letter or digit, then up to 29 letters, digits, dots, slashes, underscores or hyphens.</summary>
    public const string CodePattern = "^[A-Za-z0-9][A-Za-z0-9./_-]{0,29}$";

    /// <summary>A phone number's characters: digits, spaces, brackets and hyphens, optionally
    /// starting with +, 5 to 30 characters.</summary>
    public const string PhoneShapePattern = "^\\+?[0-9][0-9 ()-]{4,28}$";

    /// <summary>A Tax Registration Number as typed: exactly 15 digits, 0 to 9 or the Arabic-Indic
    /// ٠ to ٩ of an Arabic keyboard, with any spaces and dashes (docs/compliance/vat-trn-format.md).</summary>
    public const string TaxNumberPattern = "^[ -]*(?:[0-9٠-٩][ -]*){15}$";

    /// <summary>A Tax Registration Number as stored and answered: exactly 15 digits.</summary>
    public const string StoredTaxNumberPattern = "^[0-9]{15}$";

    public const int TaxNumberTypedMaxLength = 100;
    public const string WebsitePattern = "^(https?://)?[^\\s]{3,190}$";
    public const string PoBoxPattern = "^[A-Za-z0-9 -]{1,20}$";

    /// <summary>A decimal amount of at most ten decimal places, as the credit limit is typed.</summary>
    public const string AmountPattern = "^[0-9]{1,18}(\\.[0-9]{1,10})?$";

    /// <summary>Empty strings become null; others are trimmed.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Validator Names(this Validator validator, string? english, string? arabic)
    {
        validator.Must(!string.IsNullOrWhiteSpace(english) || !string.IsNullOrWhiteSpace(arabic), "nameEn", "contactsNameRequired")
            .MaxLength("nameEn", english?.Trim(), NameMaxLength)
            .MaxLength("nameAr", arabic?.Trim(), NameMaxLength);
        return validator;
    }

    public static Validator Code(this Validator validator, string? code)
    {
        if (Clean(code) is { } value)
        {
            validator.Must(CodeRegex().IsMatch(value), "code", "contactsCode");
        }
        return validator;
    }

    public static Validator Phone(this Validator validator, string field, string? phone)
    {
        if (Clean(phone) is { } value)
        {
            if (PhoneShapeRegex().IsMatch(value))
            {
                validator.Must(UaeNumberLengthFits(value), field, "contactsUaePhone");
            }
            else
            {
                validator.Add(field, "contactsPhone");
            }
        }
        return validator;
    }

    /// <summary>
    /// Whether a number written with the UAE's country code (+971 or 00971) has as many digits after
    /// it as its kind has in the UAE numbering plan: 8 for a fixed line (an area digit 2, 3, 4, 6, 7
    /// or 9 and seven more), 9 for a mobile number (5 and eight more) or a 200 to 900 service number,
    /// 5 to 12 for an 800 number. The same lengths the record form's phone field completes on
    /// (web/src/kernel/forms/phone.ts) and the company form checks; a number of another country is
    /// not judged.
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
            '2' or '3' or '4' or '6' or '7' or '9' => national.Length == 8 || national.Length == 9 && national[1] == '0' && national[2] == '0',
            _ => false,
        };
    }

    public static Validator Address(this Validator validator, string? line1, string? line2, string? area, string? city, string? emirate, string? poBox, string? country, string? addressAr)
    {
        validator
            .MaxLength("addressLine1", line1?.Trim(), 200)
            .MaxLength("addressLine2", line2?.Trim(), 200)
            .MaxLength("area", area?.Trim(), 100)
            .MaxLength("city", city?.Trim(), 100)
            .MaxLength("addressAr", addressAr?.Trim(), 400)
            .Required("country", country);
        if (Clean(country)?.ToUpperInvariant() is { } code)
        {
            validator.Must(Countries.Contains(code), "country", "contactsCountry");
            validator.Must(Clean(emirate) is null || code == UnitedArabEmirates, "emirate", "contactsEmirateOutsideUae");
        }
        if (Clean(emirate) is { } value)
        {
            validator.Must(EmirateSet.Contains(value), "emirate", "contactsEmirate");
        }
        if (Clean(poBox) is { } box)
        {
            validator.Must(PoBoxRegex().IsMatch(box), "poBox", "contactsPoBox");
        }
        return validator;
    }

    public static Validator Website(this Validator validator, string? website)
    {
        if (Clean(website) is { } value)
        {
            validator.Must(NormalizeWebsite(value) is not null, "website", "contactsWebsite");
        }
        return validator;
    }

    /// <summary>A web address with its scheme (https:// is added when only the host was typed), or
    /// null when it is not one.</summary>
    public static string? NormalizeWebsite(string? website)
    {
        if (Clean(website) is not { } value || !WebsiteRegex().IsMatch(value))
        {
            return null;
        }
        var full = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? value : "https://" + value;
        return full.Length <= 200 && Uri.TryCreate(full, UriKind.Absolute, out var uri) && uri.Host.Contains('.') ? full : null;
    }

    /// <summary>
    /// A Tax Registration Number, when given: exactly 15 digits once the spaces and dashes typed are
    /// removed and Arabic-Indic digits turned into 0 to 9; no prefix and no check-digit rule
    /// (docs/compliance/vat-trn-format.md, signed off by the owner on 2026-10-10 and 2026-10-17).
    /// </summary>
    public static Validator TaxNumber(this Validator validator, string? number)
    {
        if (Clean(number) is { } value)
        {
            validator.Must(value.Length <= TaxNumberTypedMaxLength && NormalizeTaxNumber(value) is { } digits && StoredTaxNumberRegex().IsMatch(digits),
                "taxRegistrationNumber", "contactsTaxNumber");
        }
        return validator;
    }

    /// <summary>The TRN as stored: without spaces and dashes, Arabic-Indic digits as 0 to 9; null when empty.</summary>
    public static string? NormalizeTaxNumber(string? number)
    {
        if (Clean(number) is not { } value)
        {
            return null;
        }
        var digits = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is ' ' or '-')
            {
                continue;
            }
            digits.Append(c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c);
        }
        return digits.ToString();
    }

    public static Validator Currency(this Validator validator, string field, string? currency, bool required)
    {
        if (required)
        {
            validator.Required(field, currency);
        }
        if (Clean(currency)?.ToUpperInvariant() is { } value)
        {
            validator.Must(Currencies.Contains(value), field, "contactsCurrency");
        }
        return validator;
    }

    /// <summary>The tags as stored: trimmed, each once (case ignored), in the order typed; null when none.</summary>
    public static string? JoinTags(IReadOnlyList<string>? tags)
    {
        var kept = SplitTags(tags);
        return kept.Count == 0 ? null : string.Join(TagSeparator, kept);
    }

    public const string TagSeparator = "; ";

    public static List<string> SplitTags(IEnumerable<string>? tags)
    {
        var kept = new List<string>();
        foreach (var tag in tags ?? [])
        {
            var value = Clean(tag);
            if (value is not null && !kept.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                kept.Add(value);
            }
        }
        return kept;
    }

    public static IReadOnlyList<string> TagsOf(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Validator Tags(this Validator validator, IReadOnlyList<string>? tags)
    {
        var kept = SplitTags(tags);
        validator.Must(kept.Count <= MaxTags, "tags", "contactsTooManyTags", MaxTags)
            .Must(kept.All(t => t.Length <= TagMaxLength && !t.Contains(';')), "tags", "contactsTag", TagMaxLength);
        return validator;
    }

    /// <summary>The name as compared for duplicates: lower case, letters and digits of any script,
    /// one space between words; Arabic letters that are often written either way (alef with hamza or
    /// madda, final ya and alef maqsura, ta marbuta and ha) are folded together, and tatweel and
    /// short vowel marks are dropped.</summary>
    public static string NameKey(string? name)
    {
        var key = new StringBuilder();
        var space = false;
        foreach (var raw in (name ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => raw,
            };
            if (c == 'ـ' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                if (space && key.Length > 0)
                {
                    key.Append(' ');
                }
                key.Append(c);
                space = false;
            }
            else
            {
                space = true;
            }
        }
        return key.Length > NameMaxLength ? key.ToString(0, NameMaxLength) : key.ToString();
    }

    /// <summary>The e-mail as compared for duplicates: trimmed, lower case; null when empty.</summary>
    public static string? EmailKey(string? email) => Clean(email)?.ToLowerInvariant();

    /// <summary>A phone number as compared for duplicates: its digits, a UAE number always as 971
    /// and the national number (typed +971 50 …, 00971 50 …, 050 … or 50 …); null when it has none.</summary>
    public static string? PhoneKey(string? phone)
    {
        if (Clean(phone) is not { } value)
        {
            return null;
        }
        var latin = new string(value.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray());
        var digits = new string(latin.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return null;
        }
        if (latin.TrimStart().StartsWith('+'))
        {
            return digits;
        }
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return digits[2..];
        }
        if (digits.StartsWith('0'))
        {
            return "971" + digits[1..];
        }
        return digits.Length is 8 or 9 ? "971" + digits : digits;
    }

    [GeneratedRegex(CodePattern)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(PhoneShapePattern)]
    private static partial Regex PhoneShapeRegex();

    [GeneratedRegex(StoredTaxNumberPattern)]
    private static partial Regex StoredTaxNumberRegex();

    [GeneratedRegex(WebsitePattern)]
    private static partial Regex WebsiteRegex();

    [GeneratedRegex(PoBoxPattern)]
    private static partial Regex PoBoxRegex();
}
