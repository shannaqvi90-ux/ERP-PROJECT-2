using Erp.Kernel.Lists;

namespace Erp.Kernel.Tests;

public sealed class ModuleListTests
{
    private static ListDefinition Valid() => new(
        "sales.orders", "sales.orders.title", "sales.orders.read", "/api/sales/orders",
        [
            new ListColumn("number", "sales.orders.number", ListColumnType.Text, Sortable: true),
            new ListColumn("total", "sales.orders.total", ListColumnType.Money, CurrencyField: "currency"),
            new ListColumn("currency", "sales.orders.currency", ListColumnType.Text),
        ],
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
        Assert.Contains(problems, p => p.Contains("money column 'total' names no currency column", StringComparison.Ordinal));
    }

    /// <summary>CLAUDE.md rule 2: an amount never travels without its currency. A money column
    /// names the column holding its currency, which must be a text or choice column of the list.</summary>
    [Fact]
    public void A_money_column_names_its_currency_column()
    {
        var list = Valid();
        var noCurrency = list with { Columns = [list.Columns[0], new ListColumn("total", "c", ListColumnType.Money, Aggregate: true)] };
        Assert.Contains(noCurrency.Problems("sales"), p => p.Contains("money column 'total' names no currency column", StringComparison.Ordinal));

        var missing = list with { Columns = [list.Columns[0], new ListColumn("total", "c", ListColumnType.Money, CurrencyField: "ccy")] };
        Assert.Contains(missing.Problems("sales"), p => p.Contains("names currency column 'ccy', which is not a text or choice column", StringComparison.Ordinal));

        var numberCurrency = list with { Columns = [list.Columns[0], list.Columns[1], new ListColumn("currency", "d", ListColumnType.Number)] };
        Assert.Contains(numberCurrency.Problems("sales"), p => p.Contains("names currency column 'currency', which is not a text or choice column", StringComparison.Ordinal));

        var notMoney = list with { Columns = [list.Columns[0], list.Columns[2], new ListColumn("total", "c", ListColumnType.Number, CurrencyField: "currency")] };
        Assert.Contains(notMoney.Problems("sales"), p => p.Contains("column 'total' names a currency column but is not a money column", StringComparison.Ordinal));
    }
}
