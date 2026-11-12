using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>The differential tells a list that moved during a pair (the audit log gaining rows)
/// from a company-Y oracle, and a marker that is the product's own text from a leak.</summary>
public sealed class MovingListsSelfTests
{
    private static string Window(int from, int count, int total, params string[] extra)
    {
        var rows = Enumerable.Range(from, count).Select(i => $"{{\"id\":\"row-{i}\",\"what\":\"changed {i}\"}}").Concat(extra);
        return $"{{\"total\":{total},\"items\":[{string.Join(",", rows)}]}}";
    }

    [Fact]
    public void A_window_that_slides_while_the_pair_is_asked_is_not_an_oracle()
    {
        // Newest first; one row arrives between the asks, then another: every ask differs from the one before.
        string Newest(int n) => $"{{\"total\":{n},\"items\":[{string.Join(",", Enumerable.Range(n - 4, 5).Reverse().Select(i => $"{{\"id\":\"row-{i}\"}}"))}]}}";
        Assert.True(MovingLists.ExplainedByMovement(Newest(100), Newest(101), Newest(101), Newest(102)));
        Assert.True(MovingLists.ExplainedByMovement(Newest(100), Newest(101), Newest(102), Newest(103)));
        Assert.True(MovingLists.ExplainedByMovement(Newest(100), Newest(100), Newest(101), Newest(101)));
        Assert.True(MovingLists.ExplainedByMovement(Window(0, 5, 10), Window(0, 5, 10), Window(0, 5, 10), Window(0, 5, 10)));
    }

    [Fact]
    public void A_row_shown_to_the_value_of_company_Y_alone_is_caught_though_the_list_moves()
    {
        string Newest(int n, params string[] extra) =>
            $"{{\"total\":{n + extra.Length},\"items\":[{string.Join(",", Enumerable.Range(n - 4, 5).Reverse().Select(i => $"{{\"id\":\"row-{i}\"}}").Concat(extra))}]}}";
        const string oracle = "{\"id\":\"y-only\"}";
        Assert.False(MovingLists.ExplainedByMovement(Newest(100, oracle), Newest(101), Newest(101, oracle), Newest(102)));
        // the same row hidden from the value of Y
        Assert.False(MovingLists.ExplainedByMovement(Newest(100), Newest(101, oracle), Newest(101), Newest(102, oracle)));
    }

    [Fact]
    public void A_number_a_text_or_an_order_that_follows_the_side_is_caught()
    {
        // a count one higher for the value of Y, with the list moving as well
        Assert.False(MovingLists.ExplainedByMovement(Window(0, 5, 11), Window(0, 5, 10), Window(0, 5, 11), Window(0, 5, 10)));
        // the same rows in another order for the value of Y
        var forward = "{\"items\":[{\"id\":\"a\"},{\"id\":\"b\"},{\"id\":\"c\"}]}";
        var swapped = "{\"items\":[{\"id\":\"b\"},{\"id\":\"a\"},{\"id\":\"c\"}]}";
        Assert.False(MovingLists.ExplainedByMovement(swapped, forward, swapped, forward));
        // a text that is not JSON is never explained
        Assert.False(MovingLists.ExplainedByMovement("a", "b", "a", "b"));
    }

    [Fact]
    public void A_text_the_product_ships_to_every_tenant_is_not_a_marker_but_one_that_contains_it_is()
    {
        const string shipped = "تنظيف المهام المنتهية";
        var strings = ProductStrings.FromRepository();
        Assert.True(strings.Count > 100, "the repository's resource files and web strings were not read");
        Assert.True(strings.Contains(shipped), "the Arabic name of the clean-up job type is product text");
        Assert.True(strings.Contains("  " + shipped + " "));

        var kept = strings.LeaveOut([shipped, "Tenant A private note about " + shipped, "تنظيف المهام المنتهية شركة الفجر", shipped + "!"], out var leftOut);
        Assert.Equal(1, leftOut);
        Assert.Equal(3, kept.Count);
        Assert.DoesNotContain(shipped, kept);
        Assert.Contains("Tenant A private note about " + shipped, kept);
    }

    [Fact]
    public void A_paging_number_is_a_window_not_a_marker_but_a_total_that_follows_the_side_is_still_caught()
    {
        Assert.True(MovingLists.SendsAsPagingValue("GET /api/audit/log?take=20002", "20002"));
        Assert.True(MovingLists.SendsAsPagingValue("GET /api/audit/log?filter=x&skip=20002", "20002"));
        Assert.False(MovingLists.SendsAsPagingValue("GET /api/audit/log?search=20002", "20002"));
        Assert.False(MovingLists.SendsAsPagingValue("GET /api/audit/log?take=20002", "20003"));
        Assert.False(MovingLists.SendsAsPagingValue("GET /api/audit/log", "20002"));

        // Two windows of a log that grew by a row between asks: other rows, one total that only grows.
        string Log(int total, params int[] rows) =>
            $"{{\"total\":{total},\"next\":\"c{total}\",\"items\":[{string.Join(",", rows.Select(i => $"{{\"id\":\"r{i}\",\"what\":\"w\"}}"))}]}}";
        Assert.True(MovingLists.PagingExplainedByMovement(Log(300, 1, 2), Log(301, 3, 4), Log(301, 5, 6), Log(302, 7, 8)));
        Assert.True(MovingLists.PagingExplainedByMovement(Log(0), Log(0, 1), Log(0), Log(0, 1)));
        // A total that is higher for the value and lower for the control, twice: the value selects rows.
        Assert.False(MovingLists.PagingExplainedByMovement(Log(301, 1), Log(300, 1), Log(301, 1), Log(300, 1)));
        // A property only the value's answers hold, another kind of answer, a text that is not JSON.
        Assert.False(MovingLists.PagingExplainedByMovement(Log(300, 1).Replace("\"next\"", "\"other\""), Log(300, 1), Log(300, 1).Replace("\"next\"", "\"other\""), Log(300, 1)));
        Assert.False(MovingLists.PagingExplainedByMovement("[]", Log(300, 1), "[]", Log(300, 1)));
        Assert.False(MovingLists.PagingExplainedByMovement("a", "b", "a", "b"));
    }
}
