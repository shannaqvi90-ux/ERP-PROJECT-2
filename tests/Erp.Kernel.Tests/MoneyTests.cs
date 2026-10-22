using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Money;
using Microsoft.EntityFrameworkCore;

namespace Erp.Kernel.Tests;

public sealed class MoneyTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Kwd = new("KWD");

    [Fact]
    public void Base_amount_is_rounded_half_away_from_zero_to_the_base_currency()
    {
        var money = Money.Money.Create(100.05m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(100.05m, money.Amount);
        Assert.Equal("USD", money.Currency);
        Assert.Equal(3.6725m, money.ExchangeRate);
        Assert.Equal(367.43m, money.BaseAmount); // 367.433625
        Assert.Equal(-367.43m, money.Negate().BaseAmount);
    }

    /// <summary>docs/compliance/vat-rounding-and-currency.md, rule 1 (owner-signed 2026-10-10):
    /// dirham amounts round to the fils half away from zero; 0.005 becomes 0.01 and -0.005 becomes
    /// -0.01. Banker's rounding (half to even) would give 0.02 for 0.025; this rule gives 0.03.</summary>
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("0.015", "0.02")]
    [InlineData("-0.015", "-0.02")]
    [InlineData("0.025", "0.03")]
    [InlineData("-0.025", "-0.03")]
    [InlineData("0.0049999", "0.00")]
    [InlineData("-0.0049999", "0.00")]
    [InlineData("367.433625", "367.43")]
    [InlineData("12.00975", "12.01")]
    public void Dirham_amounts_round_to_the_fils_half_away_from_zero(string figure, string expected)
    {
        var value = decimal.Parse(figure, System.Globalization.CultureInfo.InvariantCulture);
        var rounded = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(rounded, Money.Money.RoundToMinorUnits(value, CurrencyCode.Aed));
    }

    /// <summary>The same rule reaches the base amount Money computes: a midpoint in dirhams rounds
    /// away from zero, and a negative amount mirrors the positive one.</summary>
    [Fact]
    public void Base_amounts_at_a_fils_midpoint_round_away_from_zero_and_mirror()
    {
        Assert.Equal(0.01m, Money.Money.Create(0.01m, Usd, 0.5m, CurrencyCode.Aed).BaseAmount); // 0.005
        Assert.Equal(-0.01m, Money.Money.Create(-0.01m, Usd, 0.5m, CurrencyCode.Aed).BaseAmount); // -0.005
        Assert.Equal(0.01m, Money.Money.Create(0.001m, Kwd, 5m, CurrencyCode.Aed).BaseAmount); // 0.005
        Assert.Equal(-0.01m, Money.Money.Create(0.001m, Kwd, 5m, CurrencyCode.Aed).Negate().BaseAmount);
        Assert.Equal(0.03m, Money.Money.Create(0.05m, Usd, 0.5m, CurrencyCode.Aed).BaseAmount); // 0.025
        Assert.Equal(-0.03m, Money.Money.Create(-0.05m, Usd, 0.5m, CurrencyCode.Aed).BaseAmount); // -0.025
    }

    [Fact]
    public void Amount_must_fit_the_currency_minor_units()
    {
        Assert.Throws<ArgumentException>(() => Money.Money.Create(1.005m, Usd, 1m, Usd));
        var kwd = Money.Money.Create(1.005m, Kwd, 11.95m, CurrencyCode.Aed);
        Assert.Equal(12.01m, kwd.BaseAmount); // 12.00975
    }

    [Fact]
    public void Base_currency_amounts_use_rate_one_and_rates_are_positive()
    {
        Assert.Equal(1m, Money.Money.InBase(10m, CurrencyCode.Aed).ExchangeRate);
        Assert.Throws<ArgumentException>(() => Money.Money.Create(10m, CurrencyCode.Aed, 2m, CurrencyCode.Aed));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Money.Create(10m, Usd, 0m, CurrencyCode.Aed));
    }

    [Fact]
    public void Only_same_currency_and_rate_can_be_added()
    {
        var a = Money.Money.Create(10m, Usd, 3.6725m, CurrencyCode.Aed);
        var b = Money.Money.Create(5.5m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(15.5m, a.Add(b).Amount);
        Assert.Throws<InvalidOperationException>(() => a.Add(Money.Money.InBase(1m, CurrencyCode.Aed)));
    }

    [Fact]
    public void Money_records_its_base_currency_and_only_adds_against_the_same_base()
    {
        var aedBase = Money.Money.Create(10m, Usd, 3.6725m, CurrencyCode.Aed);
        var sarBase = Money.Money.Create(10m, Usd, 3.75m, new CurrencyCode("SAR"));
        Assert.Equal("AED", aedBase.BaseCurrency);
        Assert.Equal("SAR", sarBase.BaseCurrency);
        Assert.Equal("AED", Money.Money.InBase(1m, CurrencyCode.Aed).BaseCurrency);
        Assert.Equal("AED", aedBase.Negate().BaseCurrency);
        var sameRateOtherBase = Money.Money.Create(10m, Usd, 3.6725m, new CurrencyCode("SAR"));
        Assert.Throws<InvalidOperationException>(() => aedBase.Add(sameRateOtherBase));
    }

    [Theory]
    [InlineData("aed")]
    [InlineData("AE")]
    [InlineData("AEDX")]
    [InlineData("A1D")]
    public void Currency_codes_are_three_upper_case_letters(string code) =>
        Assert.Throws<ArgumentException>(() => new CurrencyCode(code));

    /// <summary>Three capital letters that name no current currency are refused (critic p00 round
    /// 12), and so are the list's codes without minor units (metals, SDR, test and no-currency codes).</summary>
    [Theory]
    [InlineData("ABC")]
    [InlineData("ZZZ")]
    [InlineData("XAU")]
    [InlineData("XDR")]
    [InlineData("XTS")]
    [InlineData("XXX")]
    [InlineData("DEM")]
    public void Codes_that_are_not_current_iso_4217_currencies_are_refused(string code)
    {
        Assert.Throws<ArgumentException>(() => new CurrencyCode(code));
        Assert.False(CurrencyCode.TryParse(code, out _));
        Assert.False(CurrencyCode.IsKnown(code));
    }

    /// <summary>Minor units come from ISO 4217 List One as published, not from a table in the code.</summary>
    [Theory]
    [InlineData("AED", 2, 784)]
    [InlineData("SAR", 2, 682)]
    [InlineData("USD", 2, 840)]
    [InlineData("KWD", 3, 414)]
    [InlineData("BHD", 3, 48)]
    [InlineData("OMR", 3, 512)]
    [InlineData("JPY", 0, 392)]
    [InlineData("CLF", 4, 990)]
    public void Current_currencies_carry_their_iso_4217_minor_units_and_number(string code, int minorUnits, int number)
    {
        Assert.True(CurrencyCode.TryParse(code, out var currency));
        Assert.Equal(minorUnits, currency.MinorUnits);
        Assert.Equal(number, currency.Number);
        Assert.Contains(code, CurrencyCode.All);
    }

    [Fact]
    public void The_currency_table_is_a_whole_recent_iso_4217_list()
    {
        Assert.True(CurrencyCode.All.Count >= 150, $"{CurrencyCode.All.Count} currencies");
        Assert.True(string.CompareOrdinal(Iso4217.Published, "2026-09-17") >= 0, Iso4217.Published);
        Assert.Equal(CurrencyCode.All.Order(StringComparer.Ordinal), CurrencyCode.All);
        Assert.All(CurrencyCode.All, c => Assert.Matches("^[A-Z]{3}$", c));
        // Every amount of every listed currency fits the money columns' scales.
        Assert.All(CurrencyCode.All, c => Assert.InRange(new CurrencyCode(c).MinorUnits, 0, Money.Money.BaseAmountScale));
    }

    [Fact]
    public void Money_travels_as_decimal_strings_in_json()
    {
        var options = new JsonSerializerOptions();
        ErpPlatform.ConfigureJson(options);
        var json = JsonSerializer.Serialize(Money.Money.Create(1234.5m, Usd, 3.6725m, CurrencyCode.Aed), options);
        Assert.Equal("""{"amount":"1234.5","currency":"USD","exchangeRate":"3.6725","baseAmount":"4533.70","baseCurrency":"AED"}""", json);
        var back = JsonSerializer.Deserialize<decimal>("\"0.1\"", options);
        Assert.Equal(0.1m, back);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>("\"1e3\"", options));
    }

    // docs/compliance/vat-rounding-and-currency.md, rule 2 (owner-signed 2026-10-10): rounding
    // happens once, on the figure that is stated; lines and tax-category subtotals keep their full
    // precision; the invoice total is rounded to 2 decimals and any difference may be stated as
    // the e-invoice "Rounding Amount".

    /// <summary>The critic's case (p00 rounds 9 and 10): three lines of USD 1.00 at 3.6725. The
    /// exact base total is AED 11.0175, stated once as 11.02; adding the lines' rounded base
    /// amounts (3.67 + 3.67 + 3.67) would give 11.01.</summary>
    [Fact]
    public void Lines_add_at_full_precision_and_the_total_rounds_once()
    {
        var line = Money.Money.Create(1.00m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(3.67m, line.BaseAmount); // the line on its own, for display
        var total = line.Add(line).Add(line);
        Assert.Equal(3.00m, total.Amount);
        Assert.Equal(11.0175m, total.ExactBaseAmount());
        Assert.Equal(11.02m, total.BaseAmount);
        Assert.NotEqual(line.BaseAmount * 3, total.BaseAmount);
        var stated = total.State();
        Assert.True(stated.IsStated);
        Assert.Equal(3.00m, stated.Amount);
        Assert.Equal(11.02m, stated.BaseAmount);
        Assert.Equal(0.0025m, total.BaseRoundingAmount());
        Assert.Equal(0m, stated.BaseRoundingAmount());
    }

    /// <summary>A line keeps every decimal of quantity × unit price, in its own currency too:
    /// 3 × USD 0.3333 is 0.9999, and only the stated total becomes 1.00, with a rounding amount of
    /// 0.0001 for the e-invoice.</summary>
    [Fact]
    public void A_line_keeps_more_decimals_than_its_currency_until_the_total_is_stated()
    {
        var line = Money.Money.Unrounded(0.3333m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.False(line.IsStated);
        Assert.Equal(0.3333m, line.Amount);
        Assert.Equal(1.22404425m, line.ExactBaseAmount());
        var total = line.Add(line).Add(line);
        Assert.Equal(0.9999m, total.Amount);
        Assert.Equal(3.67213275m, total.ExactBaseAmount());
        Assert.Equal(0.0001m, total.RoundingAmount());
        var stated = total.State();
        Assert.Equal(1.00m, stated.Amount);
        Assert.Equal(3.67m, stated.BaseAmount); // round(3.67213275)
        Assert.Equal(-0.00213275m, total.BaseRoundingAmount());
        Assert.Equal(0m, stated.RoundingAmount());
        // Rounding the lines first would state 3 × 0.33 = 0.99.
        Assert.NotEqual(Money.Money.RoundToMinorUnits(line.Amount, Usd) * 3, stated.Amount);
    }

    /// <summary>The stated base amount comes from the exact figure, not from the stated amount
    /// in the document currency: rounding the dollar total first and converting it would round
    /// twice.</summary>
    [Fact]
    public void The_stated_base_amount_rounds_the_exact_base_not_the_rounded_amount()
    {
        // 1.0049 USD at 10: exact base 10.049 -> 10.05; the dollar total states 1.00, and 1.00 × 10
        // would give 10.00.
        var total = Money.Money.Unrounded(1.0049m, Usd, 10m, CurrencyCode.Aed).State();
        Assert.Equal(1.00m, total.Amount);
        Assert.Equal(10.05m, total.BaseAmount);
    }

    /// <summary>Subtotals (a tax category, a group of lines) keep full precision as well; the
    /// total of subtotals equals the total of all the lines, in any order.</summary>
    [Fact]
    public void Subtotals_keep_full_precision_and_any_grouping_gives_the_same_total()
    {
        var amounts = new[] { 0.0001m, 1.0049m, 2.3333333333m, 7.15m, 0.005m, 12.3456789m, 0.0049m };
        var lines = amounts.Select(a => Money.Money.Unrounded(a, Usd, 3.6725m, CurrencyCode.Aed)).ToArray();
        var all = lines.Aggregate((a, b) => a.Add(b));
        var first = lines.Take(3).Aggregate((a, b) => a.Add(b));
        var second = lines.Skip(3).Aggregate((a, b) => a.Add(b));
        var reversed = lines.Reverse().Aggregate((a, b) => a.Add(b));
        Assert.Equal(all, first.Add(second));
        Assert.Equal(all, reversed);
        Assert.Equal(amounts.Sum() * 3.6725m, all.ExactBaseAmount());
        Assert.Equal(Money.Money.RoundToMinorUnits(amounts.Sum() * 3.6725m, CurrencyCode.Aed), all.State().BaseAmount);
        Assert.Equal(Money.Money.RoundToMinorUnits(amounts.Sum(), Usd), all.State().Amount);
    }

    /// <summary>The single rounding is rule 1's: half away from zero at the fils, and a credit note
    /// (the negated total) mirrors its invoice.</summary>
    [Fact]
    public void The_one_rounding_is_half_away_from_zero_and_a_credit_note_mirrors_it()
    {
        // Two lines of USD 0.01 at 0.25: exact base 0.005 -> 0.01 (each line alone, 0.0025, would
        // round to 0.00 and add to 0.00).
        var line = Money.Money.Create(0.01m, Usd, 0.25m, CurrencyCode.Aed);
        Assert.Equal(0.00m, line.BaseAmount);
        var total = line.Add(line);
        Assert.Equal(0.01m, total.State().BaseAmount);
        Assert.Equal(-0.01m, total.Negate().State().BaseAmount);
        Assert.Equal(-0.01m, total.State().Negate().BaseAmount);
        Assert.Equal(total.Negate().State(), total.State().Negate());
        // 0.025 dirhams in a KWD total rounds to 0.03, not banker's 0.02.
        var kwd = Money.Money.Unrounded(0.0025m, Kwd, 5m, CurrencyCode.Aed);
        Assert.Equal(0.03m, kwd.Add(kwd).State().BaseAmount); // 0.025
        Assert.Equal(0.005m, kwd.Add(kwd).State().Amount); // KWD keeps three decimals
    }

    /// <summary>Stated figures add as stated: a statement of two invoices that each stated AED
    /// 3.67 shows 7.34. Adding a stated figure to an unrounded line would round part of a total
    /// twice, so it is refused.</summary>
    [Fact]
    public void Stated_figures_add_as_stated_and_never_mix_with_lines()
    {
        var invoice = Money.Money.Create(1.00m, Usd, 3.6725m, CurrencyCode.Aed).State();
        var statement = invoice.Add(invoice);
        Assert.True(statement.IsStated);
        Assert.Equal(2.00m, statement.Amount);
        Assert.Equal(7.34m, statement.BaseAmount);
        var line = Money.Money.Create(1.00m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(7.35m, line.Add(line).State().BaseAmount); // one document: 7.345 stated once
        Assert.Throws<InvalidOperationException>(() => invoice.Add(line));
        Assert.Throws<InvalidOperationException>(() => line.Add(invoice));
        Assert.Equal(invoice, invoice.State());
    }

    /// <summary>A line is never rounded silently: more than ten decimals, or more than the
    /// columns hold, is refused. An amount as entered (Create) still fits its currency.</summary>
    [Fact]
    public void Full_precision_is_bounded_by_the_columns_and_never_rounded_silently()
    {
        Assert.Equal(1.0000000001m, Money.Money.Unrounded(1.0000000001m, Usd, 3.6725m, CurrencyCode.Aed).Amount);
        Assert.Throws<ArgumentException>(() => Money.Money.Unrounded(1.00000000001m, Usd, 3.6725m, CurrencyCode.Aed));
        Assert.Throws<ArgumentException>(() => Money.Money.Create(0.3333m, Usd, 3.6725m, CurrencyCode.Aed));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Money.Unrounded(1e18m, Usd, 1m, Usd));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Money.Unrounded(1e15m, Usd, 3.6725m, CurrencyCode.Aed));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Money.Unrounded(1e17m, Usd, 99_999_999_999m, CurrencyCode.Aed));
        var largest = Money.Money.Unrounded(-999_999_999_999_999m, Usd, 1m, Usd);
        Assert.Throws<ArgumentOutOfRangeException>(() => largest.Add(Money.Money.Unrounded(-1m, Usd, 1m, Usd)));
        Assert.Throws<ArgumentOutOfRangeException>(() => largest.State().Add(Money.Money.Create(-1m, Usd, 1m, Usd).State()));
        Assert.Throws<ArgumentException>(() => Money.Money.Unrounded(1m, CurrencyCode.Aed, 2m, CurrencyCode.Aed));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Money.Unrounded(1m, Usd, -1m, CurrencyCode.Aed));
    }

    /// <summary>The columns hold what the type holds: the amount at ten decimals (a line is not
    /// rounded on the way to the database), the base amount at four, and the stated flag, so a
    /// stated total read back still adds as stated.</summary>
    [Fact]
    public void The_money_columns_hold_full_precision_and_the_stated_flag()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MoneyColumnsContext>()
            .UseNpgsql("Host=unused").Options;
        using var db = new MoneyColumnsContext(options);
        var entity = db.Model.FindEntityType(typeof(MoneyRow))!;
        var columns = entity.GetComplexProperties().Single().ComplexType.GetProperties()
            .ToDictionary(p => p.GetColumnName(), p => p.GetColumnType());
        Assert.Equal("numeric(28,10)", columns["total_amount"]);
        Assert.Equal("char(3)", columns["total_currency"]);
        Assert.Equal("numeric(19,8)", columns["total_exchange_rate"]);
        Assert.Equal("numeric(19,4)", columns["total_base_amount"]);
        Assert.Equal("char(3)", columns["total_base_currency"]);
        Assert.Equal("boolean", columns["total_stated"]);
        Assert.Equal(6, columns.Count);
    }

    /// <summary>The JSON shape is unchanged by the stated flag and the exact base amount: answers
    /// carry the five figures.</summary>
    [Fact]
    public void A_stated_total_travels_with_the_same_five_json_fields()
    {
        var options = new JsonSerializerOptions();
        ErpPlatform.ConfigureJson(options);
        var line = Money.Money.Unrounded(0.3333m, Usd, 3.6725m, CurrencyCode.Aed);
        var json = JsonSerializer.Serialize(line.Add(line).Add(line).State(), options);
        Assert.Equal("""{"amount":"1.00","currency":"USD","exchangeRate":"3.6725","baseAmount":"3.67","baseCurrency":"AED"}""", json);
    }

    // Critic p00 round 11: the invariants could be bypassed (public init setters, so an object
    // initialiser or a with-expression set any base amount, and JSON read any figures at all).

    /// <summary>No property can be set from outside the type: no object initialiser and no
    /// with-expression compiles against it, so a base amount that disagrees with the amount and the
    /// rate cannot be made.</summary>
    [Fact]
    public void No_money_figure_can_be_set_from_outside_the_type()
    {
        var settable = typeof(Money.Money).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null || p.GetSetMethod(nonPublic: true) is not null)
            .Select(p => p.Name).ToList();
        Assert.Empty(settable);
        Assert.DoesNotContain(typeof(Money.Money).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), c => c.GetParameters().Length > 0);
    }

    /// <summary>default(Money), the one value no factory makes, has no currency: every operation and
    /// the JSON writer refuse it.</summary>
    [Fact]
    public void An_unmade_money_is_refused_by_every_operation()
    {
        var unmade = default(Money.Money);
        var made = Money.Money.Create(1m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.False(unmade.IsInitialised);
        Assert.True(made.IsInitialised);
        Assert.Throws<InvalidOperationException>(() => unmade.State());
        Assert.Throws<InvalidOperationException>(() => unmade.Add(unmade));
        Assert.Throws<InvalidOperationException>(() => made.Add(unmade));
        Assert.Throws<InvalidOperationException>(() => unmade.Add(made));
        Assert.Throws<InvalidOperationException>(() => unmade.Negate());
        Assert.Throws<InvalidOperationException>(() => unmade.ExactBaseAmount());
        Assert.Throws<InvalidOperationException>(() => unmade.RoundingAmount());
        Assert.Throws<InvalidOperationException>(() => unmade.BaseRoundingAmount());
        var options = new JsonSerializerOptions();
        ErpPlatform.ConfigureJson(options);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(unmade, options));
    }

    /// <summary>JSON is read through the type's own factory: what is written reads back as the same
    /// figure, and a base amount may be left out (the type works it out).</summary>
    [Fact]
    public void Money_read_from_json_is_built_by_the_type()
    {
        var options = new JsonSerializerOptions();
        ErpPlatform.ConfigureJson(options);
        var line = Money.Money.Unrounded(0.3333m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(line, JsonSerializer.Deserialize<Money.Money>(JsonSerializer.Serialize(line, options), options));
        var entered = Money.Money.Create(1234.5m, Usd, 3.6725m, CurrencyCode.Aed);
        Assert.Equal(entered, JsonSerializer.Deserialize<Money.Money>(JsonSerializer.Serialize(entered, options), options));
        var withoutBase = JsonSerializer.Deserialize<Money.Money>("""{"amount":"1.00","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""", options);
        Assert.Equal(3.67m, withoutBase.BaseAmount);
        Assert.False(withoutBase.IsStated);
        // Three lines read from a body still total by rule 2 (11.0175, stated 11.02).
        Assert.Equal(11.02m, withoutBase.Add(withoutBase).Add(withoutBase).State().BaseAmount);
        // JSON numbers are read too (leniently), as decimals.
        Assert.Equal(entered, JsonSerializer.Deserialize<Money.Money>("""{"amount":1234.5,"currency":"USD","exchangeRate":3.6725,"baseAmount":4533.70,"baseCurrency":"AED"}""", options));
        // Inside a body: a nullable money and a money property.
        Assert.Null(JsonSerializer.Deserialize<Money.Money?>("null", options));
        var body = JsonSerializer.Deserialize<PriceBody>("""{"price":{"amount":"2","currency":"AED","exchangeRate":"1","baseCurrency":"AED"}}""", options)!;
        Assert.Equal(Money.Money.InBase(2m, CurrencyCode.Aed), body.Price);
    }

    public sealed record PriceBody(Money.Money Price);

    /// <summary>A body cannot carry a money the type would refuse (the critic's probe was the first
    /// case: lower-case currency, rate -2, base amount 1 for amount 10.123456).</summary>
    [Theory]
    [InlineData("""{"amount":"10.123456","currency":"usd","exchangeRate":"-2","baseAmount":"1","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"usd","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"USD","exchangeRate":"3.6725","baseCurrency":"aed"}""")]
    [InlineData("""{"amount":"10","currency":"US","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"USD","exchangeRate":"-2","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"USD","exchangeRate":"0","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"USD","exchangeRate":"3.672500001","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"10","currency":"AED","exchangeRate":"2","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"100","currency":"USD","exchangeRate":"0","baseAmount":"999","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1.00","currency":"USD","exchangeRate":"3.6725","baseAmount":"50","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1.00","currency":"USD","exchangeRate":"3.6725","baseAmount":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1.00000000001","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1e3","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1000000000000000000","currency":"USD","exchangeRate":"1","baseCurrency":"USD"}""")]
    [InlineData("""{"currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1","currency":"USD","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1","currency":"USD","exchangeRate":"3.6725"}""")]
    [InlineData("""{"amount":"1","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED","isStated":true}""")]
    [InlineData("""{"amount":"1","amount":"2","currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":null,"currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":"1","currency":3,"exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""{"amount":true,"currency":"USD","exchangeRate":"3.6725","baseCurrency":"AED"}""")]
    [InlineData("""["1","USD"]""")]
    [InlineData(""""10.00 USD"""")]
    [InlineData("""null""")]
    public void Json_cannot_carry_a_money_the_type_would_refuse(string json)
    {
        var options = new JsonSerializerOptions();
        ErpPlatform.ConfigureJson(options);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money.Money>(json, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PriceBody>($$"""{"price":{{json}}}""", options));
    }

    /// <summary>The same converter applies without the platform's options (it is on the type), with
    /// the serializer's own naming and decimal handling: a JSON number amount.</summary>
    [Fact]
    public void The_converter_is_on_the_type_itself()
    {
        var plain = JsonSerializer.Deserialize<Money.Money>("""{"Amount":1.5,"Currency":"USD","ExchangeRate":3.6725,"BaseCurrency":"AED"}""");
        Assert.Equal(Money.Money.Create(1.5m, Usd, 3.6725m, CurrencyCode.Aed), plain);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money.Money>("""{"Amount":1.5,"Currency":"USD","ExchangeRate":-1,"BaseCurrency":"AED"}"""));
        Assert.Equal("""{"Amount":1.5,"Currency":"USD","ExchangeRate":3.6725,"BaseAmount":5.51,"BaseCurrency":"AED"}""", JsonSerializer.Serialize(plain));
    }

    public sealed class MoneyRow
    {
        public int Id { get; set; }
        public Money.Money Total { get; set; }
    }

    public sealed class MoneyColumnsContext(Microsoft.EntityFrameworkCore.DbContextOptions<MoneyColumnsContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options)
    {
        protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder) =>
            modelBuilder.Entity<MoneyRow>().MoneyProperty(r => r.Total, "total");
    }
}
