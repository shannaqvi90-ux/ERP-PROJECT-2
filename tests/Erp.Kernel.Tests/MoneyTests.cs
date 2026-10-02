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
        Assert.Equal("""{"amount":"1234.5","currency":"USD","exchangeRate":"3.6725","baseAmount":"4533.70"}""", json);
        var back = JsonSerializer.Deserialize<decimal>("\"0.1\"", options);
        Assert.Equal(0.1m, back);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>("\"1e3\"", options));
    }
}
