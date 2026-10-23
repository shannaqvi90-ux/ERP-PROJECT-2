using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Money;
using Erp.Kernel.Shell;
using Erp.Modules.Currency.Contracts;
using Erp.Modules.Currency.Currencies;
using Erp.Modules.Currency.Rates;
using Erp.Modules.Currency.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Currency;

/// <summary>
/// Multi-currency: the ISO 4217 currencies a workspace uses, their exchange rates by date (entered
/// by hand or imported from a file; fetching rates from an outside provider is a human gate), the
/// rate lookup with its rule for dates that have no rate, and the conversion of an amount to a
/// company's base currency (<see cref="IExchangeRates"/>, <see cref="ICurrencyConverter"/>) that
/// later modules use through the contracts. Amounts are the kernel <see cref="Money"/>.
/// </summary>
public sealed class CurrencyModule : ErpModule
{
    public override string Name => "currency";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. CurrencyPermissions.All]);
        module.DbContext<CurrencyDbContext>();
        module.Services.AddScoped<IExchangeRates, ExchangeRateService>();
        module.Services.AddScoped<ICurrencyConverter, ExchangeRateService>();
        module.Endpoints(group =>
        {
            CurrencyEndpoints.Map(group);
            RateEndpoints.Map(group);
            ConversionEndpoints.Map(group);
        });
        module.Menu(new MenuEntry("currency.rates", "currency.menu.rates", "/currency/rates", CurrencyPermissions.RatesRead, Order: 850, Group: "settings"));
        module.Menu(new MenuEntry("currency.currencies", "currency.menu.currencies", "/currency/currencies", CurrencyPermissions.CurrenciesRead, Order: 845, Group: "settings"));
        module.List(CurrenciesList.Create());
        module.List(RatesList.Create());
        // Reports print both lists exactly as their screens show them.
        module.ListRows(CurrenciesList.Key, async (services, request, http, cancellationToken) =>
            (await CurrencyEndpoints.PageAsync(services.GetRequiredService<CurrencyDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.ListRows(RatesList.Key, async (services, request, http, cancellationToken) =>
            (await RateEndpoints.PageAsync(services.GetRequiredService<CurrencyDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.Seeder<CurrencySeeder>();
    }
}

/// <summary>
/// One ISO 4217 currency of the workspace, with its names as the workspace shows them. Every
/// workspace holds every current currency of the list (seeded, and added when a newer list brings
/// one); it uses those it puts in use (<see cref="IsActive"/>, the dirham and each company's base
/// currency at least). Every company of the workspace shares it (<see cref="IWorkspaceWide"/>):
/// someone limited to some companies or branches reads it but never changes it. Its minor units
/// are ISO 4217's (<see cref="CurrencyCode.MinorUnits"/>), never the workspace's.
/// </summary>
public sealed class EnabledCurrency : TenantEntity, IWorkspaceWide
{
    /// <summary>ISO 4217 code (three capital letters, a current currency with minor units).</summary>
    public string Code { get; set; } = "";

    /// <summary>The name in English (ISO 4217's to start with); never empty.</summary>
    public string NameEn { get; set; } = "";

    /// <summary>The name in Arabic; empty when the workspace gave none (screens then show the
    /// browser's Arabic name, printed documents the English one).</summary>
    public string NameAr { get; set; } = "";

    /// <summary>In use: rates may be entered and amounts converted for new work. A currency out
    /// of use keeps its rates and lookups.</summary>
    public bool IsActive { get; set; }

    /// <summary>How many days before a date the latest rate may be dated when the date itself has
    /// none (0: only a rate of the date itself). See <see cref="RateLookupRule.LatestWithinLimit"/>.</summary>
    public int MaxRateAgeDays { get; set; } = CurrencyRules.DefaultMaxRateAgeDays;
}

/// <summary>
/// One exchange rate: <see cref="Rate"/> units of <see cref="QuoteCurrency"/> (normally a
/// company's base currency, AED) per one unit of <see cref="Currency"/>, for one date. One rate per
/// currency pair and date. Every company of the workspace shares it (<see cref="IWorkspaceWide"/>):
/// the Central Bank's rate of a date is the same for every company.
/// </summary>
public sealed class ExchangeRate : TenantEntity, IWorkspaceWide
{
    public string Currency { get; set; } = "";
    public string QuoteCurrency { get; set; } = "";
    public DateOnly RateDate { get; set; }

    /// <summary>Quote-currency units per one unit of <see cref="Currency"/>, at most eight decimals
    /// (<see cref="Money.RateScale"/>), positive.</summary>
    public decimal Rate { get; set; }

    /// <summary>How it came in (<see cref="RateSources"/>).</summary>
    public string Source { get; set; } = RateSources.Manual;

    /// <summary>Where the rate is from, as the person entering or importing it wrote it (for example
    /// "CBUAE 23 Oct 2026"), or the imported file's name.</summary>
    public string? Reference { get; set; }
}

/// <summary>The module's fixed rules.</summary>
public static class CurrencyRules
{
    /// <summary>Default for <see cref="EnabledCurrency.MaxRateAgeDays"/>: a week covers a weekend
    /// and the usual public holidays without using a rate that is weeks old.</summary>
    public const int DefaultMaxRateAgeDays = 7;

    /// <summary>Largest <see cref="EnabledCurrency.MaxRateAgeDays"/> a workspace may set.</summary>
    public const int MaxRateAgeDaysLimit = 366;

    public const int NameLength = 100;
    public const int ReferenceLength = 200;

    /// <summary>Most rows one import file may hold.</summary>
    public const int ImportRowLimit = 20_000;

    /// <summary>Largest import file, in characters.</summary>
    public const int ImportTextLimit = 2_000_000;

    /// <summary>Earliest date a rate may have.</summary>
    public static readonly DateOnly EarliestDate = new(1990, 1, 1);

    /// <summary>Latest date a rate may have.</summary>
    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    /// <summary>A rate as the columns hold it: positive, at most eight decimals, at most eleven
    /// integer digits (<c>numeric(19,8)</c>).</summary>
    public static bool IsValidRate(decimal rate) =>
        rate > 0 && decimal.Round(rate, Money.RateScale) == rate && rate < 100_000_000_000m;
}

public sealed class CurrencyDbContext(DbContextOptions<CurrencyDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "currency";

    protected override string Schema => SchemaName;

    public DbSet<EnabledCurrency> Currencies => Set<EnabledCurrency>();
    public DbSet<ExchangeRate> Rates => Set<ExchangeRate>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnabledCurrency>(e =>
        {
            e.ToTable("currencies", t =>
            {
                t.HasCheckConstraint("ck_currencies_code", "code ~ '^[A-Z]{3}$'");
                t.HasCheckConstraint("ck_currencies_names", "btrim(name_en) <> ''");
                t.HasCheckConstraint("ck_currencies_max_rate_age_days", $"max_rate_age_days BETWEEN 0 AND {CurrencyRules.MaxRateAgeDaysLimit}");
            });
            e.Property(x => x.Code).HasMaxLength(3);
            e.Property(x => x.NameEn).HasMaxLength(CurrencyRules.NameLength);
            e.Property(x => x.NameAr).HasMaxLength(CurrencyRules.NameLength);
            // Unique per workspace; rates reference (tenant_id, code).
            e.HasAlternateKey(x => new { x.TenantId, x.Code });
        });

        modelBuilder.Entity<ExchangeRate>(e =>
        {
            e.ToTable("exchange_rates", t =>
            {
                t.HasCheckConstraint("ck_exchange_rates_currency", "currency ~ '^[A-Z]{3}$' AND quote_currency ~ '^[A-Z]{3}$' AND currency <> quote_currency");
                t.HasCheckConstraint("ck_exchange_rates_rate", "rate > 0");
                t.HasCheckConstraint("ck_exchange_rates_source", "source IN ('manual', 'import')");
                t.HasCheckConstraint("ck_exchange_rates_date", "rate_date BETWEEN DATE '1990-01-01' AND DATE '2100-12-31'");
            });
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.QuoteCurrency).HasMaxLength(3);
            e.Property(x => x.Rate).HasPrecision(Money.RatePrecision, Money.RateScale);
            e.Property(x => x.Source).HasMaxLength(10);
            e.Property(x => x.Reference).HasMaxLength(CurrencyRules.ReferenceLength);
            // One rate per pair and date; also the lookup's index (latest on or before a date).
            e.HasIndex(x => new { x.TenantId, x.Currency, x.QuoteCurrency, x.RateDate }).IsUnique();
            e.HasOne<EnabledCurrency>().WithMany().HasForeignKey(x => new { x.TenantId, x.Currency })
                .HasPrincipalKey(c => new { c.TenantId, c.Code }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EnabledCurrency>().WithMany().HasForeignKey(x => new { x.TenantId, x.QuoteCurrency })
                .HasPrincipalKey(c => new { c.TenantId, c.Code }).OnDelete(DeleteBehavior.Restrict);
            // List framework: word search on a trigram index, keyset order on (tenant, column, id).
            e.HasIndex(x => new { x.Currency, x.Reference }, "ix_exchange_rates_search")
                .HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.RateDate, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Currency, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Rate, x.Id });
            e.HasIndex(x => new { x.TenantId, x.UpdatedAt, x.Id });
        });
    }
}

internal sealed class CurrencyDbContextDesignFactory : IDesignTimeDbContextFactory<CurrencyDbContext>
{
    public CurrencyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CurrencyDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", CurrencyDbContext.SchemaName);
        return new CurrencyDbContext(options.Options);
    }
}
