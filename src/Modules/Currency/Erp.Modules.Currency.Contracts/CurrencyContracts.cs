using Erp.Kernel.Money;

namespace Erp.Modules.Currency.Contracts;

/// <summary>Permissions of the currency module.</summary>
public static class CurrencyPermissions
{
    /// <summary>See the workspace's currencies and the ISO 4217 catalogue.</summary>
    public const string CurrenciesRead = "currency.currencies.read";

    /// <summary>Enable another ISO 4217 currency for the workspace.</summary>
    public const string CurrenciesCreate = "currency.currencies.create";

    /// <summary>Change an enabled currency: its names, whether it is in use, how old a rate may be.</summary>
    public const string CurrenciesUpdate = "currency.currencies.update";

    /// <summary>See exchange rates, look up the rate of a date and convert amounts.</summary>
    public const string RatesRead = "currency.rates.read";

    /// <summary>Enter an exchange rate by hand.</summary>
    public const string RatesCreate = "currency.rates.create";

    /// <summary>Correct an exchange rate.</summary>
    public const string RatesUpdate = "currency.rates.update";

    /// <summary>Delete an exchange rate.</summary>
    public const string RatesDelete = "currency.rates.delete";

    /// <summary>Import exchange rates from a file.</summary>
    public const string RatesImport = "currency.rates.import";

    public static readonly IReadOnlyList<string> All =
    [
        CurrenciesRead, CurrenciesCreate, CurrenciesUpdate, RatesRead, RatesCreate, RatesUpdate, RatesDelete, RatesImport,
    ];
}

/// <summary>How an exchange rate came into the workspace.</summary>
public static class RateSources
{
    /// <summary>Typed by a person.</summary>
    public const string Manual = "manual";

    /// <summary>Read from an imported file.</summary>
    public const string Import = "import";

    public static readonly IReadOnlyList<string> All = [Manual, Import];
}

/// <summary>Which rate a lookup accepts for a date.</summary>
public enum RateLookupRule
{
    /// <summary>Only a rate dated exactly on the date.</summary>
    ExactDate,

    /// <summary>The rate dated on the date or, when there is none, the latest rate before it that
    /// is no older than the currency's own limit (its <c>maxRateAgeDays</c>, 7 unless the workspace
    /// changed it). A rate dated after the date is never used.</summary>
    LatestWithinLimit,
}

/// <summary>The exchange rate a lookup found: <see cref="Rate"/> units of
/// <see cref="QuoteCurrency"/> per one unit of <see cref="Currency"/>, dated
/// <see cref="RateDate"/> (on or before <see cref="RequestedDate"/>).</summary>
/// <param name="RateId">The rate record used; null when the two currencies are the same (rate 1).</param>
/// <param name="Exact">True when the rate is dated on the requested date itself.</param>
/// <param name="Source">How the rate came in (<see cref="RateSources"/>); "same" for rate 1.</param>
public sealed record RateFound(
    string Currency,
    string QuoteCurrency,
    decimal Rate,
    DateOnly RateDate,
    DateOnly RequestedDate,
    bool Exact,
    string Source,
    Guid? RateId);

/// <summary>Why no rate was found for a currency and date.</summary>
public enum RateMissingReason
{
    /// <summary>The currency is not enabled for the workspace.</summary>
    CurrencyNotEnabled,

    /// <summary>No rate dated on or before the date within the currency's limit.</summary>
    NoRate,
}

/// <summary>A lookup's answer: the rate, or why there is none (with the latest rate before the
/// date, if any, so the caller can say how stale it would have been).</summary>
public sealed record RateLookup(RateFound? Found, RateMissingReason? Missing, DateOnly? LatestBefore, int MaxRateAgeDays)
{
    public bool IsFound => Found is not null;
}

/// <summary>An amount converted to a company's base currency: the kernel <see cref="Money"/>
/// (amount, currency, rate used, base amount, base currency; CLAUDE.md rule 2) and the rate record
/// it came from.</summary>
public sealed record Conversion(Money Money, RateFound Rate);

/// <summary>
/// Exchange rates of the current workspace for later modules (invoices, payments, the ledger):
/// they use this instead of the currency tables. Every lookup runs in the caller's unit of work
/// under its row-level security.
/// </summary>
public interface IExchangeRates
{
    /// <summary>The rate of <paramref name="currency"/> in <paramref name="quoteCurrency"/> for
    /// <paramref name="date"/> under <paramref name="rule"/>. The same currency on both sides is
    /// rate 1. Only a rate quoted directly in <paramref name="quoteCurrency"/> is used: no inverse
    /// and no cross rate is worked out.</summary>
    Task<RateLookup> FindAsync(CurrencyCode currency, CurrencyCode quoteCurrency, DateOnly date, RateLookupRule rule, CancellationToken cancellationToken);
}

/// <summary>Converts amounts to a company's base currency at the workspace's rates.</summary>
public interface ICurrencyConverter
{
    /// <summary>
    /// <paramref name="amount"/> of <paramref name="currency"/> in the base currency of company
    /// <paramref name="companyId"/> at the rate for <paramref name="date"/>: an unrounded
    /// <see cref="Money"/> (a line keeps full precision; its stated base amount is rounded once to
    /// the base currency's minor units, half away from zero, docs/compliance/vat-rounding-and-currency.md).
    /// Null company: the caller's working company. Answers null when the company is not in the
    /// caller's scope, and a <see cref="RateLookup"/> without a rate when none applies.
    /// </summary>
    Task<ConversionOutcome?> ToBaseAsync(decimal amount, CurrencyCode currency, DateOnly date, Guid? companyId, RateLookupRule rule, CancellationToken cancellationToken);
}

/// <summary>The conversion, or the lookup that found no rate.</summary>
public sealed record ConversionOutcome(Conversion? Conversion, RateLookup Lookup, Guid CompanyId, string BaseCurrency);
