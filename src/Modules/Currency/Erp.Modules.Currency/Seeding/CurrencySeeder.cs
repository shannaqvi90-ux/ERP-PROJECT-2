using System.Collections.Immutable;
using System.Globalization;
using Erp.Kernel.Data;
using Erp.Kernel.Money;
using Erp.Kernel.Seeding;
using Erp.Modules.Currency.Contracts;
using Erp.Modules.Currency.Currencies;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;

namespace Erp.Modules.Currency.Seeding;

/// <summary>
/// Seeds each workspace's currencies and rates. Every workspace holds every current ISO 4217
/// currency (Erp.Kernel.Money.Iso4217), with the list's English name and the product's Arabic name;
/// in use are the dirham and the base currency of each company. The demo
/// also puts in use the fifty currencies of the shared comparison dataset and adds as many exchange
/// rates as the tenant's volume (100,000 in the main demo workspace: fifty currencies, one rate a
/// day, ending today in the workspace's time zone). With <c>Erp:Seed:RatesCsv</c> set (the shared
/// comparison dataset's rates.csv, gauntlet/compare/data), the main demo workspace's rates are
/// exactly the dataset's, the ones the Odoo reference holds. The gate profile puts three currencies
/// in use and adds thirty rates over the last ten days and one fixed day (31 December 2025), tenant
/// B's references carrying its canary. Idempotent: currencies a newer ISO list brings are
/// added on the next run, nothing held is changed, and rates go only into a workspace that has none.
/// </summary>
internal sealed class CurrencySeeder(CurrencyDbContext db, ErpDbSession session, ICompanyDirectory companies, ITenantDirectory tenants, IConfiguration configuration) : ITenantSeeder
{
    public const string RatesCsvSetting = "Erp:Seed:RatesCsv";

    /// <summary>The dataset's fifty currencies and a starting AED rate for each (the generated demo's
    /// own walk; the dataset's file is used when given).</summary>
    internal static readonly ImmutableArray<(string Code, decimal Rate)> DemoCurrencies =
    [
        ("USD", 3.6725m), ("EUR", 3.98m), ("GBP", 4.66m), ("SAR", 0.979m), ("OMR", 9.54m), ("KWD", 11.95m), ("BHD", 9.74m),
        ("QAR", 1.0089m), ("INR", 0.0441m), ("PKR", 0.0132m), ("CNY", 0.507m), ("JPY", 0.0246m), ("CHF", 4.17m), ("CAD", 2.69m),
        ("AUD", 2.41m), ("NZD", 2.21m), ("SGD", 2.74m), ("HKD", 0.471m), ("EGP", 0.0758m), ("JOD", 5.18m), ("TRY", 0.107m),
        ("ZAR", 0.201m), ("SEK", 0.349m), ("NOK", 0.343m), ("DKK", 0.533m), ("PLN", 0.927m), ("CZK", 0.158m), ("HUF", 0.0101m),
        ("RUB", 0.0405m), ("BRL", 0.66m), ("MXN", 0.198m), ("THB", 0.105m), ("MYR", 0.79m), ("IDR", 0.000226m), ("PHP", 0.0643m),
        ("KRW", 0.00268m), ("BDT", 0.0301m), ("LKR", 0.0122m), ("NPR", 0.0275m), ("KES", 0.0284m), ("NGN", 0.00242m), ("MAD", 0.368m),
        ("TND", 1.18m), ("IQD", 0.0028m), ("LBP", 0.000041m), ("ILS", 0.98m), ("AZN", 2.16m), ("KZT", 0.0076m), ("UAH", 0.0889m), ("VND", 0.000145m),
    ];

    /// <summary>Currencies of the gate workspaces and of the smaller demo workspace.</summary>
    internal static readonly ImmutableArray<string> FewCurrencies = ["USD", "EUR", "GBP"];

    public const int GateRateDays = 10;

    /// <summary>A day of the gate workspaces' rates that does not move with the calendar, so a
    /// documented request (an import example) can name a rate the workspace holds.</summary>
    public static readonly DateOnly GateFixedDay = new(2025, 12, 31);

    public int Order => 120;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var profile = context.Plan.Profile;
        var main = profile == SeedProfile.Demo && context.Tenant.Id == context.Plan.Tenants[0].Id;
        var csv = configuration[RatesCsvSetting];
        var shared = main && !string.IsNullOrWhiteSpace(csv);
        IReadOnlyList<(string Currency, DateOnly Date, decimal Rate)>? dataset = shared ? SharedDatasetRates.Read(csv!) : null;

