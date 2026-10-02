using System.Text.RegularExpressions;

namespace Erp.Kernel.Lists;

/// <summary>Kind of value a list column shows; the list framework (p05) picks the cell format,
/// filter editor and alignment from it.</summary>
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

/// <param name="Key">JSON property of a row in the list endpoint's page (camelCase).</param>
/// <param name="LabelKey">Web string key of the column header (module's i18n files).</param>
public sealed record ListColumn(string Key, string LabelKey, ListColumnType Type, bool Sortable = false, bool Filterable = false);

/// <summary>
/// A searchable list a module contributes from its own folder (for example the users list). The
/// list framework, saved views and the import/export pieces read these registrations; the platform
/// checks at start-up that the endpoint exists as a GET, declares the same permission, and that
/// search fields and the default sort name real columns.
/// </summary>
/// <param name="Key">Unique key starting with the module name, for example <c>identity.users</c>.</param>
/// <param name="LabelKey">Web string key of the list's title.</param>
/// <param name="Permission">Permission needed to read the list (the endpoint's permission).</param>
/// <param name="Endpoint">GET route that returns pages of rows, for example <c>/api/identity/users</c>.</param>
/// <param name="Columns">Columns in default display order.</param>
/// <param name="SearchFields">Column keys the free-text search parameter matches.</param>
/// <param name="SearchParameter">Query parameter carrying the free-text search.</param>
/// <param name="DefaultSort">Column key of the default order; a leading '-' means descending.</param>
public sealed partial record ListDefinition(
    string Key,
    string LabelKey,
    string Permission,
    string Endpoint,
    IReadOnlyList<ListColumn> Columns,
    IReadOnlyList<string> SearchFields,
    string SearchParameter = "search",
    string? DefaultSort = null)
{
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
        foreach (var duplicate in Columns.GroupBy(c => c.Key).Where(g => g.Count() > 1))
        {
            yield return $"list '{Key}': column '{duplicate.Key}' is defined twice";
        }
        foreach (var field in SearchFields.Where(f => Columns.All(c => c.Key != f)))
        {
            yield return $"list '{Key}': search field '{field}' is not a column";
        }
        if (DefaultSort is { } sort && Columns.All(c => c.Key != sort.TrimStart('-') || !c.Sortable))
        {
            yield return $"list '{Key}': default sort '{sort}' is not a sortable column";
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*\\.[a-z][a-zA-Z0-9]*$")]
    private static partial Regex KeyRegex();
}
