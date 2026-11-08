using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Kernel.CustomFields;

namespace Erp.Modules.CustomFields;

/// <summary>Rules of custom field definitions and values: names, kinds, how a value of each kind
/// is read from JSON and kept as canonical text.</summary>
internal static partial class FieldRules
{
    public const int LabelMaxLength = 100;
    public const int MaxOptions = 50;
    public const int OptionValueMaxLength = 40;
    public const int MaxNumberDigits = 20;

    public static readonly DateOnly EarliestDate = new(1900, 1, 1);
    public static readonly DateOnly LatestDate = new(2200, 12, 31);

    /// <summary>The machine name of a field from its English label: <c>cf</c> and the words in
    /// camel case (letters and digits only), 3 to 40 characters.</summary>
    public static string KeyFor(string labelEn) => "cf" + Words(labelEn, upperFirst: true, 38);

    /// <summary>A select option's value from its English label: the words in lower camel case.</summary>
    public static string OptionValueFor(string labelEn) => Words(labelEn, upperFirst: false, OptionValueMaxLength);

    private static string Words(string text, bool upperFirst, int max)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var word in NonAlphanumeric().Split(text.Normalize(NormalizationForm.FormD)).Where(w => w.Length > 0))
        {
            var ascii = new string(word.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (ascii.Length == 0)
            {
                continue;
            }
            builder.Append(first && !upperFirst ? char.ToLowerInvariant(ascii[0]) : char.ToUpperInvariant(ascii[0]));
            builder.Append(ascii.AsSpan(1));
            first = false;
        }
        var result = builder.ToString();
        if (result.Length == 0)
        {
            result = upperFirst ? "Field" : "option";
        }
        if (!upperFirst && char.IsAsciiDigit(result[0]))
        {
            result = "o" + result;
        }
        return result.Length > max ? result[..max] : result;
    }

    public static bool IsOptionValue(string value) => OptionValueRegex().IsMatch(value);

    /// <summary>A number as canonical text with exactly <paramref name="decimals"/> decimals.</summary>
    public static string NumberText(decimal value, int decimals) =>
        decimal.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>Read a number from JSON (a number or a string; a dot for decimals, Arabic-Indic digits accepted).
    /// False when it is not one, has more than <paramref name="decimals"/> decimals or is too large.</summary>
    public static bool TryNumber(JsonElement json, int decimals, out decimal value)
    {
        value = 0;
        string? text = json.ValueKind switch
        {
            JsonValueKind.Number => json.GetRawText(),
            JsonValueKind.String => json.GetString(),
            _ => null,
        };
        return text is not null && TryNumber(text, decimals, out value);
    }

    public static bool TryNumber(string text, int decimals, out decimal value)
    {
        value = 0;
        var normal = Digits(text.Trim());
        if (normal.Length == 0 || normal.Length > MaxNumberDigits + 10 || !NumberRegex().IsMatch(normal))
        {
            return false;
        }
        if (!decimal.TryParse(normal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }
        return decimal.Round(value, decimals, MidpointRounding.AwayFromZero) == value && Math.Abs(value) < 1_000_000_000_000m;
    }

    /// <summary>Arabic-Indic and extended digits turned into 0 to 9.</summary>
    public static string Digits(string text) => new(text.Select(c => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        '٫' => '.',
        _ => c,
    }).ToArray());

    public static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(Digits(text?.Trim() ?? ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date >= EarliestDate && date <= LatestDate;

    [GeneratedRegex("[^A-Za-z0-9\\p{L}]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,39}$")]
    private static partial Regex OptionValueRegex();

    [GeneratedRegex("^-?[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?$")]
    private static partial Regex NumberRegex();
}
