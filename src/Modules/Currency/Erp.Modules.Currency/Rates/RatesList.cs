using Erp.Kernel.Lists;
using Erp.Modules.Currency.Contracts;

namespace Erp.Modules.Currency.Rates;

/// <summary>
/// The exchange rates list over <c>currency.exchange_rates</c> (row-level security limits it to the
/// workspace's rates): newest first; search on the currency code and the reference (trigram index);
/// the columns are keyed <c>currencyCode</c> and <c>exchangeRate</c> (a list's sort and grouping are
/// stored by key in saved views, and a key that is an everyday word, "currency", would read as data
/// in every printed document that wraps a heading such as "Base currency");
/// filter by currency, date, source or rate; group by currency or source. Every sortable column has
/// a (tenant_id, column, id) index. The demo holds 100,000 rates.
/// </summary>
internal static class RatesList
{
    public const string Key = "currency.rates";

    public static readonly IReadOnlyList<ListChoice> Sources =
    [
        new(RateSources.Manual, "currency.source.manual"),
        new(RateSources.Import, "currency.source.import"),
    ];

    public static ListBinding<ExchangeRate> Create() =>
        ListBinding<ExchangeRate>.For(new ListDefinition(
                Key, "currency.rates.title", CurrencyPermissions.RatesRead, "/api/currency/rates",
                [
                    new ListColumn("rateDate", "currency.rate.date", ListColumnType.Date, Sortable: true, Filterable: true),
                    new ListColumn("currencyCode", "currency.rate.currency", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("exchangeRate", "currency.rate.rate", ListColumnType.Money, Sortable: true, Filterable: true, CurrencyField: "quoteCurrency"),
                    new ListColumn("quoteCurrency", "currency.rate.quoteCurrency", ListColumnType.Text, Filterable: true, Groupable: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("inverseRate", "currency.rate.inverseRate", ListColumnType.Money, CurrencyField: "currencyCode"),
                    new ListColumn("source", "currency.rate.source", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: Sources),
                    new ListColumn("reference", "currency.rate.reference", ListColumnType.Text, Filterable: true, Width: ListColumnWidth.Wide),
                    new ListColumn("updatedAt", "currency.rate.updatedAt", ListColumnType.DateTime, Sortable: true, Filterable: true, Hidden: true),
                ],
                SearchFields: ["currencyCode", "reference"],
                DefaultSort: "-rateDate",
                Presets:
                [
                    new ListPreset("byCurrency", "currency.view.byCurrency", GroupBy: "currencyCode"),
                    new ListPreset("manual", "currency.view.manual", Filter: "source eq 'manual'"),
                    new ListPreset("imported", "currency.view.imported", Filter: "source eq 'import'"),
                ]),
                r => r.Id)
            .Column("rateDate", r => r.RateDate)
            .Column("currencyCode", r => r.Currency)
            .Column("exchangeRate", r => r.Rate)
            .Column("quoteCurrency", r => r.QuoteCurrency)
            .Column("source", r => r.Source)
            .Column("reference", r => r.Reference)
            .Column("updatedAt", r => r.UpdatedAt);
}
