using System.Globalization;
using System.Text.Json;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Reports;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Contracts;

namespace Erp.Modules.Reports;

/// <summary>How a document is printed: its language, digits and time zone.</summary>
public sealed record ReportOptions(string Language, string Numerals, TimeZoneInfo TimeZone);

/// <summary>A column of the document being built, with how to read its value from a row.</summary>
internal sealed record ColumnSpec(string Key, string LabelKey, ListColumnType Type, bool Total, IReadOnlyList<ListChoice>? Choices);

/// <summary>
/// Builds a <see cref="ReportDocument"/> from a report's data or a list's rows: picks every label
/// and value in the document's language, groups the rows, totals the totalled columns per group
/// and overall (decimal arithmetic only), and stamps who printed it and when. The same document
/// is then shown on screen, printed, rendered as PDF or exported.
/// </summary>
public sealed class ReportEngine(WebStrings strings, TimeProvider time, ICurrentUser caller, IUserDirectory users, ICompanyDirectory companies, ITenantDirectory tenants)
{
    /// <summary>Most rows a document shown, printed or rendered as PDF holds.</summary>
    public const int DocumentRowLimit = 2000;

    /// <summary>Most rows a CSV or XLSX export holds: twice the owner's main-list volume (100,000),
    /// so a whole main list exports. A list beyond it exports its first rows, and the file says so in
    /// its last line (see <see cref="Exports"/>), never silently.</summary>
    public const int ExportRowLimit = 200_000;

    /// <summary>A registered report's document.</summary>
    public async Task<ReportDocument> BuildAsync(ReportDefinition definition, ReportData data, ReportRun run, string? groupBy, ReportOptions options, CancellationToken cancellationToken)
    {
        var f = new ReportFormatter(options.Language, options.Numerals, options.TimeZone);
        var parameters = new List<ReportDocumentFact>();
        foreach (var parameter in definition.Parameters)
        {
            if (!run.Parameters.TryGetValue(parameter.Key, out var value))
            {
                continue;
            }
            var text = value switch
            {
                bool b => strings.Get(b ? "lists.yes" : "lists.no", f.Language),
                DateOnly d => f.Date(d),
                // A record the caller cannot see (another company's, another workspace's, none) is
                // "not found", never its id: the document repeats only what it found.
                Guid => data.ParameterTexts.TryGetValue(parameter.Key, out var label) ? label.For(f.Language) : strings.Get("reports.param.notFound", f.Language),
                string s when parameter.Type == ReportParameterType.Choice =>
                    parameter.Choices?.FirstOrDefault(c => c.Value == s) is { } choice ? strings.Get(choice.LabelKey, f.Language) : s,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
            };
            // Typed text and a chosen value are printed only when the report found something for
            // them: an empty document never repeats a criterion it cannot relate to the caller's
            // own records, so it reads the same for a value another workspace holds as for one
            // nobody holds.
            if (parameter.Type is ReportParameterType.Text or ReportParameterType.Choice && data.Rows.Count == 0 && data.Facts.Count == 0)
            {
                continue;
            }
            parameters.Add(new ReportDocumentFact(strings.Get(parameter.LabelKey, f.Language), text, Raw(value)));
        }
        if (groupBy is not null && definition.Column(groupBy) is { } groupColumn)
        {
            parameters.Add(new ReportDocumentFact(strings.Get("reports.param.groupBy", f.Language), strings.Get(groupColumn.LabelKey, f.Language), groupBy));
        }
        var facts = (definition.Facts ?? [])
            .Where(fact => run.Prints(fact.Key))
            .Where(fact => data.Facts.TryGetValue(fact.Key, out var v) && v is not null)
            .Select(fact =>
            {
                var cell = Cell(data.Facts[fact.Key], Spec(fact), f);
                return new ReportDocumentFact(strings.Get(fact.LabelKey, f.Language), cell.Text, cell.Value);
            })
            .ToList();
        // Columns and facts of another area that the caller cannot read are left out whatever the
        // source returned, and the document says what was left out.
        var columns = definition.Columns.Where(c => run.Prints(c.Key)).Select(Spec).ToList();
        var rows = columns.Count == 0 ? [] : data.Rows;
        var withheld = definition.Columns.Concat(definition.Facts ?? []).Where(c => !run.Prints(c.Key))
            .Select(c => strings.Get(c.LabelKey, f.Language)).Distinct(StringComparer.Ordinal).ToList();
        var notes = withheld.Count == 0
            ? []
            : new[] { strings.Get("reports.text.withheld", f.Language, new Dictionary<string, object?> { ["columns"] = string.Join(f.Arabic ? "\u060C " : ", ", withheld) }) };
        if (groupBy is not null && !run.Prints(groupBy))
        {
            groupBy = null;
        }
        var document = await ComposeAsync(definition.Key, strings.Get(definition.LabelKey, f.Language), data.Subject?.For(f.Language), parameters, facts,
            columns, rows, groupBy, columns.Count == 0 ? 0 : data.MatchCount ?? data.Rows.Count, columns.Count != 0 && data.Truncated, f, cancellationToken);
        return document with { Notes = notes };
    }

