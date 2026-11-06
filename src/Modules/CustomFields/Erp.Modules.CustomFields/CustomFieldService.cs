using System.Text.Json;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Http;
using Erp.Kernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.CustomFields;

/// <summary>
/// The custom fields as the modules that own records use them (<see cref="ICustomFields"/>): the
/// fields of a kind of record, a record's values for its form, the check and write of the values sent
/// with a record's save (in the record's own transaction) and the values of a page of records.
/// Scoped: the definitions of a kind of record are read once per request.
/// </summary>
internal sealed class CustomFieldService(CustomFieldsDbContext db, ModuleCatalog catalog, IServiceProvider services) : ICustomFields
{
    private readonly Dictionary<string, IReadOnlyList<CustomFieldInfo>> _fields = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<CustomFieldInfo>> FieldsAsync(string recordType, CancellationToken cancellationToken)
    {
        if (_fields.TryGetValue(recordType, out var known))
        {
            return known;
        }
        var rows = await db.Definitions.AsNoTracking().Where(d => d.RecordType == recordType && d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.CreatedAt).ThenBy(d => d.Id).ToListAsync(cancellationToken);
        return _fields[recordType] = rows.Select(Info).ToList();
    }

    internal static CustomFieldInfo Info(FieldDefinition d) => new(d.Id, d.RecordType, d.Key, d.LabelEn, d.LabelAr, d.FieldType, d.Required,
        d.DefaultValue, d.Decimals, d.Options, d.LookupTarget, d.SortOrder, d.Searchable);

    public async Task<IReadOnlyList<CustomFieldEntry>> EntriesAsync(string recordType, Guid? recordId, CancellationToken cancellationToken)
    {
        var fields = await FieldsAsync(recordType, cancellationToken);
        if (fields.Count == 0)
        {
            return [];
        }
        var held = recordId is { } id ? await ReadAsync(fields, [id], cancellationToken) : new Dictionary<(Guid, Guid), FieldValue>();
        var names = await LookupNamesAsync(fields, held.Values, cancellationToken);
        var entries = new List<CustomFieldEntry>();
        foreach (var field in fields)
        {
            JsonElement? value = null;
            string? label = null;
            string? labelAr = null;
            if (recordId is { } record)
            {
                if (held.TryGetValue((record, field.Id), out var row))
                {
                    value = JsonOf(field, row);
                    if (field.Type == CustomFieldTypes.Lookup && row.RefValue is { } target && names.TryGetValue(target, out var name))
                    {
                        (label, labelAr) = name;
                    }
                }
            }
            else if (field.Default is { } text)
            {
                value = JsonOfText(field, text);
            }
            var target2 = field.LookupTarget is { } t ? catalog.CustomFieldRecordTypes.FirstOrDefault(r => r.Table == t) : null;
            entries.Add(new CustomFieldEntry(field.Key, field.Type, field.LabelEn, field.LabelAr, field.Required, field.Decimals, field.Options,
                field.LookupTarget, target2?.LookupEndpoint, target2?.LabelProperty, target2?.LabelPropertyAr, value, label, labelAr, field.Searchable));
        }
        return entries;
    }

