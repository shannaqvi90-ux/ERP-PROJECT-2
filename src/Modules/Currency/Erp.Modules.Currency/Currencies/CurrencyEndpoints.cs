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
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Currency.Currencies;

/// <summary>A row of the currencies list: the currency and its latest rate in
/// <c>quoteCurrency</c> (the working company's base currency); no rate for the base currency itself.</summary>
public sealed record CurrencyRow(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    int MinorUnits,
    bool IsActive,
    int MaxRateAgeDays,
    string QuoteCurrency,
    decimal? LatestRate,
    DateOnly? LatestRateDate,
    uint Version);

/// <summary>A currency as its form shows it.</summary>
public sealed record CurrencyDto(
    Guid Id,
    string Code,
    int Number,
    string NameEn,
    string NameAr,
    int MinorUnits,
    bool IsActive,
    int MaxRateAgeDays,
    string QuoteCurrency,
    decimal? LatestRate,
    DateOnly? LatestRateDate,
    [property: Description("True when this currency is the base currency of one of the workspace's companies: it cannot be made inactive.")]
    bool IsBaseCurrency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    [property: Description("True when the caller works in every company of the workspace and every branch of each: only then may they change currencies and rates, which every company shares.")]
    bool EveryCompany,
    [property: Description("The date of the ISO 4217 list (List One) the currencies, their numbers and minor units come from.")]
    string IsoPublished);

/// <summary>Change a currency of the workspace: put it in use or out of use, and how many days old the
/// latest rate may be on a date that has none. Its code and names are ISO 4217's and the product's
/// and never change here.</summary>
public sealed record UpdateCurrencyRequest(
    bool? IsActive,
    [property: Range(0, CurrencyRules.MaxRateAgeDaysLimit)] int? MaxRateAgeDays,
    uint? Version);

