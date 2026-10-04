using System.Text.RegularExpressions;
using Erp.Kernel.Lists;

namespace Erp.Kernel.Reports;

/// <summary>Kind of value a report parameter takes; the report screen picks its editor from it.</summary>
public enum ReportParameterType
{
    /// <summary>Free text (at most 200 characters).</summary>
    Text,

    /// <summary>A calendar date, <c>yyyy-mm-dd</c>.</summary>
    Date,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>One of the parameter's <see cref="ReportParameter.Choices"/>.</summary>
    Choice,

    /// <summary>The id of a record of the registered list named by <see cref="ReportParameter.Lookup"/>.</summary>
    Reference,
}

/// <summary>One input of a report (a filter, an as-of date, the record a document prints).</summary>
/// <param name="Key">Query parameter name, lower camel case.</param>
/// <param name="LabelKey">Web string key of the parameter's label (the module's i18n files; the
/// printed document lists the parameters used with these labels).</param>
/// <param name="Type">Kind of value.</param>
/// <param name="Required">The report does not run without it.</param>
/// <param name="Choices">The allowed values of a choice parameter, with their labels.</param>
/// <param name="Lookup">For a reference parameter: the key of the registered list the value is
/// chosen from (the screen searches that list; its label column names the chosen record).</param>
public sealed record ReportParameter(
    string Key,
    string LabelKey,
    ReportParameterType Type,
    bool Required = false,
    IReadOnlyList<ListChoice>? Choices = null,
    string? Lookup = null);

/// <summary>A column of a report's table, or a fact of a record document's heading.</summary>
/// <param name="Key">Key of the value in each row the source returns, lower camel case.</param>
/// <param name="LabelKey">Web string key of the column header.</param>
/// <param name="Type">Kind of value (formats it, aligns it, totals it).</param>
/// <param name="Total">Groups and the whole report carry the column's total (number and money columns).</param>
/// <param name="Groupable">The report can be grouped by this column.</param>
/// <param name="Choices">The values of a choice column, with their labels.</param>
public sealed record ReportColumn(
    string Key,
    string LabelKey,
    ListColumnType Type,
    bool Total = false,
    bool Groupable = false,
    IReadOnlyList<ListChoice>? Choices = null);