    public async Task SaveAsync(string recordType, Guid recordId, bool isNew, IReadOnlyDictionary<string, JsonElement>? values, Validator validator, CancellationToken cancellationToken)
    {
        var fields = await FieldsAsync(recordType, cancellationToken);
        var byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var parsed = new Dictionary<Guid, Parsed>();
        foreach (var (key, json) in values ?? new Dictionary<string, JsonElement>())
        {
            if (!byKey.TryGetValue(key, out var field))
            {
                validator.Add($"customFields.{key}", "customFieldUnknown");
                continue;
            }
            parsed[field.Id] = await ParseAsync(field, json, validator, cancellationToken);
        }
        foreach (var field in fields.Where(f => !parsed.ContainsKey(f.Id)))
        {
            if (isNew && field.Default is { } text)
            {
                var defaulted = await ParseAsync(field, JsonSerializer.SerializeToElement(text), validator, cancellationToken);
                parsed[field.Id] = defaulted;
            }
            else if (isNew && field.Required)
            {
                validator.Add($"customFields.{field.Key}", "customFieldRequired");
            }
        }
        if (!validator.IsValid)
        {
            return;
        }
        var held = await ReadAsync(fields, [recordId], cancellationToken, tracked: true);
        foreach (var (fieldId, value) in parsed)
        {
            held.TryGetValue((recordId, fieldId), out var row);
            if (value.IsEmpty)
            {
                if (row is not null)
                {
                    db.Values.Remove(row);
                }
                continue;
            }
            if (row is null)
            {
                row = new FieldValue { RecordType = recordType, RecordId = recordId, FieldId = fieldId };
                db.Values.Add(row);
            }
            else if (Same(row, value))
            {
                continue;
            }
            row.TextValue = value.Text;
            row.NumberValue = value.Number;
            row.DateValue = value.Date;
            row.BoolValue = value.Bool;
            row.RefValue = value.Ref;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IDictionary<string, object?>>> ValuesAsync(string recordType, IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IDictionary<string, object?>>();
        var fields = await FieldsAsync(recordType, cancellationToken);
        if (fields.Count == 0 || recordIds.Count == 0)
        {
            return result;
        }
        var held = await ReadAsync(fields, recordIds, cancellationToken);
        var names = await LookupNamesAsync(fields, held.Values, cancellationToken);
        // Fields in the workspace's display order, so a row's properties come in the same order every time.
        foreach (var field in fields)
        {
            foreach (var record in recordIds)
            {
                if (!held.TryGetValue((record, field.Id), out var row))
                {
                    continue;
                }
                if (!result.TryGetValue(record, out var map))
                {
                    result[record] = map = new Dictionary<string, object?>(StringComparer.Ordinal);
                }
                map[field.Key] = field.Type switch
                {
                    CustomFieldTypes.Number => FieldRules.NumberText(row.NumberValue!.Value, field.Decimals),
                    CustomFieldTypes.Date => row.DateValue!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    CustomFieldTypes.Boolean => row.BoolValue,
                    CustomFieldTypes.Lookup => row.RefValue,
                    _ => row.TextValue,
                };
                if (field.Type == CustomFieldTypes.Lookup && row.RefValue is { } target && names.TryGetValue(target, out var name))
                {
                    map[field.Key + "Name"] = name.En;
                    map[field.Key + "NameAr"] = name.Ar;
                }
            }
        }
        return result;
    }

    private async Task<Dictionary<(Guid Record, Guid Field), FieldValue>> ReadAsync(IReadOnlyList<CustomFieldInfo> fields, IReadOnlyCollection<Guid> recordIds,
        CancellationToken cancellationToken, bool tracked = false)
    {
        var fieldIds = fields.Select(f => f.Id).ToList();
        var ids = recordIds.ToList();
        var query = tracked ? db.Values.AsQueryable() : db.Values.AsNoTracking();
        var rows = await query.Where(v => ids.Contains(v.RecordId) && fieldIds.Contains(v.FieldId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(v => (v.RecordId, v.FieldId));
    }

    private async Task<IReadOnlyDictionary<Guid, (string En, string Ar)>> LookupNamesAsync(IReadOnlyList<CustomFieldInfo> fields, IEnumerable<FieldValue> rows, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, (string En, string Ar)>();
        var lookups = fields.Where(f => f.Type == CustomFieldTypes.Lookup).ToList();
        if (lookups.Count == 0)
        {
            return names;
        }
        var held = rows.ToList();
        foreach (var group in lookups.GroupBy(f => f.LookupTarget!))
        {
            var fieldIds = group.Select(f => f.Id).ToHashSet();
            var ids = held.Where(r => fieldIds.Contains(r.FieldId) && r.RefValue is not null).Select(r => r.RefValue!.Value).Distinct().ToList();
            var type = catalog.CustomFieldRecordTypes.FirstOrDefault(r => r.Table == group.Key);
            if (ids.Count == 0 || type?.Names is null)
            {
                continue;
            }
            foreach (var (id, name) in await type.Names(services, ids, cancellationToken))
            {
                names[id] = name;
            }
        }
        return names;
    }

    private sealed record Parsed(string? Text = null, decimal? Number = null, DateOnly? Date = null, bool? Bool = null, Guid? Ref = null)
    {
        public bool IsEmpty => Text is null && Number is null && Date is null && Bool is null && Ref is null;
    }

    private static bool Same(FieldValue row, Parsed value) =>
        row.TextValue == value.Text && row.NumberValue == value.Number && row.DateValue == value.Date && row.BoolValue == value.Bool && row.RefValue == value.Ref;

    /// <summary>One sent value as the field's kind of value; problems are added to the validator. A
    /// null or empty text clears the value (which a required field refuses).</summary>
    private async Task<Parsed> ParseAsync(CustomFieldInfo field, JsonElement json, Validator validator, CancellationToken cancellationToken)
    {
        var name = $"customFields.{field.Key}";
        var cleared = json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || (json.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(json.GetString()));
        if (cleared)
        {
            if (field.Required)
            {
                validator.Add(name, "customFieldRequired");
            }
            return new Parsed();
        }
        switch (field.Type)
        {
            case CustomFieldTypes.Text:
                var text = json.ValueKind == JsonValueKind.String ? json.GetString()!.Trim() : null;
                if (text is null)
                {
                    validator.Add(name, "customFieldText");
                }
                else if (text.Length > CustomFieldsDbContext.MaxTextLength)
                {
                    validator.Add(name, "maxLength", CustomFieldsDbContext.MaxTextLength);
                }
                return new Parsed(Text: text);
            case CustomFieldTypes.Number:
                if (!FieldRules.TryNumber(json, field.Decimals, out var number))
                {
                    validator.Add(name, "customFieldNumber", field.Decimals);
                    return new Parsed();
                }
                return new Parsed(Number: number);
            case CustomFieldTypes.Date:
                if (json.ValueKind != JsonValueKind.String || !FieldRules.TryDate(json.GetString(), out var date))
                {
                    validator.Add(name, "customFieldDate");
                    return new Parsed();
                }
                return new Parsed(Date: date);
            case CustomFieldTypes.Boolean:
                bool? flag = json.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
                if (flag is null)
                {
                    validator.Add(name, "customFieldBoolean");
                }
                return new Parsed(Bool: flag);
            case CustomFieldTypes.Select:
                var chosen = json.ValueKind == JsonValueKind.String ? json.GetString()!.Trim() : null;
                if (chosen is null || field.Options.All(o => o.Value != chosen))
                {
                    validator.Add(name, "customFieldOption");
                    return new Parsed();
                }
                return new Parsed(Text: chosen);
            case CustomFieldTypes.Lookup:
                if (json.ValueKind != JsonValueKind.String || !Guid.TryParse(json.GetString(), out var target))
                {
                    validator.Add(name, "customFieldLookup");
                    return new Parsed();
                }
                var kind = catalog.CustomFieldRecordTypes.FirstOrDefault(r => r.Table == field.LookupTarget);
                if (kind is null || !await kind.Exists(services, target, cancellationToken))
                {
                    validator.Add(name, "customFieldLookup");
                    return new Parsed();
                }
                return new Parsed(Ref: target);
            default:
                validator.Add(name, "customFieldUnknown");
                return new Parsed();
        }
    }

    private static JsonElement JsonOf(CustomFieldInfo field, FieldValue row) => field.Type switch
    {
        CustomFieldTypes.Number => JsonSerializer.SerializeToElement(FieldRules.NumberText(row.NumberValue!.Value, field.Decimals)),
        CustomFieldTypes.Date => JsonSerializer.SerializeToElement(row.DateValue!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        CustomFieldTypes.Boolean => JsonSerializer.SerializeToElement(row.BoolValue),
        CustomFieldTypes.Lookup => JsonSerializer.SerializeToElement(row.RefValue),
        _ => JsonSerializer.SerializeToElement(row.TextValue),
    };

    private static JsonElement JsonOfText(CustomFieldInfo field, string text) => field.Type == CustomFieldTypes.Boolean
        ? JsonSerializer.SerializeToElement(text == "true")
        : JsonSerializer.SerializeToElement(text);
}
