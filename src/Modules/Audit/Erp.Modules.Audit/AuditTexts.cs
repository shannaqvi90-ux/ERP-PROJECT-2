using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Erp.Kernel.Localization;

namespace Erp.Modules.Audit;

/// <summary>
/// The audit screens' own strings (<c>web/src/modules/audit/i18n/{en,ar}.json</c>, embedded at
/// build time): the name of every recorded field (<c>audit.field.&lt;column&gt;</c>), which the audit
/// log offers as its field filter's choices, and the words of the change texts the log prints and
/// exports, so paper and screen say the same thing in English and in Arabic.
/// </summary>
public sealed class AuditTexts
{
    public const string FieldPrefix = "audit.field.";

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLanguage;

    public AuditTexts()
    {
        var assembly = typeof(AuditTexts).Assembly;
        var merged = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in Languages.All)
        {
            using var stream = assembly.GetManifestResourceStream($"audit-web-strings/{language}.json")
                ?? throw new InvalidOperationException($"The audit screens' {language} strings were not embedded.");
            merged[language] = (JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? []).ToFrozenDictionary(StringComparer.Ordinal);
        }
        _byLanguage = merged.ToFrozenDictionary(StringComparer.Ordinal);
        Fields = _byLanguage[Languages.English].Keys.Where(k => k.StartsWith(FieldPrefix, StringComparison.Ordinal))
            .Select(k => k[FieldPrefix.Length..]).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every field (column) name the audit screens have a label for, sorted.</summary>
    public IReadOnlyList<string> Fields { get; }

    public bool Has(string key, string language) => _byLanguage.TryGetValue(language, out var table) && table.ContainsKey(key);

    /// <summary>The text in the language (English, then the key itself, when missing), with
    /// <c>{name}</c> placeholders filled.</summary>
    public string Get(string key, string language, params (string Name, string Value)[] values)
    {
        var table = _byLanguage.GetValueOrDefault(language) ?? _byLanguage[Languages.English];
        if (!table.TryGetValue(key, out var text) && !_byLanguage[Languages.English].TryGetValue(key, out text))
        {
            text = key;
        }
        foreach (var (name, value) in values)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>A field's name: its label, or for a custom field (<c>custom_fields.key</c>) and a
    /// field without one, the stored name itself.</summary>
    public string FieldName(string field, string language) =>
        Has(FieldPrefix + field, language) ? Get(FieldPrefix + field, language) : field;

    /// <summary>One recorded value as text: a flag as Yes or No, a list joined with commas, nothing
    /// as "(empty)", a redacted secret as "(hidden)"; a record's id by the record's name when
    /// <paramref name="labels"/> knows it.</summary>
    public string Value(JsonElement? value, string language, IReadOnlyDictionary<Guid, AuditRecordName>? labels = null, string? recordType = null, string? field = null)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Get("audit.value.empty", language);
        }
        // A choice shows the chosen option's label, never its code: the module registers
        // audit.choice.<kind>.<field>.<value> in both languages.
        if (recordType is not null && field is not null && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            var key = $"audit.choice.{recordType}.{field}.{(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())}";
            if (Has(key, language))
            {
                return Get(key, language);
            }
            if (v.ValueKind == JsonValueKind.String)
            {
                var code = v.GetString() ?? "";
                if ((field == "country" || field.EndsWith("_country", StringComparison.Ordinal)) && CountryName(code, language) is { } country)
                {
                    return country;
                }
                if ((field == "record_type" || field == "lookup_target" || field.EndsWith("_record_type", StringComparison.Ordinal)) && Has("audit.kind." + code, language))
                {
                    return Get("audit.kind." + code, language);
                }
            }
        }
        switch (v.ValueKind)
        {
            case JsonValueKind.True:
                return Get("audit.value.yes", language);
            case JsonValueKind.False:
                return Get("audit.value.no", language);
            case JsonValueKind.Number:
                return Number(v.GetRawText());
            case JsonValueKind.String:
                var text = v.GetString() ?? "";
                if (text == AuditValues.Redacted)
                {
                    return Get("audit.value.hidden", language);
                }
                if (IsoInstant.IsMatch(text) && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var instant))
                {
                    return Instant(instant, language);
                }
                if (IsoDate.IsMatch(text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
                {
                    return $"{day.Day:00}/{day.Month:00}/{day.Year:0000}";
                }
                if (labels is not null && Guid.TryParse(text, out var id) && labels.TryGetValue(id, out var name))
                {
                    return name.For(language);
                }
                return text.Length == 0 ? Get("audit.value.empty", language) : text;
            case JsonValueKind.Array:
                var items = v.EnumerateArray().Select(i => Value(i, language, labels)).ToList();
                return items.Count == 0 ? Get("audit.value.empty", language) : string.Join(", ", items);
            default:
                return v.GetRawText();
        }
    }

