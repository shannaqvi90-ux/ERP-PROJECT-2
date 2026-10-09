using System.Text.RegularExpressions;

namespace Erp.Kernel.Lists;

/// <summary>Kind of value a list column shows; the list framework picks the cell format,
/// filter editor, operators and alignment from it.</summary>
public enum ListColumnType
{
    Text,
    Number,
    Money,
    Date,
    DateTime,
    Boolean,
    Choice,
    Reference,
}

/// <summary>One allowed value of a <see cref="ListColumnType.Choice"/> column.</summary>
/// <param name="Value">The value as stored and sent in filters.</param>
/// <param name="LabelKey">Web string key of the value's label.</param>
public sealed record ListChoice(string Value, string LabelKey);

/// <param name="Key">JSON property of a row in the list endpoint's page (camelCase).</param>
/// <param name="LabelKey">Web string key of the column header (module's i18n files).</param>
/// <param name="Sortable">The list can be ordered by this column (<c>sort=key</c> or <c>sort=-key</c>).</param>
/// <param name="Filterable">Filters may name this column (<c>filter=key eq 'value'</c>).</param>
/// <param name="Groupable">The list can be grouped by this column with counts (<c>groupBy=key</c>).</param>
/// <param name="Aggregate">Groups carry the column's total (number and money columns).</param>
/// <param name="Hidden">Not shown until the user adds it with the column chooser.</param>
/// <param name="Choices">The values of a choice column, with their labels. A flag (boolean) column
/// may name its two values instead, as the choices "true" and "false" (a role's type: System or
/// Custom); screens and documents then show those labels in place of Yes and No.</param>
/// <param name="LabelField">For a reference column: the row property that names the referenced
/// record (a branch row's <c>companyCode</c>), printed in reports in place of the id.</param>
/// <param name="ArabicField">A row property holding the value in Arabic script (a user's
/// <c>displayNameAr</c>): Arabic screens and documents show it when the row has one.</param>
/// <param name="ValuesFrom">For a choice column whose values are ids of another list's records
/// (a user's role ids): that list's key. Printed documents and exports show those records' names
/// in place of the ids to a caller who may read that list, and how many there are to others.</param>
/// <param name="CurrencyField">For a money column: the key of the column (of this list) holding the
/// ISO 4217 code of each row's amount. Every money column names one (CLAUDE.md rule 2: an amount
/// never travels without its currency), and group totals of a money column are given per currency,
/// never added across currencies.</param>
/// <param name="TrueLabelKey">For a boolean column: web string key of what a true value is called in
/// cells, groups, filter chips, prints and exports (a status column's "Active"); null: "Yes".</param>
/// <param name="FalseLabelKey">For a boolean column: what a false value is called ("Inactive"); null: "No".</param>
public sealed record ListColumn(
    string Key,
    string LabelKey,
    ListColumnType Type,
    bool Sortable = false,
    bool Filterable = false,
    bool Groupable = false,
    bool Aggregate = false,
    bool Hidden = false,
    IReadOnlyList<ListChoice>? Choices = null,
    string? LabelField = null,
    string? ArabicField = null,
    string? ValuesFrom = null,
    string? CurrencyField = null,
    string? TrueLabelKey = null,
    string? FalseLabelKey = null);

/// <summary>A view every user of the list gets (for example "Active users"), defined in code with a
/// translated label, beside the views users save themselves.</summary>
/// <param name="Key">Unique within the list, lower camel case.</param>
/// <param name="LabelKey">Web string key of the view's name.</param>
/// <param name="Filter">Filter expression in the list query language.</param>
/// <param name="Sort">Sort, for example <c>-createdAt</c>; null keeps the list's default.</param>
/// <param name="GroupBy">Groupable column key, or null.</param>
public sealed record ListPreset(string Key, string LabelKey, string? Filter = null, string? Sort = null, string? GroupBy = null);

