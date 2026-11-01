namespace Erp.Gates.Tests.G1;

/// <summary>The route values the HTTP attack leaves out of its repeated requests stand for
/// classes (<see cref="RouteValueClasses"/>); these tests pin what a class keeps.</summary>
public sealed class RouteValueClassesTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<Guid>> Tables(int tables, int idsEach) =>
        Enumerable.Range(0, tables).ToDictionary(t => $"s.table{t}", t => (IReadOnlyList<Guid>)Enumerable.Range(0, idsEach).Select(_ => Guid.NewGuid()).ToList());

    [Fact]
    public void Every_table_keeps_its_first_middle_and_last_id_and_the_tenant_and_its_code_stay()
    {
        var tables = Tables(17, 9);
        var tenant = Guid.NewGuid();
        var all = tables.Values.SelectMany(ids => ids).Select(i => i.ToString()).Append(tenant.ToString()).Append("bravo1").ToList();
        var kept = RouteValueClasses.Representatives(all, RouteValueClasses.ClassifierFor(tables, tenant, "bravo1"));
        Assert.Contains(tenant.ToString(), kept);
        Assert.Contains("bravo1", kept);
        foreach (var (table, ids) in tables)
        {
            Assert.Contains(ids[0].ToString(), kept);
            Assert.Contains(ids[ids.Count / 2].ToString(), kept);
            Assert.Contains(ids[^1].ToString(), kept);
            Assert.Equal(3, kept.Count(k => ids.Any(i => i.ToString() == k)));
        }
        Assert.Equal(17 * 3 + 2, kept.Count);
    }

    [Fact]
    public void A_class_of_three_or_fewer_is_kept_whole_and_order_is_unchanged()
    {
        var tables = Tables(2, 3);
        var all = tables.Values.SelectMany(ids => ids).Select(i => i.ToString()).ToList();
        var kept = RouteValueClasses.Representatives(all, RouteValueClasses.ClassifierFor(tables, Guid.NewGuid(), "x"));
        Assert.Equal(all, kept);
    }

    [Fact]
    public void Ids_that_are_not_in_any_table_are_one_class_and_text_is_another()
    {
        var tables = Tables(1, 2);
        var created = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid().ToString()).ToList();
        var text = new[] { "a", "b", "c", "d", "e" };
        var kept = RouteValueClasses.Representatives(created.Concat(text).ToList(), RouteValueClasses.ClassifierFor(tables, Guid.NewGuid(), "code"));
        Assert.Equal(3, kept.Count(created.Contains));
        Assert.Equal(3, kept.Count(text.Contains));
    }
}
