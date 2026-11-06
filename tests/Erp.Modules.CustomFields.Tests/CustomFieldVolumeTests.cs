using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.CustomFields.Tests;

/// <summary>The demo workspace: 100,000 contacts with four custom fields, a value of each field on
/// (nearly) every contact, plus a number, a rare text and a rare date on a few contacts.</summary>
public sealed class CustomFieldVolumeFixture : IAsyncLifetime
{
    public const int Volume = 100_000;
    public const string RareText = "Zanzibar Quasar Holdings";

    public ErpTestEnvironment Env { get; private set; } = null!;

    public SeedTenant Main => Env.Plan.Tenants[0];

    public string Score { get; private set; } = "";
    public string Rare { get; private set; } = "";
    public string Segment { get; } = "cfCustomerSegment";
    public string Manager { get; } = "cfAccountManager";
    public string Review { get; } = "cfCreditReview";
    public string Partner { get; } = "cfPreferredPartner";

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.Demo(200, ErpTestEnvironment.Password),
            new Dictionary<string, string?> { ["Erp:Seed:ContactsVolume"] = Volume.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var admin = await Env.SignInAsync(Env.Email(Main, "admin"));
        var score = await Api.DefineAsync(admin, Api.Field("number", "Annual turnover", "حجم الأعمال السنوي", new { decimals = 2 }));
        var rare = await Api.DefineAsync(admin, Api.Field("text", "Trading name", "الاسم التجاري", new { searchable = true }));
        Score = score.GetProperty("key").GetString()!;
        Rare = rare.GetProperty("key").GetString()!;
        for (var i = 0; i < 5; i++)
        {
            await Api.CreateContactAsync(admin, Api.Contact($"Volume probe {i} LLC", new() { [Score] = 123456.78m + i, [Rare] = $"{RareText} {i}", [Review] = "2031-07-04" }));
        }
        await using var connection = await Env.OpenAdminAsync();
        foreach (var table in new[] { "contacts.contacts", "customfields.field_values" })
        {
            await using var analyze = new NpgsqlCommand($"ANALYZE {table}", connection);
            await analyze.ExecuteNonQueryAsync();
        }
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// Filtering and searching by a custom field across 100,000 contacts stays fast because the values
/// are kept in indexed typed columns: a plan test (the value indexes answer the question, no
/// sequential read of the values) that does not depend on wall seconds, and a timing test run alone
/// after the others like the other lists' budgets.
/// </summary>
[CollectionDefinition(nameof(CustomFieldTimingCollection), DisableParallelization = true)]
public sealed class CustomFieldTimingCollection;

[Collection(nameof(CustomFieldTimingCollection))]
public sealed class CustomFieldVolumeTests(CustomFieldVolumeFixture fixture) : IClassFixture<CustomFieldVolumeFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>The plan of a question the contacts list asks, as the application role would run it in the workspace's transaction.</summary>
    private async Task<(JsonElement Plan, double Milliseconds)> ExplainAsync(string filterSql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Env.OpenAppAsync();
        await using var transaction = await connection.BeginTransactionAsync(Cancel);
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.company_scope', 'all', true)", connection, transaction))
        {
            set.Parameters.AddWithValue("t", fixture.Main.Id.ToString());
            await set.ExecuteNonQueryAsync(Cancel);
        }
        await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, FORMAT JSON) SELECT c.id FROM contacts.contacts c WHERE {filterSql} ORDER BY c.name_en, c.id LIMIT 101", connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        var json = (string)(await command.ExecuteScalarAsync(Cancel))!;
        var root = JsonDocument.Parse(json).RootElement[0];
        return (root.Clone(), root.GetProperty("Execution Time").GetDouble());
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                foreach (var inner in Nodes(child))
                {
                    yield return inner;
                }
            }
        }
    }

    private async Task<Guid> FieldIdAsync(string key)
    {
        await using var connection = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM customfields.definitions WHERE tenant_id = @t AND key = @k", connection);
        command.Parameters.AddWithValue("t", fixture.Main.Id);
        command.Parameters.AddWithValue("k", key);
        return (Guid)(await command.ExecuteScalarAsync(Cancel))!;
    }

    [Fact]
    public async Task A_selective_filter_on_a_custom_field_reads_the_value_indexes_not_the_whole_table()
    {
        var rare = await FieldIdAsync(fixture.Rare);
        var score = await FieldIdAsync(fixture.Score);
        var review = await FieldIdAsync(fixture.Review);
        var manager = await FieldIdAsync(fixture.Manager);
        var questions = new (string Name, string Where, (string, object)[] Parameters)[]
        {
            ("text equals", "c.id IN (SELECT m.record_id FROM customfields.match_text(@f, 'eq', @v) m)", [("f", rare), ("v", CustomFieldVolumeFixture.RareText.ToLowerInvariant() + " 3")]),
            ("text contains", "c.id IN (SELECT m.record_id FROM customfields.match_text(@f, 'contains', @v) m)", [("f", rare), ("v", "quasar holdings 2")]),
            ("number above", "c.id IN (SELECT m.record_id FROM customfields.match_number(@f, 'gt', @v) m)", [("f", score), ("v", 123459m)]),
            ("date equals", "c.id IN (SELECT m.record_id FROM customfields.match_date(@f, 'eq', @v) m)", [("f", review), ("v", new DateOnly(2031, 7, 4))]),
            ("date from", "c.id IN (SELECT m.record_id FROM customfields.match_date(@f, 'ge', @v) m)", [("f", review), ("v", new DateOnly(2030, 1, 1))]),
            ("search", "c.id IN (SELECT m.record_id FROM customfields.search_text(ARRAY[@f]::uuid[], @v) m)", [("f", rare), ("v", "%quasar holdings 4%")]),
        };
        var report = new List<string>();
        foreach (var (name, where, parameters) in questions)
        {
            var (plan, milliseconds) = await ExplainAsync(where, parameters);
            var nodes = Nodes(plan.GetProperty("Plan")).ToList();
            var onValues = nodes.Where(n => n.TryGetProperty("Relation Name", out var r) && r.GetString() == "field_values").ToList();
            report.Add($"{name}: {milliseconds:F1} ms; {string.Join(", ", nodes.Select(n => n.GetProperty("Node Type").GetString() + (n.TryGetProperty("Relation Name", out var r) ? " on " + r.GetString() : "") + (n.TryGetProperty("Index Name", out var i) ? " using " + i.GetString() : "")))}");
            // Branches the planner never ran (the other operators of the function) do not count.
            var ran = nodes.Where(n => n.GetProperty("Actual Loops").GetInt32() > 0).ToList();
            Assert.True(onValues.Any(n => n.GetProperty("Actual Loops").GetInt32() > 0), string.Join("\n", report));
            Assert.True(!ran.Any(n => n.GetProperty("Node Type").GetString() is "Seq Scan" or "Parallel Seq Scan" && n.TryGetProperty("Relation Name", out var r) && r.GetString() is "field_values" or "contacts"), string.Join("\n", report) + "\n" + string.Join("\n", nodes.Select(n => n.GetProperty("Node Type").GetString() + " loops " + n.GetProperty("Actual Loops").GetInt32())));
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", report));
        // A broad question (a fifth of the contacts) is answered too, whatever plan the planner prefers.
        var (_, broad) = await ExplainAsync("c.id IN (SELECT m.record_id FROM customfields.match_bool(@f, true) m)", ("f", await FieldIdAsync(fixture.Partner)));
        Assert.True(broad < 2000, $"{broad} ms");
        _ = manager;
    }

    [Fact]
    public async Task The_list_answers_custom_field_filters_and_search_over_100000_contacts()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts?take=1", Cancel);
        Assert.True(all.GetProperty("total").GetInt32() > CustomFieldVolumeFixture.Volume);
        async Task<JsonElement> PageAsync(string query)
        {
            var response = await admin.GetAsync($"/api/contacts/contacts?{query}", Cancel);
            var text = await response.Content.ReadAsStringAsync(Cancel);
            Assert.True(response.StatusCode == HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        string Filter(string text) => "take=100&filter=" + Uri.EscapeDataString(text);

        var rare = await PageAsync(Filter($"{fixture.Rare} eq '{CustomFieldVolumeFixture.RareText} 3'"));
        Assert.Equal(1, rare.GetProperty("total").GetInt32());
        Assert.Equal("Volume probe 3 LLC", rare.GetProperty("items")[0].GetProperty("nameEn").GetString());
        var turnover = await PageAsync(Filter($"{fixture.Score} gt 123459"));
        Assert.Equal(["Volume probe 3 LLC", "Volume probe 4 LLC"], turnover.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("nameEn").GetString()).Order());
        var segment = await PageAsync(Filter($"{fixture.Segment} eq 'government'"));
        Assert.InRange(segment.GetProperty("total").GetInt32(), 15_000, 25_000);
        var searched = await PageAsync($"take=100&search={Uri.EscapeDataString("quasar holdings 2")}");
        Assert.Equal("Volume probe 2 LLC", searched.GetProperty("items")[0].GetProperty("nameEn").GetString());
        var combined = await PageAsync(Filter($"{fixture.Segment} eq 'retail' and {fixture.Partner} eq true and isActive eq true"));
        Assert.InRange(combined.GetProperty("total").GetInt32(), 1, 10_000);
    }

    [Fact]
    [Trait(TimingBudget.Trait, TimingBudget.Value)]
    public async Task Filters_and_search_on_custom_fields_stay_within_the_list_budget()
    {
        using var admin = await Env.SignInAsync(Env.Email(fixture.Main, "admin"));
        var timings = new List<string>();
        foreach (var uri in new[]
                 {
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Rare} eq '{CustomFieldVolumeFixture.RareText} 3'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Score} ge 123456")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Review} eq '2031-07-04'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Review} ge '2026-01-01' and {fixture.Review} lt '2026-02-01'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Segment} eq 'government'")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Partner} eq true")}",
                     $"/api/contacts/contacts?take=100&filter={Uri.EscapeDataString($"{fixture.Manager} contains 'Mansoori'")}",
                     $"/api/contacts/contacts?take=100&search={Uri.EscapeDataString("quasar holdings")}",
                 })
        {
            (await admin.GetAsync(uri, Cancel)).EnsureSuccessStatusCode();
            var runs = new List<double>();
            for (var i = 0; i < 7; i++)
            {
                var clock = Stopwatch.StartNew();
                using var response = await admin.GetAsync(uri, Cancel);
                await response.Content.ReadAsStringAsync(Cancel);
                clock.Stop();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                runs.Add(clock.Elapsed.TotalMilliseconds);
            }
            runs.Sort();
            timings.Add($"{uri}: median {runs[3]:F0} ms, slowest {runs[^1]:F0} ms");
            Assert.True(runs[3] < 400 && runs[^1] < 1000, string.Join("\n", timings));
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", timings));
    }
}
