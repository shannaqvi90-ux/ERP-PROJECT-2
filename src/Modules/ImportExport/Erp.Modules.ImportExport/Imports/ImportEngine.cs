using System.Globalization;
using System.Text;
using System.Text.Json;
using Erp.Kernel.CustomFields;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Localization;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.ImportExport.Files;

namespace Erp.Modules.ImportExport.Imports;

/// <summary>A field a file can fill: a registered one, or one of the workspace's custom fields (<see cref="Custom"/>).</summary>
public sealed record ImportFieldInfo(string Key, string LabelEn, string LabelAr, bool Required, bool Custom, string? Example, IReadOnlyList<string> Aliases);

/// <summary>The rules of matching a file's columns to a kind of record's fields, and of keeping the problems of a run.</summary>
internal static class ImportEngine
{
    public const int BatchSize = 500;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The fields of a kind of record: its own, then the workspace's custom fields.</summary>
    public static async Task<IReadOnlyList<ImportFieldInfo>> FieldsAsync(ImportRecordType type, IServiceProvider services, CancellationToken cancellationToken)
    {
        var strings = (StringCatalog)services.GetService(typeof(StringCatalog))!;
        var fields = type.Fields.Select(f => new ImportFieldInfo(f.Key, strings.Get(f.LabelKey, "en"), strings.Get(f.LabelKey, "ar"), f.Required, false, f.Example, f.Aliases ?? [])).ToList();
        foreach (var custom in await CustomFieldsAsync(type, services, cancellationToken))
        {
            fields.Add(new ImportFieldInfo(ImportCustomValues.Prefix + custom.Key, custom.LabelEn, custom.LabelAr, custom.Required, true, null, []));
        }
        return fields;
    }

    public static async Task<IReadOnlyList<CustomFieldInfo>> CustomFieldsAsync(ImportRecordType type, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (type.CustomFieldRecord is null || services.GetService(typeof(ICustomFields)) is not ICustomFields custom)
        {
            return [];
        }
        return await custom.FieldsAsync(type.CustomFieldRecord, cancellationToken);
    }

    /// <summary>A header or a label compared by its letters and digits only: "E-mail", "e mail" and "EMAIL" are one.</summary>
    public static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in ImportCustomValues.Digits(text ?? "").Normalize(NormalizationForm.FormKC))
        {
            // Arabic diacritics and the tatweel are not part of a word.
            if (char.IsLetterOrDigit(c) && c is not ('ـ') && !(c is >= 'ً' and <= 'ٟ'))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }
        return builder.ToString();
    }

    /// <summary>The field of each column: the one a past import chose for the same header ("memory"), else the one whose
    /// key, label or alias is the header ("name"), each field once (the first column wins). Null: left out.</summary>
    public static (string?[] Fields, string?[] Source) Suggest(IReadOnlyList<string> headers, IReadOnlyList<ImportFieldInfo> fields, IReadOnlyDictionary<string, string> remembered)
    {
        var chosen = new string?[headers.Count];
        var source = new string?[headers.Count];
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var known = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            foreach (var name in new[] { field.Key, field.LabelEn, field.LabelAr }.Concat(field.Aliases).Select(Normalize).Where(n => n.Length > 0))
            {
                names.TryAdd(name, field.Key);
            }
        }
        for (var i = 0; i < headers.Count; i++)
        {
            var key = Normalize(headers[i]);
            if (key.Length == 0)
            {
                continue;
            }
            if (remembered.TryGetValue(key, out var field) && known.ContainsKey(field) && taken.Add(field))
            {
                chosen[i] = field;
                source[i] = "memory";
            }
        }
        for (var i = 0; i < headers.Count; i++)
        {
            if (chosen[i] is not null)
            {
                continue;
            }
            var key = Normalize(headers[i]);
            if (key.Length > 0 && names.TryGetValue(key, out var field) && taken.Add(field))
            {
                chosen[i] = field;
                source[i] = "name";
            }
        }
        return (chosen, source);
    }

    /// <summary>The fields that must be mapped and are not (a list of English and Arabic names), or empty.</summary>
    public static IReadOnlyList<ImportFieldInfo> MissingRequired(ImportRecordType type, IReadOnlyList<ImportFieldInfo> fields, IEnumerable<string?> mapping)
    {
        var mapped = mapping.Where(m => m is not null).Select(m => m!).ToHashSet(StringComparer.Ordinal);
        var missing = fields.Where(f => f.Required && !mapped.Contains(f.Key)).ToList();
        if (type.OneOfRequired is { Count: > 0 } any && !any.Any(mapped.Contains))
        {
            missing.AddRange(fields.Where(f => any.Contains(f.Key)));
        }
        return missing;
    }

    /// <summary>The rows as the importer reads them: only the cells of mapped columns, blank cells left out.</summary>
    public static IEnumerable<ImportRow> Rows(Table table, IReadOnlyList<string?> mapping)
    {
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            var cells = table.Rows[r];
            for (var c = 0; c < mapping.Count && c < cells.Length; c++)
            {
                if (mapping[c] is { } field && !string.IsNullOrWhiteSpace(cells[c]))
                {
                    values[field] = cells[c];
                }
            }
            yield return new ImportRow(r + 2, values);
        }
    }

    /// <summary>The error file: the rows that had problems, as the file had them, then the row number and the problems in English and in Arabic.</summary>
    public static byte[] ErrorFile(Table table, IReadOnlyDictionary<int, List<ImportIssue>> issuesByRow, StringCatalog strings)
    {
        var builder = new StringBuilder();
        void Write(IEnumerable<string> cells) => builder.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");
        Write(table.Headers.Concat([strings.Get("import.errors.row", "en"), strings.Get("import.errors.problemsEn", "en"), strings.Get("import.errors.problemsAr", "ar")]));
        foreach (var (row, issues) in issuesByRow.OrderBy(p => p.Key))
        {
            var cells = row - 2 >= 0 && row - 2 < table.Rows.Count ? table.Rows[row - 2] : [];
            Write(table.Headers.Select((_, i) => i < cells.Length ? cells[i] : "")
                .Concat([row.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", issues.Select(i => $"{i.Field}: {i.MessageEn}")),
                    string.Join(" | ", issues.Select(i => $"{i.Field}: {i.MessageAr}"))]));
        }
        return [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    /// <summary>A cell in CSV, quoted when needed; a cell a spreadsheet would run as a formula is made plain text first.</summary>
    // A number or a phone number typed with a sign (+971 50 123 4567, -12.5): text a spreadsheet shows as it is.
    private static readonly System.Text.RegularExpressions.Regex PlainNumber = new("^[+-]?[0-9][0-9 ()-]*(\\.[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string Quote(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' && !PlainNumber.IsMatch(text))
        {
            text = "'" + text;
        }
        return text.IndexOfAny([',', '"', '\r', '\n', ';']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static string CleanFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }
        var name = raw.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var bad = category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate || rune.Value is '"' or '*' or ':' or '<' or '>' or '?' or '|';
            builder.Append(bad ? "_" : rune.ToString());
        }
        name = builder.ToString().Trim().TrimStart('.').TrimEnd('.', ' ');
        if (name.Length > ImportLimits.MaxFileNameLength)
        {
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 && name.Length - dot <= 6 ? name[dot..] : "";
            name = name[..(ImportLimits.MaxFileNameLength - extension.Length)].TrimEnd() + extension;
        }
        return name;
    }
}

/// <summary>A problem as the session keeps it (the first few, for the screen).</summary>
public sealed record StoredIssue(int Row, string Field, string Code, string En, string Ar);
