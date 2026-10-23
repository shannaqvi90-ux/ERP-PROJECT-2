using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Validator = Erp.Kernel.Http.Validator;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Money;
using Erp.Kernel.Security;
using Erp.Modules.Currency.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Currency.Rates;

/// <summary>A row of the exchange rates list: <c>rate</c> units of <c>quoteCurrency</c> per one
/// unit of <c>currency</c> on <c>rateDate</c>; <c>inverseRate</c> is the same rate turned around
/// (for reading; eight decimals).</summary>
public sealed record RateRow(
    Guid Id,
    DateOnly RateDate,
    string Currency,
    decimal Rate,
    string QuoteCurrency,
    decimal InverseRate,
    string Source,
    string? Reference,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>An exchange rate as its form shows it.</summary>
public sealed record RateDto(
    Guid Id,
    DateOnly RateDate,
    string Currency,
    decimal Rate,
    string QuoteCurrency,
    decimal InverseRate,
    string Source,
    string? Reference,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    [property: Description("True when the caller works in every company of the workspace and every branch of each: only then may they change rates, which every company shares.")]
    bool EveryCompany);

/// <summary>Enter or correct an exchange rate: <c>rate</c> units of <c>quoteCurrency</c> per one
/// unit of <c>currency</c> on <c>rateDate</c>. A new rate's quote currency defaults to the working
/// company's base currency and its date to today in the workspace's time zone. A rate's currencies
/// never change; its date, rate and reference may.</summary>
public sealed record SaveRateRequest(
    [property: ApiExample("EUR"), StringLength(3, MinimumLength = 3)] string? Currency,
    [property: ApiExample("AED"), StringLength(3, MinimumLength = 3)] string? QuoteCurrency,
    [property: ApiExample("2026-10-23")] DateOnly? RateDate,
    [property: ApiExample("4.2875"), Description("Quote-currency units per one unit of the currency: positive, at most eight decimals.")] decimal? Rate,
    [property: ApiExample("CBUAE 23 Oct 2026"), StringLength(CurrencyRules.ReferenceLength)] string? Reference,
    uint? Version);

/// <summary>Import exchange rates from a delimited text file (CSV). The first line names the
/// columns: <c>currency</c>, <c>date</c> and <c>rate</c> are required, <c>quote_currency</c> and
/// <c>reference</c> are optional (comma, semicolon or tab between values). Dates are yyyy-mm-dd or
/// dd/mm/yyyy; rates use a dot for decimals. Nothing is saved unless every line is valid.</summary>
public sealed record ImportRatesRequest(
    [property: ApiExample("cbuae-rates-2026-10.csv"), StringLength(CurrencyRules.ReferenceLength)] string? FileName,
    [property: ApiExample("currency,date,rate\nEUR,2026-10-23,4.2875\nUSD,2026-10-23,3.6725")] string? Content,
    [property: Description("A rate already held for the same currencies and date: true replaces it with the file's, false (the default) keeps it.")] bool? ReplaceExisting,
    [property: ApiExample("AED"), StringLength(3, MinimumLength = 3), Description("Quote currency of lines that name none; the working company's base currency when left out.")] string? QuoteCurrency);

/// <summary>What an import did: lines read, rates added, rates replaced, rates kept as they were
/// (held already, ReplaceExisting false) and lines whose rate was already held unchanged.</summary>
public sealed record ImportRatesResult(int Lines, int Added, int Replaced, int Kept, int Unchanged);

internal static class RateEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/rates", List)
            .WithName("currency.rates.list")
            .WithSummary("The workspace's exchange rates, a page at a time: word search on currency and reference, filters (currency eq 'EUR', rateDate ge '2026-01-01'), sort, keyset or offset paging and grouping (the list query contract); newest first by default.")
            .RequirePermission(CurrencyPermissions.RatesRead);

        group.MapGet("/rates/{id:guid}", Get)
            .WithName("currency.rates.get")
            .WithSummary("One exchange rate.")
            .RequirePermission(CurrencyPermissions.RatesRead);

        group.MapPost("/rates", Create)
            .WithName("currency.rates.create")
            .WithSummary("Enter an exchange rate by hand (one per currency pair and date; needs every company of the workspace, which shares the rates).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(CurrencyPermissions.RatesCreate);

        group.MapPut("/rates/{id:guid}", Update)
            .WithName("currency.rates.update")
            .WithSummary("Correct an exchange rate's date, rate or reference (its currencies stay; the rate becomes a manual one).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(CurrencyPermissions.RatesUpdate);

        group.MapDelete("/rates/{id:guid}", Delete)
            .WithName("currency.rates.delete")
            .WithSummary("Delete an exchange rate (amounts already converted keep the rate they were converted at).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(CurrencyPermissions.RatesDelete);

        group.MapPost("/rates/import", Import)
            .WithName("currency.rates.import")
            .WithSummary("Import exchange rates from a CSV file's text (columns currency, date, rate, and optionally quote_currency and reference; at most 20,000 lines). Nothing is saved unless every line is valid.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(CurrencyPermissions.RatesImport);

        group.MapGet("/rates/lookup", Lookup)
            .WithName("currency.rates.lookup")
            .WithSummary("The exchange rate that applies to a currency on a date: the rate of that date, else the latest earlier rate no older than the currency's limit (7 days unless changed); never a later rate, an inverse or a cross rate. 404 with the reason when none applies.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(CurrencyPermissions.RatesRead);
    }

    private static async Task<Results<Ok<ListPage<RateRow>>, ProblemHttpResult>> List(
        CurrencyDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the rates list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<RateRow>> PageAsync(CurrencyDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await catalog.ListBinding<ExchangeRate>(RatesList.Key).QueryAsync(db.Rates.AsNoTracking(), request, http, cancellationToken);
        return result.Problem is not null ? result.Map(_ => (RateRow)null!) : result.Map(ToRow);
    }

    private static RateRow ToRow(ExchangeRate r) => new(r.Id, r.RateDate, r.Currency, CurrencyContext.Trim(r.Rate), r.QuoteCurrency,
        CurrencyContext.Inverse(r.Rate), r.Source, r.Reference, r.UpdatedAt, r.Version);

    private static async Task<Results<Ok<RateDto>, ProblemHttpResult>> Get(Guid id, CurrencyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var rate = await db.Rates.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rate is null ? Problems.NotFound(http) : TypedResults.Ok(ToDto(rate, session));
    }

    private static async Task<Results<Created<RateDto>, ProblemHttpResult>> Create(
        SaveRateRequest request, CurrencyDbContext db, ErpDbSession session, ITenantDirectory tenants, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var currency = Code(request.Currency);
        var quote = Code(request.QuoteCurrency) ?? await CurrencyContext.WorkingBaseCurrencyAsync(companies, cancellationToken);
        var date = request.RateDate ?? await CurrencyContext.TodayAsync(tenants, cancellationToken);
        var validator = new Validator(http).Required("currency", currency).Required("rate", request.Rate);
        CheckFields(validator, currency, quote, date, request.Rate, request.Reference);
        if (validator.IsValid)
        {
            await CheckEnabledAsync(validator, db, currency!, quote, cancellationToken);
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        // Every company of the workspace converts at the same rates: only someone who works in all
        // of them, in every branch of each, enters one. The kernel refuses the write as well.
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "currency.workspaceNeedsEveryCompany");
        }
        if (await db.Rates.AnyAsync(r => r.Currency == currency && r.QuoteCurrency == quote && r.RateDate == date, cancellationToken))
        {
            return Problems.Conflict(http, "currency.rateExists");
        }
        var rate = new ExchangeRate
        {
            Currency = currency!,
            QuoteCurrency = quote,
            RateDate = date,
            Rate = request.Rate!.Value,
            Source = RateSources.Manual,
            Reference = Clean(request.Reference),
        };
        db.Rates.Add(rate);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/currency/rates/{rate.Id}", ToDto(rate, session));
    }

    private static async Task<Results<Ok<RateDto>, ProblemHttpResult>> Update(
        Guid id, SaveRateRequest request, CurrencyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var currency = Code(request.Currency);
        var quote = Code(request.QuoteCurrency);
        var validator = new Validator(http)
            .Required("currency", currency).Required("quoteCurrency", quote).Required("rateDate", request.RateDate)
            .Required("rate", request.Rate).Required("version", request.Version);
        CheckFields(validator, currency, quote, request.RateDate, request.Rate, request.Reference);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "currency.workspaceNeedsEveryCompany");
        }
        var rate = await db.Rates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rate is null)
        {
            return Problems.NotFound(http);
        }
        if (currency != rate.Currency || quote != rate.QuoteCurrency)
        {
            return new Validator(http).Add(currency != rate.Currency ? "currency" : "quoteCurrency", "currencyRateCurrencyFixed").ToResult();
        }
        var date = request.RateDate!.Value;
        if (date != rate.RateDate && await db.Rates.AnyAsync(r => r.Currency == rate.Currency && r.QuoteCurrency == rate.QuoteCurrency && r.RateDate == date && r.Id != id, cancellationToken))
        {
            return Problems.Conflict(http, "currency.rateExists");
        }
        db.Entry(rate).Property(r => r.Version).OriginalValue = request.Version!.Value;
        rate.RateDate = date;
        rate.Rate = request.Rate!.Value;
        rate.Reference = Clean(request.Reference);
        rate.Source = RateSources.Manual;
        db.Entry(rate).Property(r => r.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(rate, session));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(Guid id, CurrencyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "currency.workspaceNeedsEveryCompany");
        }
        var rate = await db.Rates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rate is null)
        {
            return Problems.NotFound(http);
        }
        db.Rates.Remove(rate);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ImportRatesResult>, ProblemHttpResult>> Import(
        ImportRatesRequest request, CurrencyDbContext db, ErpDbSession session, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("content", request.Content)
            .MaxLength("fileName", request.FileName, CurrencyRules.ReferenceLength);
        var defaultQuote = Code(request.QuoteCurrency) ?? await CurrencyContext.WorkingBaseCurrencyAsync(companies, cancellationToken);
        validator.Must(CurrencyCode.IsKnown(defaultQuote), "quoteCurrency", "currencyCode");
        if (request.Content is { Length: > CurrencyRules.ImportTextLimit })
        {
            validator.Add("content", "currencyImportTooLarge", CurrencyRules.ImportTextLimit);
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "currency.workspaceNeedsEveryCompany");
        }
        var enabled = (await db.Currencies.AsNoTracking().Select(c => new { c.Code, c.IsActive }).ToListAsync(cancellationToken))
            .ToDictionary(c => c.Code, c => c.IsActive, StringComparer.Ordinal);
        var parsed = RateFile.Parse(request.Content!, defaultQuote, enabled);
        if (parsed.Errors.Count > 0)
        {
            var errors = new Validator(http);
            foreach (var error in parsed.Errors.Take(RateFile.ErrorLimit))
            {
                errors.Add("content", error.Code, error.Line, error.Value);
            }
            if (parsed.Errors.Count > RateFile.ErrorLimit)
            {
                errors.Add("content", "currencyImportMoreErrors", parsed.Errors.Count - RateFile.ErrorLimit);
            }
            return errors.ToResult();
        }
        var reference = Clean(request.FileName);
        var lines = parsed.Lines;
        var pairs = lines.Select(l => l.Currency + l.Quote).Distinct().ToList();
        var first = lines.Min(l => l.Date);
        var last = lines.Max(l => l.Date);
        var currencies = lines.Select(l => l.Currency).Distinct().ToList();
        var held = await db.Rates
            .Where(r => currencies.Contains(r.Currency) && r.RateDate >= first && r.RateDate <= last)
            .ToListAsync(cancellationToken);
        var byKey = held.Where(r => pairs.Contains(r.Currency + r.QuoteCurrency))
            .ToDictionary(r => (r.Currency, r.QuoteCurrency, r.RateDate));
        int added = 0, replaced = 0, kept = 0, unchanged = 0;
        foreach (var line in lines)
        {
            if (byKey.TryGetValue((line.Currency, line.Quote, line.Date), out var existing))
            {
                if (existing.Rate == line.Rate)
                {
                    unchanged++;
                }
                else if (request.ReplaceExisting == true)
                {
                    existing.Rate = line.Rate;
                    existing.Source = RateSources.Import;
                    existing.Reference = line.Reference ?? reference;
                    db.Entry(existing).Property(r => r.UpdatedAt).IsModified = true;
                    replaced++;
                }
                else
                {
                    kept++;
                }
                continue;
            }
            db.Rates.Add(new ExchangeRate
            {
                Currency = line.Currency,
                QuoteCurrency = line.Quote,
                RateDate = line.Date,
                Rate = line.Rate,
                Source = RateSources.Import,
                Reference = line.Reference ?? reference,
            });
            added++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ImportRatesResult(lines.Count, added, replaced, kept, unchanged));
    }

    private static async Task<Results<Ok<RateFound>, ProblemHttpResult>> Lookup(
        [FromQuery, Description("ISO 4217 code of the currency."), ApiExample("EUR")] string? currency,
        [FromQuery, Description("The date (yyyy-mm-dd); today in the workspace's time zone when left out.")] DateOnly? date,
        [FromQuery, Description("The currency the rate is quoted in; the working company's base currency when left out."), ApiExample("AED")] string? quoteCurrency,
        [FromQuery, Description("True accepts only a rate dated on the date itself.")] bool? exact,
        IExchangeRates rates, ITenantDirectory tenants, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var code = Code(currency);
        var quote = Code(quoteCurrency) ?? await CurrencyContext.WorkingBaseCurrencyAsync(companies, cancellationToken);
        var validator = new Validator(http).Required("currency", code);
        if (code is not null)
        {
            validator.Must(CurrencyCode.IsKnown(code), "currency", "currencyCode");
        }
        validator.Must(CurrencyCode.IsKnown(quote), "quoteCurrency", "currencyCode");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var on = date ?? await CurrencyContext.TodayAsync(tenants, cancellationToken);
        var lookup = await rates.FindAsync(new CurrencyCode(code!), new CurrencyCode(quote), on, exact == true ? RateLookupRule.ExactDate : RateLookupRule.LatestWithinLimit, cancellationToken);
        return lookup.Found is { } found ? TypedResults.Ok(found) : MissingRate(http, code!, quote, on, lookup);
    }

    /// <summary>404 naming why no rate applies: the currency is not enabled, or no rate within its limit.</summary>
    internal static ProblemHttpResult MissingRate(HttpContext http, string currency, string quote, DateOnly date, RateLookup lookup)
    {
        var day = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (lookup.Missing == RateMissingReason.CurrencyNotEnabled)
        {
            return Problems.Result(http, StatusCodes.Status404NotFound, "currency.currencyNotEnabled", null, currency);
        }
        return lookup.LatestBefore is { } latest
            ? Problems.Result(http, StatusCodes.Status404NotFound, "currency.rateTooOld", null, currency, quote, day,
                latest.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), lookup.MaxRateAgeDays)
            : Problems.Result(http, StatusCodes.Status404NotFound, "currency.rateMissing", null, currency, quote, day);
    }

    private static void CheckFields(Validator validator, string? currency, string? quote, DateOnly? date, decimal? rate, string? reference)
    {
        if (currency is not null)
        {
            validator.Must(CurrencyCode.IsKnown(currency), "currency", "currencyCode");
        }
        if (quote is not null)
        {
            validator.Must(CurrencyCode.IsKnown(quote), "quoteCurrency", "currencyCode");
            validator.Must(currency is null || currency != quote, "quoteCurrency", "currencySameAsQuote");
        }
        if (date is { } day)
        {
            validator.Must(day >= CurrencyRules.EarliestDate && day <= CurrencyRules.LatestDate, "rateDate", "currencyRateDate");
        }
        if (rate is { } value)
        {
            validator.Must(CurrencyRules.IsValidRate(value), "rate", "currencyRate");
        }
        validator.MaxLength("reference", reference, CurrencyRules.ReferenceLength);
    }

    private static async Task CheckEnabledAsync(Validator validator, CurrencyDbContext db, string currency, string quote, CancellationToken cancellationToken)
    {
        var codes = new[] { currency, quote };
        var enabled = await db.Currencies.AsNoTracking().Where(c => codes.Contains(c.Code)).Select(c => new { c.Code, c.IsActive }).ToListAsync(cancellationToken);
        var own = enabled.FirstOrDefault(c => c.Code == currency);
        if (own is null)
        {
            validator.Add("currency", "currencyNotEnabled", currency);
        }
        else if (!own.IsActive)
        {
            validator.Add("currency", "currencyInactive", currency);
        }
        if (enabled.All(c => c.Code != quote))
        {
            validator.Add("quoteCurrency", "currencyNotEnabled", quote);
        }
    }

    private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RateDto ToDto(ExchangeRate r, ErpDbSession session) => new(r.Id, r.RateDate, r.Currency, CurrencyContext.Trim(r.Rate), r.QuoteCurrency,
        CurrencyContext.Inverse(r.Rate), r.Source, r.Reference, r.CreatedAt, r.UpdatedAt, r.Version, session.HoldsWholeWorkspace);
}
