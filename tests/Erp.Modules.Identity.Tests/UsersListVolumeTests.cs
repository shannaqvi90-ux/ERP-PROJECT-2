using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Identity.Tests;

/// <summary>The demo volume: 100,000 users in the main workspace (and 1,000 in the second).</summary>
public sealed class UsersVolumeFixture : IAsyncLifetime
{
    public const int Volume = 100_000;
    public const string NeedleName = "Shamma Waleed Al Romaithi";

    public const string NeedleNameAr = "شمة وليد الرميثي";

    public ErpTestEnvironment Env { get; private set; } = null!;

    public SeedTenant Main => Env.Plan.Tenants[0];

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.Demo(Volume, ErpTestEnvironment.Password));
        await using (var admin = await Env.OpenAdminAsync())
        {
            await using var analyze = new NpgsqlCommand("ANALYZE identity.users", admin);
            await analyze.ExecuteNonQueryAsync();
        }
        using var client = await Env.SignInAsync(Env.Email(Main, "admin"));
        var created = await client.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"shamma.romaithi@{Main.EmailDomain}",
            displayName = NeedleName,
            displayNameAr = NeedleNameAr,
            language = "ar",
            password = ErpTestEnvironment.Password,
        });
        created.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The timing tests run alone, after the rest of this assembly's tests, so the suite's own
/// parallel tests do not share the machine with the measurement (the budgets are unchanged).</summary>
/// <para>./erp verify also runs them in a step of their own after every other .NET test (trait
/// <see cref="TimingBudget.Trait"/>), so no other assembly's tests share the machine with the measurement
/// either, and the 100,000-user fixture is seeded then.</para>
[CollectionDefinition(nameof(VolumeTimingCollection), DisableParallelization = true)]
public sealed class VolumeTimingCollection;

/// <summary>
/// The owner's bar: find one record among 100,000 in well under a second of server time. Search,
/// filters, sorted pages and deep keyset pages over 100,004 users answer within the budget, and
/// PostgreSQL serves the word search from the trigram index rather than scanning the table.
/// </summary>
[Collection(nameof(VolumeTimingCollection))]
[Trait(TimingBudget.Trait, TimingBudget.Value)]
public sealed class UsersListVolumeTests(UsersVolumeFixture fixture) : IClassFixture<UsersVolumeFixture>
{
    /// <summary>Server time allowed for the median of seven runs of one list request at demo volume
    /// (measured in-process, so on a machine shared with other test runs).</summary>
    private const int MedianBudgetMilliseconds = 400;

    /// <summary>No single run may take a second ("well under a second"), even on a busy machine.</summary>
    private const int CeilingMilliseconds = 1000;

    private ErpTestEnvironment Env => fixture.Env;

    private sealed record Timing(string Uri, double Median, double Slowest)
    {
        public bool WithinBudget => Median < MedianBudgetMilliseconds && Slowest < CeilingMilliseconds;

        public override string ToString() => $"{Uri}: median {Median:F0} ms, slowest {Slowest:F0} ms";
    }

