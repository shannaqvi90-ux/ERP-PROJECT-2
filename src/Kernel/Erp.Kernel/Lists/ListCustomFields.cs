using System.Globalization;
using System.Linq.Expressions;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Lists;

/// <summary>A custom field as a column of a list: the column the screen shows and, for the query,
/// how a filter on it becomes a condition on the row's id (p09). Built per request from the
/// tenant's definitions; never registered.</summary>
internal sealed record ExtraColumn(CustomFieldInfo Field, ListColumn Column, Func<FilterCondition, Expression, Expression> Condition);

/// <summary>
/// Turns a workspace's custom fields into list columns and filter conditions. A condition is
/// <c>row.Id IN (customfields.match_…(field, operator, value))</c>: one uncorrelated question per
/// condition that the value indexes answer, so filtering 100,000 records by a custom field costs an
/// index range read and a semi-join, not a probe per record. Fields are filterable and (text) searchable;
/// they are not sortable or groupable (a sort by a value held in another table cannot use a keyset index).
/// </summary>
internal static class ListCustomFields
{
    /// <summary>The fields of the workspace for a list, or none (no custom fields module, a list
    /// no kind of record is registered for, or a workspace that defined none).</summary>
    public static async Task<IReadOnlyList<CustomFieldInfo>> FieldsAsync(IServiceProvider services, string listKey, CancellationToken cancellationToken)
    {
        var catalog = services.GetService<ModuleCatalog>();
        var customFields = services.GetService<ICustomFields>();
        if (catalog is null || customFields is null)
        {
            return [];
        }
        var record = catalog.CustomFieldRecordTypes.FirstOrDefault(r => r.ListKey == listKey);
        return record is null ? [] : await customFields.FieldsAsync(record.Table, cancellationToken);
    }

    public static IReadOnlyList<ListColumn> Columns(IReadOnlyList<CustomFieldInfo> fields) => fields.Select(Column).ToList();

    public static ListColumn Column(CustomFieldInfo field) => new(
        field.Key,
        field.LabelKey,
        field.Type switch
        {
            CustomFieldTypes.Number => ListColumnType.Number,
            CustomFieldTypes.Date => ListColumnType.Date,
            CustomFieldTypes.Boolean => ListColumnType.Boolean,
            CustomFieldTypes.Select => ListColumnType.Choice,
            CustomFieldTypes.Lookup => ListColumnType.Reference,
            _ => ListColumnType.Text,
        },
        Filterable: true,
        Sortable: field.Type != CustomFieldTypes.Lookup,
        Groupable: field.Type is CustomFieldTypes.Select or CustomFieldTypes.Boolean,
        Hidden: true,
        Choices: field.Type == CustomFieldTypes.Select ? field.Options.Select(o => new ListChoice(o.Value, field.OptionKey(o.Value))).ToList() : null,
        LabelField: field.Type == CustomFieldTypes.Lookup ? field.Key + "Name" : null,
        ArabicField: field.Type == CustomFieldTypes.Lookup ? field.Key + "NameAr" : null,
        Decimals: field.Type == CustomFieldTypes.Number ? field.Decimals : null);

