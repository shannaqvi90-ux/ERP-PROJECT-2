using System.ComponentModel;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Money;
using Erp.Kernel.Security;
using Erp.Modules.Currency.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Currency.Rates;

/// <summary>An amount converted to a company's base currency. <c>money</c> carries the amount, its
/// currency, the rate used, the base amount (rounded once to the base currency's minor units, half
/// away from zero) and the base currency; <c>exactBaseAmount</c> is the amount times the rate before
/// that rounding.</summary>
public sealed record ConversionDto(
    Money Money,
    decimal ExactBaseAmount,
    decimal InverseRate,
    DateOnly RequestedDate,
    DateOnly RateDate,
    [property: Description("True when the rate is dated on the requested date itself; false when it is the latest earlier rate within the currency's limit.")]
    bool Exact,
    string Source,
    Guid? RateId,
    Guid CompanyId);

internal static class ConversionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/convert", Convert)
            .WithName("currency.convert")
            .WithSummary("An amount of a currency in a company's base currency (the working company's when none is named) at the rate that applies on a date (today in the workspace's time zone when left out): the amount, its currency, the rate used and its date, and the base amount rounded once to the base currency's minor units, half away from zero. 404 with the reason when no rate applies.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(CurrencyPermissions.RatesRead);
    }

    private static async Task<Results<Ok<ConversionDto>, ProblemHttpResult>> Convert(
        [FromQuery, Description("The amount, a decimal with a dot (at most ten decimals)."), ApiExample("1250.00")] string? amount,
        [FromQuery, Description("ISO 4217 code of the amount's currency."), ApiExample("EUR")] string? currency,
        [FromQuery, Description("The date of the rate (yyyy-mm-dd); today in the workspace's time zone when left out.")] DateOnly? date,
        [FromQuery, Description("The company whose base currency the amount is converted to; the working company when left out.")] Guid? companyId,
        [FromQuery, Description("True accepts only a rate dated on the date itself.")] bool? exact,
        ICurrencyConverter converter, ITenantDirectory tenants, HttpContext http, CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        var validator = new Validator(http).Required("amount", amount).Required("currency", code);
        decimal value = 0;
        if (!string.IsNullOrWhiteSpace(amount))
        {
            validator.Must(decimal.TryParse(amount.Trim(), System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out value) && decimal.Round(value, Money.AmountScale) == value && Math.Abs(value) <= Money.MaxAmount,
                "amount", "currencyAmount");
        }
        if (code is not null)
        {
            validator.Must(CurrencyCode.IsKnown(code), "currency", "currencyCode");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var on = date ?? await CurrencyContext.TodayAsync(tenants, cancellationToken);
        ConversionOutcome? outcome;
        try
        {
            outcome = await converter.ToBaseAsync(value, new CurrencyCode(code!), on, companyId, exact == true ? RateLookupRule.ExactDate : RateLookupRule.LatestWithinLimit, cancellationToken);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new Validator(http).Add("amount", "currencyAmountTooLarge").ToResult();
        }
        if (outcome is null)
        {
            return companyId is null ? Problems.Result(http, StatusCodes.Status404NotFound, "currency.noWorkingCompany") : Problems.NotFound(http);
        }
        if (outcome.Conversion is not { } conversion)
        {
            return RateEndpoints.MissingRate(http, code!, outcome.BaseCurrency, on, outcome.Lookup);
        }
        var rate = conversion.Rate;
        return TypedResults.Ok(new ConversionDto(conversion.Money, conversion.Money.ExactBaseAmount(),
            rate.Rate == 1m ? 1m : CurrencyContext.Inverse(rate.Rate), on, rate.RateDate, rate.Exact, rate.Source, rate.RateId, outcome.CompanyId));
    }
}
