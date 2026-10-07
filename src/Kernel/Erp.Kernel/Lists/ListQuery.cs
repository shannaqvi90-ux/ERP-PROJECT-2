using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Kernel.Lists;

/// <summary>
/// The query parameters every registered list endpoint accepts (bind with <c>[AsParameters]</c>).
/// One contract for every list: free-text search, filter expression, sort, keyset or offset
/// paging, grouping.
/// </summary>
public sealed class ListRequest
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;
    public const int MaxSearchLength = 200;
    public const int MaxSearchWords = 8;

    [FromQuery(Name = "search")]
    [Description("Free text. Every word must occur (anywhere, any case) in at least one of the list's search fields; Arabic words also match the letter variants people type for one another (ا أ إ آ, ي ى, ه ة, و ؤ). Without a sort, rows come best match first (when at most 10,000 rows match; the page says ranked): words at the start of a field or of a word in it, the whole search equal to or starting a field, the words in the typed order; then shorter values, then the list's default sort.")]
    public string? Search { get; init; }

    [FromQuery(Name = "filter")]
    [Description("Filter expression, for example: language eq 'ar' and (isActive eq true or lastSignInAt is null). Operators eq ne lt le gt ge contains startswith endswith in, is null, is not null; and, or, not, parentheses. Text in single quotes (double a quote inside), numbers with a dot, dates and times as ISO 8601 text.")]
    public string? Filter { get; init; }

    [FromQuery(Name = "sort")]
    [Description("Sortable column keys separated by commas; a leading '-' sorts descending. Defaults to best match first when there is a search, else the list's default sort.")]
    public string? Sort { get; init; }

    [FromQuery(Name = "after")]
    [Description("Keyset paging: the 'next' value of the previous page. Cannot be combined with skip.")]
    public string? After { get; init; }

    [FromQuery(Name = "skip")]
    [Description("Offset paging: rows to skip. Cannot be combined with after.")]
    public int? Skip { get; init; }

    [FromQuery(Name = "take")]
    [Description("Rows per page, 1 to 200 (default 50).")]
    public int? Take { get; init; }

    [FromQuery(Name = "groupBy")]
    [Description("A groupable column key: the page also carries the groups of the filtered list with their counts and totals.")]
    public string? GroupBy { get; init; }
}

/// <summary>One page of a registered list.</summary>
/// <param name="Items">The rows of this page.</param>
/// <param name="Total">Rows matching the search and filter (all pages).</param>
/// <param name="Next">Pass as <c>after</c> to read the following page; null on the last page.</param>
/// <param name="Groups">With <c>groupBy</c>: every group of the matching rows, ordered by key.</param>
/// <param name="Ranked">The rows are in relevance order, best match first (a search without a
/// sort, matching at most <see cref="ListSearch.MaxRankedRows"/> rows).</param>
public sealed record ListPage<T>(IReadOnlyList<T> Items, int Total, string? Next = null, IReadOnlyList<ListGroup>? Groups = null, bool Ranked = false);

/// <summary>A group of rows sharing one value of the grouped column.</summary>
/// <param name="Key">The value (null for rows without one).</param>
/// <param name="Count">Rows in the group.</param>
/// <param name="Totals">Sum of each total-carrying number column over the group (decimal strings).</param>
/// <param name="MoneyTotals">Sum of each total-carrying money column over the group, one line per
/// currency (amounts in different currencies are never added together; CLAUDE.md rule 2).</param>
public sealed record ListGroup(object? Key, int Count, IReadOnlyDictionary<string, decimal>? Totals = null,
    IReadOnlyDictionary<string, IReadOnlyList<ListMoneyTotal>>? MoneyTotals = null);

/// <summary>The total of a money column over a group's rows in one currency.</summary>
/// <param name="Currency">ISO 4217 code of the amounts (null for rows without one).</param>
/// <param name="Amount">Their sum (a decimal string).</param>
public sealed record ListMoneyTotal(string? Currency, decimal Amount);

/// <summary>One sort key: a sortable column and its direction.</summary>
public sealed record ListSortKey(string Column, bool Descending)
{
    public const int MaxKeys = 4;

    public override string ToString() => (Descending ? "-" : "") + Column;

    public static string Format(IEnumerable<ListSortKey> keys) => string.Join(",", keys);

