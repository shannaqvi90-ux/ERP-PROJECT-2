using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using Erp.Kernel.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Erp.Kernel.Lists;

/// <summary>A column of a list bound to a value of the row type.</summary>
/// <param name="Key">The list column key.</param>
/// <param name="ValueType">CLR type of the bound value.</param>
/// <param name="Expression">The value as an expression over the row (translated to SQL).</param>
/// <param name="Member">The mapped property the value reads, when it reads one directly (the
/// index gate finds the database column from it); null for computed values.</param>
public sealed record ListBoundColumn(string Key, Type ValueType, LambdaExpression Expression, string? Member);

/// <summary>What the platform and the gates read from any list binding.</summary>
public interface IListBinding
{
    ListDefinition Definition { get; }

    /// <summary>The row type the binding queries (an entity for database lists).</summary>
    Type RowType { get; }

    /// <summary>Why the list is queried in memory instead of in the database (a small, bounded
    /// list), or null for database lists. In-memory lists are exempt from the index gate.</summary>
    string? InMemoryReason { get; }

    IReadOnlyList<ListBoundColumn> Columns { get; }

    /// <summary>Problems with the binding (unbound sortable/filterable/search columns, value types
    /// that do not suit the column type). The host refuses to start with any.</summary>
    IEnumerable<string> Problems();

    /// <summary>The same rows and bound values serving another list's definition (a list another
    /// module shows over this module's rows, see <c>ModuleBuilder.List(definition, servedBy)</c>):
    /// the columns the other definition names keep their bindings, the rest are left out.</summary>
    IListBinding ServeAs(ListDefinition definition);
}

/// <summary>The result of one list query: the page's rows, or the problem the caller must fix.</summary>
public sealed class ListResult<T>
{
    private ListResult(IReadOnlyList<T> rows, int total, string? next, IReadOnlyList<ListGroup>? groups, ProblemHttpResult? problem, bool ranked = false)
    {
        Ranked = ranked;
        Rows = rows;
        Total = total;
        Next = next;
        Groups = groups;
        Problem = problem;
    }

    public IReadOnlyList<T> Rows { get; }
    public int Total { get; }
    public string? Next { get; }
    public IReadOnlyList<ListGroup>? Groups { get; }

    /// <summary>A 400 validation problem naming the query parameter to correct, or null.</summary>
    public ProblemHttpResult? Problem { get; }

    /// <summary>The rows are in relevance order (best match first).</summary>
    public bool Ranked { get; }

    internal static ListResult<T> Valid(IReadOnlyList<T> rows, int total, string? next, IReadOnlyList<ListGroup>? groups, bool ranked = false) =>
        new(rows, total, next, groups, null, ranked);

    internal static ListResult<T> Invalid(ProblemHttpResult problem) => new([], 0, null, null, problem);

    /// <summary>The page in the shape every list endpoint returns.</summary>
    public ListPage<TItem> ToPage<TItem>(Func<T, TItem> map) => new(Rows.Select(map).ToList(), Total, Next, Groups, Ranked);

    /// <summary>The same result over other row objects (for example a module's public summary of
    /// its own rows, handed to another module through a contract); a problem stays the problem.</summary>
    public ListResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        Problem is { } problem ? ListResult<TOut>.Invalid(problem) : ListResult<TOut>.Valid(Rows.Select(map).ToList(), Total, Next, Groups, Ranked);
}

/// <summary>
/// Serves a registered list's query contract (<see cref="ListRequest"/>) over an
/// <see cref="IQueryable{T}"/>: word search across the search fields, the filter language, sort by
/// sortable columns with the row id as tie-breaker, keyset paging (<c>after</c>) or offset paging
/// (<c>skip</c>), and grouping with counts and totals. Database lists run as one SQL query for the
/// count, one for the page and one for the groups; the tenant comes from the DbContext's query
/// filter and row-level security, never from the request. Every value from the request reaches SQL
/// as a parameter. Small lists already in memory (a workspace's roles) use the same contract
/// through <see cref="InMemory"/>.
/// </summary>
public sealed class ListBinding<T> : IListBinding where T : class
{
    public const int MaxGroups = 1000;
    private const int MaxTotals = 8;

    private readonly FrozenDictionary<string, Bound> _columns;
    private readonly Expression<Func<T, Guid>> _id;
    private readonly Func<T, Guid> _idOf;

    // A binding never changes once built: Column and InMemory return a new binding, so a
    // registered binding (shared by every request of every tenant) holds no state of its own.
    private ListBinding(ListDefinition definition, Expression<Func<T, Guid>> id, Func<T, Guid> idOf,
        FrozenDictionary<string, Bound> columns, string? inMemoryReason)
    {
        Definition = definition;
        _id = id;
        _idOf = idOf;
        _columns = columns;
        InMemoryReason = inMemoryReason;
    }

    public ListDefinition Definition { get; }

    public Type RowType => typeof(T);

    public string? InMemoryReason { get; }

    public IReadOnlyList<ListBoundColumn> Columns =>
        _columns.Values.OrderBy(b => b.Order).Select(b => new ListBoundColumn(b.Key, b.ValueType, b.Expression, b.Member)).ToList();

    /// <summary>Start a binding for the list over rows of <typeparamref name="T"/>, whose unique
    /// id breaks ties in every sort.</summary>
    public static ListBinding<T> For(ListDefinition definition, Expression<Func<T, Guid>> id) =>
        new(definition, id, id.Compile(), FrozenDictionary<string, Bound>.Empty, null);

