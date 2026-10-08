using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Erp.Kernel.Lists;

/// <summary>A request parameter of a list query the caller must correct. The message is looked
/// up from <c>validation.{Code}</c>; it never repeats what the caller sent, so an answer cannot
/// differ by the value (no existence oracle, no echo of another tenant's text).</summary>
public sealed class ListQueryException(string parameter, string code, params object?[] args)
    : Exception($"{parameter}: {code}")
{
    public string Parameter { get; } = parameter;
    public string Code { get; } = code;
    public IReadOnlyList<object?> Args { get; } = args;
}

public enum FilterOperator
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    Contains,
    StartsWith,
    EndsWith,
    In,
    IsNull,
    IsNotNull,
}

public enum FilterValueKind
{
    Text,
    Number,
    Boolean,
    Null,
}

/// <summary>A literal in a filter: <c>'text'</c>, <c>12.5</c>, <c>true</c>, <c>null</c>.</summary>
public sealed record FilterValue(FilterValueKind Kind, string? Text);

public abstract record FilterNode;

/// <summary>Every item holds (<c>and</c>).</summary>
public sealed record FilterAll(IReadOnlyList<FilterNode> Items) : FilterNode;

/// <summary>At least one item holds (<c>or</c>).</summary>
public sealed record FilterAny(IReadOnlyList<FilterNode> Items) : FilterNode;

/// <summary>The item does not hold (<c>not</c>).</summary>
public sealed record FilterNot(FilterNode Item) : FilterNode;

/// <summary><c>column operator value</c>, <c>column in (v1, v2)</c> or <c>column is [not] null</c>.</summary>
public sealed record FilterCondition(string Column, FilterOperator Operator, IReadOnlyList<FilterValue> Values) : FilterNode;

/// <summary>
/// The list filter language, one for every list and every client (screens, API, saved views):
/// <code>
/// language eq 'ar' and (isActive eq true or lastSignInAt is null)
/// createdAt ge '2026-01-01T00:00:00+04:00' and not email endswith '.example'
/// status in ('open', 'approved')
/// </code>
/// Operators: <c>eq ne lt le gt ge contains startswith endswith in</c>, <c>is null</c>,
/// <c>is not null</c>; joined with <c>and</c>, <c>or</c>, <c>not</c> and parentheses. Text is
/// quoted with single quotes (a quote inside is doubled); numbers use a dot; dates and times are
/// ISO 8601 text. Text comparisons ignore case.
/// </summary>
public static class ListFilter
{
    public const int MaxLength = 2000;
    public const int MaxConditions = 25;
    public const int MaxDepth = 8;
    public const int MaxInValues = 100;

    private const string Parameter = "filter";

    public static FilterNode Parse(string text)
    {
        if (text.Length > MaxLength)
        {
            throw new ListQueryException(Parameter, "maxLength", MaxLength);
        }
        var parser = new Parser(Tokenize(text));
        var node = parser.Expression(0);
        if (!parser.AtEnd)
        {
            throw Syntax();
        }
        if (parser.Conditions > MaxConditions)
        {
            throw new ListQueryException(Parameter, "list.filterTooComplex", MaxConditions);
        }
        return node;
    }

    /// <summary>Check columns, operators and value kinds against the list. Throws
    /// <see cref="ListQueryException"/> for the first problem.</summary>
    public static void Validate(FilterNode node, ListDefinition list)
    {
        switch (node)
        {
            case FilterAll all:
                foreach (var item in all.Items) Validate(item, list);
                break;
            case FilterAny any:
                foreach (var item in any.Items) Validate(item, list);
                break;
            case FilterNot not:
                Validate(not.Item, list);
                break;
            case FilterCondition condition:
                ValidateCondition(condition, list);
                break;
        }
    }

    private static void ValidateCondition(FilterCondition condition, ListDefinition list)
    {
        if (list.Column(condition.Column) is not { } column)
        {
            throw new ListQueryException(Parameter, "list.unknownColumn");
        }
        if (!column.Filterable)
        {
            throw new ListQueryException(Parameter, "list.notFilterable", column.Key);
        }
        if (!Allowed(column.Type).Contains(condition.Operator))
        {
            throw new ListQueryException(Parameter, "list.operatorNotAllowed", column.Key);
        }
        foreach (var value in condition.Values)
        {
            if (value.Kind == FilterValueKind.Null)
            {
                if (condition.Operator is not (FilterOperator.Eq or FilterOperator.Ne))
                {
                    throw new ListQueryException(Parameter, "list.operatorNotAllowed", column.Key);
                }
                continue;
            }
            var ok = column.Type switch
            {
                ListColumnType.Number or ListColumnType.Money => value.Kind == FilterValueKind.Number,
                ListColumnType.Boolean => value.Kind == FilterValueKind.Boolean,
                ListColumnType.Date => value.Kind == FilterValueKind.Text && TryDate(value.Text!, out _),
                ListColumnType.DateTime => value.Kind == FilterValueKind.Text && TryDateTime(value.Text!, out _),
                ListColumnType.Reference => value.Kind == FilterValueKind.Text && Guid.TryParse(value.Text, out _),
                ListColumnType.Choice => value.Kind == FilterValueKind.Text && (column.Choices is not { Count: > 0 } || column.Choices.Any(c => c.Value == value.Text)),
                _ => value.Kind == FilterValueKind.Text,
            };
            if (!ok)
            {
                throw new ListQueryException(Parameter, ValueCode(column.Type), column.Key);
            }
        }
    }

