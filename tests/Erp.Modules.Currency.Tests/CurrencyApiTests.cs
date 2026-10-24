using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Currency.Tests;

public sealed class CurrencyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The currency module through its API, on the gate seed (each workspace holds every ISO 4217
/// currency, AED, USD, EUR and GBP in use, thirty rates over the last ten days and three on 31
/// December 2025): the rate lookup's rules, conversion and its one rounding, entering, correcting,
/// deleting and importing rates, putting currencies in use and changing them, the workspace-wide
/// rule for writes, validation in both languages and the audit trail.
/// </summary>
public sealed class CurrencyApiTests(CurrencyFixture fixture) : IClassFixture<CurrencyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai")).DateTime);

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpClient> AdminAsync(string local = "admin") => await Env.SignInAsync(Env.Email(Env.TenantA, local));

    /// <summary>Puts a currency of the test's own in use (so tests do not share rates).</summary>
    private static async Task<Guid> EnableAsync(HttpClient admin, string code)
    {
        var row = (await admin.GetFromJsonAsync<JsonElement>($"/api/currency/currencies?search={code}")).GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == code);
        var response = await admin.PutAsJsonAsync($"/api/currency/currencies/{row.GetProperty("id").GetGuid()}", new
        {
            isActive = true, maxRateAgeDays = 7, version = row.GetProperty("version").GetUInt32(),
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return row.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> RateAsync(HttpClient admin, string currency, DateOnly date, string rate, string? reference = null)
    {
        var response = await admin.PostAsJsonAsync("/api/currency/rates", new { currency, rateDate = Day(date), rate, reference });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    [Fact]
    public async Task The_seeded_workspace_holds_every_ISO_4217_currency_those_in_use_first_and_its_rates()
    {
        using var viewer = await AdminAsync("viewer");
        var currencies = await viewer.GetFromJsonAsync<JsonElement>("/api/currency/currencies?take=200");
        var rows = currencies.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(165, currencies.GetProperty("total").GetInt32());
        Assert.Equal(Erp.Kernel.Money.CurrencyCode.All.Order(StringComparer.Ordinal), rows.Select(c => c.GetProperty("code").GetString()!).Order(StringComparer.Ordinal));
        // Those in use first, then by code.
        var inUse = rows.TakeWhile(c => c.GetProperty("isActive").GetBoolean()).Select(c => c.GetProperty("code").GetString()!).ToList();
        Assert.All(new[] { "AED", "EUR", "GBP", "USD" }, code => Assert.Contains(code, inUse));
        Assert.Equal(inUse.Order(StringComparer.Ordinal), inUse);
        Assert.All(rows.Skip(inUse.Count), c => Assert.False(c.GetProperty("isActive").GetBoolean()));
        var usd = rows.Single(c => c.GetProperty("code").GetString() == "USD");
        Assert.Equal("3.6725", usd.GetProperty("latestRate").GetString());
        Assert.Equal(Day(Today), usd.GetProperty("latestRateDate").GetString());
        Assert.Equal("AED", usd.GetProperty("quoteCurrency").GetString());
        Assert.Equal(2, usd.GetProperty("minorUnits").GetInt32());
        var aed = rows.Single(c => c.GetProperty("code").GetString() == "AED");
        Assert.Equal(JsonValueKind.Null, aed.GetProperty("latestRate").ValueKind);
        var kwd = rows.Single(c => c.GetProperty("code").GetString() == "KWD");
        Assert.Equal((3, "Kuwaiti Dinar", "دينار كويتي", false), (kwd.GetProperty("minorUnits").GetInt32(), kwd.GetProperty("nameEn").GetString(), kwd.GetProperty("nameAr").GetString(), kwd.GetProperty("isActive").GetBoolean()));
        // Every currency has its names in both languages.
        Assert.All(rows, c => Assert.Matches(@"\p{IsArabic}", c.GetProperty("nameAr").GetString()!));
        Assert.Equal(("CFA Franc BEAC", "فرنك وسط أفريقيا"), (rows.Single(c => c.GetProperty("code").GetString() == "XAF").GetProperty("nameEn").GetString(),
            rows.Single(c => c.GetProperty("code").GetString() == "XAF").GetProperty("nameAr").GetString()));
        var form = await viewer.GetFromJsonAsync<JsonElement>($"/api/currency/currencies/{kwd.GetProperty("id").GetGuid()}");
        Assert.Equal((414, "2026-09-17", false), (form.GetProperty("number").GetInt32(), form.GetProperty("isoPublished").GetString(), form.GetProperty("isBaseCurrency").GetBoolean()));

        var rates = await viewer.GetFromJsonAsync<JsonElement>("/api/currency/rates?filter=" + Uri.EscapeDataString("currencyCode eq 'EUR'"));
        Assert.True(rates.GetProperty("total").GetInt32() >= 11);
        var first = rates.GetProperty("items")[0];
        Assert.Equal(Day(Today), first.GetProperty("rateDate").GetString());
        Assert.Equal("import", first.GetProperty("source").GetString());
        Assert.True(first.TryGetProperty("exchangeRate", out _));
    }

    [Fact]
    public async Task The_lookup_uses_the_dates_rate_then_the_latest_earlier_one_within_the_limit_and_never_a_later_one()
    {
        using var admin = await AdminAsync();
        await EnableAsync(admin, "CHF");
        var monday = new DateOnly(2026, 6, 1);
        await RateAsync(admin, "CHF", monday, "4.5");
        await RateAsync(admin, "CHF", monday.AddDays(10), "4.6");

        async Task<HttpResponseMessage> Lookup(DateOnly date, bool exact = false) =>
            await admin.GetAsync($"/api/currency/rates/lookup?currency=chf&date={Day(date)}{(exact ? "&exact=true" : "")}");

        var same = await Json(await Lookup(monday));
        Assert.Equal(("4.5", Day(monday), true, "manual"), (same.GetProperty("rate").GetString(), same.GetProperty("rateDate").GetString(), same.GetProperty("exact").GetBoolean(), same.GetProperty("source").GetString()));
        // A weekend or holiday: the latest earlier rate, up to seven days older.
        var later = await Json(await Lookup(monday.AddDays(7)));
        Assert.Equal(("4.5", Day(monday), false, Day(monday.AddDays(7))),
            (later.GetProperty("rate").GetString(), later.GetProperty("rateDate").GetString(), later.GetProperty("exact").GetBoolean(), later.GetProperty("requestedDate").GetString()));
        // Eight days on: too old, and the answer says when the latest was.
        var stale = await Lookup(monday.AddDays(8));
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var problem = await Json(stale);
        Assert.Equal("currency.rateTooOld", problem.GetProperty("code").GetString());
        Assert.Contains(Day(monday), problem.GetProperty("title").GetString());
        // The day before the first rate: a later rate is never used.
        var before = await Lookup(monday.AddDays(-1));
        Assert.Equal("currency.rateMissing", (await Json(before)).GetProperty("code").GetString());
        // Exact only: a date without its own rate has none.
        Assert.Equal("currency.rateTooOld", (await Json(await Lookup(monday.AddDays(3), exact: true))).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Lookup(monday.AddDays(10), exact: true)).StatusCode);
        // The currency's own limit decides: 0 means the date's own rate only, 30 reaches back further.
        var id = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=CHF")).GetProperty("items")[0];
        var update = await admin.PutAsJsonAsync($"/api/currency/currencies/{id.GetProperty("id").GetGuid()}",
            new { isActive = true, maxRateAgeDays = 30, version = id.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("4.6", (await Json(await Lookup(monday.AddDays(35)))).GetProperty("rate").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Lookup(monday.AddDays(41))).StatusCode);
        // A currency out of use still answers from the rates it has (none here), and the same currency on both sides is 1.
        var unused = await admin.GetAsync($"/api/currency/rates/lookup?currency=ZAR&date={Day(monday)}");
        Assert.Equal("currency.rateMissing", (await Json(unused)).GetProperty("code").GetString());
        var aed = await Json(await admin.GetAsync($"/api/currency/rates/lookup?currency=AED&date={Day(monday)}"));
        Assert.Equal(("1", "same"), (aed.GetProperty("rate").GetString(), aed.GetProperty("source").GetString()));
        // Bad input is a validation problem, not a lookup.
        var bad = await admin.GetAsync("/api/currency/rates/lookup?currency=ABC");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("currencyCode", (await Json(bad)).GetProperty("errors").GetProperty("currency")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_amount_is_converted_at_the_rate_found_and_its_base_amount_is_rounded_once_half_away_from_zero()
    {
        using var admin = await AdminAsync();
        await EnableAsync(admin, "NOK");
        var date = new DateOnly(2026, 5, 4);
        await RateAsync(admin, "NOK", date, "4.2875");

        var conversion = await Json(await admin.GetAsync($"/api/currency/convert?amount=1250&currency=NOK&date={Day(date)}"));
        var money = conversion.GetProperty("money");
        Assert.Equal(("1250", "NOK", "4.2875", "5359.38", "AED"),
            (money.GetProperty("amount").GetString(), money.GetProperty("currency").GetString(), money.GetProperty("exchangeRate").GetString(),
             money.GetProperty("baseAmount").GetString(), money.GetProperty("baseCurrency").GetString()));
        // 1250 x 4.2875 = 5359.375: the fils midpoint rounds away from zero, and a credit note mirrors it.
        Assert.Equal("5359.375", conversion.GetProperty("exactBaseAmount").GetString());
        Assert.Equal(Day(date), conversion.GetProperty("rateDate").GetString());
        Assert.True(conversion.GetProperty("exact").GetBoolean());
        var credit = await Json(await admin.GetAsync($"/api/currency/convert?amount=-1250&currency=NOK&date={Day(date)}"));
        Assert.Equal("-5359.38", credit.GetProperty("money").GetProperty("baseAmount").GetString());
        // A line amount keeps its precision: 0.333 x 4.2875 = 1.4277375, stated 1.43.
        var line = await Json(await admin.GetAsync($"/api/currency/convert?amount=0.333&currency=NOK&date={Day(date)}"));
        Assert.Equal(("0.333", "1.43"), (line.GetProperty("money").GetProperty("amount").GetString(), line.GetProperty("money").GetProperty("baseAmount").GetString()));
        // The base currency itself converts at 1.
        var aed = await Json(await admin.GetAsync("/api/currency/convert?amount=10.5&currency=AED"));
        Assert.Equal(("10.5", "1"), (aed.GetProperty("money").GetProperty("baseAmount").GetString(), aed.GetProperty("money").GetProperty("exchangeRate").GetString()));
        // No rate, bad input and another tenant's company are refused.
        Assert.Equal("currency.rateMissing", (await Json(await admin.GetAsync("/api/currency/convert?amount=1&currency=NOK&date=2026-01-01"))).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/currency/convert?amount=1,000&currency=NOK")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/currency/convert?amount=1&currency=XYZ")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/currency/convert?amount=1&currency=NOK&companyId={Guid.CreateVersion7()}")).StatusCode);
    }

    [Fact]
    public async Task Rates_are_entered_corrected_and_deleted_with_validation_in_the_callers_language()
    {
        using var admin = await AdminAsync();
        await EnableAsync(admin, "SEK");
        var date = new DateOnly(2026, 4, 7);
        var created = await RateAsync(admin, "sek", date, "0.34875", "CBUAE 7 Apr 2026");
        Assert.Equal(("SEK", "AED", "0.34875", "manual", "CBUAE 7 Apr 2026"),
            (created.GetProperty("currency").GetString(), created.GetProperty("quoteCurrency").GetString(), created.GetProperty("rate").GetString(),
             created.GetProperty("source").GetString(), created.GetProperty("reference").GetString()));
        Assert.Equal("2.86738351", created.GetProperty("inverseRate").GetString());
        var id = created.GetProperty("id").GetGuid();

        // A date may receive another rate: the one saved last applies, the earlier stays as entered.
        var again = await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "SEK", rateDate = Day(date), rate = "0.35" });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        var second = (await Json(again)).GetProperty("id").GetGuid();
        Assert.NotEqual(id, second);
        var applies = await Json(await admin.GetAsync($"/api/currency/rates/lookup?currency=SEK&date={Day(date)}"));
        Assert.Equal(("0.35", second), (applies.GetProperty("rate").GetString(), applies.GetProperty("rateId").GetGuid()));
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>("/api/currency/rates?filter=" + Uri.EscapeDataString($"currencyCode eq 'SEK' and rateDate eq '{Day(date)}'"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/currency/rates/{second}")).StatusCode);
        Assert.Equal("0.34875", (await Json(await admin.GetAsync($"/api/currency/rates/lookup?currency=SEK&date={Day(date)}"))).GetProperty("rate").GetString());

        // Field rules, in Arabic for someone who works in Arabic.
        using var arabic = await AdminAsync("admin.ar");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/currency/rates")
        {
            Content = JsonContent.Create(new { currency = "SEK", quoteCurrency = "SEK", rateDate = "1980-01-01", rate = "0.123456789", reference = new string('x', 201) }),
        };
        request.Headers.AcceptLanguage.ParseAdd("ar");
        var invalid = await arabic.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Json(invalid)).GetProperty("errors");
        Assert.Equal("currencySameAsQuote", errors.GetProperty("quoteCurrency")[0].GetProperty("code").GetString());
        Assert.Equal("currencyRateDate", errors.GetProperty("rateDate")[0].GetProperty("code").GetString());
        Assert.Equal("currencyRate", errors.GetProperty("rate")[0].GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", errors.GetProperty("rate")[0].GetProperty("message").GetString()!);
        Assert.True(errors.TryGetProperty("reference", out _));
        var notInUse = await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "DKK", rate = "0.5" });
        Assert.Equal("currencyInactive", (await Json(notInUse)).GetProperty("errors").GetProperty("currency")[0].GetProperty("code").GetString());
        var zero = await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "SEK", rate = "0" });
        Assert.Equal("currencyRate", (await Json(zero)).GetProperty("errors").GetProperty("rate")[0].GetProperty("code").GetString());

        // A correction keeps the currencies, may move the date, and makes the rate a manual one.
        var moved = await admin.PutAsJsonAsync($"/api/currency/rates/{id}", new
        {
            currency = "SEK", quoteCurrency = "AED", rateDate = Day(date.AddDays(1)), rate = "0.349", reference = (string?)null, version = created.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var corrected = await Json(moved);
        Assert.Equal((Day(date.AddDays(1)), "0.349"), (corrected.GetProperty("rateDate").GetString(), corrected.GetProperty("rate").GetString()));
        var stale = await admin.PutAsJsonAsync($"/api/currency/rates/{id}", new
        {
            currency = "SEK", quoteCurrency = "AED", rateDate = Day(date), rate = "0.3", version = created.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var otherCurrency = await admin.PutAsJsonAsync($"/api/currency/rates/{id}", new
        {
            currency = "USD", quoteCurrency = "AED", rateDate = Day(date), rate = "0.3", version = corrected.GetProperty("version").GetUInt32(),
        });
        Assert.Equal("currencyRateCurrencyFixed", (await Json(otherCurrency)).GetProperty("errors").GetProperty("currency")[0].GetProperty("code").GetString());

        // Deleted: gone from the list and the lookup.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/currency/rates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/currency/rates/{id}")).StatusCode);
        // Deleting again answers as deleted (DELETE is idempotent), and writes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/currency/rates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/currency/rates/lookup?currency=SEK&date={Day(date.AddDays(1))}")).StatusCode);

        // Every change is in the audit trail with who made it.
        await using var db = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(
            "SELECT array_agg(action ORDER BY id) FROM audit.entries WHERE table_schema = 'currency' AND table_name = 'exchange_rates' AND record_id = @id AND actor_id IS NOT NULL", db);
        command.Parameters.AddWithValue("id", id);
        var operations = (string[])(await command.ExecuteScalarAsync())!;
        Assert.Equal(["insert", "update", "delete"], operations);
    }

    [Fact]
    public async Task A_file_is_imported_whole_or_not_at_all_and_held_rates_are_kept_unless_replacing()
    {
        using var admin = await AdminAsync();
        await EnableAsync(admin, "PLN");
        var held = await RateAsync(admin, "PLN", new DateOnly(2026, 3, 2), "0.9");
        var file = "currency,date,rate,reference\nPLN,2026-03-02,0.91,\nPLN,03/03/2026,0.92,\nPLN,2026-03-04,0.93,Bulletin 12\n";

        var kept = await Json(await admin.PostAsJsonAsync("/api/currency/rates/import", new { fileName = "pln-march.csv", content = file }));
        Assert.Equal((3, 2, 0, 1, 0), (kept.GetProperty("lines").GetInt32(), kept.GetProperty("added").GetInt32(), kept.GetProperty("replaced").GetInt32(),
            kept.GetProperty("kept").GetInt32(), kept.GetProperty("unchanged").GetInt32()));
        var rows = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/rates?filter=" + Uri.EscapeDataString("currencyCode eq 'PLN'") + "&sort=rateDate"))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["0.9", "0.92", "0.93"], rows.Select(r => r.GetProperty("exchangeRate").GetString()));
        Assert.Equal(["manual", "import", "import"], rows.Select(r => r.GetProperty("source").GetString()));
        Assert.Equal([null, "pln-march.csv", "Bulletin 12"], rows.Select(r => r.GetProperty("reference").GetString()));

        var replaced = await Json(await admin.PostAsJsonAsync("/api/currency/rates/import", new { fileName = "pln-march.csv", content = file, replaceExisting = true }));
        Assert.Equal((0, 1, 0, 2), (replaced.GetProperty("added").GetInt32(), replaced.GetProperty("replaced").GetInt32(), replaced.GetProperty("kept").GetInt32(), replaced.GetProperty("unchanged").GetInt32()));
        var now = await admin.GetFromJsonAsync<JsonElement>($"/api/currency/rates/{held.GetProperty("id").GetGuid()}");
        Assert.Equal(("0.91", "import"), (now.GetProperty("rate").GetString(), now.GetProperty("source").GetString()));

        // One bad line: nothing is saved, and each problem names its line.
        var bad = await admin.PostAsJsonAsync("/api/currency/rates/import", new { content = "currency,date,rate\nPLN,2026-03-09,0.95\nPLN,2026-03-10,abc\nXXX,2026-03-10,1\n" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var messages = (await Json(bad)).GetProperty("errors").GetProperty("content").EnumerateArray().Select(e => e.GetProperty("message").GetString()!).ToList();
        Assert.Equal(2, messages.Count);
        Assert.StartsWith("Line 3:", messages[0]);
        Assert.StartsWith("Line 4:", messages[1]);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/currency/rates/lookup?currency=PLN&date=2026-03-09&exact=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/currency/rates/import", new { content = "" })).StatusCode);
    }

    [Fact]
    public async Task Currencies_are_put_in_use_and_taken_out_of_use_but_a_base_currency_stays()
    {
        using var admin = await AdminAsync();
        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=JPY")).GetProperty("items")[0];
        var id = row.GetProperty("id").GetGuid();
        Assert.False(row.GetProperty("isActive").GetBoolean());
        Assert.Equal(("Yen", "ين ياباني", 0), (row.GetProperty("nameEn").GetString(), row.GetProperty("nameAr").GetString(), row.GetProperty("minorUnits").GetInt32()));
        // Rates only for a currency in use.
        var refused = await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "JPY", rate = "0.024" });
        Assert.Equal("currencyInactive", (await Json(refused)).GetProperty("errors").GetProperty("currency")[0].GetProperty("code").GetString());
        var on = await admin.PutAsJsonAsync($"/api/currency/currencies/{id}", new { isActive = true, maxRateAgeDays = 3, version = row.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var jpy = await Json(on);
        Assert.Equal(("Yen", "ين ياباني", true, 3, 392), (jpy.GetProperty("nameEn").GetString(), jpy.GetProperty("nameAr").GetString(), jpy.GetProperty("isActive").GetBoolean(),
            jpy.GetProperty("maxRateAgeDays").GetInt32(), jpy.GetProperty("number").GetInt32()));
        await RateAsync(admin, "JPY", new DateOnly(2026, 1, 5), "0.0235");
        // Validation: both fields, a rate age from 0 to 366, the version read.
        var invalid = await admin.PutAsJsonAsync($"/api/currency/currencies/{id}", new { maxRateAgeDays = 400, version = jpy.GetProperty("version").GetUInt32() });
        var errors = (await Json(invalid)).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("isActive")[0].GetProperty("code").GetString());
        Assert.Equal("currencyMaxRateAge", errors.GetProperty("maxRateAgeDays")[0].GetProperty("code").GetString());
        var stale = await admin.PutAsJsonAsync($"/api/currency/currencies/{id}", new { isActive = true, maxRateAgeDays = 7, version = row.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Out of use: no new rates, the old ones stay and are still looked up.
        var off = await admin.PutAsJsonAsync($"/api/currency/currencies/{id}", new { isActive = false, maxRateAgeDays = 3, version = jpy.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "JPY", rate = "0.024" })).StatusCode);
        Assert.Equal("0.0235", (await Json(await admin.GetAsync("/api/currency/rates/lookup?currency=JPY&date=2026-01-05"))).GetProperty("rate").GetString());

        // The dirham is a company's base currency: it stays in use.
        var aed = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=AED")).GetProperty("items")[0];
        var form = await admin.GetFromJsonAsync<JsonElement>($"/api/currency/currencies/{aed.GetProperty("id").GetGuid()}");
        Assert.True(form.GetProperty("isBaseCurrency").GetBoolean());
        var dirham = await admin.PutAsJsonAsync($"/api/currency/currencies/{aed.GetProperty("id").GetGuid()}",
            new { isActive = false, maxRateAgeDays = 7, version = aed.GetProperty("version").GetUInt32() });
        Assert.Equal("currencyBaseStaysActive", (await Json(dirham)).GetProperty("errors").GetProperty("isActive")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Only_someone_who_works_in_every_company_with_the_permission_changes_the_shared_currencies_and_rates()
    {
        using var admin = await AdminAsync();
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.True(companies.Count >= 2);
        var permissions = new[] { "currency.currencies.read", "currency.currencies.update", "currency.rates.read", "currency.rates.create", "currency.rates.import", "currency.rates.delete" };
        var role = await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Treasury", nameAr = "الخزينة", permissions }));
        var roleId = role.GetProperty("id").GetGuid();

        // Holds the role everywhere, but works in one company only.
        var oneCompany = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"treasury.one@{Env.TenantA.EmailDomain}", displayName = "Treasury One", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { roleId }, companyRoles = Array.Empty<object>(),
        }));
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{oneCompany.GetProperty("id").GetGuid()}")).GetProperty("version").GetUInt32();
        var access = await admin.PutAsJsonAsync($"/api/tenancy/access/{oneCompany.GetProperty("id").GetGuid()}",
            new { companies = new[] { new { companyId = companies[0], allBranches = true, branchIds = Array.Empty<Guid>() } }, version });
        Assert.True(access.IsSuccessStatusCode, await access.Content.ReadAsStringAsync());
        // Works in every company, but holds the role in one of them only.
        var inOneCompany = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"treasury.role@{Env.TenantA.EmailDomain}", displayName = "Treasury Role", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = Array.Empty<Guid>(), companyRoles = new[] { new { roleId, companyId = companies[0] } },
        });
        Assert.Equal(HttpStatusCode.Created, inOneCompany.StatusCode);
        var roleUser = (await Json(inOneCompany)).GetProperty("id").GetGuid();
        var accessVersion = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{roleUser}")).GetProperty("version").GetUInt32();
        var everywhere = await admin.PutAsJsonAsync($"/api/tenancy/access/{roleUser}",
            new { companies = companies.Select(c => new { companyId = c, allBranches = true, branchIds = Array.Empty<Guid>() }), version = accessVersion });
        Assert.True(everywhere.IsSuccessStatusCode, await everywhere.Content.ReadAsStringAsync());
        // Working in the company where the role is held, so it counts for the request.
        var start = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{roleUser}/default-company");
        var started = await admin.PutAsJsonAsync($"/api/identity/users/{roleUser}/default-company", new { companyId = companies[0], version = start.GetProperty("version").GetUInt32() });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());

        var rateId = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/rates?take=1")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var cad = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=CAD")).GetProperty("items")[0];
        foreach (var local in new[] { "treasury.one", "treasury.role" })
        {
            using var user = await Env.SignInAsync(Env.Email(Env.TenantA, local));
            // Reading is fine.
            var read = await user.GetAsync("/api/currency/rates?take=1");
            Assert.True(read.StatusCode == HttpStatusCode.OK, $"{local}: {(int)read.StatusCode} {await read.Content.ReadAsStringAsync()}");
            foreach (var response in new[]
                     {
                         await user.PutAsJsonAsync($"/api/currency/currencies/{cad.GetProperty("id").GetGuid()}",
                             new { isActive = true, maxRateAgeDays = 7, version = cad.GetProperty("version").GetUInt32() }),
                         await user.PostAsJsonAsync("/api/currency/rates", new { currency = "EUR", rateDate = "2025-01-05", rate = "3.9" }),
                         await user.PostAsJsonAsync("/api/currency/rates/import", new { content = "currency,date,rate\nEUR,2025-01-06,3.9\n" }),
                         await user.DeleteAsync($"/api/currency/rates/{rateId}"),
                     })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("currency.workspaceNeedsEveryCompany", (await Json(response)).GetProperty("code").GetString());
            }
        }

        // A reader may not write at all.
        using var viewer = await AdminAsync("viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/currency/rates", new { currency = "EUR", rate = "3.9" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/currency/convert?amount=1&currency=USD")).StatusCode);
        using var nobody = await AdminAsync("noaccess");
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/currency/rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/currency/convert?amount=1&currency=USD")).StatusCode);
    }

    [Fact]
    public async Task Each_workspace_sees_and_converts_with_its_own_rates_only()
    {
        using var a = await AdminAsync();
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        await EnableAsync(b, "HUF");
        var date = new DateOnly(2026, 2, 3);
        var bRate = await RateAsync(b, "HUF", date, "0.0101", "B only");
        // A has no HUF rate at all, and B's rate is not found by id.
        Assert.Equal("currency.rateMissing", (await Json(await a.GetAsync($"/api/currency/rates/lookup?currency=HUF&date={Day(date)}"))).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/currency/rates/{bRate.GetProperty("id").GetGuid()}")).StatusCode);
        // A's delete of B's rate answers as for any id A cannot see, and B's rate stays.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/currency/rates/{bRate.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/currency/rates/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync($"/api/currency/rates/{bRate.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/currency/rates?search=HUF")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await b.GetFromJsonAsync<JsonElement>("/api/currency/rates?search=HUF")).GetProperty("total").GetInt32());
        // The gate's tenant B was seeded before the newest ISO code: it does not hold it, and says so.
        Assert.Equal(164, (await b.GetFromJsonAsync<JsonElement>("/api/currency/currencies?take=1")).GetProperty("total").GetInt32());
        Assert.Equal("currency.currencyNotEnabled", (await Json(await b.GetAsync("/api/currency/rates/lookup?currency=XAD"))).GetProperty("code").GetString());
        Assert.Equal("currency.currencyNotEnabled", (await Json(await b.GetAsync("/api/currency/convert?amount=1&currency=XAD"))).GetProperty("code").GetString());
    }
}
