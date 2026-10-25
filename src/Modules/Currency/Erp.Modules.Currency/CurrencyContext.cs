using Erp.Kernel.Money;
using Erp.Modules.Tenancy.Contracts;

namespace Erp.Modules.Currency;

/// <summary>What the currency screens and endpoints take from the rest of the workspace: today's
/// date in the workspace's time zone, and the base currency of the caller's working company.</summary>
internal static class CurrencyContext
{
    public const string FallbackTimeZone = "Asia/Dubai";

    /// <summary>Today in the workspace's time zone (rates are dated by the UAE calendar day, not UTC's).</summary>
    public static async Task<DateOnly> TodayAsync(ITenantDirectory tenants, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetCurrentAsync(cancellationToken);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant?.TimeZone ?? FallbackTimeZone, out var found)
            ? found
            : TimeZoneInfo.TryFindSystemTimeZoneById(FallbackTimeZone, out var dubai) ? dubai : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    /// <summary>The base currency of the caller's working company, or the dirham when there is none.</summary>
    public static async Task<string> WorkingBaseCurrencyAsync(ICompanyDirectory companies, CancellationToken cancellationToken)
    {
        var company = await companies.GetWorkingAsync(cancellationToken);
        return company is not null && CurrencyCode.IsKnown(company.BaseCurrency) ? company.BaseCurrency : CurrencyCode.Aed.Code;
    }

    /// <summary>A decimal without trailing zeros (4.28750000 is 4.2875): rates are shown as entered.</summary>
    public static decimal Trim(decimal value) => value / 1.000000000000000000000000000000000m;

    /// <summary>Quote units per one unit, turned around (one unit of the quote currency in the
    /// currency), to eight decimals half away from zero; for reading only, never stored or used to
    /// convert.</summary>
    public static decimal Inverse(decimal rate) => Trim(decimal.Round(1m / rate, Money.RateScale, MidpointRounding.AwayFromZero));
}
