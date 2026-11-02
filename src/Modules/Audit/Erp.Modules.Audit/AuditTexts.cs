using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;

namespace Erp.Modules.Audit;

/// <summary>What a change's values are shown with: the names of the records they refer to by id,
/// the workspace's time zone and the digits of the document.</summary>
public sealed record AuditValueContext(IReadOnlyDictionary<Guid, AuditRecordName>? Names, TimeZoneInfo TimeZone, bool ArabicDigits = false);

/// <summary>
/// The audit screens' own strings (<c>web/src/modules/audit/i18n/{en,ar}.json</c>, embedded at
/// build time): the name of every recorded field (<c>audit.field.&lt;column&gt;</c>), which the audit
/// log offers as its field filter's choices, and the words of the change texts the log prints and
/// exports, so paper and screen say the same thing in English and in Arabic.
/// </summary>
public sealed partial class AuditTexts
{
    public const string FieldPrefix = "audit.field.";

    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLanguage;

    /// <summary>Permission key to its name in each language (a role's permissions are printed by
    /// name, as the role's screen shows them); empty where the texts are read for their field names only.</summary>
    private readonly FrozenDictionary<string, (string En, string Ar)> _permissions = FrozenDictionary<string, (string, string)>.Empty;

    /// <summary>The texts with the names of the product's permissions (the catalogue and its words).</summary>
    public AuditTexts(ModuleCatalog catalog, StringCatalog strings) : this()
    {
        _permissions = catalog.Permissions
            .ToDictionary(p => p.Key, p => (strings.Get(p.LabelKey, Languages.English), strings.Get(p.LabelKey, Languages.Arabic)), StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);
    }

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
    /// as "(empty)", a redacted secret as "(hidden)", a moment as the screens write it (the
    /// workspace's time zone, the language's form), a permission by its name; a record's id by the
    /// record's name when <paramref name="context"/> knows it.</summary>
    public string Value(JsonElement? value, string language, AuditValueContext? context = null)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Get("audit.value.empty", language);
        }
        switch (v.ValueKind)
        {
            case JsonValueKind.True:
                return Get("audit.value.yes", language);
            case JsonValueKind.False:
                return Get("audit.value.no", language);
            case JsonValueKind.Number:
                return v.GetRawText();
            case JsonValueKind.String:
                var text = v.GetString() ?? "";
                if (text == AuditValues.Redacted)
                {
                    return Get("audit.value.hidden", language);
                }
                if (text.Length == 36 && context?.Names is { Count: > 0 } labels && Guid.TryParse(text, out var id) && labels.TryGetValue(id, out var name))
                {
                    return name.For(language);
                }
                if (text.Length >= 17 && text[4] == '-' && text[10] == 'T' && Instant().IsMatch(text) &&
                    DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment))
                {
                    return Moment(moment, language, context);
                }
                if (_permissions.TryGetValue(text, out var permission))
                {
                    return language == Languages.Arabic ? permission.Ar : permission.En;
                }
                return text.Length == 0 ? Get("audit.value.empty", language) : text;
            case JsonValueKind.Array:
                var items = v.EnumerateArray().Select(i => Value(i, language, context)).ToList();
                return items.Count == 0 ? Get("audit.value.empty", language) : string.Join(", ", items);
            default:
                return v.GetRawText();
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Instant();

    private const string ArabicDigits = "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669";
    private static readonly System.Collections.Immutable.ImmutableArray<string> Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>A moment as the screens write it (web/src/kernel/format.ts): 4 Oct 2026, 5:31 PM in
    /// English; 04/10/2026، 5:31 م in Arabic, with the Arabic-Indic digits when the document asks.</summary>
    private static string Moment(DateTimeOffset instant, string language, AuditValueContext? context)
    {
        var local = TimeZoneInfo.ConvertTime(instant, context?.TimeZone ?? TimeZoneInfo.Utc);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        if (language != Languages.Arabic)
        {
            return $"{local.Day} {Months[local.Month - 1]} {local.Year:0000}, {hour}:{local.Minute:00} {(local.Hour < 12 ? "AM" : "PM")}";
        }
        var arabic = $"{local.Day:00}\u200F/{local.Month:00}\u200F/{local.Year:0000}\u060C {hour}:{local.Minute:00} {(local.Hour < 12 ? "\u0635" : "\u0645")}";
        return context?.ArabicDigits == true ? string.Concat(arabic.Select(c => c is >= '0' and <= '9' ? ArabicDigits[c - '0'] : c)) : arabic;
    }

    /// <summary>The longest change text the log lists, prints and exports: every old and new value
    /// whole in practice (a spreadsheet cell holds 32,767 characters, so no file could hold more; a
    /// longer text is cut there with "…"). The screen's list shows the start of it and the whole text
    /// as the cell's tooltip; the change panel shows every field.</summary>
    public const int SummaryLength = 32_000;

    /// <summary>The changes of one audit row as one line ("E-mail: from a@x.ae to b@x.ae; Name: …"), in
    /// the language, at most <paramref name="max"/> characters.</summary>
    public string Summary(IReadOnlyList<AuditFieldChange> changes, string action, string language, AuditValueContext? labels, int max = SummaryLength)
    {
        var builder = new StringBuilder();
        var separator = Get("audit.value.separator", language);
        foreach (var change in changes)
        {
            var field = FieldName(change.Field, language);
            var line = action switch
            {
                "insert" => Get("audit.value.set", language, ("field", field), ("new", Value(change.New, language, labels))),
                "delete" => Get("audit.value.set", language, ("field", field), ("new", Value(change.Old, language, labels))),
                _ when change.New is null || change.New.Value.ValueKind == JsonValueKind.Null =>
                    Get("audit.value.cleared", language, ("field", field), ("old", Value(change.Old, language, labels))),
                _ => Get("audit.value.from", language, ("field", field), ("old", Value(change.Old, language, labels)), ("new", Value(change.New, language, labels))),
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
