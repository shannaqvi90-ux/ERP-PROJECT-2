using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Money;

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
}
