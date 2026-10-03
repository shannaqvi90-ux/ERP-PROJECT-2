using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Kernel.Money;

/// <summary>An ISO 4217 currency code (three upper-case letters) and its minor-unit digits.</summary>
public readonly record struct CurrencyCode
{
    public CurrencyCode(string code)
    {
        if (code is null || code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"'{code}' is not an ISO 4217 currency code.", nameof(code));
        }
        Code = code;
    }

    public string Code { get; }

    /// <summary>Digits after the decimal point for amounts in this currency. Three-decimal
    /// currencies (Gulf dinars and rials) and zero-decimal ones are listed; all others use two.
    /// p08 replaces this with the tenant's currency table.</summary>
    public int MinorUnits => Code switch
    {
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "UGX" or "XAF" or "XOF" => 0,
        _ => 2,
    };

    public static readonly CurrencyCode Aed = new("AED");

    public override string ToString() => Code;
}

/// <summary>
/// An amount of money as the ledger rule requires it: the amount in its currency, the exchange
/// rate used, and the amount in the base currency, together with which currency that base is
/// (companies of one tenant may keep different base currencies). Decimal only — never floating
/// point. Stored as <c>numeric(19,4)</c> amounts, <c>numeric(19,8)</c> rate and <c>char(3)</c>
/// currencies.
/// </summary>
public readonly record struct Money
{
    public const int AmountPrecision = 19;
    public const int AmountScale = 4;
    public const int RatePrecision = 19;
    public const int RateScale = 8;

    private Money(decimal amount, string currency, decimal exchangeRate, decimal baseAmount, string baseCurrency)
    {
        Amount = amount;
        Currency = currency;
        ExchangeRate = exchangeRate;
        BaseAmount = baseAmount;
        BaseCurrency = baseCurrency;
    }

    /// <summary>Amount in <see cref="Currency"/>.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217 code.</summary>
    public string Currency { get; init; }

    /// <summary>Base-currency units per one unit of <see cref="Currency"/>.</summary>
    public decimal ExchangeRate { get; init; }

    /// <summary>Amount in the company's base currency, rounded to the base currency's minor units.</summary>
    public decimal BaseAmount { get; init; }

    /// <summary>ISO 4217 code of the base currency <see cref="BaseAmount"/> is in.</summary>
    public string BaseCurrency { get; init; }

    /// <summary>Money in the base currency itself (rate 1).</summary>
    public static Money InBase(decimal amount, CurrencyCode currency) =>
        Create(amount, currency, 1m, currency);

    /// <summary>Build money in a foreign currency, computing the base amount from the rate with
    /// half-away-from-zero rounding to the base currency's minor units.</summary>
    public static Money Create(decimal amount, CurrencyCode currency, decimal exchangeRate, CurrencyCode baseCurrency)
    {
        if (exchangeRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exchangeRate), "Exchange rate must be positive.");
        }
        if (currency == baseCurrency && exchangeRate != 1m)
        {
            throw new ArgumentException("An amount in the base currency must use rate 1.", nameof(exchangeRate));
        }
        var rounded = decimal.Round(amount, currency.MinorUnits, MidpointRounding.AwayFromZero);
        if (rounded != amount)
        {
            throw new ArgumentException($"{currency} amounts have {currency.MinorUnits} decimal places.", nameof(amount));
        }
        var rate = decimal.Round(exchangeRate, RateScale, MidpointRounding.AwayFromZero);
        var baseAmount = decimal.Round(amount * rate, baseCurrency.MinorUnits, MidpointRounding.AwayFromZero);
        return new Money(amount, currency.Code, rate, baseAmount, baseCurrency.Code);
    }

    /// <summary>Sum of two amounts in the same currency and rate, against the same base currency.</summary>
    public Money Add(Money other)
    {
        if (other.Currency != Currency || other.ExchangeRate != ExchangeRate || other.BaseCurrency != BaseCurrency)
        {
            throw new InvalidOperationException("Only amounts in the same currency at the same rate, against the same base currency, can be added.");
        }
        return new Money(Amount + other.Amount, Currency, ExchangeRate, BaseAmount + other.BaseAmount, BaseCurrency);
    }

    public Money Negate() => new(-Amount, Currency, ExchangeRate, -BaseAmount, BaseCurrency);

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";
}

/// <summary>
/// JSON numbers are binary floating point in JavaScript. Decimals therefore travel as strings
/// (<c>"1234.50"</c>) so no client parses money into a float. Reading accepts strings and,
/// leniently, JSON numbers parsed directly as decimal.
/// </summary>
public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDecimal();
        }
        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        throw new JsonException("Expected a decimal number as a string, for example \"1234.50\".");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
