using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Erp.Kernel.Lists;

/// <summary>
/// Runs a list query over rows held in memory (a small, bounded list such as the roles) the way
/// <c>AsQueryable()</c> does, with the same operators and the same results, but interprets each
/// query instead of compiling it. <see cref="EnumerableQuery{T}"/> turns every query it runs into
/// a new dynamic method that the runtime then compiles to machine code: a list request (its count,
/// its groups and its page) paid for three such compilations, every time, for a few dozen rows.
/// Here the query's <see cref="Queryable"/> calls are rewritten to the matching
/// <see cref="Enumerable"/> calls (lambdas passed as delegates) and the result is run by the
/// expression interpreter (<c>Compile(preferInterpretation: true)</c>), which costs a small
/// fraction of that for lists of this size.
/// </summary>
internal static class InMemoryQuery
{
    /// <summary>The rows of an in-memory source as a query this provider runs.</summary>
    public static IQueryable<T> Over<T>(IQueryable<T> source) =>
        source is InMemoryQuery<T> ? source : new InMemoryQuery<T>(source.AsEnumerable());

    /// <summary>Every <see cref="Queryable"/> method (generic definition) with the <see cref="Enumerable"/>
    /// method it stands for (same name, generic arity and parameters, with queries as sequences and
    /// lambda expressions as delegates). Worked out once and never changed: nothing a request does
    /// adds process-wide state (the process-state gate judges every static).</summary>
    private static readonly System.Collections.Frozen.FrozenDictionary<MethodInfo, MethodInfo> Counterparts = BuildCounterparts();

    // An explicit static constructor: the map is built when Prepare runs (a list registering as
    // in memory, at start-up), never later inside a request.
    static InMemoryQuery()
    {
    }

    /// <summary>Builds the map now (called when a list registers as in memory, at start-up).</summary>
    public static void Prepare()
    {
    }

    private static System.Collections.Frozen.FrozenDictionary<MethodInfo, MethodInfo> BuildCounterparts()
    {
        var enumerable = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .GroupBy(m => Signature(m, m.GetParameters().Select(p => p.ParameterType)))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var map = new Dictionary<MethodInfo, MethodInfo>();
        foreach (var method in typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (enumerable.TryGetValue(Signature(method, method.GetParameters().Select(p => AsEnumerable(p.ParameterType))), out var counterpart))
            {
                map[method] = counterpart;
            }
        }
        return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(map);
    }

    /// <summary>A method's name, generic arity and parameter types, with its generic parameters
    /// written by position (so Queryable.Where&lt;T&gt; and Enumerable.Where&lt;T&gt; compare equal).</summary>
    private static string Signature(MethodInfo method, IEnumerable<Type> parameters) =>
        $"{method.Name}`{(method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0)}({string.Join(",", parameters.Select(TypeName))})";

    private static string TypeName(Type type) =>
        type.IsGenericMethodParameter ? "!!" + type.GenericParameterPosition
        : type.IsArray ? TypeName(type.GetElementType()!) + "[]"
        : type.IsByRef ? TypeName(type.GetElementType()!) + "&"
        : type.IsGenericType ? $"{type.GetGenericTypeDefinition().FullName}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>"
        : type.FullName ?? type.Name;

    /// <summary>The query's result, run over the rows.</summary>
    internal static TResult Run<TResult>(Expression expression) =>
        Expression.Lambda<Func<TResult>>(new Rewriter().Visit(expression)).Compile(preferInterpretation: true)();

    /// <summary>The query's result as an object, run over the rows.</summary>
    internal static object? Run(Expression expression) =>
        Expression.Lambda(Expression.Convert(new Rewriter().Visit(expression), typeof(object))).Compile(preferInterpretation: true).DynamicInvoke();

    /// <summary>The <see cref="Enumerable"/> method a <see cref="Queryable"/> method call stands for.</summary>
    private static MethodInfo Counterpart(MethodInfo queryable)
    {
        var definition = queryable.IsGenericMethod ? queryable.GetGenericMethodDefinition() : queryable;
        if (!Counterparts.TryGetValue(definition, out var counterpart))
        {
            throw new NotSupportedException($"Queryable.{queryable.Name} has no Enumerable counterpart for an in-memory list");
        }
        return queryable.IsGenericMethod ? counterpart.MakeGenericMethod(queryable.GetGenericArguments()) : counterpart;
    }

    /// <summary><c>IQueryable&lt;X&gt;</c> as <c>IEnumerable&lt;X&gt;</c>, <c>IOrderedQueryable&lt;X&gt;</c> as
    /// <c>IOrderedEnumerable&lt;X&gt;</c>, <c>Expression&lt;F&gt;</c> as <c>F</c>; other types as they are.</summary>
    private static Type AsEnumerable(Type type)
    {
        if (!type.IsGenericType)
        {
            return type;
        }
        var definition = type.GetGenericTypeDefinition();
        var argument = type.GetGenericArguments()[0];
        if (definition == typeof(IQueryable<>))
        {
            return typeof(IEnumerable<>).MakeGenericType(argument);
        }
        if (definition == typeof(IOrderedQueryable<>))
        {
            return typeof(IOrderedEnumerable<>).MakeGenericType(argument);
        }
        if (definition == typeof(Expression<>))
        {
            return argument;
        }
        return type;
    }

    private sealed class Rewriter : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node) =>
            node.Value is IInMemoryRows rows ? Expression.Constant(rows.Rows, typeof(IEnumerable<>).MakeGenericType(rows.ElementType)) : node;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(Queryable))
            {
                return base.VisitMethodCall(node);
            }
            var arguments = node.Arguments.Select(a => Unquote(Visit(a))).ToList();
            return Expression.Call(Counterpart(node.Method), arguments);
        }

        private static Expression Unquote(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;
    }
}

/// <summary>The rows at the root of an in-memory query.</summary>
internal interface IInMemoryRows
{
    IEnumerable Rows { get; }

    Type ElementType { get; }
}

/// <summary>A query over rows in memory, run by <see cref="InMemoryQuery"/>.</summary>
internal sealed class InMemoryQuery<T> : IOrderedQueryable<T>, IQueryProvider, IInMemoryRows
{
    private readonly IEnumerable<T>? _rows;

    public InMemoryQuery(IEnumerable<T> rows)
    {
        _rows = rows;
        Expression = Expression.Constant(this);
    }

    private InMemoryQuery(Expression expression) => Expression = expression;

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider => this;

    IEnumerable IInMemoryRows.Rows => _rows ?? throw new InvalidOperationException("not the root of an in-memory query");

    public IEnumerator<T> GetEnumerator() =>
        (_rows ?? InMemoryQuery.Run<IEnumerable<T>>(Expression)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IQueryable CreateQuery(Expression expression)
    {
        var element = expression.Type.GetInterfaces().Append(expression.Type)
            .First(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
        return (IQueryable)typeof(InMemoryQuery<>).MakeGenericType(element)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(Expression)])!.Invoke([expression]);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new InMemoryQuery<TElement>(expression);

    public object? Execute(Expression expression) => InMemoryQuery.Run(expression);

    public TResult Execute<TResult>(Expression expression) => InMemoryQuery.Run<TResult>(expression);
}
