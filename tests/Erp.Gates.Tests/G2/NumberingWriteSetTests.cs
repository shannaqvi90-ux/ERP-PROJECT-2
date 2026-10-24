using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what numbering.sequences.update lets a request write. Every company holds a sequence for each
/// document type from the moment it exists (seeded, or made with the company), so the generic
/// write-set gate (<see cref="WriteSets"/>) has no POST to make one for the edit and lists PUT
/// /api/numbering/sequences/{id} in tests/Gates/write-set-unexercised.txt. This gate sends that edit
/// itself, the way the generic one sends every other: as a user holding exactly the permission and
/// working in every company and branch, the valid request (a fresh prefix), then every true-or-false
/// field turned the other way, every other documented choice of the reset, the largest number of
/// digits and a new next number, each at a sequence of its own with its own version. The audit trail
/// must show each request changing only that sequence's pattern columns and its own counters (the
/// write set below, reviewed here), and every request must be accepted. It also checks that a reader
/// and a caller limited to one branch of the company are refused with nothing written.
/// </summary>
public sealed class NumberingWriteSetTests(NumberingWriteSetFixture fixture) : IClassFixture<NumberingWriteSetFixture>
{
    /// <summary>The reviewed write set of numbering.sequences.update on the sequence itself.</summary>
    private static readonly HashSet<string> SequenceColumns = new(StringComparer.Ordinal) { "prefix", "suffix", "padding", "reset", "gapless", "per_branch" };

