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
    public string Value(JsonElement? value, string language, IReadOnlyDictionary<Guid, AuditRecordName>? labels = null)
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

    /// <summary>The changes of one audit row as one line ("E-mail: a@x.ae → b@x.ae; Name: …"), in
    /// the language, at most <paramref name="max"/> characters.</summary>
    public string Summary(IReadOnlyList<AuditFieldChange> changes, string action, string language, IReadOnlyDictionary<Guid, AuditRecordName>? labels, int max = 1000)
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
