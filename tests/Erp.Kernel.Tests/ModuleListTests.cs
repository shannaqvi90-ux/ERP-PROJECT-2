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

    [Fact]
    public void A_flag_column_may_name_its_two_values_and_nothing_else()
    {
        ListDefinition With(params ListChoice[] choices) => Valid() with
        {
            Columns = [.. Valid().Columns, new ListColumn("isOpen", "sales.orders.open", ListColumnType.Boolean, Filterable: true, Choices: choices)],
        };
        Assert.Empty(With(new ListChoice("true", "sales.orders.open"), new ListChoice("false", "sales.orders.closed")).Problems("sales"));
        Assert.Contains(With(new ListChoice("yes", "a"), new ListChoice("no", "b")).Problems("sales"), p => p.Contains("may only name its values 'true' and 'false'", StringComparison.Ordinal));
        Assert.Contains(With(new ListChoice("true", "a")).Problems("sales"), p => p.Contains("has choices but is not a choice column", StringComparison.Ordinal));
        var text = Valid() with { Columns = [.. Valid().Columns, new ListColumn("note", "n", ListColumnType.Text, Choices: [new ListChoice("true", "a"), new ListChoice("false", "b")])] };
        Assert.Contains(text.Problems("sales"), p => p.Contains("has choices but is not a choice column", StringComparison.Ordinal));
    }
}