    /// <summary>A list's document: the rows a list query selects, with the list's own labels.</summary>
    /// <param name="names">For a choice column whose values are other records' ids (<see cref="ListColumn.ValuesFrom"/>):
    /// those records' names by id, when the caller may read them; without them the document shows how many there are.</param>
    public async Task<ReportDocument> BuildListAsync(ListDefinition list, IReadOnlyList<JsonElement> rows, int matchCount, ListRequest request,
        IReadOnlyList<ListColumn> columns, string? groupBy, ReportOptions options, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LocalText>>? names = null)
    {
        var f = new ReportFormatter(options.Language, options.Numerals, options.TimeZone);
        var parameters = new List<ReportDocumentFact>();
        // The search and the filter's values are text the caller typed: printed when the list found
        // rows for them, left out of an empty document (it says that nothing matched).
        if (!string.IsNullOrWhiteSpace(request.Search) && rows.Count > 0)
        {
            parameters.Add(new ReportDocumentFact(strings.Get("reports.param.search", f.Language), request.Search.Trim(), request.Search.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(request.Filter) && rows.Count > 0)
        {
            parameters.AddRange(FilterFacts(list, request.Filter, rows, f));
        }
        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            var keys = ListSortKey.Parse(request.Sort, list);
            // In words, not arrows: "Type, descending; Role, ascending" (the embedded fonts have no
            // arrows, and words read the same aloud and in a copied text).
            var text = string.Join(f.Arabic ? "\u061B " : "; ", keys.Select(k => strings.Get(k.Descending ? "reports.sort.descending" : "reports.sort.ascending", f.Language,
                new Dictionary<string, object?> { ["column"] = strings.Get(list.Column(k.Column)!.LabelKey, f.Language) })));
            parameters.Add(new ReportDocumentFact(strings.Get("reports.param.sort", f.Language), text, request.Sort));
        }
        if (groupBy is not null)
        {
            parameters.Add(new ReportDocumentFact(strings.Get("reports.param.groupBy", f.Language), strings.Get(list.Column(groupBy)!.LabelKey, f.Language), groupBy));
        }
        var specs = columns.Select(c => new ColumnSpec(c.Key, c.LabelKey, c.Type, c.Aggregate, c.Choices)).ToList();
        if (groupBy is not null && specs.All(s => s.Key != groupBy))
        {
            var group = list.Column(groupBy)!;
            specs.Add(new ColumnSpec(group.Key, group.LabelKey, group.Type, false, group.Choices));
        }
        var typed = rows.Select(row => ListRow(list, row, specs, f.Language, names)).ToList();
        var shown = specs.Where(s => columns.Any(c => c.Key == s.Key)).ToList();
        return await ComposeAsync(list.Key, strings.Get(list.LabelKey, f.Language), null, parameters, [], shown, typed, groupBy,
            matchCount, matchCount > rows.Count, f, cancellationToken, specs);
    }

    private async Task<ReportDocument> ComposeAsync(string key, string title, string? subject, List<ReportDocumentFact> parameters, List<ReportDocumentFact> facts,
        IReadOnlyList<ColumnSpec> columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string? groupBy, int matchCount, bool truncated,
        ReportFormatter f, CancellationToken cancellationToken, IReadOnlyList<ColumnSpec>? allSpecs = null)
    {
        var language = f.Language;
        var groupSpec = groupBy is null ? null : (allSpecs ?? columns).FirstOrDefault(c => c.Key == groupBy);
        var groups = new List<ReportDocumentGroup>();
        if (groupSpec is null)
        {
            groups.Add(Group(null, rows, columns, f));
        }
        else
        {
            var compare = StringComparer.Create(CultureInfo.GetCultureInfo(language == Languages.Arabic ? "ar-AE" : "en-AE"), ignoreCase: true);
            var buckets = rows
                .GroupBy(r => GroupKey(r.GetValueOrDefault(groupSpec.Key)))
                .Select(g => (Cell: Cell(g.First().GetValueOrDefault(groupSpec.Key), groupSpec, f), Rows: g.ToList()))
                .OrderBy(g => g.Cell.Value is null ? 1 : 0)
                .ThenBy(g => g.Cell.Text, compare)
                .ToList();
            foreach (var (cell, groupRows) in buckets)
            {
                groups.Add(Group(cell.Value is null ? strings.Get("reports.text.noValue", language) : cell.Text, groupRows, columns, f));
            }
        }
        var totals = Totals(rows, columns, f);
        var now = time.GetUtcNow();
        // Whole seconds: the moment is printed to the minute and served (JSON, X-Erp-Printed-At) as is.
        var printedAt = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var printedAtText = f.DateTime(printedAt);
        var printedBy = (await users.GetAsync([caller.UserId], cancellationToken)).GetValueOrDefault(caller.UserId)?.DisplayName ?? "";
        var countText = truncated
            ? strings.Get("reports.rows.truncated", language, new Dictionary<string, object?> { ["shown"] = f.Integer(rows.Count), ["total"] = f.Integer(matchCount) })
            : Plural("reports.rows.count", rows.Count, f);
        return new ReportDocument
        {
            Key = key,
            Title = title,
            Subject = subject,
            Issuer = await IssuerAsync(language, cancellationToken),
            Language = language,
            Direction = language == Languages.Arabic ? "rtl" : "ltr",
            Numerals = f.Numerals,
            Parameters = parameters,
            Facts = facts,
            // Numbers stand at the end of their cells, and so do counts (a column of other records'
            // ids the caller cannot name prints how many there are).
            Columns = columns.Select(c => new ReportDocumentColumn(c.Key, strings.Get(c.LabelKey, language), TypeName(c.Type),
                c.Type is ListColumnType.Number or ListColumnType.Money || Counted(rows, c.Key) ? "end" : "start", c.Total)).ToList(),
            GroupBy = groupSpec?.Key,
            GroupLabel = groupSpec is null ? null : strings.Get(groupSpec.LabelKey, language),
            Groups = groups,
            Totals = totals,
            RowCount = rows.Count,
            MatchCount = matchCount,
            RowCountText = countText,
            Truncated = truncated,
            PrintedAt = printedAt,
            PrintedAtText = printedAtText,
            PrintedBy = printedBy,
            Texts = new ReportDocumentTexts(
                strings.Get("reports.text.total", language),
                strings.Get("reports.text.printed", language, new Dictionary<string, object?> { ["time"] = printedAtText, ["name"] = printedBy }),
                strings.Get("reports.text.page", language),
                strings.Get("reports.text.empty", language),
                strings.Get("reports.text.noValue", language)),
        };
    }

    private ReportDocumentGroup Group(string? label, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<ColumnSpec> columns, ReportFormatter f) =>
        new(label, rows.Count, Plural("reports.group.count", rows.Count, f),
            rows.Select(r => new ReportDocumentRow(columns.Select(c => Cell(r.GetValueOrDefault(c.Key), c, f)).ToList())).ToList(),
            Totals(rows, columns, f));

    /// <summary>A plural message whose number is printed with the document's grouping and digits.</summary>
    private string Plural(string key, int count, ReportFormatter f)
    {
        var text = strings.Get(key, f.Language, new Dictionary<string, object?> { ["count"] = count });
        var raw = count.ToString(CultureInfo.InvariantCulture);
        return text.Contains(raw, StringComparison.Ordinal) ? text.Replace(raw, f.Integer(count), StringComparison.Ordinal) : text;
    }

    /// <summary>Per totalled column the sum of its values (per currency for amounts), null for other columns.</summary>
    private static IReadOnlyList<ReportCell?> Totals(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<ColumnSpec> columns, ReportFormatter f) =>
        columns.Select(c =>
        {
            if (!c.Total)
            {
                return null;
            }
            var sums = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var value in rows.Select(r => r.GetValueOrDefault(c.Key)))
            {
                var (currency, amount) = value switch
                {
                    ReportMoney m => (m.Currency.ToUpperInvariant(), m.Amount),
                    decimal d => ("", d),
                    int i => ("", i),
                    long l => ("", l),
                    _ => ("", 0m),
                };
                if (value is null)
                {
                    continue;
                }
                sums[currency] = sums.GetValueOrDefault(currency) + amount;
            }
            if (sums.Count == 0)
            {
                sums[""] = 0m;
            }
            if (sums.Count == 1 && sums.Keys.Single() is var only)
            {
                var total = sums[only];
                return only.Length == 0
                    ? new ReportCell(total.ToString(CultureInfo.InvariantCulture), c.Type == ListColumnType.Money ? f.Decimal(total, 2) : f.Decimal(total))
                    : new ReportCell(new { amount = total.ToString(CultureInfo.InvariantCulture), currency = only }, f.Money(total, only));
            }
            return new ReportCell(sums.Select(s => new { amount = s.Value.ToString(CultureInfo.InvariantCulture), currency = s.Key }).ToList(),
                string.Join(" \u00B7 ", sums.Select(s => s.Key.Length == 0 ? f.Decimal(s.Value, 2) : f.Money(s.Value, s.Key))));
        }).ToList();