/// <summary>
/// A report a module contributes from its own folder: its parameters, the columns of its table
/// (with totals and groupings), and for a record document the facts printed in its heading. The
/// module's <see cref="IReportSource"/> produces the rows; the reports module validates the
/// parameters, groups, totals, formats and renders the result on screen, in print, as PDF in
/// English or Arabic, and as CSV or XLSX, through <c>/api/reports/run/{key}</c>, guarded by
/// <see cref="Permission"/>.
/// </summary>
/// <param name="Key">Unique key starting with the module name, for example <c>tenancy.branchDirectory</c>.</param>
/// <param name="LabelKey">Web string key of the report's title.</param>
/// <param name="Permission">Permission needed to run it: the read permission of the data it shows.</param>
/// <param name="Parameters">Inputs, in the order the screen shows them.</param>
/// <param name="Columns">Columns of the report's table, in order.</param>
/// <param name="DescriptionKey">Web string key of a one-line description for the report catalogue.</param>
/// <param name="DefaultGroupBy">Groupable column the report is grouped by unless the caller chooses.</param>
/// <param name="Facts">For a record document: the facts its heading prints (label and value), from
/// the source's <see cref="ReportData.Facts"/>.</param>
public sealed partial record ReportDefinition(
    string Key,
    string LabelKey,
    string Permission,
    IReadOnlyList<ReportParameter> Parameters,
    IReadOnlyList<ReportColumn> Columns,
    string? DescriptionKey = null,
    string? DefaultGroupBy = null,
    IReadOnlyList<ReportColumn>? Facts = null)
{
    /// <summary>Parameter names the reports module itself reads on every report.</summary>
    public static readonly IReadOnlySet<string> ReservedParameters =
        new HashSet<string>(["format", "language", "numerals", "timeZone", "groupBy"], StringComparer.Ordinal);

    public ReportColumn? Column(string key) => Columns.FirstOrDefault(c => c.Key == key);

    /// <summary>Problems with the definition itself (lookups are checked once every list is registered).</summary>
    public IEnumerable<string> Problems(string module)
    {
        if (!KeyRegex().IsMatch(Key) || !Key.StartsWith(module + ".", StringComparison.Ordinal))
        {
            yield return $"report '{Key}': key must look like '{module}.name'";
        }
        if (!Security.PermissionDefinition.IsValidKey(Permission))
        {
            yield return $"report '{Key}': permission '{Permission}' is not a permission key";
        }
        if (Columns.Count == 0)
        {
            yield return $"report '{Key}': has no columns";
        }
        foreach (var duplicate in Parameters.GroupBy(p => p.Key).Where(g => g.Count() > 1))
        {
            yield return $"report '{Key}': parameter '{duplicate.Key}' is defined twice";
        }
        foreach (var parameter in Parameters)
        {
            if (!ListDefinition.ColumnKeyRegex().IsMatch(parameter.Key) || ReservedParameters.Contains(parameter.Key))
            {
                yield return $"report '{Key}': parameter key '{parameter.Key}' must be lower camel case and not one of {string.Join(", ", ReservedParameters)}";
            }
            if (parameter.Type == ReportParameterType.Choice && parameter.Choices is not { Count: > 0 })
            {
                yield return $"report '{Key}': choice parameter '{parameter.Key}' lists no choices";
            }
            if (parameter.Type != ReportParameterType.Choice && parameter.Choices is { Count: > 0 })
            {
                yield return $"report '{Key}': parameter '{parameter.Key}' has choices but is not a choice parameter";
            }
            if ((parameter.Type == ReportParameterType.Reference) != (parameter.Lookup is not null))
            {
                yield return $"report '{Key}': parameter '{parameter.Key}': a reference parameter, and only one, names the list it looks up";
            }
        }
        foreach (var (kind, columns) in new[] { ("column", Columns), ("fact", Facts ?? []) })
        {
            foreach (var duplicate in columns.GroupBy(c => c.Key).Where(g => g.Count() > 1))
            {
                yield return $"report '{Key}': {kind} '{duplicate.Key}' is defined twice";
            }
            foreach (var column in columns)
            {
                if (!ListDefinition.ColumnKeyRegex().IsMatch(column.Key))
                {
                    yield return $"report '{Key}': {kind} key '{column.Key}' must be lower camel case";
                }
                if (column.Total && column.Type is not (ListColumnType.Number or ListColumnType.Money))
                {
                    yield return $"report '{Key}': {kind} '{column.Key}' totals only number and money values";
                }
                if (column.Groupable && column.Type is ListColumnType.Money or ListColumnType.Number or ListColumnType.DateTime)
                {
                    yield return $"report '{Key}': {kind} '{column.Key}' of type {column.Type} cannot be grouped";
                }
                if (column.Type == ListColumnType.Choice && column.Choices is not { Count: > 0 })
                {
                    yield return $"report '{Key}': choice {kind} '{column.Key}' lists no choices";
                }
            }
        }
        if (DefaultGroupBy is { } group && Column(group) is not { Groupable: true })
        {
            yield return $"report '{Key}': default grouping '{group}' is not a groupable column";
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*\\.[a-z][a-zA-Z0-9]*$")]
    private static partial Regex KeyRegex();
}

/// <summary>Text a record holds in both languages (a legal name in English and Arabic): a report
/// prints the one of the document's language, the other when that one is empty.</summary>
public sealed record LocalText(string? En, string? Ar)
{
    public string For(string language) =>
        language == "ar" ? (string.IsNullOrWhiteSpace(Ar) ? En ?? "" : Ar) : (string.IsNullOrWhiteSpace(En) ? Ar ?? "" : En);
}

/// <summary>An amount with its currency (CLAUDE.md rule 2: decimal, never floating point).</summary>
public sealed record ReportMoney(decimal Amount, string Currency);

/// <summary>What a report's source produced. Values are <see cref="string"/>, <see cref="LocalText"/>,
/// integers, <see cref="decimal"/>, <see cref="ReportMoney"/>, <see cref="bool"/>, <see cref="DateOnly"/>,
/// <see cref="DateTimeOffset"/> or null; a choice column holds the choice's value.</summary>
public sealed class ReportData
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

    /// <summary>A record document's heading facts, by fact key.</summary>
    public IReadOnlyDictionary<string, object?> Facts { get; init; } = new Dictionary<string, object?>();

    /// <summary>What the document is about (a company's code and name), printed under its title.</summary>
    public LocalText? Subject { get; init; }

    /// <summary>The source stopped at the row limit: more rows match than the report holds.</summary>
    public bool Truncated { get; init; }
}

/// <summary>One run of a report: its definition, the validated parameter values (by key:
/// <see cref="string"/>, <see cref="DateOnly"/>, <see cref="bool"/> or <see cref="Guid"/>; absent
/// when not given), the document language and the most rows the report may hold.</summary>
public sealed record ReportRun(ReportDefinition Definition, IReadOnlyDictionary<string, object> Parameters, string Language, int MaxRows)
{
    public T? Get<T>(string key) where T : struct => Parameters.TryGetValue(key, out var value) && value is T typed ? typed : null;

    public string? Text(string key) => Parameters.TryGetValue(key, out var value) ? value as string : null;
}

/// <summary>Produces a report's rows (and a document's facts) inside the caller's unit of work, so
/// row-level security and the company scope apply. Registered with
/// <c>ModuleBuilder.Report&lt;TSource&gt;(definition)</c> and resolved per request.</summary>
public interface IReportSource
{
    /// <summary>The report's data, or null when the record a document prints does not exist for
    /// the caller (the report answers 404).</summary>
    Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken);
}

/// <summary>A report a module registered, with its source type.</summary>
public sealed record ReportRegistration(ReportDefinition Definition, Type SourceType, string Module);