    /// <summary>The label strings of the fields (and of a select's options) in both languages,
    /// handed to the screen with the list definition: the tenant's own words, not the product's.</summary>
    public static IReadOnlyDictionary<string, DynamicString> Strings(IReadOnlyList<CustomFieldInfo> fields)
    {
        var strings = new Dictionary<string, DynamicString>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            strings[field.LabelKey] = new DynamicString(field.LabelEn, field.LabelAr);
            foreach (var option in field.Options)
            {
                strings[field.OptionKey(option.Value)] = new DynamicString(option.LabelEn, option.LabelAr);
            }
        }
        return strings;
    }

    /// <summary>The field's value for the record whose id is <paramref name="id"/>, as a scalar the
    /// store can order and group by (<c>customfields.value_*</c>); null for a kind that is not ordered.</summary>
    public static Expression? ValueOf(CustomFieldInfo field, Expression id)
    {
        var name = field.Type switch
        {
            CustomFieldTypes.Number => "ValueNumber",
            CustomFieldTypes.Date => "ValueDate",
            CustomFieldTypes.Boolean => "ValueBool",
            CustomFieldTypes.Lookup => null,
            _ => "ValueText",
        };
        return name is null ? null : Call(name, Boxed(field.Id), id);
    }

    public static IReadOnlyList<ExtraColumn> Build(IReadOnlyList<CustomFieldInfo> fields) =>
        fields.Select(f => new ExtraColumn(f, Column(f), (condition, id) => Condition(f, condition, id))).ToList();

    private static Expression Condition(CustomFieldInfo field, FilterCondition condition, Expression id)
    {
        switch (condition.Operator)
        {
            case FilterOperator.IsNull:
                return Expression.Not(InSet(Call("MatchAny", Expression.Constant(field.Id)), id));
            case FilterOperator.IsNotNull:
                return InSet(Call("MatchAny", Expression.Constant(field.Id)), id);
        }
        if (condition.Values.Count == 1 && condition.Values[0].Kind == FilterValueKind.Null)
        {
            var any = InSet(Call("MatchAny", Expression.Constant(field.Id)), id);
            return condition.Operator == FilterOperator.Eq ? Expression.Not(any) : any;
        }
        Expression One(FilterValue value, FilterOperator op) => InSet(Match(field, op, value), id);
        return condition.Operator switch
        {
            FilterOperator.Ne => Expression.Not(One(condition.Values[0], FilterOperator.Eq)),
            FilterOperator.In => condition.Values.Select(v => One(v, FilterOperator.Eq)).Aggregate(Expression.OrElse),
            var op => One(condition.Values[0], op),
        };
    }

    private static Expression Match(CustomFieldInfo field, FilterOperator op, FilterValue value)
    {
        var id = Expression.Constant(field.Id);
        var name = op switch
        {
            FilterOperator.Eq => "eq",
            FilterOperator.Lt => "lt",
            FilterOperator.Le => "le",
            FilterOperator.Gt => "gt",
            FilterOperator.Ge => "ge",
            FilterOperator.Contains => "contains",
            FilterOperator.StartsWith => "startswith",
            FilterOperator.EndsWith => "endswith",
            _ => throw new ListQueryException("filter", "list.operatorNotAllowed", field.Key),
        };
        switch (field.Type)
        {
            case CustomFieldTypes.Number:
                return Call("MatchNumber", id, Expression.Constant(name), Boxed(decimal.Parse(value.Text!, NumberStyles.Number, CultureInfo.InvariantCulture)));
            case CustomFieldTypes.Date:
                ListFilter.TryDate(value.Text!, out var date);
                return Call("MatchDate", id, Expression.Constant(name), Boxed(date));
            case CustomFieldTypes.Boolean:
                return Call("MatchBool", id, Boxed(value.Text == "true"));
            case CustomFieldTypes.Lookup:
                return Call("MatchRef", id, Boxed(Guid.Parse(value.Text!)));
            case CustomFieldTypes.Select:
                return Call("MatchText", id, Expression.Constant("eq"), Boxed(value.Text));
            default:
                return Call("MatchText", id, Expression.Constant(name), Boxed(value.Text));
        }
    }

    /// <summary>The search for a word: records whose text value in a searchable field matches the pattern.</summary>
    public static Expression Search(IReadOnlyList<ExtraColumn> columns, string pattern, Expression id)
    {
        var fields = columns.Where(c => c.Field is { Searchable: true, Type: CustomFieldTypes.Text }).Select(c => c.Field.Id).ToArray();
        return fields.Length == 0
            ? Expression.Constant(false)
            : InSet(Call("SearchText", Boxed(fields), Boxed(pattern)), id);
    }

    public static bool Searches(IReadOnlyList<ExtraColumn> columns) => columns.Any(c => c.Field is { Searchable: true, Type: CustomFieldTypes.Text });

    private static Expression Call(string method, params Expression[] arguments)
    {
        var info = CustomFieldSql.MethodOf(method);
        var parameters = info.GetParameters();
        return Expression.Call(info, arguments.Select((a, i) => a.Type == parameters[i].ParameterType ? a : Expression.Convert(a, parameters[i].ParameterType)));
    }

    /// <summary>A value held in an object so EF Core sends it as a parameter, not a constant in the SQL text.</summary>
    private static Expression Boxed<TValue>(TValue value) =>
        Expression.Property(Expression.Constant(new ValueBox<TValue>(value)), nameof(ValueBox<TValue>.Value));

    /// <summary><c>id IN (SELECT m.record_id FROM match(…) m)</c>.</summary>
    private static Expression InSet(Expression matches, Expression id)
    {
        var m = Expression.Parameter(typeof(CustomFieldMatch), "m");
        var ids = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [typeof(CustomFieldMatch), typeof(Guid)], matches,
            Expression.Quote(Expression.Lambda<Func<CustomFieldMatch, Guid>>(Expression.Property(m, nameof(CustomFieldMatch.RecordId)), m)));
        return Expression.Call(typeof(Queryable), nameof(Queryable.Contains), [typeof(Guid)], ids, id);
    }
}

/// <summary>A label in both languages that is the tenant's own text (a custom field's name).</summary>
public sealed record DynamicString(string En, string Ar);

/// <summary>A list's definition with the workspace's custom fields added as columns (p09): what the
/// list's definition endpoint answers, and what a saved view is checked against.</summary>
public static class ListCustomFieldDefinitions
{
    public static async Task<(ListDefinition Definition, IReadOnlyDictionary<string, DynamicString> Strings)> ExtendAsync(
        ListDefinition list, IServiceProvider services, CancellationToken cancellationToken)
    {
        var fields = await ListCustomFields.FieldsAsync(services, list.Key, cancellationToken);
        return fields.Count == 0
            ? (list, new Dictionary<string, DynamicString>())
            : (list with { Columns = [.. list.Columns, .. ListCustomFields.Columns(fields)] }, ListCustomFields.Strings(fields));
    }
}
