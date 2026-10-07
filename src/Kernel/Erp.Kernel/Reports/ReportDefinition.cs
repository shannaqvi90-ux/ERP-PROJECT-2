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
/// <param name="Permission">A permission the caller needs, beyond the report's own, to use this
/// parameter: one that names records of another area (a role in a users report needs the roles'
/// read permission, since the document prints the role's name). Others are not offered it and are
/// refused when they send it.</param>
public sealed record ReportParameter(
    string Key,
    string LabelKey,
    ReportParameterType Type,
    bool Required = false,
    IReadOnlyList<ListChoice>? Choices = null,
    string? Lookup = null,
    string? Permission = null);

/// <summary>A column of a report's table, or a fact of a record document's heading.</summary>
/// <param name="Key">Key of the value in each row the source returns, lower camel case.</param>
/// <param name="LabelKey">Web string key of the column header.</param>
/// <param name="Type">Kind of value (formats it, aligns it, totals it).</param>
/// <param name="Total">Groups and the whole report carry the column's total (number and money columns).</param>
/// <param name="Groupable">The report can be grouped by this column.</param>
/// <param name="Choices">The values of a choice column, with their labels.</param>
/// <param name="Permission">A permission the caller needs, beyond the report's own, to see this
/// column or fact: data of another area (a company's branches in its profile need the branches'
/// read permission). For a caller without it the column is left out of the document (its source
/// is told, through <see cref="ReportRun.Prints"/>, not to read it), the document says what was
/// left out, and the report cannot be grouped by it.</param>
public sealed record ReportColumn(
    string Key,
    string LabelKey,
    ListColumnType Type,
    bool Total = false,
    bool Groupable = false,
    IReadOnlyList<ListChoice>? Choices = null,
    string? Permission = null);

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
/// <param name="Permission">Permission needed to run it: the read permission of the data it shows.
/// Data of another area needs that area's read permission as well, declared on the column, fact or
/// parameter that shows it (<see cref="ReportColumn.Permission"/>, <see cref="ReportParameter.Permission"/>);
/// the G2 gate prints every report as users holding exactly these permissions and fails it when it
/// shows a value that no other endpoint those permissions open shows.</param>
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
        new HashSet<string>(["format", "language", "numerals", "timeZone", "groupBy", "disposition"], StringComparer.Ordinal);

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
            if (parameter.Permission is { } extra && (!Security.PermissionDefinition.IsValidKey(extra) || extra == Permission))
            {
                yield return $"report '{Key}': parameter '{parameter.Key}': permission '{extra}' is not a permission key other than the report's own";
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
                if (column.Permission is { } extra && (!Security.PermissionDefinition.IsValidKey(extra) || extra == Permission))
                {
                    yield return $"report '{Key}': {kind} '{column.Key}': permission '{extra}' is not a permission key other than the report's own";
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

    /// <summary>How many rows matched in all (when <see cref="Truncated"/>; otherwise the row count).</summary>
    public int? MatchCount { get; init; }

    /// <summary>How the chosen value of a reference parameter is printed (the company's code and
    /// name for a company id), by parameter key; an id without one is printed as given.</summary>
    public IReadOnlyDictionary<string, LocalText> ParameterTexts { get; init; } = new Dictionary<string, LocalText>();
}

/// <summary>One run of a report: its definition, the validated parameter values (by key:
/// <see cref="string"/>, <see cref="DateOnly"/>, <see cref="bool"/> or <see cref="Guid"/>; absent
/// when not given), the document language and the most rows the report may hold.</summary>
public sealed record ReportRun(ReportDefinition Definition, IReadOnlyDictionary<string, object> Parameters, string Language, int MaxRows)
{
    public T? Get<T>(string key) where T : struct => Parameters.TryGetValue(key, out var value) && value is T typed ? typed : null;

    public string? Text(string key) => Parameters.TryGetValue(key, out var value) ? value as string : null;

    /// <summary>Columns and facts left out for this caller (it lacks their <see cref="ReportColumn.Permission"/>).</summary>
    public IReadOnlySet<string> Withheld { get; init; } = new HashSet<string>();

    /// <summary>The caller's permissions, for a source that shapes a value by them (a branch
    /// report prints the company's code to every reader of branches, its legal name only to a
    /// reader of companies). Nothing by default.</summary>
    public Func<string, bool> Holds { get; init; } = _ => false;

    /// <summary>The column or fact is printed for this caller: a source need not read it otherwise.</summary>
    public bool Prints(string key) => !Withheld.Contains(key);

    /// <summary>At least one column of the table is printed for this caller (otherwise the source
    /// need not read any row).</summary>
    public bool PrintsRows => Definition.Columns.Any(c => Prints(c.Key));
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
