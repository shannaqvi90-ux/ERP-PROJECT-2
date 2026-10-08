using Erp.Kernel.Lists;

namespace Erp.Kernel.Tests;

/// <summary>The interpreted in-memory query (the list engine's rows-in-memory path) answers every
/// query exactly as LINQ to objects (<c>AsQueryable()</c>) does.</summary>
public sealed class InMemoryQueryTests
{
    private sealed record Row(Guid Id, string Name, string? Code, int Quantity, decimal Amount, bool Active, DateOnly Day, string Currency);

    private static readonly List<Row> Rows = Enumerable.Range(0, 60).Select(i => new Row(
        Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
        i % 4 == 0 ? $"Ünïcode {i % 9}" : $"Row {i % 11}",
        i % 5 == 0 ? null : $"C-{i % 13:D3}",
        i % 9 - 3,
        (i % 4) * 2.5m + i / 100m,
        i % 3 != 0,
        new DateOnly(2026, 1 + i % 12, 1 + i % 27),
        i % 3 == 0 ? "USD" : "AED")).ToList();

    public static TheoryData<string> Queries => new(Cases.Keys);

    private static readonly Dictionary<string, Func<IQueryable<Row>, object>> Cases = new()
    {
        ["where, order, then by, skip, take"] = q => q.Where(r => r.Active && r.Quantity >= 0).OrderBy(r => r.Name).ThenByDescending(r => r.Id).Skip(3).Take(7).Select(r => r.Id).ToList(),
        ["count with a predicate on a nullable text"] = q => q.Where(r => r.Code != null && r.Code.Contains("01", StringComparison.OrdinalIgnoreCase)).Count(),
        ["group with counts and sums"] = q => q.GroupBy(r => r.Day.Month).Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(r => r.Amount) }).OrderBy(g => g.Key).ToList()
            .Select(g => $"{g.Key}:{g.Count}:{g.Sum}").ToList(),
        ["distinct ordered currencies"] = q => q.Select(r => r.Currency).Distinct().OrderBy(c => c).Take(5).ToList(),
        ["order by descending with nulls"] = q => q.OrderByDescending(r => r.Code).ThenBy(r => r.Id).Select(r => r.Code ?? "-").ToList(),
        ["any and first"] = q => new object[] { q.Any(r => r.Amount > 7m), q.OrderBy(r => r.Day).ThenBy(r => r.Id).First().Id },
        ["project to a new record"] = q => q.Where(r => !r.Active).Select(r => new { r.Name, Twice = r.Quantity * 2 }).OrderBy(x => x.Twice).ThenBy(x => x.Name).ToList()
            .Select(x => $"{x.Name}/{x.Twice}").ToList(),
        ["enumerated without operators"] = q => q.ToList().Select(r => r.Id).ToList(),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void Answers_as_linq_to_objects_does(string query)
    {
        var expected = Cases[query](Rows.AsQueryable());
        var actual = Cases[query](InMemoryQuery.Over(Rows.AsQueryable()));
        Assert.Equal(Serialize(expected), Serialize(actual));
    }

    [Fact]
    public void Wraps_a_source_once_and_keeps_its_rows()
    {
        var once = InMemoryQuery.Over(Rows.AsQueryable());
        Assert.Same(once, InMemoryQuery.Over(once));
        Assert.Equal(Rows, once.ToList());
    }

    private static string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}