/// <summary>
/// A searchable list a module contributes from its own folder (for example the users list). The
/// list framework, saved views and the import/export pieces read these registrations; the platform
/// checks at start-up that the endpoint exists as a GET, declares the same permission, and that
/// search fields and the default sort name real columns. A list is registered together with its
/// query binding (<see cref="ListBinding{T}"/>), which serves search, filters, sort, keyset paging
/// and grouping for the endpoint.
/// </summary>
/// <param name="Key">Unique key starting with the module name, for example <c>identity.users</c>.</param>
/// <param name="LabelKey">Web string key of the list's title.</param>
/// <param name="Permission">Permission needed to read the list (the endpoint's permission).</param>
/// <param name="Endpoint">GET route that returns pages of rows, for example <c>/api/identity/users</c>.</param>
/// <param name="Columns">Columns in default display order.</param>
/// <param name="SearchFields">Column keys the free-text search parameter matches.</param>
/// <param name="SearchParameter">Query parameter carrying the free-text search.</param>
/// <param name="DefaultSort">Sort applied when the request names none: column keys separated by
/// commas, a leading '-' meaning descending.</param>
/// <param name="Presets">Built-in views every user of the list gets.</param>
/// <param name="ArabicSearchFields">Column keys a search word written in Arabic letters matches
/// instead of <paramref name="SearchFields"/> (for example a name and its Arabic spelling, but not an
/// e-mail address, which never holds Arabic letters). Null: Arabic words match the search fields.</param>
public sealed partial record ListDefinition(
    string Key,
    string LabelKey,
    string Permission,
    string Endpoint,
    IReadOnlyList<ListColumn> Columns,
    IReadOnlyList<string> SearchFields,
    string SearchParameter = "search",
    string? DefaultSort = null,
    IReadOnlyList<ListPreset>? Presets = null,
    IReadOnlyList<string>? ArabicSearchFields = null)
{
    /// <summary>The fields a search word matches: <see cref="ArabicSearchFields"/> for a word
    /// written in Arabic letters when the list names them, else <see cref="SearchFields"/>. Arabic
    /// letters only (<see cref="ListSearch.HasArabicLetter"/>): a number typed in Arabic-Indic digits
    /// ("١٢٣") is not a word in Arabic and still searches every search field.</summary>
    public IReadOnlyList<string> SearchFieldsFor(string word) =>
        ArabicSearchFields is { Count: > 0 } arabic && ListSearch.HasArabicLetter(word) ? arabic : SearchFields;

    /// <summary>Every field any search word can match.</summary>
    public IEnumerable<string> AllSearchFields => SearchFields.Concat(ArabicSearchFields ?? []).Distinct(StringComparer.Ordinal);

    public ListColumn? Column(string key) => Columns.FirstOrDefault(c => c.Key == key);

    /// <summary>Choices that name a flag's two values: exactly "true" and "false".</summary>
    private static bool IsFlagNaming(IReadOnlyList<ListChoice> choices) =>
        choices.Count == 2 && choices.Any(c => c.Value == "true") && choices.Any(c => c.Value == "false");

    /// <summary>Problems with the definition itself (the host adds checks against endpoints).</summary>
    public IEnumerable<string> Problems(string module)
    {
        if (!KeyRegex().IsMatch(Key) || !Key.StartsWith(module + ".", StringComparison.Ordinal))
        {
            yield return $"list '{Key}': key must look like '{module}.name'";
        }
        if (!Endpoint.StartsWith("/api/", StringComparison.Ordinal))
        {
            yield return $"list '{Key}': endpoint must be an /api/ route";
        }
        if (Columns.Count == 0)
        {
            yield return $"list '{Key}': has no columns";
        }
        if (SearchParameter != "search")
        {
            yield return $"list '{Key}': the search parameter must be 'search' (one query contract for every list)";
        }
        foreach (var duplicate in Columns.GroupBy(c => c.Key).Where(g => g.Count() > 1))
        {
            yield return $"list '{Key}': column '{duplicate.Key}' is defined twice";
        }
        foreach (var column in Columns)
        {
            if (!ColumnKeyRegex().IsMatch(column.Key))
            {
                yield return $"list '{Key}': column key '{column.Key}' must be lower camel case";
            }
            if (column.Aggregate && column.Type is not (ListColumnType.Number or ListColumnType.Money))
            {
                yield return $"list '{Key}': column '{column.Key}' totals only number and money columns";
            }
            if (column.Groupable && column.Type is ListColumnType.DateTime or ListColumnType.Money or ListColumnType.Number)
            {
                yield return $"list '{Key}': column '{column.Key}' of type {column.Type} cannot be grouped (group by a date, choice, flag, reference or text)";
            }
            if (column.Type == ListColumnType.Money && column.CurrencyField is null)
            {
                yield return $"list '{Key}': money column '{column.Key}' names no currency column (CurrencyField): an amount is never shown or totalled without its currency";
            }
            if (column.CurrencyField is { } currency)
            {
                if (column.Type != ListColumnType.Money)
                {
                    yield return $"list '{Key}': column '{column.Key}' names a currency column but is not a money column";
                }
                else if (Column(currency) is not { Type: ListColumnType.Text or ListColumnType.Choice })
                {
                    yield return $"list '{Key}': money column '{column.Key}' names currency column '{currency}', which is not a text or choice column of the list";
                }
            }
            if ((column.TrueLabelKey is not null || column.FalseLabelKey is not null) && column.Type != ListColumnType.Boolean)
            {
                yield return $"list '{Key}': column '{column.Key}' names words for true and false but is not a boolean column";
            }
            if ((column.TrueLabelKey is null) != (column.FalseLabelKey is null))
            {
                yield return $"list '{Key}': column '{column.Key}' names a word for only one of true and false (name both, or neither for Yes and No)";
            }
            if (column.Choices is { Count: > 0 } && column.Type != ListColumnType.Choice && !(column.Type == ListColumnType.Boolean && IsFlagNaming(column.Choices)))
            {
                yield return $"list '{Key}': column '{column.Key}' has choices but is not a choice column (a flag column may only name its values 'true' and 'false')";
            }
            if (column.Type == ListColumnType.Choice && (column.Filterable || column.Groupable) && column.Choices is not { Count: > 0 })
            {
                yield return $"list '{Key}': choice column '{column.Key}' is filterable or groupable but lists no choices";
            }
            foreach (var duplicate in (column.Choices ?? []).GroupBy(c => c.Value).Where(g => g.Count() > 1))
            {
                yield return $"list '{Key}': column '{column.Key}' lists choice '{duplicate.Key}' twice";
            }
        }
        foreach (var field in AllSearchFields.Where(f => Columns.All(c => c.Key != f)))
        {
            yield return $"list '{Key}': search field '{field}' is not a column";
        }
        foreach (var field in AllSearchFields.Where(f => Column(f) is { } c && c.Type is not (ListColumnType.Text or ListColumnType.Choice)))
        {
            yield return $"list '{Key}': search field '{field}' is not a text column";
        }
        if (DefaultSort is { } sort && Error(() => ListSortKey.Parse(sort, this)) is { } sortError)
        {
            yield return $"list '{Key}': default sort '{sort}' is not a sortable column ({sortError})";
        }
        foreach (var duplicate in (Presets ?? []).GroupBy(p => p.Key).Where(g => g.Count() > 1))
        {
            yield return $"list '{Key}': preset '{duplicate.Key}' is defined twice";
        }
        foreach (var preset in Presets ?? [])
        {
            if (!ColumnKeyRegex().IsMatch(preset.Key))
            {
                yield return $"list '{Key}': preset key '{preset.Key}' must be lower camel case";
            }
            if (preset.Sort is { } presetSort && Error(() => ListSortKey.Parse(presetSort, this)) is { } presetSortError)
            {
                yield return $"list '{Key}': preset '{preset.Key}' sort '{presetSort}' is invalid ({presetSortError})";
            }
            if (preset.GroupBy is { } group && Column(group) is not { Groupable: true })
            {
                yield return $"list '{Key}': preset '{preset.Key}' groups by '{group}', which is not a groupable column";
            }
            if (preset.Filter is { } filter && Error(() => ListFilter.Validate(ListFilter.Parse(filter), this)) is { } filterError)
            {
                yield return $"list '{Key}': preset '{preset.Key}' filter is invalid ({filterError})";
            }
        }
    }

    private static string? Error(Action check)
    {
        try
        {
            check();
            return null;
        }
        catch (ListQueryException e)
        {
            return e.Code;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*\\.[a-z][a-zA-Z0-9]*$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*$")]
    internal static partial Regex ColumnKeyRegex();
}