    private static readonly System.Text.RegularExpressions.Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A number with its thousands grouped (25,000.50), the fraction kept exactly as stored (never through a binary float).</summary>
    private static string Number(string raw)
    {
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"^(-?)(\d+)(\.\d+)?$");
        if (!match.Success)
        {
            return raw;
        }
        var digits = match.Groups[2].Value;
        var grouped = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                grouped.Append(',');
            }
            grouped.Append(digits[i]);
        }
        return match.Groups[1].Value + grouped + match.Groups[3].Value;
    }

    /// <summary>Country names by language and ISO 3166-1 alpha-2 code (Resources/countries.json, made by build/gen-audit-countries.mjs from CLDR, the names the screens show).</summary>
    /// Loaded once when the type is first used, never filled in later, so the gates' product-state
    /// snapshot sees the same value before and after the tenants use the app (a Lazy cache filled
    /// during the attack read as product state changed in GateSelfTests).
    private static readonly System.Collections.Frozen.FrozenDictionary<string, System.Collections.Frozen.FrozenDictionary<string, string>> CountryNames = LoadCountryNames();

    private static System.Collections.Frozen.FrozenDictionary<string, System.Collections.Frozen.FrozenDictionary<string, string>> LoadCountryNames()
    {
        using var stream = typeof(AuditTexts).Assembly.GetManifestResourceStream("Erp.Modules.Audit.Resources.countries.json")
            ?? throw new InvalidOperationException("The country names were not embedded.");
        var names = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? [];
        return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(names, n => n.Key, n => System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(n.Value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>A country's name in the language, or null when the code is not a country.</summary>
    internal static string? CountryName(string code, string language) =>
        CountryNames.TryGetValue(language == Languages.Arabic ? "ar" : "en", out var names) && names.TryGetValue(code, out var name) ? name : null;

    private static readonly System.Text.RegularExpressions.Regex IsoInstant =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Collections.Immutable.ImmutableArray<string> Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>The workspace's default time zone (the audit texts are one line for screen and paper; the
    /// product's workspaces are in the UAE).</summary>
    private static readonly TimeSpan Gulf = TimeSpan.FromHours(4);

    /// <summary>An instant as a date and time in the language, Gulf time: 14 Nov 2026, 11:37 PM; in Arabic
    /// 14/11/2026، 11:37 م with Latin digits (as the reports print it).</summary>
    public static string Instant(DateTimeOffset instant, string language)
    {
        var local = instant.ToOffset(Gulf);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        if (language == Languages.Arabic)
        {
            const char rlm = '\u200F';
            return $"{local.Day:00}{rlm}/{local.Month:00}{rlm}/{local.Year:0000}\u060C {hour}:{local.Minute:00} {(local.Hour < 12 ? "\u0635" : "\u0645")}";
        }
        return $"{local.Day} {Months[local.Month - 1]} {local.Year:0000}, {hour}:{local.Minute:00} {(local.Hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>The longest change line the log lists, prints and exports (a change of many fields,
    /// a creation's first values, is cut there with "…"; its panel shows every field).</summary>
    public const int SummaryLength = 200;

    /// <summary>The changes of one audit row as one line ("E-mail: from a@x.ae to b@x.ae; Name: …"), in
    /// the language, at most <paramref name="max"/> characters.</summary>
    public string Summary(IReadOnlyList<AuditFieldChange> changes, string action, string language, IReadOnlyDictionary<Guid, AuditRecordName>? labels, int max = SummaryLength,
        IReadOnlyDictionary<string, AuditFieldLabel>? fieldLabels = null, string? recordType = null)
    {
        var builder = new StringBuilder();
        var separator = Get("audit.value.separator", language);
        foreach (var change in changes)
        {
            var field = fieldLabels is not null && fieldLabels.TryGetValue(change.Field, out var custom) ? custom.For(language) : FieldName(change.Field, language);
            var line = action switch
            {
                "insert" => Get("audit.value.set", language, ("field", field), ("new", Value(change.New, language, labels, recordType, change.Field))),
                "delete" => Get("audit.value.set", language, ("field", field), ("new", Value(change.Old, language, labels, recordType, change.Field))),
                _ when change.New is null || change.New.Value.ValueKind == JsonValueKind.Null =>
                    Get("audit.value.cleared", language, ("field", field), ("old", Value(change.Old, language, labels, recordType, change.Field))),
                _ => Get("audit.value.from", language, ("field", field), ("old", Value(change.Old, language, labels, recordType, change.Field)), ("new", Value(change.New, language, labels, recordType, change.Field))),
            };
            if (builder.Length > 0)
            {
                builder.Append(separator);
            }
            builder.Append(line);
            if (builder.Length >= max)
            {
                break;
            }
        }
        return builder.Length > max ? builder.ToString(0, max - 1) + "…" : builder.ToString();
    }

}
