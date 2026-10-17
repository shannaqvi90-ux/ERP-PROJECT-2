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
/// point.
/// <para>
/// Rounding follows the owner-signed rule docs/compliance/vat-rounding-and-currency.md. Rule 1:
/// a figure rounds to its currency's minor units half away from zero. Rule 2: rounding happens
/// once, on the figure that is stated; lines and subtotals keep their full precision. So a
/// <see cref="Money"/> is either <em>unrounded</em> (a line or a subtotal: the default) or
/// <em>stated</em> (<see cref="State"/> rounded it once). An unrounded money carries the exact
/// amount, and its exact base amount is always <c>Amount × ExchangeRate</c>; <see cref="Add"/>
/// sums the exact figures and never the rounded base amounts, so three lines of USD 1.00 at
/// 3.6725 total AED 11.0175, stated as 11.02 (not 3.67 + 3.67 + 3.67 = 11.01). Stated figures
/// add as stated figures (a statement of two invoices sums what each invoice stated). Mixing the
/// two would round part of a total twice, so <see cref="Add"/> refuses it.
/// </para>
/// Stored as <c>numeric(28,10)</c> amount, <c>numeric(19,8)</c> rate, <c>numeric(19,4)</c> base
/// amount, <c>char(3)</c> currencies and a <c>boolean</c> stated flag (<see cref="MoneyMapping"/>).
/// </summary>
public readonly record struct Money
{
    /// <summary>Column precision and scale of <see cref="Amount"/>: an unrounded line or subtotal
    /// keeps up to ten decimals, so quantity × unit price is held without rounding.</summary>
    public const int AmountPrecision = 28;
    public const int AmountScale = 10;
    /// <summary>Column precision and scale of <see cref="BaseAmount"/>, which is always rounded to
    /// the base currency's minor units (at most three).</summary>
    public const int BaseAmountPrecision = 19;
    public const int BaseAmountScale = 4;
    public const int RatePrecision = 19;
    public const int RateScale = 8;

    /// <summary>Largest magnitude an amount may have: the integer digits of
    /// <c>numeric(28,10)</c>.</summary>
    public static readonly decimal MaxAmount = 999_999_999_999_999_999.9999999999m;

    /// <summary>Largest magnitude a base amount may have: the integer digits of
    /// <c>numeric(19,4)</c>.</summary>
    public static readonly decimal MaxBaseAmount = 999_999_999_999_999.9999m;

    private Money(decimal amount, string currency, decimal exchangeRate, decimal baseAmount, string baseCurrency, bool isStated)
    {
        Amount = amount;
        Currency = currency;
        ExchangeRate = exchangeRate;
        BaseAmount = baseAmount;
        BaseCurrency = baseCurrency;
        IsStated = isStated;
    }

    /// <summary>Amount in <see cref="Currency"/>: exact for an unrounded money, rounded to the
    /// currency's minor units once it is stated.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217 code.</summary>
    public string Currency { get; init; }

    /// <summary>Base-currency units per one unit of <see cref="Currency"/>.</summary>
    public decimal ExchangeRate { get; init; }

    /// <summary>Amount in the company's base currency, rounded once to the base currency's minor
    /// units. For an unrounded money it is the exact base amount (<see cref="ExactBaseAmount"/>)
    /// rounded once, which is what this figure states on its own; totals never add these rounded
    /// values (see <see cref="Add"/>). For a stated money it is the stated base amount.</summary>
    public decimal BaseAmount { get; init; }

    /// <summary>ISO 4217 code of the base currency <see cref="BaseAmount"/> is in.</summary>
    public string BaseCurrency { get; init; }

    /// <summary>True once <see cref="State"/> rounded this figure (rule 2's stated figure, such as
    /// an invoice total); false for a line or a subtotal, which keeps full precision. Not part of
    /// the JSON shape: answers show figures, and clients do no money arithmetic.</summary>
    [JsonIgnore]
    public bool IsStated { get; init; }

    /// <summary>Money in the base currency itself (rate 1), unrounded.</summary>
    public static Money InBase(decimal amount, CurrencyCode currency) =>
        Create(amount, currency, 1m, currency);

    /// <summary>Build an unrounded money from an amount as entered in its currency, which must
    /// fit the currency's minor units (a price or a paid amount). The base amount is reported
    /// rounded once, half away from zero, to the base currency's minor units; the exact product
    /// is kept for totals (<see cref="ExactBaseAmount"/>).</summary>
    public static Money Create(decimal amount, CurrencyCode currency, decimal exchangeRate, CurrencyCode baseCurrency)
    {
        if (RoundToMinorUnits(amount, currency) != amount)
        {
            throw new ArgumentException($"{currency} amounts have {currency.MinorUnits} decimal places.", nameof(amount));
        }
        return Unrounded(amount, currency, exchangeRate, baseCurrency);
    }

    /// <summary>Build an unrounded money that keeps full precision, such as a line amount worked
    /// out as quantity × unit price (rule 2: lines and subtotals are not rounded). The amount may
    /// carry up to <see cref="AmountScale"/> decimals and is never rounded here; an amount that
    /// needs more is refused rather than rounded silently.</summary>
    public static Money Unrounded(decimal amount, CurrencyCode currency, decimal exchangeRate, CurrencyCode baseCurrency)
    {
        if (exchangeRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exchangeRate), "Exchange rate must be positive.");
        }
        if (currency == baseCurrency && exchangeRate != 1m)
        {
            throw new ArgumentException("An amount in the base currency must use rate 1.", nameof(exchangeRate));
        }
        CheckFullPrecision(amount, nameof(amount));
        var rate = decimal.Round(exchangeRate, RateScale, MidpointRounding.AwayFromZero);
        return new Money(amount, currency.Code, rate, StatedBase(amount, rate, baseCurrency, nameof(amount)), baseCurrency.Code, isStated: false);
    }

    /// <summary>
    /// Rounds a figure to the currency's minor units by mathematical rounding, half away from zero:
    /// in dirhams, 0.005 becomes 0.01 and -0.005 becomes -0.01, so a credit note mirrors its
    /// invoice. This is the owner-signed rule docs/compliance/vat-rounding-and-currency.md, rule 1
    /// (Cabinet Decision No. 52 of 2017, Article 61), for AED amounts, tax amounts and base-currency
    /// amounts. Rule 2 of the same record says when to round: once, on the stated figure
    /// (<see cref="State"/>).
    /// </summary>
    public static decimal RoundToMinorUnits(decimal amount, CurrencyCode currency) =>
        decimal.Round(amount, currency.MinorUnits, MidpointRounding.AwayFromZero);

    /// <summary>The base amount before rounding: <c>Amount × ExchangeRate</c> for an unrounded
    /// money, and the stated base amount once stated. The product is exact while it is below
    /// 10^10 (the amount has at most ten decimals and the rate eight, and a decimal holds 28
    /// significant digits); above that, the digits dropped are beyond the 14th decimal.</summary>
    public decimal ExactBaseAmount() => IsStated ? BaseAmount : Amount * ExchangeRate;

    /// <summary>
    /// Rule 2's single rounding: the stated figure. The amount is rounded to its currency's minor
    /// units and the base amount to the base currency's, each once and from the exact figure, half
    /// away from zero. A stated money is returned as it is.
    /// </summary>
    public Money State()
    {
        if (IsStated)
        {
            return this;
        }
        var currency = new CurrencyCode(Currency);
        var baseCurrency = new CurrencyCode(BaseCurrency);
        return new Money(
            RoundToMinorUnits(Amount, currency), Currency, ExchangeRate,
            StatedBase(Amount, ExchangeRate, baseCurrency, nameof(Amount)), BaseCurrency, isStated: true);
    }

    /// <summary>What stating this figure adds to it in its own currency: the stated amount minus
    /// the exact amount. This is the value an electronic invoice may give in its optional
    /// "Rounding Amount" field (rule 2). Zero for a figure already stated.</summary>
    public decimal RoundingAmount() =>
        IsStated ? 0m : RoundToMinorUnits(Amount, new CurrencyCode(Currency)) - Amount;

    /// <summary>What stating this figure adds to it in the base currency: the stated base amount
    /// minus the exact base amount. Zero for a figure already stated.</summary>
    public decimal BaseRoundingAmount() => IsStated ? 0m : BaseAmount - Amount * ExchangeRate;

    /// <summary>
    /// Sum of two amounts in the same currency and rate, against the same base currency. Two
    /// unrounded figures (lines, subtotals) add at full precision, and the base amount of the sum
    /// is the exact sum rounded once; the rounded base amounts of the parts are never added. Two
    /// stated figures add as stated figures. An unrounded figure and a stated one are refused:
    /// state the lines' total first.
    /// </summary>
    public Money Add(Money other)
    {
        if (other.Currency != Currency || other.ExchangeRate != ExchangeRate || other.BaseCurrency != BaseCurrency)
        {
            throw new InvalidOperationException("Only amounts in the same currency at the same rate, against the same base currency, can be added.");
        }
        if (other.IsStated != IsStated)
        {
            throw new InvalidOperationException("A stated figure and an unrounded line or subtotal cannot be added: state the lines' total first, or add stated figures together.");
        }
        var amount = Amount + other.Amount;
        CheckFullPrecision(amount, nameof(other));
        if (IsStated)
        {
            var stated = BaseAmount + other.BaseAmount;
            if (Math.Abs(stated) > MaxBaseAmount)
            {
                throw new ArgumentOutOfRangeException(nameof(other), $"A base amount must be smaller than 10^{BaseAmountPrecision - BaseAmountScale}.");
            }
            return new Money(amount, Currency, ExchangeRate, stated, BaseCurrency, isStated: true);
        }
        var baseAmount = StatedBase(amount, ExchangeRate, new CurrencyCode(BaseCurrency), nameof(other));
        return new Money(amount, Currency, ExchangeRate, baseAmount, BaseCurrency, isStated: false);
    }

    /// <summary>The same figure with the opposite sign (a credit note mirrors its invoice:
    /// rounding half away from zero is symmetric, so the base amount mirrors too).</summary>
    public Money Negate() => new(-Amount, Currency, ExchangeRate, -BaseAmount, BaseCurrency, IsStated);

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";

    /// <summary>The exact base amount rounded once (rule 1), refused when it would not fit its
    /// column.</summary>
    private static decimal StatedBase(decimal amount, decimal rate, CurrencyCode baseCurrency, string parameter)
    {
        decimal exact;
        try
        {
            exact = amount * rate;
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(parameter, "The base amount is too large.");
        }
        var rounded = RoundToMinorUnits(exact, baseCurrency);
        if (Math.Abs(rounded) > MaxBaseAmount)
        {
            throw new ArgumentOutOfRangeException(parameter, $"A base amount must be smaller than 10^{BaseAmountPrecision - BaseAmountScale}.");
        }
        return rounded;
    }

    private static void CheckFullPrecision(decimal amount, string parameter)
    {
        if (decimal.Round(amount, AmountScale) != amount)
        {
            throw new ArgumentException($"An amount keeps at most {AmountScale} decimal places.", parameter);
        }
        if (Math.Abs(amount) > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(parameter, $"An amount must be smaller than 10^{AmountPrecision - AmountScale}.");
        }
    }
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