    /// <summary>Operators each column type accepts.</summary>
    public static IReadOnlyList<FilterOperator> Allowed(ListColumnType type) => type switch
    {
        ListColumnType.Text =>
        [
            FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.EndsWith,
            FilterOperator.In, FilterOperator.IsNull, FilterOperator.IsNotNull,
        ],
        ListColumnType.Number or ListColumnType.Money or ListColumnType.Date or ListColumnType.DateTime =>
        [
            FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Lt, FilterOperator.Le, FilterOperator.Gt, FilterOperator.Ge,
            FilterOperator.In, FilterOperator.IsNull, FilterOperator.IsNotNull,
        ],
        ListColumnType.Boolean => [FilterOperator.Eq, FilterOperator.Ne, FilterOperator.IsNull, FilterOperator.IsNotNull],
        _ => [FilterOperator.Eq, FilterOperator.Ne, FilterOperator.In, FilterOperator.IsNull, FilterOperator.IsNotNull],
    };

    private static string ValueCode(ListColumnType type) => type switch
    {
        ListColumnType.Number or ListColumnType.Money => "list.valueNumber",
        ListColumnType.Boolean => "list.valueBoolean",
        ListColumnType.Date => "list.valueDate",
        ListColumnType.DateTime => "list.valueDateTime",
        ListColumnType.Reference => "list.valueId",
        ListColumnType.Choice => "list.valueChoice",
        _ => "list.valueText",
    };