    /// <summary>A binding with one more column bound to a value of the row (the binding itself
    /// does not change).</summary>
    public ListBinding<T> Column<TValue>(string key, Expression<Func<T, TValue>> value)
    {
        if (_columns.ContainsKey(key))
        {
            throw new InvalidOperationException($"list '{Definition.Key}': column '{key}' is bound twice");
        }
        var member = value.Body is MemberExpression { Member: PropertyInfo property } access && access.Expression == value.Parameters[0] ? property.Name : null;
        var columns = new Dictionary<string, Bound>(_columns, StringComparer.Ordinal)
        {
            [key] = new Bound(key, typeof(TValue), value, member, CompileGetter(value), _columns.Count),
        };
        return new ListBinding<T>(Definition, _id, _idOf, columns.ToFrozenDictionary(StringComparer.Ordinal), InMemoryReason);
    }

    /// <inheritdoc/>
    public IListBinding ServeAs(ListDefinition definition)
    {
        var columns = new Dictionary<string, Bound>(StringComparer.Ordinal);
        foreach (var column in definition.Columns)
        {
            if (_columns.TryGetValue(column.Key, out var bound))
            {
                columns[column.Key] = bound;
            }
        }
        return new ListBinding<T>(definition, _id, _idOf, columns.ToFrozenDictionary(StringComparer.Ordinal), InMemoryReason);
    }

