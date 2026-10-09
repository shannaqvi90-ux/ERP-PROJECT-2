using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, row versions (critic p03 round 6). PostgreSQL's <c>xmin</c> is the id of the transaction
/// that wrote a row, counted across the whole database: shown to a client as a record's version, it
/// lets tenant A count tenant B's writes between two of its own (B's role got version 944, A's next
/// record 945). Tenant A writes a record, tenant B writes several, tenant A writes another; every
/// version tenant A is shown for its own records (read from every list it can open, at any depth of
/// the answer) must be none of the transaction ids of any tenant row, and A's two versions must not
/// be as far apart as the transactions between them.
/// </summary>
public sealed class G1RowVersionTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_record_version_is_never_a_database_transaction_id()
    {
        using var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var tag = Guid.NewGuid().ToString("N")[..8];
        var first = await CreateRoleAsync(a, $"Versions A1 {tag}");
        for (var i = 0; i < 3; i++)
        {
            await CreateRoleAsync(b, $"Versions B{i} {tag}");
        }
        var second = await CreateRoleAsync(a, $"Versions A2 {tag}");

        // Every version tenant A is shown, from every list it can open (no route values).
        var shown = new HashSet<long> { first, second };
        foreach (var endpoint in EndpointInventory.From(Env.Factory.Services).Where(e => e.Method == "GET" && e.RouteParameters.Count == 0 && !e.IsAnonymous))
        {
            using var response = await a.GetAsync(endpoint.Pattern);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                continue;
            }
            Collect(JsonNode.Parse(await response.Content.ReadAsStringAsync()), shown);
        }

        await using var owner = await Env.OpenAdminAsync();
        var transactions = new HashSet<long>();
        var rows = 0;
        foreach (var table in await DbCatalog.TenantTablesAsync(owner))
        {
            foreach (var xmin in await DbCatalog.ReadAsync(owner, $"SELECT xmin::text::bigint FROM {table.Qualified}", r => r.GetInt64(0)))
            {
                transactions.Add(xmin);
                rows++;
            }
        }
        var same = shown.Where(transactions.Contains).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"{shown.Count} versions shown to tenant A; {transactions.Count} transaction ids over {rows} tenant rows");
        Assert.True(shown.Count >= Ratchet.Min("g1.rowVersionsJudged"), $"{shown.Count} versions shown to tenant A; ratchet minimum {Ratchet.Min("g1.rowVersionsJudged")}");
        Assert.True(same.Count == 0, $"versions shown to tenant A that are transaction ids of tenant rows: {string.Join(", ", same.Take(10))}");
        // With transaction ids, A's second version would be its first plus B's writes plus one.
        Assert.False(Math.Abs(second - first) <= 16, $"tenant A's two versions are {first} and {second}: as close as the transactions between them");
    }

    private static async Task<long> CreateRoleAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/identity/roles", new { nameEn = name, nameAr = "دور " + name, permissions = new[] { "identity.profile.update" } });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"creating the role {name} failed: {(int)response.StatusCode} {body}");
        var id = JsonNode.Parse(body)!["id"]!.GetValue<string>();
        var role = await client.GetFromJsonAsync<JsonObject>($"/api/identity/roles/{id}");
        return role!["version"]!.GetValue<long>();
    }

    private static void Collect(JsonNode? node, HashSet<long> versions)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    if (name.Equals("version", StringComparison.OrdinalIgnoreCase) && value is JsonValue v && v.TryGetValue<long>(out var number))
                    {
                        versions.Add(number);
                    }
                    else
                    {
                        Collect(value, versions);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, versions);
                }
                break;
        }
    }
}