    /// <summary>Parse <c>-createdAt,displayName</c> against the list's sortable columns.</summary>
    public static IReadOnlyList<ListSortKey> Parse(string text, ListDefinition list)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > MaxKeys)
        {
            throw new ListQueryException("sort", "list.sortTooMany", MaxKeys);
        }
        var keys = new List<ListSortKey>();
        foreach (var part in parts)
        {
            var descending = part.StartsWith('-');
            var name = descending ? part[1..] : part;
            if (!ListDefinition.ColumnKeyRegex().IsMatch(name))
            {
                throw new ListQueryException("sort", "list.sortSyntax");
            }
            if (list.Column(name) is not { } column)
            {
                throw new ListQueryException("sort", "list.unknownColumn");
            }
            if (!column.Sortable)
            {
                throw new ListQueryException("sort", "list.notSortable", column.Key);
            }
            if (keys.Any(k => k.Column == name))
            {
                throw new ListQueryException("sort", "list.sortSyntax");
            }
            keys.Add(new ListSortKey(name, descending));
        }
        return keys;
    }
}

/// <summary>
/// The keyset paging position: the sort it belongs to, the sort keys' values of the last row of a
/// page, and its id. Opaque to clients (base64url JSON); a cursor made for another sort, or one
/// that does not decode, is refused rather than guessed at. It carries no tenant: the tenant
/// always comes from the session, so a forged cursor can only move within the caller's own rows.
/// </summary>
internal static class ListCursor
{
    public const int MaxLength = 2000;

    public static string Encode(string sort, IReadOnlyList<object?> values, Guid id)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("s", sort);
            writer.WriteStartArray("k");
            foreach (var value in values)
            {
                switch (value)
                {
                    case null: writer.WriteNullValue(); break;
                    case string s: writer.WriteStringValue(s); break;
                    case bool b: writer.WriteBooleanValue(b); break;
                    case Guid g: writer.WriteStringValue(g); break;
                    case DateTimeOffset d: writer.WriteStringValue(d.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); break;
                    case DateTime d: writer.WriteStringValue(d.ToString("O", CultureInfo.InvariantCulture)); break;
                    case DateOnly d: writer.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                    case IFormattable f: writer.WriteStringValue(f.ToString(null, CultureInfo.InvariantCulture)); break;
                    default: writer.WriteStringValue(value.ToString()); break;
                }
            }
            writer.WriteEndArray();
            writer.WriteString("i", id);
            writer.WriteEndObject();
        }
        return System.Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The sort a cursor belongs to, or null when it does not decode.</summary>
    public static string? SortOf(string cursor)
    {
        if (cursor.Length > MaxLength)
        {
            return null;
        }
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            using var document = JsonDocument.Parse(System.Convert.FromBase64String(base64));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The cursor's values converted to the sort keys' types, and its id.</summary>
    public static (IReadOnlyList<object?> Values, Guid Id) Decode(string cursor, string sort, IReadOnlyList<Type> types)
    {
        if (cursor.Length > MaxLength)
        {
            throw Invalid();
        }
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            using var document = JsonDocument.Parse(System.Convert.FromBase64String(base64));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("s", out var s) || s.ValueKind != JsonValueKind.String || s.GetString() != sort ||
                !root.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.Array || k.GetArrayLength() != types.Count ||
                !root.TryGetProperty("i", out var i) || i.ValueKind != JsonValueKind.String || !Guid.TryParse(i.GetString(), out var id))
            {
                throw Invalid();
            }
            var values = new List<object?>();
            var index = 0;
            foreach (var element in k.EnumerateArray())
            {
                values.Add(ToValue(element, types[index++]));
            }
            return (values, id);
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or OverflowException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static object? ToValue(JsonElement element, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null ? null : throw Invalid();
        }
        if (target == typeof(bool))
        {
            return element.GetBoolean();
        }
        var text = element.GetString() ?? throw Invalid();
        if (target == typeof(string)) return text;
        if (target == typeof(Guid)) return Guid.Parse(text);
        if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateOnly)) return DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (target == typeof(decimal)) return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (target == typeof(int)) return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (target == typeof(long)) return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (target == typeof(short)) return short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        throw Invalid();
    }

    private static ListQueryException Invalid() => new("after", "list.invalidCursor");
}

/// <summary>Builds the text of a filter from code (presets, tests, other modules).</summary>
public static class ListFilterText
{
    public static string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static string Eq(string column, string text) => $"{column} eq {Quote(text)}";

    public static string Eq(string column, bool value) => $"{column} eq {(value ? "true" : "false")}";

    public static string In(string column, IEnumerable<string> values) =>
        new StringBuilder(column).Append(" in (").AppendJoin(", ", values.Select(Quote)).Append(')').ToString();
}

/// <summary>Returns one page of a registered list's rows exactly as the list's endpoint does (the
/// same query contract and row shape), inside the caller's unit of work. Modules register one per
/// printable list (<c>ModuleBuilder.ListRows</c>); reports page through it.</summary>
public delegate Task<ListResult<object>> ListRowReader(IServiceProvider services, ListRequest request, Microsoft.AspNetCore.Http.HttpContext http, CancellationToken cancellationToken);
