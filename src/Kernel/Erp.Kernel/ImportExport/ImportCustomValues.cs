using System.Globalization;
using System.Text.Json;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Http;

namespace Erp.Kernel.ImportExport;

/// <summary>
/// Turns the text of a file's cells into the values of a record's custom fields (p14), for the importer of any
/// kind of record: <c>custom.&lt;key&gt;</c> cells become the typed values <see cref="ICustomFields.SaveAsync"/> takes,
/// and a value that does not fit its field's type is reported on the validator under <c>customFields.&lt;key&gt;</c>.
/// </summary>
public static class ImportCustomValues
{
    public const string Prefix = "custom.";

    /// <summary>The values of the row's custom-field cells that hold text, by field key. A blank cell leaves the field alone
    /// (it keeps its default on a new record). A required field without a value is left for the save to refuse.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Convert(IReadOnlyDictionary<string, string?> row, IReadOnlyList<CustomFieldInfo> fields, Validator validator)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!row.TryGetValue(Prefix + field.Key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var text = raw.Trim();
            var name = "customFields." + field.Key;
            switch (field.Type)
            {
                case CustomFieldTypes.Number:
                    var digits = Digits(text).Replace(",", "").Replace(" ", "");
                    if (decimal.TryParse(digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    {
                        values[field.Key] = JsonSerializer.SerializeToElement(number);
                    }
                    else
                    {
                        validator.Add(name, "importNumber");
                    }
                    break;
                case CustomFieldTypes.Boolean:
                    var truth = Truth(text);
                    if (truth is null)
                    {
                        validator.Add(name, "importBoolean");
                    }
                    else
                    {
                        values[field.Key] = JsonSerializer.SerializeToElement(truth.Value);
                    }
                    break;
                case CustomFieldTypes.Date:
                    if (Date(text) is { } date)
                    {
                        values[field.Key] = JsonSerializer.SerializeToElement(date);
                    }
                    else
                    {
                        validator.Add(name, "importDate");
                    }
                    break;
                case CustomFieldTypes.Select:
                    var option = field.Options.FirstOrDefault(o => string.Equals(o.Value, text, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(o.LabelEn, text, StringComparison.OrdinalIgnoreCase) || string.Equals(o.LabelAr, text, StringComparison.Ordinal));
                    if (option is null)
                    {
                        validator.Add(name, "importOption");
                    }
                    else
                    {
                        values[field.Key] = JsonSerializer.SerializeToElement(option.Value);
                    }
                    break;
                case CustomFieldTypes.Lookup:
                    if (Guid.TryParse(text, out var id))
                    {
                        values[field.Key] = JsonSerializer.SerializeToElement(id.ToString());
                    }
                    else
                    {
                        validator.Add(name, "importLookup");
                    }
                    break;
                default:
                    values[field.Key] = JsonSerializer.SerializeToElement(text);
                    break;
            }
        }
        return values;
    }

    /// <summary>Arabic-Indic and Persian digits as 0 to 9.</summary>
    public static string Digits(string text) => string.Create(text.Length, text, (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠'))
                : c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰'))
                : c == '٫' ? '.' : c;
        }
    });

    /// <summary>yes/no in the ways a spreadsheet says it, in English and Arabic; null when it is neither.</summary>
    public static bool? Truth(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "y" or "1" or "نعم" or "صح" => true,
        "false" or "no" or "n" or "0" or "لا" or "خطأ" => false,
        _ => null,
    };

    /// <summary>A date as yyyy-mm-dd, from yyyy-mm-dd, dd/mm/yyyy or dd-mm-yyyy (the day first, as in the UAE), or null.</summary>
    public static string? Date(string text)
    {
        var plain = Digits(text.Trim());
        if (plain.Length > 10 && plain[10] is ' ' or 'T')
        {
            plain = plain[..10];
        }
        string[] formats = ["yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy"];
        return DateTime.TryParseExact(plain, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}
