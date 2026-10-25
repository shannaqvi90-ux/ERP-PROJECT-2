using Erp.Modules.Currency.Rates;
using Erp.Modules.Currency.Seeding;

namespace Erp.Modules.Currency.Tests;

/// <summary>The rate file reader (no database): columns by name, delimiters, quoting, both date
/// forms, and every problem reported with its line.</summary>
public sealed class RateFileTests
{
    private static readonly Dictionary<string, bool> Enabled = new(StringComparer.Ordinal) { ["AED"] = true, ["USD"] = true, ["EUR"] = true, ["GBP"] = false };

    [Fact]
    public void Reads_columns_by_name_with_any_delimiter_and_both_date_forms()
    {
        var comma = RateFile.Parse("currency,date,rate\nEUR,2026-10-23,4.2875\nusd,23/10/2026,3.6725\n", "AED", Enabled);
        Assert.Empty(comma.Errors);
        Assert.Equal(2, comma.Lines.Count);
        Assert.Equal(("EUR", "AED", new DateOnly(2026, 10, 23), 4.2875m), (comma.Lines[0].Currency, comma.Lines[0].Quote, comma.Lines[0].Date, comma.Lines[0].Rate));
        Assert.Equal(("USD", new DateOnly(2026, 10, 23)), (comma.Lines[1].Currency, comma.Lines[1].Date));

        var semicolon = RateFile.Parse("﻿Rate;Currency;Date;Reference\r\n4.2875;EUR;2026-10-23;\"CBUAE; 23 Oct\"\r\n", "AED", Enabled);
        Assert.Empty(semicolon.Errors);
        Assert.Equal("CBUAE; 23 Oct", semicolon.Lines.Single().Reference);

        var tab = RateFile.Parse("quote_currency\tcurrency\tdate\trate\nEUR\tUSD\t2026-10-23\t1.16748\n", "AED", Enabled);
        Assert.Empty(tab.Errors);
        Assert.Equal(("USD", "EUR"), (tab.Lines.Single().Currency, tab.Lines.Single().Quote));
    }

    [Fact]
    public void Every_problem_is_reported_with_its_line_and_nothing_is_read_from_a_bad_file()
    {
        var text = string.Join('\n',
            "currency,date,rate",
            "EUR,2026-10-23,4.2875",
            "ZZZ,2026-10-23,1",
            "GBP,2026-10-23,4.9",
            "JPY,2026-10-23,0.024",
            "EUR,2026-13-01,4.2",
            "EUR,2026-10-24,-1",
            "EUR,2026-10-25,4.123456789",
            "EUR,2026-10-23,4.3",
            "AED,2026-10-23,1",
            "",
            "EUR,1989-12-31,4");
        var parsed = RateFile.Parse(text, "AED", Enabled);
        Assert.Equal(
            [(3, "currencyImportCode"), (4, "currencyImportInactive"), (5, "currencyImportNotEnabled"), (6, "currencyImportDate"), (7, "currencyImportRate"),
             (8, "currencyImportRate"), (9, "currencyImportRepeated"), (10, "currencyImportSameAsQuote"), (12, "currencyImportDate")],
            parsed.Errors.Select(e => (e.Line, e.Code)).ToArray());
        Assert.Equal("2", parsed.Errors.Single(e => e.Code == "currencyImportRepeated").Value);
    }

    [Fact]
    public void A_file_without_the_required_columns_or_rows_is_refused()
    {
        Assert.Equal(["currencyImportColumnMissing"], RateFile.Parse("code,day,value\nEUR,2026-10-23,4\n", "AED", Enabled).Errors.Select(e => e.Code).Distinct());
        Assert.Equal(["date", "rate"], RateFile.Parse("currency\nEUR\n", "AED", Enabled).Errors.Select(e => e.Value));
        Assert.Equal("currencyImportEmpty", RateFile.Parse("currency,date,rate\n\n", "AED", Enabled).Errors.Single().Code);
        Assert.Equal("currencyImportEmpty", RateFile.Parse("  \n", "AED", Enabled).Errors.Single().Code);
    }

