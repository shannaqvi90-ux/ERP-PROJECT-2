using Erp.Kernel.Lists;

namespace Erp.Kernel.Tests;

public sealed class ModuleListTests
{
    private static ListDefinition Valid() => new(
        "sales.orders", "sales.orders.title", "sales.orders.read", "/api/sales/orders",
        [new ListColumn("number", "sales.orders.number", ListColumnType.Text, Sortable: true), new ListColumn("total", "sales.orders.total", ListColumnType.Money)],
        ["number"], DefaultSort: "-number");

    [Fact]
    public void A_valid_list_has_no_problems() => Assert.Empty(Valid().Problems("sales"));

    [Fact]
    public void A_list_must_belong_to_its_module_and_name_real_columns()
    {
        var problems = (Valid() with
        {
            Key = "other.orders",
            Endpoint = "/orders",
            SearchFields = ["customer"],
            DefaultSort = "total",
            Columns = [new ListColumn("number", "a", ListColumnType.Text), new ListColumn("number", "b", ListColumnType.Text), new ListColumn("total", "c", ListColumnType.Money)],
        }).Problems("sales").ToList();
        Assert.Contains(problems, p => p.Contains("key must look like 'sales.name'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("must be an /api/ route", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("search field 'customer' is not a column", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("default sort 'total' is not a sortable column", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("column 'number' is defined twice", StringComparison.Ordinal));
    }
}
