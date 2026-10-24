using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Contacts.Tests;

/// <summary>The demo's main list: 100,000 contacts in the main workspace (a few users beside them:
/// the users list's own budgets are measured by the identity tests).</summary>
public sealed class ContactsVolumeFixture : IAsyncLifetime
{
    public const int Volume = 100_000;
    public const string NeedleName = "Shamma Waleed Al Romaithi";
    public const string NeedleNameAr = "شمة وليد الرميثي";
    public const string NeedleMobile = "+971 52 555 7351";

    public ErpTestEnvironment Env { get; private set; } = null!;

    public SeedTenant Main => Env.Plan.Tenants[0];

    public Guid NeedleId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.Demo(200, ErpTestEnvironment.Password),
            new Dictionary<string, string?> { ["Erp:Seed:ContactsVolume"] = Volume.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await using (var admin = await Env.OpenAdminAsync())
        {
            await using var analyze = new NpgsqlCommand("ANALYZE contacts.contacts", admin);
            await analyze.ExecuteNonQueryAsync();
        }
        using var client = await Env.SignInAsync(Env.Email(Main, "admin"));
        var created = await client.PostAsJsonAsync("/api/contacts/contacts", new
        {
            kind = "person", nameEn = NeedleName, nameAr = NeedleNameAr, mobile = NeedleMobile, email = $"shamma.romaithi@{Main.EmailDomain}",
            taxRegistrationNumber = "100777666555443", country = "AE", defaultCurrency = "AED", tags = Array.Empty<string>(),
        });
        created.EnsureSuccessStatusCode();
        NeedleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

[CollectionDefinition(nameof(ContactsTimingCollection), DisableParallelization = true)]
public sealed class ContactsTimingCollection;

/// <summary>
/// The owner's bar: find one record among 100,000 in well under a second of server time. Searches
/// by name in either script, initials, e-mail, number and tax number, filters, sorted pages, groups
/// and deep keyset pages over 100,006 contacts answer within the budget. Run alone, after every
/// other test (trait <see cref="TimingBudget.Trait"/>), as the users list's budgets are.
/// </summary>
[Collection(nameof(ContactsTimingCollection))]
[Trait(TimingBudget.Trait, TimingBudget.Value)]
public sealed class ContactsListVolumeTests(ContactsVolumeFixture fixture) : IClassFixture<ContactsVolumeFixture>
{
    /// <summary>Server time allowed for the median of seven runs of one list request at demo volume.</summary>
    private const int MedianBudgetMilliseconds = 400;

    /// <summary>No single run may take a second ("well under a second").</summary>
    private const int CeilingMilliseconds = 1000;

    private ErpTestEnvironment Env => fixture.Env;

    private sealed record Timing(string Uri, double Median, double Slowest)
    {
        public bool WithinBudget => Median < MedianBudgetMilliseconds && Slowest < CeilingMilliseconds;

        public override string ToString() => $"{Uri}: median {Median:F0} ms, slowest {Slowest:F0} ms";
    }

    private static async Task<(JsonElement Page, Timing Timing)> TimedAsync(HttpClient client, string uri)
    {
        (await client.GetAsync(uri)).EnsureSuccessStatusCode();
        var runs = new List<double>();
        JsonElement page = default;
        for (var i = 0; i < 7; i++)
        {
            var clock = Stopwatch.StartNew();
            using var response = await client.GetAsync(uri);
            var text = await response.Content.ReadAsStringAsync();
            clock.Stop();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{uri}: {(int)response.StatusCode} {text}");
            page = JsonDocument.Parse(text).RootElement.Clone();
            runs.Add(clock.Elapsed.TotalMilliseconds);
        }
        runs.Sort();
        return (page, new Timing(uri, runs[runs.Count / 2], runs[^1]));
    }

    [Fact]
    public async Task One_contact_is_found_among_100000_by_name_initials_e_mail_number_or_tax_number_in_well_under_a_second()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var (all, _) = await TimedAsync(admin, "/api/contacts/contacts?take=1");
        Assert.True(all.GetProperty("total").GetInt32() > ContactsVolumeFixture.Volume, $"{all.GetProperty("total").GetInt32()} contacts");

        var timings = new List<Timing>();
        foreach (var search in new[] { "shamma romaithi", "Romaithi", "SHAMMA WALEED", "swar", "شمة الرميثي", "الرميثى شمه", "shamma.romaithi@", "555 7351", "100777666555443" })
        {
            var (page, timing) = await TimedAsync(admin, $"/api/contacts/contacts?search={Uri.EscapeDataString(search)}");
            timings.Add(timing);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Count > 0 && items[0].GetProperty("id").GetGuid() == fixture.NeedleId, $"'{search}': the contact is not the best match");
            Assert.True(timing.WithinBudget, string.Join("\n", timings));
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }

    [Fact]
    public async Task Sorted_filtered_grouped_broad_and_deep_keyset_pages_stay_within_budget()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var timings = new List<Timing>();
        foreach (var uri in new[]
                 {
                     "/api/contacts/contacts?take=100",
                     "/api/contacts/contacts?take=100&sort=-createdAt",
                     "/api/contacts/contacts?take=100&sort=city",
                     "/api/contacts/contacts?take=100&sort=email",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString("kind eq 'organisation' and isActive eq true and emirate eq 'dubai'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString("tags contains 'Supplier'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString("creditLimit gt 250000")}",
                     "/api/contacts/contacts?take=1&groupBy=emirate",
                     $"/api/contacts/contacts?take=100&search={Uri.EscapeDataString("al")}",
                     $"/api/contacts/contacts?take=100&search={Uri.EscapeDataString("محمد")}",
                 })
        {
            var (_, timing) = await TimedAsync(admin, uri);
            timings.Add(timing);
            Assert.True(timing.WithinBudget, string.Join("\n", timings));
        }

        string? next = null;
        for (var i = 0; i < 30; i++)
        {
            var page = await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts?take=200&sort=nameEn" + (next is null ? "" : $"&after={Uri.EscapeDataString(next)}"));
            next = page.GetProperty("next").GetString();
        }
        var (deep, deepTiming) = await TimedAsync(admin, $"/api/contacts/contacts?take=200&sort=nameEn&after={Uri.EscapeDataString(next!)}");
        timings.Add(deepTiming with { Uri = "keyset page 31 by name" });
        Assert.Equal(200, deep.GetProperty("items").GetArrayLength());
        Assert.True(deepTiming.WithinBudget, string.Join("\n", timings));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }

    [Fact]
    public async Task A_duplicate_check_among_100000_answers_within_the_budget()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var (page, timing) = await TimedAsync(admin,
            $"/api/contacts/contacts/duplicates?sameName={Uri.EscapeDataString("shamma waleed al-romaithi")}&sameMobile={Uri.EscapeDataString("052 555 7351")}&sameEmail=x%40y.example");
        Assert.True(timing.WithinBudget, timing.ToString());
        var hit = page.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == fixture.NeedleId);
        Assert.Equal(["name", "phone"], hit.GetProperty("matches").EnumerateArray().Select(m => m.GetString()));
    }
}