    public static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>ISO 8601 date and time with an offset (or Z), or a date alone (midnight UTC).</summary>
    public static bool TryDateTime(string text, out DateTimeOffset value)
    {
        if (TryDate(text, out var date))
        {
            value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return true;
        }
        if (text.Length >= 16 && text[10] == 'T' &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Write a filter back as text (canonical form: lower-case operators, quoted text).</summary>
    public static string Format(FilterNode node) => node switch
    {
        FilterAll all => string.Join(" and ", all.Items.Select(i => i is FilterAny ? $"({Format(i)})" : Format(i))),
        FilterAny any => string.Join(" or ", any.Items.Select(i => i is FilterAll ? $"({Format(i)})" : Format(i))),
        FilterNot not => $"not ({Format(not.Item)})",
        FilterCondition c => c.Operator switch
        {
            FilterOperator.IsNull => $"{c.Column} is null",
            FilterOperator.IsNotNull => $"{c.Column} is not null",
            FilterOperator.In => $"{c.Column} in ({string.Join(", ", c.Values.Select(Literal))})",
            _ => $"{c.Column} {c.Operator.ToString().ToLowerInvariant()} {Literal(c.Values[0])}",
        },
        _ => "",
    };

    public static string Literal(FilterValue value) => value.Kind switch
    {
        FilterValueKind.Text => "'" + value.Text!.Replace("'", "''", StringComparison.Ordinal) + "'",
        FilterValueKind.Null => "null",
        _ => value.Text!,
    };

    private static ListQueryException Syntax() => new(Parameter, "list.filterSyntax");

    private enum TokenKind
    {
        Word,
        Text,
        Number,
        Open,
        Close,
        Comma,
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '(')
            {
                tokens.Add(new Token(TokenKind.Open, "("));
                i++;
            }
            else if (c == ')')
            {
                tokens.Add(new Token(TokenKind.Close, ")"));
                i++;
            }
            else if (c == ',')
            {
                tokens.Add(new Token(TokenKind.Comma, ","));
                i++;
            }
            else if (c == '\'')
            {
                var value = new StringBuilder();
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            value.Append('\'');
                            i += 2;
                            continue;
                        }
                        closed = true;
                        i++;
                        break;
                    }
                    value.Append(text[i]);
                    i++;
                }
                if (!closed)
                {
                    throw Syntax();
                }
                tokens.Add(new Token(TokenKind.Text, value.ToString()));
            }
            else if (char.IsAsciiDigit(c) || (c == '-' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }
                var number = text[start..i];
                if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _) || number.EndsWith('.'))
                {
                    throw Syntax();
                }
                tokens.Add(new Token(TokenKind.Number, number));
            }
            else if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
                {
                    i++;
                }
                tokens.Add(new Token(TokenKind.Word, text[start..i]));
            }
            else
            {
                throw Syntax();
            }
        }
        return tokens;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _position;

        public int Conditions { get; private set; }

        public bool AtEnd => _position >= tokens.Count;

        private Token? Peek => _position < tokens.Count ? tokens[_position] : null;

        private bool IsWord(string word) => Peek is { Kind: TokenKind.Word } t && string.Equals(t.Value, word, StringComparison.OrdinalIgnoreCase);

        private Token Next() => _position < tokens.Count ? tokens[_position++] : throw Syntax();

        private void Expect(TokenKind kind)
        {
            if (Next().Kind != kind)
            {
                throw Syntax();
            }
        }

        public FilterNode Expression(int depth)
        {
            if (depth > MaxDepth)
            {
                throw new ListQueryException(Parameter, "list.filterTooComplex", MaxConditions);
            }
            var items = new List<FilterNode> { And(depth) };
            while (IsWord("or"))
            {
                _position++;
                items.Add(And(depth));
            }
            return items.Count == 1 ? items[0] : new FilterAny(items);
        }

        private FilterNode And(int depth)
        {
            var items = new List<FilterNode> { Unary(depth) };
            while (IsWord("and"))
            {
                _position++;
                items.Add(Unary(depth));
            }
            return items.Count == 1 ? items[0] : new FilterAll(items);
        }

        private FilterNode Unary(int depth)
        {
            if (IsWord("not"))
            {
                _position++;
                return new FilterNot(Unary(depth + 1));
            }
            if (Peek is { Kind: TokenKind.Open })
            {
                _position++;
                var inner = Expression(depth + 1);
                Expect(TokenKind.Close);
                return inner;
            }
            return Condition();
        }

        private FilterCondition Condition()
        {
            var column = Next();
            if (column.Kind != TokenKind.Word || Keywords.Contains(column.Value))
            {
                throw Syntax();
            }
            Conditions++;
            var op = Next();
            if (op.Kind != TokenKind.Word)
            {
                throw Syntax();
            }
            switch (op.Value.ToLowerInvariant())
            {
                case "is":
                    var negated = false;
                    if (IsWord("not"))
                    {
                        _position++;
                        negated = true;
                    }
                    if (!IsWord("null"))
                    {
                        throw Syntax();
                    }
                    _position++;
                    return new FilterCondition(column.Value, negated ? FilterOperator.IsNotNull : FilterOperator.IsNull, []);
                case "in":
                    Expect(TokenKind.Open);
                    var values = new List<FilterValue> { Value() };
                    while (Peek is { Kind: TokenKind.Comma })
                    {
                        _position++;
                        values.Add(Value());
                    }
                    Expect(TokenKind.Close);
                    if (values.Count > MaxInValues)
                    {
                        throw new ListQueryException(Parameter, "list.filterTooComplex", MaxConditions);
                    }
                    if (values.Any(v => v.Kind == FilterValueKind.Null))
                    {
                        throw Syntax();
                    }
                    return new FilterCondition(column.Value, FilterOperator.In, values);
                default:
                    if (!Operators.TryGetValue(op.Value.ToLowerInvariant(), out var parsed))
                    {
                        throw Syntax();
                    }
                    var value = Value();
                    if (value.Kind == FilterValueKind.Null)
                    {
                        return parsed switch
                        {
                            FilterOperator.Eq => new FilterCondition(column.Value, FilterOperator.IsNull, []),
                            FilterOperator.Ne => new FilterCondition(column.Value, FilterOperator.IsNotNull, []),
                            _ => throw Syntax(),
                        };
                    }
                    return new FilterCondition(column.Value, parsed, [value]);
            }
        }

        private FilterValue Value()
        {
            var token = Next();
            return token.Kind switch
            {
                TokenKind.Text => new FilterValue(FilterValueKind.Text, token.Value),
                TokenKind.Number => new FilterValue(FilterValueKind.Number, token.Value),
                TokenKind.Word when string.Equals(token.Value, "true", StringComparison.OrdinalIgnoreCase) => new FilterValue(FilterValueKind.Boolean, "true"),
                TokenKind.Word when string.Equals(token.Value, "false", StringComparison.OrdinalIgnoreCase) => new FilterValue(FilterValueKind.Boolean, "false"),
                TokenKind.Word when string.Equals(token.Value, "null", StringComparison.OrdinalIgnoreCase) => new FilterValue(FilterValueKind.Null, null),
                _ => throw Syntax(),
            };
        }

        private static readonly FrozenDictionary<string, FilterOperator> Operators = new Dictionary<string, FilterOperator>(StringComparer.Ordinal)
        {
            ["eq"] = FilterOperator.Eq,
            ["ne"] = FilterOperator.Ne,
            ["lt"] = FilterOperator.Lt,
            ["le"] = FilterOperator.Le,
            ["gt"] = FilterOperator.Gt,
            ["ge"] = FilterOperator.Ge,
            ["contains"] = FilterOperator.Contains,
            ["startswith"] = FilterOperator.StartsWith,
            ["endswith"] = FilterOperator.EndsWith,
        }.ToFrozenDictionary(StringComparer.Ordinal);

        private static readonly FrozenSet<string> Keywords = new[] { "and", "or", "not", "true", "false", "null" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
