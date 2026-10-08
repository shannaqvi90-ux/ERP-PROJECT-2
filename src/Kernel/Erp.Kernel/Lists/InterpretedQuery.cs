using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace Erp.Kernel.Lists;

/// <summary>
/// Runs a list query over rows held in memory (a small, bounded list such as roles) without
/// compiling it. LINQ's own in-memory provider (<see cref="EnumerableQuery{T}"/>) turns every
/// query it runs into IL and compiles it, about a millisecond or more of processor time each, and a
/// list query runs two or three of them (the count, the page, the groups) on every request. This
/// provider rewrites the same query to <see cref="Enumerable"/> calls and runs it with the
/// expression interpreter instead; the rows, their order and every value are the same.
/// </summary>
internal static class InterpretedQuery
{
    /// <summary>The same query over the same rows, run by interpretation.</summary>
    public static IQueryable<T> Of<T>(IQueryable<T> source) =>
        source.Provider is Provider ? source : new Query<T>(Provider.Instance, source.Expression);

    private sealed class Query<T>(Provider provider, Expression expression) : IOrderedQueryable<T>
    {
        public Type ElementType => typeof(T);

        public Expression Expression => expression;

        public IQueryProvider Provider => provider;

        public IEnumerator<T> GetEnumerator() => Provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Provider : IQueryProvider
    {
        public static readonly Provider Instance = new();

        public IQueryable CreateQuery(Expression expression) =>
            (IQueryable)Activator.CreateInstance(typeof(Query<>).MakeGenericType(ElementTypeOf(expression.Type)), this, expression)!;

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new Query<TElement>(this, expression);

        public object? Execute(Expression expression) => Run(expression);

        public TResult Execute<TResult>(Expression expression) => (TResult)Run(expression)!;

        private static object? Run(Expression expression)
        {
            var body = new ToEnumerable().Visit(expression);
            return Expression.Lambda<Func<object?>>(Expression.Convert(body, typeof(object))).Compile(preferInterpretation: true)();
        }

        private static Type ElementTypeOf(Type sequence) =>
            sequence.IsGenericType && sequence.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? sequence.GetGenericArguments()[0]
                : sequence.GetInterfaces().Append(sequence)
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];
    }

    /// <summary>Queryable calls become the Enumerable calls of the same name and shape, with their
    /// lambdas unquoted; the in-memory rows (an <see cref="EnumerableQuery{T}"/> constant) become a
    /// plain sequence.</summary>
    private sealed class ToEnumerable : ExpressionVisitor
    {
        // Read once, never changed: process-wide state that holds no request's data.
        private static readonly FrozenDictionary<string, ImmutableArray<MethodInfo>> EnumerableMethods =
            typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .GroupBy(m => m.Name, StringComparer.Ordinal)
                .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.Ordinal);

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IQueryable { Provider: not Provider } rows && node.Type.IsAssignableTo(typeof(IQueryable)))
            {
                var element = rows.ElementType;
                return Expression.Constant(rows, typeof(IEnumerable<>).MakeGenericType(element));
            }
            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(Queryable))
            {
                return base.VisitMethodCall(node);
            }
            var arguments = node.Arguments.Select(a => Visit(StripQuote(a))).ToArray();
            var method = Counterpart(node.Method, arguments)
                         ?? throw new NotSupportedException($"No in-memory counterpart of Queryable.{node.Method.Name} for these arguments.");
            return Expression.Call(method, arguments);
        }

        private static Expression StripQuote(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

        private static MethodInfo? Counterpart(MethodInfo queryable, Expression[] arguments)
        {
            var typeArguments = queryable.IsGenericMethod ? queryable.GetGenericArguments() : [];
            foreach (var candidate in EnumerableMethods.GetValueOrDefault(queryable.Name, []))
            {
                if (candidate.GetParameters().Length != arguments.Length ||
                    candidate.IsGenericMethodDefinition != queryable.IsGenericMethod ||
                    (candidate.IsGenericMethodDefinition && candidate.GetGenericArguments().Length != typeArguments.Length))
                {
                    continue;
                }
                MethodInfo closed;
                try
                {
                    closed = candidate.IsGenericMethodDefinition ? candidate.MakeGenericMethod(typeArguments) : candidate;
                }
                catch (ArgumentException)
                {
                    continue;
                }
                var parameters = closed.GetParameters();
                if (parameters.Select((p, i) => p.ParameterType.IsAssignableFrom(arguments[i].Type)).All(fits => fits))
                {
                    return closed;
                }
            }
            return null;
        }
    }
}