    /// <summary>A value as printed, with its raw form.</summary>
    internal ReportCell Cell(object? value, ColumnSpec column, ReportFormatter f)
    {
        switch (value)
        {
            case null:
                return new ReportCell(null, "");
            case LocalText text:
                var chosen = text.For(f.Language);
                return new ReportCell(chosen, chosen);
            case ReportMoney money:
                return new ReportCell(new { amount = money.Amount.ToString(CultureInfo.InvariantCulture), currency = money.Currency }, f.Money(money.Amount, money.Currency));
            case decimal d:
                return new ReportCell(d.ToString(CultureInfo.InvariantCulture), column.Type == ListColumnType.Money ? f.Decimal(d, 2) : f.Decimal(d));
            case int or long or short:
                var number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return new ReportCell(number, f.Integer(number));
            case bool b:
                // A flag column may name its two values (a role's type: System or Custom), else Yes or No.
                var named = column.Choices?.FirstOrDefault(c => c.Value == (b ? "true" : "false"));
                return new ReportCell(b, strings.Get(named?.LabelKey ?? (b ? "lists.yes" : "lists.no"), f.Language));
            case DateOnly date:
                return new ReportCell(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), f.Date(date));
            case DateTimeOffset instant:
                // The raw value in the document's time zone, to the second (its offset says which zone).
                var local = TimeZoneInfo.ConvertTime(instant, f.TimeZone);
                local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerSecond));
                return new ReportCell(local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), f.DateTime(instant));
            case IReadOnlyList<string> many:
                return new ReportCell(many, f.Integer(many.Count));
            case string s when column.Type == ListColumnType.Choice:
                var choice = column.Choices?.FirstOrDefault(c => c.Value == s);
                return new ReportCell(s, choice is null ? s : strings.Get(choice.LabelKey, f.Language));
            default:
                var plain = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return new ReportCell(plain, plain);
        }
    }

    private static object? Raw(object? value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        _ => value,
    };

    private static string GroupKey(object? value) => value switch
    {
        null => "\0",
        LocalText t => "t:" + t.En + "\u001f" + t.Ar,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static ColumnSpec Spec(ReportColumn column) => new(column.Key, column.LabelKey, column.Type, column.Total, column.Choices);

    private static bool Counted(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string key) =>
        rows.Any(r => r.GetValueOrDefault(key) is IReadOnlyList<string>) && rows.All(r => r.GetValueOrDefault(key) is null or IReadOnlyList<string>);

    private static string TypeName(ListColumnType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    /// <summary>The typed values of a list row (as the list's endpoint returns it) for the columns.</summary>
    private static Dictionary<string, object?> ListRow(ListDefinition list, JsonElement row, IReadOnlyList<ColumnSpec> columns, string language,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LocalText>>? names)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var spec in columns)
        {
            var column = list.Column(spec.Key)!;
            if (!row.TryGetProperty(column.Key, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                values[spec.Key] = null;
                continue;
            }
            if (language == Languages.Arabic && column.ArabicField is { } arabic && row.TryGetProperty(arabic, out var ar)
                && ar.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ar.GetString()))
            {
                element = ar;
            }
            values[spec.Key] = column.Type switch
            {
                ListColumnType.Number => element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture),
                // An amount with its currency (the list's currency column), so totals are per currency.
                ListColumnType.Money when column.CurrencyField is { } currencyField && row.TryGetProperty(currencyField, out var currency) &&
                                           currency.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(currency.GetString()) =>
                    new ReportMoney(element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture), currency.GetString()!),
                ListColumnType.Money => element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture),
                ListColumnType.Date => DateOnly.ParseExact(element.GetString()![..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ListColumnType.DateTime => DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ListColumnType.Boolean => element.ValueKind == JsonValueKind.True,
                // Other records' ids (a user's roles): their names, in the document's language and
                // alphabetical order, when the caller may read them; otherwise the ids (printed as a count).
                ListColumnType.Choice when element.ValueKind == JsonValueKind.Array && names?.GetValueOrDefault(column.Key) is { } known =>
                    string.Join(language == Languages.Arabic ? "\u060C " : ", ", element.EnumerateArray()
                        .Select(e => known.GetValueOrDefault(e.ToString())?.For(language)).OfType<string>()
                        .Order(StringComparer.Create(CultureInfo.GetCultureInfo(language == Languages.Arabic ? "ar-AE" : "en-AE"), ignoreCase: true))),
                ListColumnType.Choice when element.ValueKind == JsonValueKind.Array => element.EnumerateArray().Select(e => e.ToString()).ToList(),
                ListColumnType.Reference when column.LabelField is { } labelField && row.TryGetProperty(labelField, out var label) && label.ValueKind == JsonValueKind.String => label.GetString(),
                _ => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
            };
        }
        return values;
    }

    /// <summary>A filter as printed facts: one per condition of a plain "and" filter ("Status: is
    /// Yes"); any other shape as the filter's text.</summary>
    private IEnumerable<ReportDocumentFact> FilterFacts(ListDefinition list, string filter, IReadOnlyList<JsonElement> rows, ReportFormatter f)
    {
        var node = ListFilter.Parse(filter);
        var conditions = node switch
        {
            FilterCondition c => [c],
            FilterAll all when all.Items.All(i => i is FilterCondition) => all.Items.Cast<FilterCondition>().ToList(),
            _ => null,
        };
        if (conditions is null)
        {
            yield return new ReportDocumentFact(strings.Get("reports.param.filter", f.Language), ListFilter.Format(node), filter);
            yield break;
        }
        foreach (var condition in conditions)
        {
            var column = list.Column(condition.Column)!;
            var spec = new ColumnSpec(column.Key, column.LabelKey, column.Type, false, column.Choices);
            var op = condition.Operator switch
            {
                FilterOperator.StartsWith => "startsWith",
                FilterOperator.EndsWith => "endsWith",
                FilterOperator.IsNull => "isNull",
                FilterOperator.IsNotNull => "isNotNull",
                _ => condition.Operator.ToString().ToLowerInvariant(),
            };
            var shown = condition.Values.Select(v => v.Kind switch
            {
                FilterValueKind.Boolean => Cell(v.Text == "true", spec, f).Text,
                FilterValueKind.Null => "",
                _ when column.Type == ListColumnType.Date && ListFilter.TryDate(v.Text!, out var date) => f.Date(date),
                _ when column.Type == ListColumnType.DateTime && ListFilter.TryDateTime(v.Text!, out var instant) => f.DateTime(instant),
                // A reference prints the referenced record's name as the printed rows show it,
                // never the id; an id none of the rows carries is "not found".
                _ when column.Type == ListColumnType.Reference => ReferenceLabel(column, v.Text, rows) ?? strings.Get("reports.param.notFound", f.Language),
                _ => Cell(v.Text, spec, f).Text,
            }).Where(t => t.Length > 0);
            var text = $"{strings.Get($"lists.op.{op}", f.Language)} {string.Join(f.Arabic ? "\u060C " : ", ", shown)}".Trim();
            yield return new ReportDocumentFact(strings.Get(column.LabelKey, f.Language), text, ListFilter.Format(condition));
        }
    }

    private static string? ReferenceLabel(ListColumn column, string? id, IReadOnlyList<JsonElement> rows)
    {
        if (id is null || column.LabelField is not { } labelField)
        {
            return null;
        }
        foreach (var row in rows)
        {
            if (row.TryGetProperty(column.Key, out var value) && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), id, StringComparison.OrdinalIgnoreCase)
                && row.TryGetProperty(labelField, out var label) && label.ValueKind == JsonValueKind.String)
            {
                return label.GetString();
            }
        }
        return null;
    }

    /// <summary>The letterhead: the working company's legal name to a caller who may see companies
    /// (their records or the workplace switcher), otherwise the workspace's name (every signed-in
    /// user's session shows it).</summary>
    private async Task<string> IssuerAsync(string language, CancellationToken cancellationToken)
    {
        if ((caller.Has(TenancyPermissions.CompaniesRead) || caller.Has(TenancyPermissions.WorkplaceRead)) &&
            await companies.GetWorkingAsync(cancellationToken) is { } company)
        {
            return new LocalText(company.LegalNameEn, company.LegalNameAr).For(language);
        }
        var tenant = await tenants.GetCurrentAsync(cancellationToken);
        return tenant is null ? "" : new LocalText(tenant.NameEn, tenant.NameAr).For(language);
    }
}
