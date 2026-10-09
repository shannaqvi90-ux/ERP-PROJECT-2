using Erp.Kernel.Lists;

namespace Erp.Kernel.Tests;

/// <summary>
/// Lists held in memory run their queries by interpretation (<see cref="InMemoryQuery"/>):
/// the same rows in the same order as LINQ's own in-memory provider, without compiling a method
/// for every query.
/// </summary>
public sealed class InMemoryQueryAgainstCompiledTests
{
    public sealed record Row(Guid Id, string Name, string? Code, int Quantity, decimal? Amount, bool Active, DateOnly Day);

    private static readonly List<Row> Rows = Enumerable.Range(0, 60).Select(i => new Row(
        Guid.CreateVersion7(DateTimeOffset.UnixEpoch.AddDays(i)),
        i % 3 == 0 ? $"صنف {i}" : $"Item {i:00}",
        i % 5 == 0 ? null : $"C-{i % 7}",
        i % 9,
        i % 4 == 0 ? null : i * 1.25m,
        i % 2 == 0,
        new DateOnly(2026, 1, 1).AddDays(i % 11))).ToList();

    public static TheoryData<string> Queries() => ["where", "order", "page", "count", "group", "select", "any", "sum", "keyset"];

    private static object Run(string name, IQueryable<Row> rows) => name switch
    {
        "where" => rows.Where(r => r.Active && r.Code != null && r.Code.StartsWith("C-")).Select(r => r.Id).ToList(),
        "order" => rows.OrderBy(r => r.Code).ThenByDescending(r => r.Quantity).ThenBy(r => r.Id).Select(r => r.Id).ToList(),
        "page" => rows.OrderBy(r => r.Name).Skip(7).Take(11).Select(r => r.Name).ToList(),
        "count" => rows.Count(r => r.Amount > 10m),
        "group" => rows.GroupBy(r => r.Day).Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(r => r.Amount) }).OrderBy(g => g.Key).ToList()
            .Select(g => $"{g.Key}:{g.Count}:{g.Sum}").ToList(),
        "select" => rows.Where(r => r.Name.Contains("صنف")).Select(r => new { r.Id, Twice = r.Quantity * 2 }).ToList().Select(x => $"{x.Id}:{x.Twice}").ToList(),
        "any" => rows.Any(r => r.Quantity == 8 && !r.Active),
        "sum" => rows.Where(r => r.Active).Sum(r => r.Amount) ?? 0m,
        "keyset" => rows.Where(r => r.Quantity > 3 || (r.Quantity == 3 && r.Id.CompareTo(Rows[30].Id) > 0)).OrderBy(r => r.Quantity).ThenBy(r => r.Id).Take(5)
            .Select(r => r.Id).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void The_same_rows_in_the_same_order_as_the_compiled_in_memory_query(string name)
    {
        var compiled = Run(name, Rows.AsQueryable());
        var interpreted = Run(name, InMemoryQuery.Over(Rows.AsQueryable()));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(compiled), System.Text.Json.JsonSerializer.Serialize(interpreted));
    }

    [Fact]
    public void Running_a_query_again_compiles_no_method()
    {
        var query = InMemoryQuery.Over(Rows.AsQueryable());
        // Warm up: the interpreter's own code is compiled once, like any other code.
        for (var i = 0; i < 3; i++)
        {
            _ = query.Where(r => r.Quantity > i).OrderBy(r => r.Name).Skip(1).Take(5).ToList();
            _ = query.Count(r => r.Active);
        }
        var before = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true);
        for (var i = 0; i < 25; i++)
        {
            _ = query.Where(r => r.Quantity > i % 9).OrderBy(r => r.Name).Skip(1).Take(5).ToList();
            _ = query.Count(r => r.Active);
        }
        var compiled = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - before;
        // LINQ's own provider compiles at least one method for each of these 50 queries.
        Assert.True(compiled < 10, $"{compiled} methods compiled while running 50 queries");
    }

    [Fact]
    public void A_query_already_interpreted_is_kept_and_its_operators_stay_interpreted()
    {
        var once = InMemoryQuery.Over(Rows.AsQueryable());
        Assert.Same(once, InMemoryQuery.Over(once));
        var narrowed = once.Where(r => r.Active).OrderBy(r => r.Name);
        Assert.IsType<InMemoryQuery<Row>>(narrowed.Provider);
        Assert.Equal(Rows.Where(r => r.Active).OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => r.Id),
            narrowed.Select(r => r.Id).ToList().OrderBy(id => Rows.Single(r => r.Id == id).Name, StringComparer.Ordinal));
    }
}
