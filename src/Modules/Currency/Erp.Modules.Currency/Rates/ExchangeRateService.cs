using Erp.Kernel.Money;
using Erp.Modules.Currency.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Currency.Rates;

/// <summary>
/// The rate lookup and the conversion to a company's base currency, for this module's endpoints
/// and for later modules through <see cref="IExchangeRates"/> and <see cref="ICurrencyConverter"/>.
/// The rules (docs/decisions/p08-currency-rate-lookup.md):
/// <list type="number">
/// <item>The same currency on both sides is rate 1.</item>
/// <item>Only a rate quoted directly in the wanted currency counts: no inverse, no cross rate.</item>
/// <item>A rate dated on the date itself is used (exact).</item>
/// <item>Otherwise, under <see cref="RateLookupRule.LatestWithinLimit"/>, the latest rate dated
/// before the date, if it is at most the currency's <c>maxRateAgeDays</c> older (7 by default).</item>
/// <item>A rate dated after the date is never used.</item>
/// <item>Otherwise there is no rate, and the answer says why and when the latest earlier rate was.</item>
/// </list>
/// The conversion builds the kernel <see cref="Money"/> with the rate found: an unrounded figure whose
/// stated base amount is rounded once, half away from zero, to the base currency's minor units
/// (docs/compliance/vat-rounding-and-currency.md, rules 1 and 2).
/// </summary>
internal sealed class ExchangeRateService(CurrencyDbContext db, ICompanyDirectory companies) : IExchangeRates, ICurrencyConverter
{
    public const string SameCurrencySource = "same";

    public async Task<RateLookup> FindAsync(CurrencyCode currency, CurrencyCode quoteCurrency, DateOnly date, RateLookupRule rule, CancellationToken cancellationToken)
    {
        if (currency == quoteCurrency)
        {
            return new RateLookup(new RateFound(currency.Code, quoteCurrency.Code, 1m, date, date, true, SameCurrencySource, null), null, null, 0);
        }
        var enabled = await db.Currencies.AsNoTracking().Where(c => c.Code == currency.Code)
            .Select(c => new { c.MaxRateAgeDays }).SingleOrDefaultAsync(cancellationToken);
        if (enabled is null)
        {
            return new RateLookup(null, RateMissingReason.CurrencyNotEnabled, null, 0);
        }
        var maxAge = rule == RateLookupRule.ExactDate ? 0 : enabled.MaxRateAgeDays;
        var latest = await db.Rates.AsNoTracking()
            .Where(r => r.Currency == currency.Code && r.QuoteCurrency == quoteCurrency.Code && r.RateDate <= date)
            .OrderByDescending(r => r.RateDate)
            .Select(r => new { r.Id, r.RateDate, r.Rate, r.Source })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null || latest.RateDate < date.AddDays(-maxAge))
        {
            return new RateLookup(null, RateMissingReason.NoRate, latest?.RateDate, maxAge);
        }
        return new RateLookup(
            new RateFound(currency.Code, quoteCurrency.Code, CurrencyContext.Trim(latest.Rate), latest.RateDate, date, latest.RateDate == date, latest.Source, latest.Id),
            null, null, maxAge);
    }

    public async Task<ConversionOutcome?> ToBaseAsync(decimal amount, CurrencyCode currency, DateOnly date, Guid? companyId, RateLookupRule rule, CancellationToken cancellationToken)
    {
        var company = companyId is { } id ? await companies.GetAsync(id, cancellationToken) : await companies.GetWorkingAsync(cancellationToken);
        if (company is null || !CurrencyCode.TryParse(company.BaseCurrency, out var baseCurrency))
        {
            return null;
        }
        var lookup = await FindAsync(currency, baseCurrency, date, rule, cancellationToken);
        if (lookup.Found is not { } rate)
        {
            return new ConversionOutcome(null, lookup, company.Id, baseCurrency.Code);
        }
        var money = Money.Unrounded(amount, currency, rate.Rate, baseCurrency);
        return new ConversionOutcome(new Conversion(money, rate), lookup, company.Id, baseCurrency.Code);
    }
}