        var inUse = new HashSet<string>(StringComparer.Ordinal) { CurrencyCode.Aed.Code };
        foreach (var company in await companies.ListAsync(cancellationToken))
        {
            if (CurrencyCode.IsKnown(company.BaseCurrency))
            {
                inUse.Add(company.BaseCurrency);
            }
        }
        if (profile == SeedProfile.Demo)
        {
            inUse.UnionWith(dataset is not null ? dataset.Select(r => r.Currency) : main ? DemoCurrencies.Select(c => c.Code) : FewCurrencies);
        }
        else if (profile == SeedProfile.Gate)
        {
            inUse.UnionWith(FewCurrencies);
        }
        var held = (await db.Currencies.Select(c => c.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var code in CurrencyCode.All.Where(c => !held.Contains(c)))
        {
            db.Currencies.Add(new EnabledCurrency
            {
                Code = code,
                // The names are ISO 4217's and the product's, the same in every workspace (the gate's
                // tenant B carries no canary in them: they are standard text, not its own data, and
                // its currencies are known by their ids). Every current code has its Arabic name (a
                // test keeps the two lists in step); a code a newer list brings before its name is
                // written starts with its English name.
                NameEn = Iso4217Names.English.GetValueOrDefault(code, code),
                NameAr = CurrencyNames.Arabic.GetValueOrDefault(code) ?? Iso4217Names.English.GetValueOrDefault(code, code),
                IsActive = inUse.Contains(code),
                MaxRateAgeDays = CurrencyRules.DefaultMaxRateAgeDays,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        if (profile is not (SeedProfile.Demo or SeedProfile.Gate) || await db.Rates.AnyAsync(cancellationToken))
        {
            return;
        }
        var today = await CurrencyContext.TodayAsync(tenants, cancellationToken);
        IEnumerable<(string Currency, DateOnly Date, decimal Rate)> rates = profile == SeedProfile.Gate
            ? GeneratedRates(FewCurrencies.Select(c => (c, DemoCurrencies.Single(d => d.Code == c).Rate)).ToArray(), GateRateDays, today, context.Tenant.Code)
                .Concat(FewCurrencies.Select(c => (c, GateFixedDay, DemoCurrencies.Single(d => d.Code == c).Rate)))
            : dataset ?? (main
                ? GeneratedRates(DemoCurrencies, Math.Max(1, context.Tenant.Volume / DemoCurrencies.Length), today, "demo")
                : GeneratedRates(FewCurrencies.Select(c => (c, DemoCurrencies.Single(d => d.Code == c).Rate)).ToArray(),
                    Math.Max(1, context.Tenant.Volume / FewCurrencies.Length), today, context.Tenant.Code));
        var reference = context.Mark(dataset is not null ? "Comparison dataset (rates.csv)" : profile == SeedProfile.Gate ? "Gate rates" : "Demo rates");
        await InsertAsync(context.Tenant.Id, rates, reference, cancellationToken);
    }

    private async Task InsertAsync(Guid tenantId, IEnumerable<(string Currency, DateOnly Date, decimal Rate)> rates, string reference, CancellationToken cancellationToken)
    {
        var columns = new BulkColumn[]
        {
            new("id", NpgsqlDbType.Uuid),
            new("tenant_id", NpgsqlDbType.Uuid),
            new("currency", NpgsqlDbType.Varchar),
            new("quote_currency", NpgsqlDbType.Varchar),
            new("rate_date", NpgsqlDbType.Date),
            new("rate", NpgsqlDbType.Numeric),
            new("source", NpgsqlDbType.Varchar),
            new("reference", NpgsqlDbType.Varchar),
        };
        var rows = rates.Select(r => new object?[] { Guid.CreateVersion7(), tenantId, r.Currency, CurrencyCode.Aed.Code, r.Date, r.Rate, RateSources.Import, reference });
        await BulkInsert.InsertAsync(session, CurrencyDbContext.SchemaName, "exchange_rates", columns, rows, cancellationToken);
    }

    /// <summary>
    /// A deterministic daily walk for each currency, <paramref name="days"/> days ending on
    /// <paramref name="last"/>: each day moves a little and drifts back towards the starting rate;
    /// six decimals; the dollar stays at its peg (3.6725). Decimal arithmetic on an integer
    /// generator (no floating point).
    /// </summary>
    internal static IEnumerable<(string Currency, DateOnly Date, decimal Rate)> GeneratedRates(IReadOnlyList<(string Code, decimal Rate)> currencies, int days, DateOnly last, string seed)
    {
        var state = 0x9E3779B97F4A7C15UL ^ (ulong)(uint)StableHash(seed);
        foreach (var (code, start) in currencies)
        {
            var value = start;
            for (var d = days - 1; d >= 0; d--)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                var step = (state % 1_000_001UL) / 1_000_000m - 0.5m;
                value = start + (value - start) * 0.98m + start * step * 0.004m;
                var rate = code == "USD" ? 3.6725m : decimal.Round(value, 6, MidpointRounding.AwayFromZero);
                if (rate <= 0)
                {
                    rate = start;
                }
                yield return (code, last.AddDays(-d), rate);
            }
        }
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }
}

/// <summary>
/// The exchange rates of the shared comparison dataset (gauntlet/compare/data/generate.mjs,
/// rates.csv: currency, date, rate in AED per one unit, six decimals), so the demo holds exactly the
/// rates the Odoo reference holds.
/// </summary>
internal static class SharedDatasetRates
{
    public static IReadOnlyList<(string Currency, DateOnly Date, decimal Rate)> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{CurrencySeeder.RatesCsvSetting} names {path}, which does not exist. Generate it with node gauntlet/compare/data/generate.mjs.", path);
        }
        var rows = new List<(string, DateOnly, decimal)>();
        using var reader = new StreamReader(path);
        var header = reader.ReadLine()?.Split(',').Select(h => h.Trim().Trim('"')).ToList()
            ?? throw new InvalidDataException($"{path} is empty.");
        int At(string name) => header.IndexOf(name) is var i and >= 0 ? i : throw new InvalidDataException($"{path} has no '{name}' column.");
        var (currencyAt, dateAt, rateAt) = (At("currency"), At("date"), At("rate"));
        string? line;
        var number = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }
            var values = line.Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            if (!CurrencyCode.IsKnown(values[currencyAt]) ||
                !DateOnly.TryParseExact(values[dateAt], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !decimal.TryParse(values[rateAt], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || !CurrencyRules.IsValidRate(rate))
            {
                throw new InvalidDataException($"{path}, line {number}: not a rate ({line}).");
            }
            rows.Add((values[currencyAt], date, rate));
        }
        return rows;
    }
}
