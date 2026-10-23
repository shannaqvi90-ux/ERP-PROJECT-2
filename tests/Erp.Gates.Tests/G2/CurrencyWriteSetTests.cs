using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what currency.currencies.update lets a request write. Every workspace holds the ISO 4217
/// currencies from its seed, so the generic write-set gate (<see cref="WriteSets"/>) has no POST to
/// make a currency for the edit and lists PUT /api/currency/currencies/{id} in
/// tests/Gates/write-set-unexercised.txt. This gate sends that edit itself, the way the generic one
/// sends every other: as a user holding exactly the permission and working in every company and
/// branch, the valid request (fresh names), then the in-use flag turned the other way, then the
/// largest rate age the field takes, each on a currency of the workspace with its own version. The
/// audit trail must show each request changing only the one currency's names, in-use flag and rate
/// age (the write set below, reviewed here), and every request must be accepted. It also checks that
/// a user holding only currency.currencies.read is refused, and that a caller outside the whole
/// workspace is refused with nothing written.
/// </summary>
public sealed class CurrencyWriteSetTests(CurrencyWriteSetFixture fixture) : IClassFixture<CurrencyWriteSetFixture>
{
    /// <summary>The reviewed write set of currency.currencies.update: one currency's own columns.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "name_en", "name_ar", "is_active", "max_rate_age_days" };

    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_currency_edit_writes_only_that_currencys_names_in_use_flag_and_rate_age()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var editor = await UserHoldingAsync(admin, "cws.editor", ["currency.currencies.update"]);
        var currencies = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?take=200")).GetProperty("items").EnumerateArray().ToList();
        Assert.True(currencies.Count >= 150, $"{currencies.Count} currencies: every workspace holds the ISO 4217 list");
        // Currencies no company keeps its books in, so taking one out of use is allowed.
        var targets = currencies.Where(c => c.GetProperty("code").GetString() is "CHF" or "NOK" or "SEK").Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(3, targets.Count);

        var variants = new (string Name, Func<JsonObject, JsonObject> Change)[]
        {
            ("valid", body => body),
            ("isActive turned", body => { body["isActive"] = !(body["isActive"]?.GetValue<bool>() ?? false); return body; }),
            ("maxRateAgeDays=366", body => { body["maxRateAgeDays"] = 366; return body; }),
        };
        var problems = new List<string>();
        var judged = 0;
        var anyRequest = WriteSets.Load().Where(l => l.Permission == "*").ToList();
        for (var i = 0; i < variants.Length; i++)
        {
            var id = targets[i];
            var current = await admin.GetFromJsonAsync<JsonObject>($"/api/currency/currencies/{id}") ?? [];
            var body = variants[i].Change(new JsonObject
            {
                ["nameEn"] = $"Write set {i} {current["code"]}",
                ["nameAr"] = $"مجموعة الكتابة {i}",
                ["isActive"] = current["isActive"]?.GetValue<bool>() ?? false,
                ["maxRateAgeDays"] = 1,
                ["version"] = current["version"]?.DeepClone(),
            });
            var before = await LastAuditIdAsync();
            using var response = await editor.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/currency/currencies/{id}", body));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                problems.Add($"PUT /api/currency/currencies/{{id}} [{variants[i].Name}] answered {(int)response.StatusCode}: {text}");
                continue;
            }
            // Writes any signed-in request may make (permission * of the reviewed write sets) are not the edit's.
            var writes = (await WritesSinceAsync(before))
                .Where(w => !anyRequest.Any(l => l.Covers("*", new WriteSets.Write(0, w.Table, w.Action, w.Columns)))).ToList();
            if (writes.Count == 0)
            {
                problems.Add($"[{variants[i].Name}] changed nothing in the audit trail: the edit was not judged");
            }
            foreach (var (table, action, record, columns) in writes)
            {
                judged++;
                if (table != "currency.currencies" || action != "update" || record != id || !columns.All(Allowed.Contains))
                {
                    problems.Add($"[{variants[i].Name}] as a user holding exactly [currency.currencies.update] wrote {action} {table} {record} ({string.Join(", ", columns)}), outside the write set (one currency's {string.Join(", ", Allowed)})");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(judged >= variants.Length);
    }

    [Fact]
    public async Task A_reader_and_a_caller_outside_the_whole_workspace_change_no_currency()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var reader = await UserHoldingAsync(admin, "cws.reader", ["currency.currencies.read"]);
        using var oneCompany = await UserHoldingAsync(admin, "cws.one", ["currency.currencies.update", "currency.currencies.read"], everyCompany: false);
        var chf = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=CHF")).GetProperty("items")[0];
        var body = new JsonObject
        {
            ["nameEn"] = "Refused", ["nameAr"] = "مرفوض", ["isActive"] = true, ["maxRateAgeDays"] = 2, ["version"] = chf.GetProperty("version").GetUInt32(),
        };
        var before = await LastAuditIdAsync();
        using var asReader = await reader.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/currency/currencies/{chf.GetProperty("id").GetGuid()}", body));
        Assert.Equal(HttpStatusCode.Forbidden, asReader.StatusCode);
        using var outside = await oneCompany.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/currency/currencies/{chf.GetProperty("id").GetGuid()}", body));
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal("currency.workspaceNeedsEveryCompany", (await outside.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty((await WritesSinceAsync(before)).Where(w => w.Table.StartsWith("currency.", StringComparison.Ordinal)));
    }

    private async Task<HttpClient> UserHoldingAsync(HttpClient admin, string local, string[] permissions, bool everyCompany = true)
    {
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Role {local}", nameAr = $"دور {local}", permissions });
        Assert.True(role.StatusCode == HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"{local}@{Env.TenantA.EmailDomain}", displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { roleId },
        });
        Assert.True(user.StatusCode == HttpStatusCode.Created, await user.Content.ReadAsStringAsync());
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await admin.GetFromJsonAsync<JsonObject>($"/api/tenancy/access/{userId}") ?? [];
        var options = (access["options"] as JsonArray ?? []).Select(o => o!["id"]!.GetValue<Guid>()).ToList();
        Assert.True(options.Count >= 2, "the gate workspace has two companies");
        var given = (everyCompany ? options : options.Take(1)).Select(c => new { companyId = c, allBranches = true, branchIds = Array.Empty<Guid>() });
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access["version"]?.GetValue<uint>() });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return await Env.SignInAsync($"{local}@{Env.TenantA.EmailDomain}");
    }

    private async Task<long> LastAuditIdAsync()
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<long>(owner, "SELECT COALESCE(max(id), 0) FROM audit.entries");
    }

    private async Task<List<(string Table, string Action, Guid? Record, string[] Columns)>> WritesSinceAsync(long after)
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ReadAsync(owner,
            "SELECT table_schema || '.' || table_name, action, record_id, ARRAY(SELECT jsonb_object_keys(changes) ORDER BY 1) FROM audit.entries WHERE id > @after ORDER BY id",
            r => (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2), r.GetFieldValue<string[]>(3)), ("after", after));
    }
}

/// <summary>Its own environment: the gate changes currencies of tenant A.</summary>
public sealed class CurrencyWriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
