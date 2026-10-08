using System.Collections;
using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<MethodInfo, MethodInfo> Counterparts = new();

    /// <summary>The query's result, run over the rows.</summary>
    internal static TResult Run<TResult>(Expression expression) =>
        Expression.Lambda<Func<TResult>>(new Rewriter().Visit(expression)).Compile(preferInterpretation: true)();

    /// <summary>The query's result as an object, run over the rows.</summary>
    internal static object? Run(Expression expression) =>
        Expression.Lambda(Expression.Convert(new Rewriter().Visit(expression), typeof(object))).Compile(preferInterpretation: true).DynamicInvoke();

    /// <summary>The <see cref="Enumerable"/> method a <see cref="Queryable"/> method stands for
    /// (same name, generic arguments and parameters, with queries as sequences and lambda
    /// expressions as delegates).</summary>
    private static MethodInfo Counterpart(MethodInfo queryable) => Counterparts.GetOrAdd(queryable, static method =>
    {
        var definition = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
        var arguments = method.IsGenericMethod ? method.GetGenericArguments() : [];
        var wanted = method.GetParameters().Select(p => AsEnumerable(p.ParameterType)).ToArray();
        foreach (var candidate in typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != definition.Name || candidate.IsGenericMethodDefinition != method.IsGenericMethod ||
                (candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length != arguments.Length))
            {
                continue;
            }
            var closed = candidate.IsGenericMethodDefinition ? candidate.MakeGenericMethod(arguments) : candidate;
            var parameters = closed.GetParameters();
            if (parameters.Length == wanted.Length && parameters.Select(p => p.ParameterType).SequenceEqual(wanted))
            {
                return closed;
            }
        }
        throw new NotSupportedException($"Queryable.{method.Name} has no Enumerable counterpart for an in-memory list");
    });

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