    [Fact]
    public void A_file_longer_than_the_limit_stops_at_the_limit()
    {
        var lines = Enumerable.Range(0, CurrencyRules.ImportRowLimit + 5).Select(i => $"EUR,{new DateOnly(2000, 1, 1).AddDays(i):yyyy-MM-dd},4.1");
        var parsed = RateFile.Parse("currency,date,rate\n" + string.Join('\n', lines), "AED", Enabled);
        Assert.Equal(CurrencyRules.ImportRowLimit, parsed.Lines.Count);
        Assert.Equal("currencyImportTooManyLines", parsed.Errors.Single().Code);
    }

    [Fact]
    public void Quoted_values_keep_delimiters_and_doubled_quotes()
    {
        Assert.Equal(["a,b", "say \"hi\"", "", "x"], RateFile.Split("\"a,b\",\"say \"\"hi\"\"\",,x", ','));
    }

    [Fact]
    public void The_demo_rates_are_the_same_every_run_positive_and_the_dollar_keeps_its_peg()
    {
        var last = new DateOnly(2026, 10, 24);
        var first = CurrencySeeder.GeneratedRates(CurrencySeeder.DemoCurrencies, 2000, last, "demo").ToList();
        var second = CurrencySeeder.GeneratedRates(CurrencySeeder.DemoCurrencies, 2000, last, "demo").ToList();
        Assert.Equal(100_000, first.Count);
        Assert.Equal(first, second);
        Assert.All(first, r => Assert.True(CurrencyRules.IsValidRate(r.Rate), $"{r.Currency} {r.Date}: {r.Rate}"));
        Assert.All(first.Where(r => r.Currency == "USD"), r => Assert.Equal(3.6725m, r.Rate));
        Assert.Equal(100_000, first.Select(r => (r.Currency, r.Date)).Distinct().Count());
        Assert.Equal(last, first.Max(r => r.Date));
        Assert.Equal(last.AddDays(-1999), first.Min(r => r.Date));
        // A walk, not a constant: the euro moves, and stays near where it started.
        var euro = first.Where(r => r.Currency == "EUR").Select(r => r.Rate).ToList();
        Assert.True(euro.Distinct().Count() > 1000);
        Assert.All(euro, r => Assert.InRange(r, 3.98m * 0.8m, 3.98m * 1.2m));
    }

    [Fact]
    public void The_shared_dataset_file_is_read_as_written()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rates-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "currency,date,rate\nUSD,2026-09-30,3.672500\nEUR,2026-09-30,3.981234\n");
        try
        {
            var rows = SharedDatasetRates.Read(path);
            Assert.Equal([("USD", new DateOnly(2026, 9, 30), 3.6725m), ("EUR", new DateOnly(2026, 9, 30), 3.981234m)], rows.ToArray());
            File.WriteAllText(path, "currency,date,rate\nUSD,30/09/2026,3.67\n");
            Assert.Throws<InvalidDataException>(() => SharedDatasetRates.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Throws<FileNotFoundException>(() => SharedDatasetRates.Read(path));
    }

    [Fact]
    public void The_English_names_come_from_the_same_ISO_list_as_the_kernel_codes()
    {
        Assert.Equal(Erp.Kernel.Money.Iso4217.Published, Erp.Modules.Currency.Currencies.Iso4217Names.Published);
        Assert.Equal(Erp.Kernel.Money.Iso4217.Currencies.Keys.Order(StringComparer.Ordinal), Erp.Modules.Currency.Currencies.Iso4217Names.English.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("UAE Dirham", Erp.Modules.Currency.Currencies.Iso4217Names.English["AED"]);
        // Every current ISO 4217 currency has the product's Arabic name, in Arabic letters, and no other code does.
        Assert.Equal(Erp.Kernel.Money.Iso4217.Currencies.Keys.Order(StringComparer.Ordinal), Erp.Modules.Currency.Currencies.CurrencyNames.Arabic.Keys.Order(StringComparer.Ordinal));
        Assert.All(Erp.Modules.Currency.Currencies.CurrencyNames.Arabic, pair =>
        {
            Assert.True(Erp.Kernel.Money.CurrencyCode.IsKnown(pair.Key), pair.Key);
            Assert.Matches(@"\p{IsArabic}", pair.Value);
        });
        Assert.All(CurrencySeeder.DemoCurrencies, c => Assert.True(Erp.Modules.Currency.Currencies.CurrencyNames.Arabic.ContainsKey(c.Code), c.Code));
    }
}