    /// <summary>A binding that queries this list in memory (LINQ to objects over rows already
    /// loaded) because it is small and bounded; the reason is reviewed by the index gate.</summary>
    public ListBinding<T> InMemory(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An in-memory list needs a reason.", nameof(reason));
        }
        InMemoryQuery.Prepare();
        return new ListBinding<T>(Definition, _id, _idOf, _columns, reason);
    }

    public IEnumerable<string> Problems()
    {
        var list = Definition.Key;
        foreach (var column in Definition.Columns)
        {
            var needsBinding = column.Sortable || column.Filterable || column.Groupable || column.Aggregate || Definition.AllSearchFields.Contains(column.Key);
            if (!_columns.TryGetValue(column.Key, out var bound))
            {
                if (needsBinding)
                {
                    yield return $"list '{list}': column '{column.Key}' is sortable, filterable, groupable, totalled or searched but not bound to a value";
                }
                continue;
            }
            if (!Suits(column.Type, bound.ValueType))
            {
                yield return $"list '{list}': column '{column.Key}' ({column.Type}) is bound to a {bound.ValueType.Name}";
            }
            if (Definition.AllSearchFields.Contains(column.Key) && bound.ValueType != typeof(string))
            {
                yield return $"list '{list}': search field '{column.Key}' must be bound to text";
            }
            if (column is { Type: ListColumnType.Money, Aggregate: true, CurrencyField: { } currency } &&
                !(_columns.TryGetValue(currency, out var currencyBound) && currencyBound.ValueType == typeof(string)))
            {
                yield return $"list '{list}': money column '{column.Key}' is totalled per currency, but its currency column '{currency}' is not bound to text";
            }
            if (column.Sortable && column.Type == ListColumnType.Boolean && bound.ValueType != typeof(bool))
            {
                yield return $"list '{list}': sortable flag '{column.Key}' must be bound to a non-nullable bool";
            }
        }
        foreach (var key in _columns.Keys.Where(k => Definition.Column(k) is null))
        {
            yield return $"list '{list}': '{key}' is bound but is not a column";
        }
    }

    private static bool Suits(ListColumnType type, Type valueType)
    {
        var t = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return type switch
        {
            ListColumnType.Text or ListColumnType.Choice => t == typeof(string),
            ListColumnType.Number => t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(decimal),
            ListColumnType.Money => t == typeof(decimal),
            ListColumnType.Date => t == typeof(DateOnly),
            ListColumnType.DateTime => t == typeof(DateTimeOffset) || t == typeof(DateTime),
            ListColumnType.Boolean => t == typeof(bool),
            ListColumnType.Reference => t == typeof(Guid),
            _ => false,
        };
    }

    /// <summary>Run the request against the rows: a page with total, next cursor and groups, or
    /// a 400 problem naming the parameter to correct.</summary>
    public async Task<ListResult<T>> QueryAsync(IQueryable<T> source, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        Plan plan;
        try
        {
            plan = PlanFor(request);
        }
        catch (ListQueryException e)
        {
            return ListResult<T>.Invalid(ToProblem(http, e));
        }
        var database = source.Provider is IAsyncQueryProvider;
        if (!database)
        {
            // Rows in memory: interpreted, not compiled anew for every count, group and page.
            source = InMemoryQuery.Over(source);
        }
        var filtered = Filtered(source, plan, database);
        var total = database ? await filtered.CountAsync(cancellationToken) : filtered.Count();
        IReadOnlyList<ListGroup>? groups;
        try
        {
            groups = plan.GroupBy is { } group ? await GroupsAsync(filtered, group, database, cancellationToken) : null;
        }
        catch (ListQueryException e)
        {
            return ListResult<T>.Invalid(ToProblem(http, e));
        }
        if (plan.Relevance && plan.After is null && total > ListSearch.MaxRankedRows)
        {
            // A search this broad (a letter or two) is listed in the default order: ranking every
            // row would cost more than it tells. A cursor keeps the order its first page had.
            plan = plan with { Relevance = false, Sort = plan.Sort.Skip(1).ToList() };
        }

        var page = filtered;
        if (plan.After is { } after)
        {
            page = page.Where(Keyset(plan, after.Values, after.Id, database));
        }
        page = Sorted(page, plan, database);
        if (plan.Skip > 0)
        {
            page = page.Skip(plan.Skip);
        }
        page = page.Take(plan.Take + 1);
        List<T> rows;
        List<int>? ranks = null;
        if (plan.Relevance)
        {
            // The relevance of each row comes from the same query that ordered it, so the cursor
            // carries exactly the value the store compared (never one recomputed here).
            var row = Expression.Parameter(typeof(T), "row");
            var ranked = page.Select(Expression.Lambda<Func<T, RankedRow<T>>>(
                Expression.New(typeof(RankedRow<T>).GetConstructors()[0], row, Rank(plan, row, database)), row));
            var found = database ? await ranked.ToListAsync(cancellationToken) : ranked.ToList();
            rows = found.Select(r => r.Row).ToList();
            ranks = found.Select(r => r.Rank).ToList();
        }
        else
        {
            rows = database ? await page.ToListAsync(cancellationToken) : page.ToList();
        }
        string? next = null;
        if (rows.Count > plan.Take)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            var lastRank = ranks?[rows.Count - 1];
            next = ListCursor.Encode(ListSortKey.Format(plan.Sort),
                plan.Sort.Select(k => k.Column == ListSearch.RelevanceKey ? lastRank : _columns[k.Column].Getter(last)).ToList(), _idOf(last));
        }
        return ListResult<T>.Valid(rows, total, next, groups, plan.Relevance);
    }

    /// <summary>The rows the request selects, in its order, without paging (exports, bulk
    /// actions on "everything that matches"). Throws <see cref="ListQueryException"/>.</summary>
    public IQueryable<T> Apply(IQueryable<T> source, ListRequest request)
    {
        var plan = PlanFor(request);
        var database = source.Provider is IAsyncQueryProvider;
        return Sorted(Filtered(source, plan, database), plan, database);
    }

    /// <summary>Every row the request's search and filter select, in no order and without paging:
    /// what a change of "everything that matches" acts on (the list's own count of the same request).
    /// The request's sort, cursor, paging and grouping are ignored, so they can neither narrow nor
    /// widen the match. Throws <see cref="ListQueryException"/> for a bad search or filter.</summary>
    public IQueryable<T> Matching(IQueryable<T> source, ListRequest request)
    {
        var plan = PlanFor(new ListRequest { Search = request.Search, Filter = request.Filter });
        return Filtered(source, plan, source.Provider is IAsyncQueryProvider);
    }

    /// <summary>A 400 validation problem for a list query error, in the request's language.</summary>
    public static ProblemHttpResult ToProblem(HttpContext http, ListQueryException error) =>
        new Validator(http).Add(error.Parameter, error.Code, [.. error.Args]).ToResult();

    /// <param name="Words">The search words (lower case), each with the spellings it matches.</param>
    /// <param name="Relevance">Ordered by relevance to the search (a search without a sort): the
    /// first sort key is <see cref="ListSearch.RelevanceKey"/>.</param>
    private sealed record Plan(
        IReadOnlyList<(string Word, IReadOnlyList<string> Spellings)> Words,
        FilterNode? Filter,
        IReadOnlyList<ListSortKey> Sort,
        (IReadOnlyList<object?> Values, Guid Id)? After,
        int Skip,
        int Take,
        string? GroupBy,
        bool Relevance);

    private Plan PlanFor(ListRequest request)
    {
        var words = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            if (request.Search.Length > ListRequest.MaxSearchLength)
            {
                throw new ListQueryException("search", "maxLength", ListRequest.MaxSearchLength);
            }
            words.AddRange(request.Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.ToLowerInvariant()).Distinct(StringComparer.Ordinal));
            if (words.Count > ListRequest.MaxSearchWords)
            {
                throw new ListQueryException("search", "list.searchTooManyWords", ListRequest.MaxSearchWords);
            }
            if (Definition.SearchFields.Count == 0)
            {
                throw new ListQueryException("search", "list.notSearchable");
            }
        }
        FilterNode? filter = null;
        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            filter = ListFilter.Parse(request.Filter);
            ListFilter.Validate(filter, Definition);
        }
        var sortText = string.IsNullOrWhiteSpace(request.Sort) ? Definition.DefaultSort : request.Sort;
        var sort = sortText is null ? [] : ListSortKey.Parse(sortText, Definition);
        // A search without a sort of its own: best matches first, then the list's default order.
        var relevance = words.Count > 0 && string.IsNullOrWhiteSpace(request.Sort);
        if (relevance && !string.IsNullOrEmpty(request.After) && ListCursor.SortOf(request.After) == ListSortKey.Format(sort))
        {
            // The cursor of a search too broad to rank (see QueryAsync) continues in the default order.
            relevance = false;
        }
        if (relevance)
        {
            sort = [new ListSortKey(ListSearch.RelevanceKey, true), .. sort];
        }
        string? groupBy = null;
        if (!string.IsNullOrWhiteSpace(request.GroupBy))
        {
            if (Definition.Column(request.GroupBy) is not { } column)
            {
                throw new ListQueryException("groupBy", "list.unknownColumn");
            }
            if (!column.Groupable)
            {
                throw new ListQueryException("groupBy", "list.notGroupable", column.Key);
            }
            groupBy = column.Key;
        }
        (IReadOnlyList<object?>, Guid)? after = null;
        if (!string.IsNullOrEmpty(request.After))
        {
            if (request.Skip is > 0)
            {
                throw new ListQueryException("after", "list.afterWithSkip");
            }
            after = ListCursor.Decode(request.After, ListSortKey.Format(sort),
                sort.Select(k => k.Column == ListSearch.RelevanceKey ? typeof(int) : _columns[k.Column].ValueType).ToList());
        }
        var take = Math.Clamp(request.Take ?? ListRequest.DefaultTake, 1, ListRequest.MaxTake);
        var skip = Math.Max(0, request.Skip ?? 0);
        return new Plan(words.Select(w => (w, ListSearch.Spellings(w))).ToList(), filter, sort, after, skip, take, groupBy, relevance);
    }

    private IQueryable<T> Filtered(IQueryable<T> source, Plan plan, bool database)
    {
        var row = Expression.Parameter(typeof(T), "row");
        var conditions = new List<Expression>();
        foreach (var (word, spellings) in plan.Words)
        {
            var fields = SearchFieldsFor([word]);
            if (database && spellings.Count > 1)
            {
                // A word with several spellings (Arabic) is first tested with one case-insensitive
                // regular expression per field, which accepts every spelling (letter classes, marks
                // after any letter): a row that cannot match is then refused after a few tests
                // instead of one LIKE per spelling and field (measured: half the time of a two-word
                // Arabic search over 100,000 users). The LIKE patterns stay, so the trigram indexes
                // still find the rows and the result is exactly theirs (each pattern implies the
                // expression).
                conditions.Add(fields.Select(field => RegexMatch(Value(field, row), ListSearch.Pattern(word), database))
                    .DefaultIfEmpty(Expression.Constant(false)).Aggregate(Expression.OrElse));
            }
            conditions.Add(AnyField(row, fields, spellings.Select(s => "%" + EscapeLike(s) + "%"), database));
        }
        if (plan.Filter is { } filter)
        {
            conditions.Add(Condition(filter, row, database));
        }
        if (conditions.Count == 0)
        {
            return source;
        }
        return source.Where(Expression.Lambda<Func<T, bool>>(conditions.Aggregate(Expression.AndAlso), row));
    }

    /// <summary>The search fields quick search tries for all of the words: the fields each word
    /// matches (<see cref="ListDefinition.SearchFieldsFor"/>: the Arabic search fields for a word
    /// written in Arabic letters), kept only when every word matches them.</summary>
    private IReadOnlyList<string> SearchFieldsFor(IReadOnlyList<string> words) =>
        words.Select(Definition.SearchFieldsFor)
            .Aggregate((IEnumerable<string>)Definition.AllSearchFields.ToList(), (fields, wordFields) => fields.Where(wordFields.Contains))
            .ToList();

    /// <summary>Some of the fields matches one of the patterns (false when no field can).</summary>
    private Expression AnyField(ParameterExpression row, IReadOnlyList<string> fields, IEnumerable<string> patterns, bool database)
    {
        var list = patterns.ToList();
        return fields
            .SelectMany(field => list.Select(pattern => Like(Value(field, row), pattern, database)))
            .DefaultIfEmpty(Expression.Constant(false))
            .Aggregate(Expression.OrElse);
    }

    /// <summary>
    /// How well a row matches the search, as one whole number (higher is better). Each word scores
    /// when a search field starts with it, less when a word inside a field does (a last name, the
    /// part of an e-mail after a dot); the whole search scores more when a field equals it or
    /// starts with it. Equal scores prefer the shorter value of the first search field (closer to
    /// what was typed). Computed by the store in the same query that orders and pages the rows.
    /// </summary>
    private Expression Rank(Plan plan, ParameterExpression row, bool database)
    {
        // Regular expressions (case-insensitive), one per field and test: much cheaper per row
        // than one LIKE per spelling, and a letter class covers every Arabic spelling at once.
        Expression Score(Expression condition, int score) => Expression.Condition(condition, Expression.Constant(score), Expression.Constant(0));
        // Each test looks only at the fields the words can occur in (see SearchFieldsFor).
        Expression Any(IReadOnlyList<string> fields, string pattern) =>
            fields.Select(field => RegexMatch(Value(field, row), pattern, database)).DefaultIfEmpty(Expression.Constant(false)).Aggregate(Expression.OrElse);
        var words = plan.Words.Select(w => ListSearch.Pattern(w.Word)).ToList();
        var separators = "[" + string.Concat(ListSearch.WordSeparators) + "]";
        var parts = new List<Expression>();
        foreach (var (typed, _) in plan.Words)
        {
            var word = ListSearch.Pattern(typed);
            var fields = SearchFieldsFor([typed]);
            parts.Add(Expression.Condition(Any(fields, "^" + word), Expression.Constant(ListSearch.FieldStartScore), Score(Any(fields, separators + word), ListSearch.WordStartScore)));
        }
        // The whole search equal to a field, at its start, or its words in the typed order (the
        // first at the start, each next one at the start of a later word: "yous wang" for
        // "Yousef Wang", not "Wang Yousef"), in the fields every word can occur in.
        var whole = SearchFieldsFor(plan.Words.Select(w => w.Word).ToList());
        parts.Add(Score(Any(whole, "^" + string.Join(" ", words) + "$"), ListSearch.ExactScore));
        if (words.Count > 1)
        {
            parts.Add(Score(Any(whole, "^" + string.Join(" ", words)), ListSearch.PhraseStartScore));
            parts.Add(Score(Any(whole, "^" + string.Join(".*" + separators, words)), ListSearch.InOrderScore));
        }
        var score = parts.Aggregate(Expression.Add);
        var first = Value(Definition.SearchFields[0], row);
        var cap = Expression.Constant(ListSearch.LengthSlots - 1);
        Expression length = Expression.Call(typeof(Math).GetMethod(nameof(Math.Min), [typeof(int), typeof(int)])!,
            Expression.Property(first, nameof(string.Length)), cap);
        length = Expression.Condition(Expression.Equal(first, Expression.Constant(null, typeof(string))), cap, length);
        return Expression.Subtract(Expression.Multiply(score, Expression.Constant(ListSearch.LengthSlots)), length);
    }

    private static readonly MethodInfo RegexIsMatch = typeof(System.Text.RegularExpressions.Regex).GetMethod(
        nameof(System.Text.RegularExpressions.Regex.IsMatch), [typeof(string), typeof(string), typeof(System.Text.RegularExpressions.RegexOptions)])!;

    private static readonly MethodInfo MatchesPattern = typeof(ListBinding<T>).GetMethod(nameof(MatchesRegex), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>A case-insensitive regular expression match: <c>~*</c> in PostgreSQL, .NET's
    /// regular expressions in memory (the patterns use only what both read alike).</summary>
    private static Expression RegexMatch(Expression value, string pattern, bool database) => database
        ? Expression.Call(RegexIsMatch, value, Box(pattern, typeof(string)), Expression.Constant(System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        : Expression.Call(MatchesPattern, value, Expression.Constant(pattern));

    private static bool MatchesRegex(string? value, string pattern) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The value a sort key orders by: a column's bound value, or the row's relevance.</summary>
    private Expression SortValue(ListSortKey key, Plan plan, ParameterExpression row, bool database) =>
        key.Column == ListSearch.RelevanceKey ? Rank(plan, row, database) : Value(key.Column, row);

    private Expression Value(string column, ParameterExpression row)
    {
        var bound = _columns[column];
        return new Rebind(bound.Expression.Parameters[0], row).Visit(bound.Expression.Body);
    }

    private Expression Condition(FilterNode node, ParameterExpression row, bool database) => node switch
    {
        FilterAll all => all.Items.Select(i => Condition(i, row, database)).Aggregate(Expression.AndAlso),
        FilterAny any => any.Items.Select(i => Condition(i, row, database)).Aggregate(Expression.OrElse),
        FilterNot not => Expression.Not(Condition(not.Item, row, database)),
        FilterCondition c => Condition(c, row, database),
        _ => throw new InvalidOperationException("Unknown filter node."),
    };

    private Expression Condition(FilterCondition condition, ParameterExpression row, bool database)
    {
        var column = Definition.Column(condition.Column)!;
        var member = Value(column.Key, row);
        var nullable = !member.Type.IsValueType || Nullable.GetUnderlyingType(member.Type) is not null;
        Expression IsNull() => nullable ? Expression.Equal(member, Expression.Constant(null, member.Type)) : Expression.Constant(false);
        if (condition.Operator == FilterOperator.IsNull)
        {
            return IsNull();
        }
        if (condition.Operator == FilterOperator.IsNotNull)
        {
            return Expression.Not(IsNull());
        }
        var text = column.Type == ListColumnType.Text;
        var values = condition.Values.Select(v => ToClr(v, member.Type, column)).ToList();
        switch (condition.Operator)
        {
            case FilterOperator.Eq when text:
                return Like(member, EscapeLike((string)values[0]!), database);
            case FilterOperator.Ne when text:
                return Expression.OrElse(IsNull(), Expression.Not(Like(member, EscapeLike((string)values[0]!), database)));
            case FilterOperator.In when text:
                return values.Select(v => Like(member, EscapeLike((string)v!), database)).Aggregate(Expression.OrElse);
            case FilterOperator.Contains:
                return Like(member, "%" + EscapeLike((string)values[0]!) + "%", database);
            case FilterOperator.StartsWith:
                return Like(member, EscapeLike((string)values[0]!) + "%", database);
            case FilterOperator.EndsWith:
                return Like(member, "%" + EscapeLike((string)values[0]!), database);
            case FilterOperator.Eq:
                return Expression.Equal(member, Box(values[0], member.Type));
            case FilterOperator.Ne:
                return Expression.NotEqual(member, Box(values[0], member.Type));
            case FilterOperator.In:
                var listType = typeof(List<>).MakeGenericType(member.Type);
                var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
                foreach (var value in values)
                {
                    list.Add(value);
                }
                return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [member.Type], Box(list, listType), member);
            case FilterOperator.Lt:
                return Expression.LessThan(member, Box(values[0], member.Type));
            case FilterOperator.Le:
                return Expression.LessThanOrEqual(member, Box(values[0], member.Type));
            case FilterOperator.Gt:
                return Expression.GreaterThan(member, Box(values[0], member.Type));
            case FilterOperator.Ge:
                return Expression.GreaterThanOrEqual(member, Box(values[0], member.Type));
            default:
                throw new ListQueryException("filter", "list.operatorNotAllowed", column.Key);
        }
    }

    /// <summary>A filter literal as the bound value's CLR type (already validated for the column type).</summary>
    private static object? ToClr(FilterValue value, Type type, ListColumn column)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            if (target == typeof(string)) return value.Text;
            if (target == typeof(bool)) return value.Text == "true";
            if (target == typeof(Guid)) return Guid.Parse(value.Text!);
            if (target == typeof(decimal)) return decimal.Parse(value.Text!, System.Globalization.NumberStyles.Number, invariant);
            if (target == typeof(int)) return int.Parse(value.Text!, System.Globalization.NumberStyles.Integer, invariant);
            if (target == typeof(long)) return long.Parse(value.Text!, System.Globalization.NumberStyles.Integer, invariant);
            if (target == typeof(short)) return short.Parse(value.Text!, System.Globalization.NumberStyles.Integer, invariant);
            if (target == typeof(DateOnly) && ListFilter.TryDate(value.Text!, out var date)) return date;
            if (target == typeof(DateTimeOffset) && ListFilter.TryDateTime(value.Text!, out var instant)) return instant;
            if (target == typeof(DateTime) && ListFilter.TryDateTime(value.Text!, out var utc)) return utc.UtcDateTime;
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            // Falls through to the value error below (for example a fraction for a whole-number column).
        }
        throw new ListQueryException("filter", column.Type is ListColumnType.Number or ListColumnType.Money ? "list.valueNumber" : "list.valueText", column.Key);
    }

    private static readonly MethodInfo ILike = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly MethodInfo Matches = typeof(ListBinding<T>).GetMethod(nameof(MatchesLike), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Case-insensitive LIKE with backslash escapes: ILIKE in PostgreSQL (served by the
    /// trigram indexes), the same pattern semantics in memory.</summary>
    private static Expression Like(Expression value, string pattern, bool database) => database
        ? Expression.Call(ILike, Expression.Constant(EF.Functions), value, Box(pattern, typeof(string)), Expression.Constant("\\"))
        : Expression.Call(Matches, value, Expression.Constant(pattern));

    private static bool MatchesLike(string? value, string pattern)
    {
        if (value is null)
        {
            return false;
        }
        var regex = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                regex.Append(System.Text.RegularExpressions.Regex.Escape(pattern[++i].ToString()));
            }
            else if (c == '%')
            {
                regex.Append(".*");
            }
            else if (c == '_')
            {
                regex.Append('.');
            }
            else
            {
                regex.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
            }
        }
        regex.Append('$');
        return System.Text.RegularExpressions.Regex.IsMatch(value, regex.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>A request value as a closure member, so EF Core sends it as a SQL parameter and
    /// caches one query plan per query shape (never one per value).</summary>
    private static Expression Box(object? value, Type type)
    {
        var box = Activator.CreateInstance(typeof(ValueBox<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(box), nameof(ValueBox<object>.Value));
    }

    private IQueryable<T> Sorted(IQueryable<T> source, Plan plan, bool database)
    {
        var sort = plan.Sort;
        var row = Expression.Parameter(typeof(T), "row");
        var ordered = source.Expression;
        var first = true;
        foreach (var key in sort)
        {
            ordered = OrderCall(ordered, SortValue(key, plan, row, database), row, key.Descending, first);
            first = false;
        }
        var idDescending = sort.Count > 0 && sort[0].Descending;
        ordered = OrderCall(ordered, new Rebind(_id.Parameters[0], row).Visit(_id.Body), row, idDescending, first);
        return source.Provider.CreateQuery<T>(ordered);
    }

    private static Expression OrderCall(Expression source, Expression key, ParameterExpression row, bool descending, bool first)
    {
        var name = (first, descending) switch
        {
            (true, false) => nameof(Queryable.OrderBy),
            (true, true) => nameof(Queryable.OrderByDescending),
            (false, false) => nameof(Queryable.ThenBy),
            (false, true) => nameof(Queryable.ThenByDescending),
        };
        return Expression.Call(typeof(Queryable), name, [typeof(T), key.Type], source, Expression.Quote(Expression.Lambda(key, row)));
    }

    /// <summary>
    /// Rows after the cursor in the sort order: for keys k1..kn and the id, (k1 after v1) or
    /// (k1 = v1 and k2 after v2) … or (all equal and id after the cursor's id). Nulls sort last
    /// ascending and first descending in PostgreSQL, first ascending in memory; "after" follows
    /// the store's order. A redundant range on the first non-nullable key lets an index seek.
    /// </summary>
    private Expression<Func<T, bool>> Keyset(Plan plan, IReadOnlyList<object?> values, Guid id, bool database)
    {
        var sort = plan.Sort;
        var row = Expression.Parameter(typeof(T), "row");
        var idDescending = sort.Count > 0 && sort[0].Descending;
        var members = sort.Select(k => SortValue(k, plan, row, database)).ToList();
        var terms = new List<Expression>();
        Expression? equalSoFar = null;
        for (var i = 0; i < sort.Count; i++)
        {
            var after = After(members[i], values[i], sort[i].Descending, database);
            terms.Add(equalSoFar is null ? after : Expression.AndAlso(equalSoFar, after));
            var equal = values[i] is null
                ? Expression.Equal(members[i], Expression.Constant(null, members[i].Type))
                : Expression.Equal(members[i], Box(values[i], members[i].Type));
            equalSoFar = equalSoFar is null ? equal : Expression.AndAlso(equalSoFar, equal);
        }
        var idMember = new Rebind(_id.Parameters[0], row).Visit(_id.Body);
        var idAfter = Compare(idMember, Box(id, typeof(Guid)), greater: !idDescending);
        terms.Add(equalSoFar is null ? idAfter : Expression.AndAlso(equalSoFar, idAfter));
        Expression body = terms.Aggregate(Expression.OrElse);
        if (sort.Count > 0 && values[0] is not null && members[0].Type.IsValueType && Nullable.GetUnderlyingType(members[0].Type) is null)
        {
            // first >= v (ascending) or first <= v (descending): an index range the planner can seek.
            var bound = sort[0].Descending
                ? CompareOrEqual(members[0], Box(values[0], members[0].Type), greater: false)
                : CompareOrEqual(members[0], Box(values[0], members[0].Type), greater: true);
            body = Expression.AndAlso(bound, body);
        }
        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    private static Expression After(Expression member, object? value, bool descending, bool database)
    {
        var nullable = !member.Type.IsValueType || Nullable.GetUnderlyingType(member.Type) is not null;
        var isNull = nullable ? Expression.Equal(member, Expression.Constant(null, member.Type)) : (Expression)Expression.Constant(false);
        if (value is null)
        {
            // Only nullable keys can hold null.
            var nullsLast = database ? !descending : descending;
            return nullsLast ? Expression.Constant(false) : Expression.Not(isNull);
        }
        var beyond = Compare(member, Box(value, member.Type), greater: !descending);
        if (!nullable)
        {
            return beyond;
        }
        var nullsAfterValues = database ? !descending : descending;
        return nullsAfterValues ? Expression.OrElse(beyond, isNull) : beyond;
    }

    private static Expression Compare(Expression left, Expression right, bool greater) => CompareCore(left, right, greater, orEqual: false);

    private static Expression CompareOrEqual(Expression left, Expression right, bool greater) => CompareCore(left, right, greater, orEqual: true);

    private static Expression CompareCore(Expression left, Expression right, bool greater, bool orEqual)
    {
        var type = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
        if (type == typeof(string))
        {
            var compare = Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!, left, right);
            return Build(compare, Expression.Constant(0), greater, orEqual);
        }
        if (type == typeof(bool))
        {
            Expression AsNumber(Expression e) => Expression.Condition(e, Expression.Constant(1), Expression.Constant(0));
            return Build(AsNumber(left), AsNumber(right), greater, orEqual);
        }
        if (type == typeof(Guid))
        {
            var compare = Expression.Call(left, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, right);
            return Build(compare, Expression.Constant(0), greater, orEqual);
        }
        return Build(left, right, greater, orEqual);
    }

    private static Expression Build(Expression left, Expression right, bool greater, bool orEqual)
    {
        if (left.Type != right.Type)
        {
            right = Expression.Convert(right, left.Type);
        }
        return (greater, orEqual) switch
        {
            (true, false) => Expression.GreaterThan(left, right),
            (true, true) => Expression.GreaterThanOrEqual(left, right),
            (false, false) => Expression.LessThan(left, right),
            (false, true) => Expression.LessThanOrEqual(left, right),
        };
    }

    private async Task<IReadOnlyList<ListGroup>> GroupsAsync(IQueryable<T> source, string column, bool database, CancellationToken cancellationToken)
    {
        var bound = _columns[column];
        var aggregates = Definition.Columns.Where(c => c.Aggregate && _columns.ContainsKey(c.Key)).Take(MaxTotals).ToList();
        var totals = aggregates.Where(c => c.Type != ListColumnType.Money).Select(c => _columns[c.Key]).ToList();
        var method = typeof(ListBinding<T>).GetMethod(nameof(GroupsCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(bound.ValueType);
        var groups = await (Task<IReadOnlyList<ListGroup>>)method.Invoke(this, [source, bound.Expression, totals, database, cancellationToken])!;
        var money = aggregates.Where(c => c.Type == ListColumnType.Money).ToList();
        if (money.Count == 0)
        {
            return groups;
        }
        // Money is totalled per currency (CLAUDE.md rule 2): the groups' sums are read once for each
        // currency the matching rows hold, so amounts in different currencies are never added.
        var byKey = new Dictionary<string, Dictionary<string, List<ListMoneyTotal>>>(StringComparer.Ordinal);
        foreach (var moneyColumn in money)
        {
            var amount = _columns[moneyColumn.Key];
            var currency = _columns[moneyColumn.CurrencyField!];
            var currencyOf = (Expression<Func<T, string?>>)currency.Expression;
            var currencies = database
                ? await source.Select(currencyOf).Distinct().OrderBy(c => c).Take(MaxCurrencies + 1).ToListAsync(cancellationToken)
                : source.Select(currencyOf).Distinct().OrderBy(c => c).Take(MaxCurrencies + 1).ToList();
            if (currencies.Count > MaxCurrencies)
            {
                throw new ListQueryException("groupBy", "list.tooManyCurrencies", MaxCurrencies);
            }
            foreach (var code in currencies)
            {
                var row = currencyOf.Parameters[0];
                var value = code is null ? (Expression)Expression.Constant(null, typeof(string)) : Expression.Property(Expression.Constant(new ValueBox<string>(code)), nameof(ValueBox<string>.Value));
                var inCurrency = source.Where(Expression.Lambda<Func<T, bool>>(Expression.Equal(currencyOf.Body, value), row));
                var sums = await (Task<IReadOnlyList<ListGroup>>)method.Invoke(this, [inCurrency, bound.Expression, new List<Bound> { amount }, database, cancellationToken])!;
                foreach (var sum in sums)
                {
                    var key = GroupKey(sum.Key);
                    if (!byKey.TryGetValue(key, out var columns))
                    {
                        byKey[key] = columns = new Dictionary<string, List<ListMoneyTotal>>(StringComparer.Ordinal);
                    }
                    if (!columns.TryGetValue(moneyColumn.Key, out var lines))
                    {
                        columns[moneyColumn.Key] = lines = [];
                    }
                    lines.Add(new ListMoneyTotal(code, sum.Totals?[amount.Key] ?? 0m));
                }
            }
        }
        return groups.Select(g => g with
        {
            MoneyTotals = money.ToDictionary(c => c.Key,
                c => (IReadOnlyList<ListMoneyTotal>)(byKey.TryGetValue(GroupKey(g.Key), out var columns) && columns.TryGetValue(c.Key, out var lines) ? lines : []),
                StringComparer.Ordinal),
        }).ToList();
    }

    /// <summary>Most currencies a money column is totalled in for one grouping.</summary>
    public const int MaxCurrencies = 200;

    private static string GroupKey(object? key) => key switch
    {
        null => "\0null",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString() ?? "",
    };

    private async Task<IReadOnlyList<ListGroup>> GroupsCoreAsync<TKey>(IQueryable<T> source, LambdaExpression key, List<Bound> totals, bool database, CancellationToken cancellationToken)
    {
        var group = Expression.Parameter(typeof(IGrouping<TKey, T>), "group");
        var rowType = typeof(GroupRow<TKey>);
        var bindings = new List<MemberBinding>
        {
            Expression.Bind(rowType.GetProperty(nameof(GroupRow<TKey>.Key))!, Expression.Property(group, nameof(IGrouping<TKey, T>.Key))),
            Expression.Bind(rowType.GetProperty(nameof(GroupRow<TKey>.Count))!, Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(T)], group)),
        };
        for (var i = 0; i < totals.Count; i++)
        {
            var parameter = totals[i].Expression.Parameters[0];
            var selector = Expression.Lambda<Func<T, decimal?>>(Expression.Convert(totals[i].Expression.Body, typeof(decimal?)), parameter);
            var sum = Expression.Call(typeof(Enumerable), nameof(Enumerable.Sum), [typeof(T)], group, selector);
            bindings.Add(Expression.Bind(rowType.GetProperty($"S{i}")!, sum));
        }
        var projection = Expression.Lambda<Func<IGrouping<TKey, T>, GroupRow<TKey>>>(Expression.MemberInit(Expression.New(rowType), bindings), group);
        var query = source.GroupBy((Expression<Func<T, TKey>>)key).Select(projection).OrderBy(r => r.Key).Take(MaxGroups);
        var rows = database ? await query.ToListAsync(cancellationToken) : query.ToList();
        return rows
            .OrderBy(r => r.Key is null ? 1 : 0)
            .Select(r => new ListGroup(r.Key, r.Count, totals.Count == 0 ? null : totals.Select((t, i) => (t.Key, Sum: r.Sum(i) ?? 0m)).ToDictionary(x => x.Key, x => x.Sum)))
            .ToList();
    }

    private static Func<T, object?> CompileGetter<TValue>(Expression<Func<T, TValue>> value)
    {
        var compiled = value.Compile();
        return row => compiled(row);
    }

    private sealed record Bound(string Key, Type ValueType, LambdaExpression Expression, string? Member, Func<T, object?> Getter, int Order);

    /// <summary>Replaces a lambda's parameter with another, to combine column expressions.</summary>
    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>A row with its relevance to the search, as the store computed it.</summary>
internal sealed class RankedRow<TRow>(TRow row, int rank)
{
    public TRow Row { get; } = row;
    public int Rank { get; } = rank;
}

/// <summary>Holds one request value so EF Core sends it as a parameter.</summary>
internal sealed class ValueBox<TValue>(TValue value)
{
    public TValue Value { get; } = value;
}

/// <summary>One group as read from the store.</summary>
internal sealed class GroupRow<TKey>
{
    public TKey Key { get; set; } = default!;
    public int Count { get; set; }
    public decimal? S0 { get; set; }
    public decimal? S1 { get; set; }
    public decimal? S2 { get; set; }
    public decimal? S3 { get; set; }
    public decimal? S4 { get; set; }
    public decimal? S5 { get; set; }
    public decimal? S6 { get; set; }
    public decimal? S7 { get; set; }

    public decimal? Sum(int index) => index switch
    {
        0 => S0,
        1 => S1,
        2 => S2,
        3 => S3,
        4 => S4,
        5 => S5,
        6 => S6,
        7 => S7,
        _ => null,
    };
}
