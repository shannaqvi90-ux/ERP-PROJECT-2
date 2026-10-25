using Erp.Kernel.Lists;
using Erp.Modules.Currency.Contracts;

namespace Erp.Modules.Currency.Currencies;

/// <summary>
/// The workspace's currencies: every current ISO 4217 currency (165 in the list of 2026-09-17), those
/// in use first, each with its latest rate in the working company's base currency. The endpoint
/// loads them whole with their latest rates (one query) and the list is searched, filtered and
/// sorted in memory.
/// </summary>
internal static class CurrenciesList
{
    public const string Key = "currency.currencies";

    public static ListBinding<CurrencyRow> Create() =>
        ListBinding<CurrencyRow>.For(new ListDefinition(
                Key, "currency.currencies.title", CurrencyPermissions.CurrenciesRead, "/api/currency/currencies",
                [
                    new ListColumn("code", "currency.currency.code", ListColumnType.Text, Sortable: true, Filterable: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("nameEn", "currency.currency.name", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "nameAr", Width: ListColumnWidth.Wide),
                    new ListColumn("nameAr", "currency.currency.nameAr", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("latestRate", "currency.currency.latestRate", ListColumnType.Money, Sortable: true, CurrencyField: "quoteCurrency"),
                    new ListColumn("latestRateDate", "currency.currency.latestRateDate", ListColumnType.Date, Sortable: true, Filterable: true),
                    new ListColumn("latestRateReference", "currency.currency.latestRateReference", ListColumnType.Text, Filterable: true, Hidden: true, Width: ListColumnWidth.Wide),
                    new ListColumn("quoteCurrency", "currency.currency.quoteCurrency", ListColumnType.Text, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("minorUnits", "currency.currency.minorUnits", ListColumnType.Number, Sortable: true, Filterable: true),
                    new ListColumn("maxRateAgeDays", "currency.currency.maxRateAgeDays", ListColumnType.Number, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("isActive", "currency.common.status", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "currency.common.active", FalseLabelKey: "currency.common.inactive"),
                ],
                // The latest rate's reference too: "CBUAE 23 Oct" finds the currencies whose latest rate came from that bulletin.
                SearchFields: ["code", "nameEn", "nameAr", "latestRateReference"],
                DefaultSort: "-isActive,code",
                Presets:
                [
                    new ListPreset("active", "currency.view.active", Filter: "isActive eq true"),
                    new ListPreset("inactive", "currency.view.inactive", Filter: "isActive eq false"),
                ]),
                r => r.Id)
            .Column("code", r => r.Code)
            .Column("nameEn", r => r.NameEn)
            .Column("nameAr", r => r.NameAr)
            .Column("latestRate", r => r.LatestRate)
            .Column("latestRateDate", r => r.LatestRateDate)
            .Column("latestRateReference", r => r.LatestRateReference)
            .Column("minorUnits", r => r.MinorUnits)
            .Column("maxRateAgeDays", r => r.MaxRateAgeDays)
            .Column("isActive", r => r.IsActive)
            .InMemory("A workspace holds the current ISO 4217 currencies (165): the endpoint loads them whole with each one's latest rate (one query) and the list is searched, filtered and sorted in memory.");
}