    /// <summary>On the sequence's own counters: today's counter moved, or made when the sequence starts
    /// again at another time or its next number is set.</summary>
    private static readonly HashSet<string> CounterColumns = new(StringComparer.Ordinal) { "next_value" };

    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_sequence_edit_writes_only_that_sequences_pattern_and_its_own_counters()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var editor = await UserHoldingAsync(admin, "nws.editor", ["numbering.sequences.update"]);
        var rows = (await admin.GetFromJsonAsync<JsonElement>("/api/numbering/sequences?take=200")).GetProperty("items").EnumerateArray().ToList();
        var variants = new (string Name, Func<JsonObject, JsonObject> Change)[]
        {
            ("valid", body => { body["prefix"] = "WS1-{YYYY}-"; return body; }),
            ("gapless turned", body => { body["gapless"] = !body["gapless"]!.GetValue<bool>(); return body; }),
            ("perBranch turned", body => { body["prefix"] = "WS3-{BRANCH}-{YYYY}-"; body["perBranch"] = true; return body; }),
            ("reset=monthly", body => { body["prefix"] = "WS4-{YYYY}{MM}-"; body["reset"] = "monthly"; return body; }),
            ("reset=never", body => { body["prefix"] = "WS5-"; body["reset"] = "never"; return body; }),
            ("padding=12", body => { body["padding"] = 12; return body; }),
            ("nextNumber=4000", body => { body["nextNumber"] = 4000; return body; }),
            ("suffix", body => { body["suffix"] = "/WS8"; return body; }),
        };
        var targets = rows.Where(r => r.GetProperty("canUpdate").GetBoolean() && !r.GetProperty("gapless").GetBoolean() && !r.GetProperty("perBranch").GetBoolean())
            .Take(variants.Length).ToList();
        Assert.Equal(variants.Length, targets.Count);
        var problems = new List<string>();
        var judged = 0;
        var anyRequest = WriteSets.Load().Where(l => l.Permission == "*").ToList();
        for (var i = 0; i < variants.Length; i++)
        {
            var id = targets[i].GetProperty("id").GetGuid();
            var current = await admin.GetFromJsonAsync<JsonObject>($"/api/numbering/sequences/{id}") ?? [];
            var body = variants[i].Change(new JsonObject
            {
                ["prefix"] = current["prefix"]?.DeepClone(),
                ["suffix"] = current["suffix"]?.DeepClone(),
                ["padding"] = current["padding"]?.DeepClone(),
                ["reset"] = current["reset"]?.DeepClone(),
                ["gapless"] = current["gapless"]?.DeepClone(),
                ["perBranch"] = current["perBranch"]?.DeepClone(),
                ["nextNumber"] = null,
                ["version"] = current["version"]?.DeepClone(),
            });
            var before = await LastAuditIdAsync();
            using var response = await editor.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/numbering/sequences/{id}", body));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                problems.Add($"PUT /api/numbering/sequences/{{id}} [{variants[i].Name}] answered {(int)response.StatusCode}: {text}");
                continue;
            }
            var writes = (await WritesSinceAsync(before))
                .Where(w => !anyRequest.Any(l => l.Covers("*", new WriteSets.Write(0, w.Table, w.Action, w.Columns)))).ToList();
            if (writes.Count == 0)
            {
                problems.Add($"[{variants[i].Name}] changed nothing in the audit trail: the edit was not judged");
            }
            var counters = await CountersOfAsync(id);
            foreach (var (table, action, record, columns) in writes)
            {
                judged++;
                var allowed = (table, action) switch
                {
                    ("numbering.sequences", "update") => record == id && columns.All(SequenceColumns.Contains),
                    ("numbering.counters", "insert") => record is { } made && counters.Contains(made),
                    ("numbering.counters", "update") => record is { } moved && counters.Contains(moved) && columns.All(CounterColumns.Contains),
                    _ => false,
                };
                if (!allowed)
                {
                    problems.Add($"[{variants[i].Name}] as a user holding exactly [numbering.sequences.update] wrote {action} {table} {record} ({string.Join(", ", columns)}), " +
                                 $"outside the write set (the sequence's {string.Join(", ", SequenceColumns)}; its own counters' {string.Join(", ", CounterColumns)})");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(judged >= variants.Length);
    }

    [Fact]
    public async Task A_reader_and_a_caller_limited_to_one_branch_of_the_company_change_no_sequence()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var reader = await UserHoldingAsync(admin, "nws.reader", ["numbering.sequences.read"]);
        using var oneBranch = await UserHoldingAsync(admin, "nws.branch", ["numbering.sequences.update", "numbering.sequences.read"], oneBranch: true);
        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/numbering/sequences?take=200")).GetProperty("items").EnumerateArray()
            .First(r => r.GetProperty("documentType").GetString() == "purchasing.bill");
        var body = new JsonObject
        {
            ["prefix"] = "NWS-{YYYY}-", ["suffix"] = "", ["padding"] = 5, ["reset"] = "yearly", ["gapless"] = false, ["perBranch"] = false,
            ["nextNumber"] = null, ["version"] = row.GetProperty("version").GetUInt32(),
        };
        var path = $"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}";
        var before = await LastAuditIdAsync();
        using var asReader = await reader.SendAsync(G1WriteOracle.Json(HttpMethod.Put, path, body));
        Assert.Equal(HttpStatusCode.Forbidden, asReader.StatusCode);
        using var limited = await oneBranch.SendAsync(G1WriteOracle.Json(HttpMethod.Put, path, body));
        Assert.Equal(HttpStatusCode.Forbidden, limited.StatusCode);
        Assert.Equal("numbering.companyNeedsEveryBranch", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.DoesNotContain(await WritesSinceAsync(before), w => w.Table.StartsWith("numbering.", StringComparison.Ordinal));
    }

    private async Task<HttpClient> UserHoldingAsync(HttpClient admin, string local, string[] permissions, bool oneBranch = false)
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
        var options = (access["options"] as JsonArray ?? []).ToList();
        Assert.True(options.Count >= 2, "the gate workspace has two companies");
        // Every company and every branch, or every company with one branch only of the first.
        var given = options.Select((o, i) =>
        {
            var company = o!["id"]!.GetValue<Guid>();
            var branches = (o["branches"] as JsonArray ?? []).Select(b => b!["id"]!.GetValue<Guid>()).ToArray();
            return oneBranch && i == 0
                ? new { companyId = company, allBranches = false, branchIds = branches.Take(1).ToArray() }
                : new { companyId = company, allBranches = true, branchIds = Array.Empty<Guid>() };
        });
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access["version"]?.GetValue<uint>() });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return await Env.SignInAsync($"{local}@{Env.TenantA.EmailDomain}");
    }

    private async Task<HashSet<Guid>> CountersOfAsync(Guid sequence)
    {
        await using var owner = await Env.OpenAdminAsync();
        return (await DbCatalog.ReadAsync(owner, "SELECT id FROM numbering.counters WHERE sequence_id = @s", r => r.GetGuid(0), ("s", sequence))).ToHashSet();
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

/// <summary>Its own environment: the gate changes sequences of tenant A.</summary>
public sealed class NumberingWriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