internal static class CurrencyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/currencies", List)
            .WithName("currency.currencies.list")
            .WithSummary("The workspace's currencies (every current ISO 4217 currency), each with its latest rate in the working company's base currency, a page at a time: word search on code and names, filters (isActive eq true for those in use), sort, paging and grouping (the list query contract); those in use first, then by code.")
            .RequirePermission(CurrencyPermissions.CurrenciesRead);

        group.MapGet("/currencies/{id:guid}", Get)
            .WithName("currency.currencies.get")
            .WithSummary("One of the workspace's currencies.")
            .RequirePermission(CurrencyPermissions.CurrenciesRead);

        group.MapPut("/currencies/{id:guid}", Update)
            .WithName("currency.currencies.update")
            .WithSummary("Put a currency in use or out of use, or change how old the latest rate may be on a date without one (needs every company of the workspace, which shares its currencies). A company's base currency stays in use.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(CurrencyPermissions.CurrenciesUpdate);
    }

    private static async Task<Results<Ok<ListPage<CurrencyRow>>, ProblemHttpResult>> List(
        CurrencyDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the currencies list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<CurrencyRow>> PageAsync(CurrencyDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var quote = await CurrencyContext.WorkingBaseCurrencyAsync(http.RequestServices.GetRequiredService<ICompanyDirectory>(), cancellationToken);
        var rows = await RowsAsync(db, quote, null, cancellationToken);
        return await catalog.ListBinding<CurrencyRow>(CurrenciesList.Key).QueryAsync(rows.AsQueryable(), request, http, cancellationToken);
    }

    /// <summary>The workspace's currencies (or one) with each one's latest rate in <paramref name="quote"/>.</summary>
    private static async Task<List<CurrencyRow>> RowsAsync(CurrencyDbContext db, string quote, Guid? id, CancellationToken cancellationToken)
    {
        var currencies = db.Currencies.AsNoTracking();
        if (id is { } one)
        {
            currencies = currencies.Where(c => c.Id == one);
        }
        var found = await currencies
            .Select(c => new
            {
                Currency = c,
                Latest = db.Rates.Where(r => r.Currency == c.Code && r.QuoteCurrency == quote)
                    .OrderByDescending(r => r.RateDate)
                    .Select(r => new { r.RateDate, r.Rate })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return found.Select(x => new CurrencyRow(
                x.Currency.Id, x.Currency.Code, x.Currency.NameEn, x.Currency.NameAr, MinorUnits(x.Currency.Code), x.Currency.IsActive,
                x.Currency.MaxRateAgeDays, quote,
                x.Latest is null ? null : CurrencyContext.Trim(x.Latest.Rate), x.Latest?.RateDate, x.Currency.Version))
            .ToList();
    }

    private static int MinorUnits(string code) => CurrencyCode.TryParse(code, out var currency) ? currency.MinorUnits : 2;

    private static async Task<Results<Ok<CurrencyDto>, ProblemHttpResult>> Get(Guid id, CurrencyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var dto = await ToDtoAsync(db, session, http, id, cancellationToken);
        return dto is null ? Problems.NotFound(http) : TypedResults.Ok(dto);
    }

    private static async Task<Results<Ok<CurrencyDto>, ProblemHttpResult>> Update(
        Guid id, UpdateCurrencyRequest request, CurrencyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("isActive", request.IsActive)
            .Required("maxRateAgeDays", request.MaxRateAgeDays)
            .Required("version", request.Version);
        CheckMaxAge(validator, request.MaxRateAgeDays);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "currency.workspaceNeedsEveryCompany");
        }
        var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (currency is null)
        {
            return Problems.NotFound(http);
        }
        if (request.IsActive == false && currency.IsActive && await IsBaseCurrencyAsync(http, currency.Code, cancellationToken))
        {
            return new Validator(http).Add("isActive", "currencyBaseStaysActive").ToResult();
        }
        db.Entry(currency).Property(c => c.Version).OriginalValue = request.Version!.Value;
        currency.IsActive = request.IsActive!.Value;
        currency.MaxRateAgeDays = request.MaxRateAgeDays!.Value;
        db.Entry(currency).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToDtoAsync(db, session, http, currency.Id, cancellationToken))!);
    }

    private static void CheckMaxAge(Validator validator, int? days)
    {
        if (days is { } value)
        {
            validator.Must(value is >= 0 and <= CurrencyRules.MaxRateAgeDaysLimit, "maxRateAgeDays", "currencyMaxRateAge", CurrencyRules.MaxRateAgeDaysLimit);
        }
    }

    /// <summary>Whether a company the caller can see keeps its books in <paramref name="code"/>.
    /// Only a caller who works in every company changes a currency, so every company is seen.</summary>
    private static async Task<bool> IsBaseCurrencyAsync(HttpContext http, string code, CancellationToken cancellationToken) =>
        (await http.RequestServices.GetRequiredService<ICompanyDirectory>().ListAsync(cancellationToken)).Any(c => c.BaseCurrency == code);

    private static async Task<CurrencyDto?> ToDtoAsync(CurrencyDbContext db, ErpDbSession session, HttpContext http, Guid id, CancellationToken cancellationToken)
    {
        var quote = await CurrencyContext.WorkingBaseCurrencyAsync(http.RequestServices.GetRequiredService<ICompanyDirectory>(), cancellationToken);
        var row = (await RowsAsync(db, quote, id, cancellationToken)).SingleOrDefault();
        if (row is null)
        {
            return null;
        }
        var times = await db.Currencies.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.CreatedAt, c.UpdatedAt }).SingleAsync(cancellationToken);
        var number = Iso4217.Currencies.TryGetValue(row.Code, out var entry) ? entry.Number : 0;
        return new CurrencyDto(row.Id, row.Code, number, row.NameEn, row.NameAr, row.MinorUnits, row.IsActive, row.MaxRateAgeDays, row.QuoteCurrency,
            row.LatestRate, row.LatestRateDate, await IsBaseCurrencyAsync(http, row.Code, cancellationToken), times.CreatedAt, times.UpdatedAt, row.Version,
            session.HoldsWholeWorkspace, Iso4217.Published);
    }
}