    private static async Task<(JsonElement Page, Timing Timing)> TimedAsync(HttpClient client, string uri)
    {
        // Warm the plan cache and connection pool, then time seven runs (in-process: server time).
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
    public async Task One_user_is_found_among_100000_in_well_under_a_second()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var (all, _) = await TimedAsync(admin, "/api/identity/users?take=1");
        Assert.True(all.GetProperty("total").GetInt32() > UsersVolumeFixture.Volume, $"{all.GetProperty("total").GetInt32()} users");

        var timings = new List<Timing>();
        foreach (var search in new[] { "shamma romaithi", "Romaithi", "SHAMMA WALEED", "shamma.romaithi@" })
        {
            var (page, timing) = await TimedAsync(admin, $"/api/identity/users?search={Uri.EscapeDataString(search)}");
            timings.Add(timing);
            Assert.Contains(page.GetProperty("items").EnumerateArray(), u => u.GetProperty("displayName").GetString() == UsersVolumeFixture.NeedleName);
            Assert.True(page.GetProperty("total").GetInt32() <= 2, $"'{search}' matched {page.GetProperty("total").GetInt32()} users");
            Assert.True(timing.WithinBudget, string.Join("\n", timings));
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }

    [Fact]
    public async Task One_user_is_found_among_100000_by_the_name_written_in_Arabic_in_well_under_a_second()
    {
        // Critic p03 round 3: an Arabic administrator searching a colleague's name as written in
        // Arabic found nobody. Arabic words search the name and the Arabic name, two fields per
        // word like any other search, inside the same budget.
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var timings = new List<Timing>();
        foreach (var search in new[] { "شمة الرميثي", "الرميثي شمة وليد", "romaithi الرميثي" })
        {
            var (page, timing) = await TimedAsync(admin, $"/api/identity/users?search={Uri.EscapeDataString(search)}");
            timings.Add(timing);
            Assert.Contains(page.GetProperty("items").EnumerateArray(), u => u.GetProperty("displayName").GetString() == UsersVolumeFixture.NeedleName);
            Assert.True(page.GetProperty("total").GetInt32() <= 2, $"'{search}' matched {page.GetProperty("total").GetInt32()} users");
            Assert.True(timing.WithinBudget, string.Join("\n", timings));
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }

    [Fact]
    public async Task Sorted_filtered_grouped_and_deep_keyset_pages_stay_within_budget()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var timings = new List<Timing>();
        foreach (var uri in new[]
                 {
                     "/api/identity/users?take=100",
                     "/api/identity/users?take=100&sort=displayName",
                     "/api/identity/users?take=100&sort=-lastSignInAt",
                     "/api/identity/users?take=100&sort=email",
                     $"/api/identity/users?take=100&filter={Uri.EscapeDataString("language eq 'ar' and isActive eq true")}",
                     "/api/identity/users?take=1&groupBy=language",
                     $"/api/identity/users?take=100&search={Uri.EscapeDataString("al")}",
                 })
        {
            var (_, timing) = await TimedAsync(admin, uri);
            timings.Add(timing);
            Assert.True(timing.WithinBudget, string.Join("\n", timings));
        }

        // Walk 30 pages deep by keyset, then time the next step: as fast as the first page.
        string? next = null;
        for (var i = 0; i < 30; i++)
        {
            var page = await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?take=200&sort=displayName" + (next is null ? "" : $"&after={Uri.EscapeDataString(next)}"));
            next = page.GetProperty("next").GetString();
        }
        var (deep, deepTiming) = await TimedAsync(admin, $"/api/identity/users?take=200&sort=displayName&after={Uri.EscapeDataString(next!)}");
        timings.Add(deepTiming with { Uri = "keyset page 31 by name" });
        Assert.Equal(200, deep.GetProperty("items").GetArrayLength());
        Assert.True(deepTiming.WithinBudget, string.Join("\n", timings));
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }

    [Fact]
    public async Task Word_search_is_served_by_the_trigram_index_not_a_table_scan()
    {
        await using var app = await Env.OpenAppAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using (var bind = new NpgsqlCommand(
                         "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)", app, tx))
        {
            bind.Parameters.AddWithValue("t", fixture.Main.Id.ToString());
            await bind.ExecuteNonQueryAsync();
        }
        await using var explain = new NpgsqlCommand("""
            EXPLAIN (FORMAT TEXT)
            SELECT id FROM identity.users
             WHERE tenant_id = @t
               AND (display_name ILIKE @p ESCAPE '\' OR email_normalized ILIKE @p ESCAPE '\')
               AND (display_name ILIKE @q ESCAPE '\' OR email_normalized ILIKE @q ESCAPE '\')
             ORDER BY created_at DESC, id DESC LIMIT 51
            """, app, tx);
        explain.Parameters.AddWithValue("t", fixture.Main.Id);
        explain.Parameters.AddWithValue("p", "%shamma%");
        explain.Parameters.AddWithValue("q", "%romaithi%");
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }
        var text = string.Join("\n", plan);
        Assert.True(text.Contains("ix_users_display_name_email_normalized", StringComparison.Ordinal), text);
        Assert.DoesNotContain("Seq Scan", text, StringComparison.Ordinal);
    }
}
