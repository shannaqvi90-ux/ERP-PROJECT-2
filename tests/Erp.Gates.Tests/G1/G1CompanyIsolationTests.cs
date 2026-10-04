using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: the attack creates a user and writes in company X.</summary>
public sealed class G1CompanyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, company layer over HTTP. Inside tenant A, an administrator who may work only in company X
/// (every permission, one company) and the read-only user of company X attack company Y: every
/// endpoint the running app exposes receives Y's ids in every route parameter, in documented and
/// guessed query parameters (companyId, branchId, id, …), in every id field of every request body,
/// and Y's own text values in every query and body text field. No answer may contain any of Y's
/// ids or Y-only text; a GET may not answer differently for a Y value than for a value that exists
/// nowhere; no answer may be a server error; and no row of company Y may change. Granting someone
/// access to Y and switching to Y are attempted directly as well.
/// </summary>
public sealed class G1CompanyIsolationTests(G1CompanyFixture fixture) : IClassFixture<G1CompanyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_user_of_company_X_cannot_reach_company_Y_through_any_endpoint()
    {
        var report = await CompanyAttack.RunAsync(Env);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{report.EndpointsAttacked} endpoints, {report.Requests} requests, {report.Markers} company Y markers, {report.DifferentialChecks} differential checks");
        Assert.True(report.Leaks.Count == 0, $"{report.Leaks.Count} leaks of company Y:\n" + string.Join("\n", report.Leaks.Take(40)));
        Assert.True(report.Oracles.Count == 0, "Answers that tell company Y's values apart from values that exist nowhere:\n" + string.Join("\n", report.Oracles.Take(30)));
        Assert.True(report.ServerErrors.Count == 0, "Server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        Assert.True(report.ChangedTables.Count == 0, "Company Y rows changed in: " + string.Join(", ", report.ChangedTables));
        Assert.True(report.Escalations.Count == 0, string.Join("\n", report.Escalations));
        Assert.True(report.EndpointsAttacked >= Ratchet.Min("g1.companyEndpointsAttacked"),
            $"g1.companyEndpointsAttacked: {report.EndpointsAttacked}; ratchet minimum {Ratchet.Min("g1.companyEndpointsAttacked")}");
        Assert.True(report.Requests >= Ratchet.Min("g1.companyAttackRequests"),
            $"g1.companyAttackRequests: {report.Requests}; ratchet minimum {Ratchet.Min("g1.companyAttackRequests")}");
        Assert.True(report.Markers >= Ratchet.Min("g1.companyMarkers"), $"g1.companyMarkers: {report.Markers}; ratchet minimum {Ratchet.Min("g1.companyMarkers")}");
    }
}

public sealed record CompanyAttackReport(
    IReadOnlyList<string> Leaks,
    IReadOnlyList<string> Oracles,
    IReadOnlyList<string> ServerErrors,
    IReadOnlyList<string> ChangedTables,
    IReadOnlyList<string> Escalations,
    int EndpointsAttacked,
    int Requests,
    int Markers,
    int DifferentialChecks);

/// <summary>The company attack, reusable by the gate self-tests.</summary>
public static class CompanyAttack
{
    private static readonly string[] GuessedQueryNames = ["companyId", "branchId", "company", "branch", "id", "userId", "search", "q"];

    public static async Task<CompanyAttackReport> RunAsync(ErpTestEnvironment env)
    {
        var tenant = env.TenantA;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Name is not ("auth.signIn" or "auth.signOut") && !e.Pattern.Contains("{*", StringComparison.Ordinal))
            .ToList();

        await using var admin = await env.OpenAdminAsync();
        var companies = await DbCatalog.ReadAsync(admin, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenant.Id));
        Assert.True(companies.Count >= 2, "tenant A needs two companies");
        var (x, y) = (companies[0], companies[1]);

        // An administrator (every permission) who may work only in company X.
        using var tenantAdmin = await env.SignInAsync(env.Email(tenant, "admin"));
        var administratorRole = (await tenantAdmin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var email = $"company.x.{Guid.NewGuid():N}@{tenant.EmailDomain}";
        var created = await tenantAdmin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Company X administrator", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { administratorRole } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var scopedUser = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var granted = await tenantAdmin.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
            new { companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } } });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        var examples = openApi.ExampleValues();
        var before = await CompanySnapshot.TakeAsync(env, tenant.Id, y, examples);
        Assert.True(before.Ids.Count >= 5, "company Y has too few rows to attack");
        var attackers = new List<(string Name, HttpClient Client)>
        {
            ("company X administrator", await env.SignInAsync(email)),
            ("company X read-only user", await env.SignInAsync(env.Email(tenant, "viewer"))),
        };
        var state = new State(before);
        var yIds = before.Ids.Select(i => i.ToString()).ToList();
        var yValues = yIds.Take(25).Concat(before.Strings.Take(25)).ToList();
        var attacked = 0;

        foreach (var endpoint in endpoints)
        {
            attacked++;
            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var documentedQueries = openApi.Parameters(endpoint.Method, endpoint.Pattern).Where(p => p.In == "query").Select(p => p.Name);
            var queries = documentedQueries.Concat(GuessedQueryNames).Distinct().ToList();
            foreach (var (name, client) in attackers)
            {
                // Y's ids in every route parameter (bodies carry Y's ids in every id field).
                var routeValues = endpoint.RouteParameters.Count == 0 ? [""] : yIds.Take(40).ToList();
                var n = 0;
                foreach (var value in routeValues)
                {
                    var path = endpoint.Path(_ => value);
                    await state.SendAsync(name, client, endpoint.Method, path, Body(openApi, schema, yIds, n++), [value]);
                }
                // Y's ids and texts in every query parameter, with a value that exists nowhere for GETs.
                var basePath = endpoint.Path(_ => Guid.NewGuid().ToString());
                if (endpoint.RouteParameters.Count == 0)
                {
                    foreach (var query in queries)
                    {
                        foreach (var value in yValues)
                        {
                            string Uri(string v) => $"{basePath}?{System.Uri.EscapeDataString(query)}={System.Uri.EscapeDataString(v)}";
                            var answer = await state.SendAsync(name, client, endpoint.Method, Uri(value), Body(openApi, schema, yIds, n++), [value]);
                            if (endpoint.Method == "GET")
                            {
                                var control = Guid.TryParse(value, out _) ? Guid.NewGuid().ToString() : Scramble(value);
                                var other = await state.SendAsync(name, client, "GET", Uri(control), null, [control]);
                                state.Compare(name, $"GET {Uri(value)}", answer, other, value, control);
                            }
                        }
                    }
                }
                // Y's texts in every text field of the body.
                if (schema is { } bodySchema && endpoint.RouteParameters.Count == 0)
                {
                    foreach (var field in openApi.StringLeaves(bodySchema))
                    {
                        foreach (var value in before.Strings.Take(25))
                        {
                            var body = openApi.BuildBody(bodySchema, (leaf, type, format, leafName) =>
                                leafName == field && type == "string" && format != "uuid" ? value : openApi.Conform(leaf, Leaf(type, format, yIds, n++)));
                            await state.SendAsync(name, client, endpoint.Method, endpoint.Path(_ => ""), body, [value]);
                        }
                    }
                }
            }
        }

        // Direct escalations: give someone access to Y, switch to Y, open Y.
        var escalations = new List<string>();
        var scopedClient = attackers[0].Client;
        var viewerId = (await tenantAdmin.GetFromJsonAsync<JsonElement>($"/api/identity/users?search={Uri.EscapeDataString(env.Email(tenant, "viewer"))}"))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var grantY = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{viewerId}",
            new { companies = new object[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() }, new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } } });
        if (grantY.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"granting company Y answered {(int)grantY.StatusCode}, expected 400");
        var grantSelf = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
            new { companies = new[] { new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } } });
        if (grantSelf.IsSuccessStatusCode) escalations.Add($"granting itself company Y answered {(int)grantSelf.StatusCode}");
        var switchY = await scopedClient.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = y, branchId = (Guid?)null });
        if (switchY.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"switching to company Y answered {(int)switchY.StatusCode}, expected 400");
        var openY = await scopedClient.GetAsync($"/api/tenancy/companies/{y}");
        if (openY.StatusCode != HttpStatusCode.NotFound) escalations.Add($"opening company Y answered {(int)openY.StatusCode}, expected 404");
        var workplace = await scopedClient.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
        if (workplace.GetProperty("companies").EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == y))
            escalations.Add("the workplace switcher offers company Y");
        var session = await scopedClient.GetFromJsonAsync<JsonElement>("/api/auth/session");
        if (session.GetProperty("permissions").GetArrayLength() == 0) escalations.Add("the company X administrator holds no permissions (the attack would be blind)");

        var after = await CompanySnapshot.TakeAsync(env, tenant.Id, y, examples);
        foreach (var (_, client) in attackers)
        {
            client.Dispose();
        }
        return new CompanyAttackReport(state.Leaks, state.Oracles, state.ServerErrors, CompanySnapshot.Differences(before, after), escalations,
            attacked, state.Requests, before.Markers.Count, state.DifferentialChecks);
    }

    private static JsonNode? Body(OpenApiDocument openApi, JsonElement? schema, List<string> yIds, int n)
    {
        if (schema is not { } s)
        {
            return null;
        }
        var body = openApi.BuildBody(s, (leaf, type, format, _) => openApi.Conform(leaf, Leaf(type, format, yIds, n))) as JsonObject ?? [];
        body["companyId"] = yIds[n % yIds.Count];
        return body;
    }

    private static JsonNode? Leaf(string type, string? format, List<string> yIds, int n) => type switch
    {
        "string" when format == "uuid" => yIds[n % yIds.Count],
        "string" when format == "date-time" => DateTimeOffset.UtcNow.ToString("O"),
        "string" => $"company-attack-{n}",
        "integer" => 1,
        "number" => "1",
        "boolean" => true,
        _ => null,
    };

    private static string Scramble(string value)
    {
        var random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(value.Length);
        return new string(value.Select((c, i) => char.IsLetter(c) ? (char)('a' + random[i] % 26) : char.IsDigit(c) ? (char)('0' + random[i] % 10) : c).ToArray());
    }

    private sealed class State(CompanySnapshot victim)
    {
        public List<string> Leaks { get; } = [];
        public List<string> Oracles { get; } = [];
        public List<string> ServerErrors { get; } = [];
        public int Requests { get; private set; }
        public int DifferentialChecks { get; private set; }

        /// <summary>Y texts the attacker wrote into its own records by successful writes.</summary>
        public HashSet<string> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<(int Status, string Text)> SendAsync(string attacker, HttpClient client, string method, string path, JsonNode? body, IReadOnlyCollection<string> sent)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body is not null || method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent((body ?? new JsonObject()).ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await client.SendAsync(request);
            var text = method == "GET" && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.Ordinal) == true
                ? Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync())
                : await response.Content.ReadAsStringAsync();
            Requests++;
            var status = (int)response.StatusCode;
            // An echo of what this request sent is not a leak, and neither is a Y text the attacker
            // itself stored earlier in a record it may keep (a user it created, named with Y's
            // branch name, is listed by the access list): those are the attacker's own data.
            var scrubbed = sent.Concat(Stored).Where(v => v.Length > 0).Distinct().OrderByDescending(v => v.Length)
                .Aggregate(text, (t, v) => t.Replace(v, "<sent>", StringComparison.OrdinalIgnoreCase).Replace(JsonSerializer.Serialize(v)[1..^1], "<sent>", StringComparison.OrdinalIgnoreCase));
            if (victim.FindMarker(scrubbed) is { } marker)
            {
                Leaks.Add($"{attacker} → {method} {path} → {status}: contains company Y marker {marker}");
            }
            if (method is "POST" or "PUT" or "PATCH" && status is >= 200 and < 300)
            {
                // A successful write: the values it carried are stored where the attacker may read them.
                foreach (var value in sent.Where(v => v.Length > 0 && !Guid.TryParse(v, out _)))
                {
                    Stored.Add(value);
                }
            }
            if (status >= 500)
            {
                ServerErrors.Add($"{attacker} → {method} {path} → {status}: {text[..Math.Min(200, text.Length)]}");
            }
            return (status, text);
        }

        public void Compare(string attacker, string label, (int Status, string Text) answer, (int Status, string Text) control, string value, string controlValue)
        {
            DifferentialChecks++;
            static string Normalize(string text, string a, string b)
            {
                try
                {
                    var node = JsonNode.Parse(text);
                    if (node is JsonObject obj) obj.Remove("traceId");
                    text = node?.ToJsonString() ?? "";
                }
                catch (JsonException)
                {
                }
                return text.Replace(a, "<v>", StringComparison.OrdinalIgnoreCase).Replace(b, "<v>", StringComparison.OrdinalIgnoreCase)
                    .Replace(Uri.EscapeDataString(a), "<v>", StringComparison.OrdinalIgnoreCase).Replace(Uri.EscapeDataString(b), "<v>", StringComparison.OrdinalIgnoreCase);
            }
            var left = Normalize(answer.Text, value, controlValue);
            var right = Normalize(control.Text, value, controlValue);
            if (answer.Status != control.Status || left != right)
            {
                Oracles.Add($"{attacker} → {label}: {answer.Status} {left[..Math.Min(160, left.Length)]} but for a value that exists nowhere {control.Status} {right[..Math.Min(160, right.Length)]}");
            }
        }
    }
}

/// <summary>Everything that identifies one company's data, read with the superuser: the ids of
/// its rows in every company table, its texts that appear in no other row of the tenant, and a
/// checksum of its rows.</summary>
public sealed class CompanySnapshot
{
    public required IReadOnlyList<Guid> Ids { get; init; }
    public required IReadOnlyList<string> Strings { get; init; }
    public required IReadOnlyDictionary<string, string> Checksums { get; init; }

    public IReadOnlyList<string> Markers => Ids.Select(i => i.ToString()).Concat(Strings.Where(s => s.Length >= 8)).ToList();

    public string? FindMarker(string text) => Markers.FirstOrDefault(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <param name="publicValues">Published API example values: they identify no company.</param>
    public static async Task<CompanySnapshot> TakeAsync(ErpTestEnvironment env, Guid tenant, Guid company, IReadOnlySet<string> publicValues)
    {
        await using var admin = await env.OpenAdminAsync();
        var ids = new List<Guid> { company };
        var strings = new List<string>();
        var checksums = new Dictionary<string, string>();
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        foreach (var table in tables)
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            ids.AddRange(await DbCatalog.ReadAsync(admin, $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c ORDER BY id",
                r => r.GetGuid(0), ("t", tenant), ("c", company)));
            checksums[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE tenant_id = @t AND company_id = @c",
                ("t", tenant), ("c", company));
            foreach (var column in columns.Where(c => c.Type.StartsWith("character varying", StringComparison.Ordinal) || c.Type == "text"))
            {
                strings.AddRange(await DbCatalog.ReadAsync(admin,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c AND \"{column.Name}\" IS NOT NULL AND length(\"{column.Name}\") >= 4",
                    r => r.GetString(0), ("t", tenant), ("c", company)));
            }
        }
        // Only text no other row of the tenant holds (outside the audit trail) identifies company Y.
        var others = new List<string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            var isCompanyTable = tables.Contains(table);
            others.AddRange(await DbCatalog.ReadAsync(admin,
                $"SELECT lower(t::text) FROM {table.Qualified} t WHERE tenant_id = @t" + (isCompanyTable ? " AND company_id <> @c" : ""),
                r => r.GetString(0), ("t", tenant), ("c", company)));
        }
        var unique = strings.Distinct(StringComparer.Ordinal)
            .Where(s => !publicValues.Contains(s.Trim()))
            .Where(s => !others.Any(o => o.Contains(s.ToLowerInvariant(), StringComparison.Ordinal)))
            .ToList();
        return new CompanySnapshot { Ids = ids.Distinct().ToList(), Strings = unique, Checksums = checksums };
    }

    public static IReadOnlyList<string> Differences(CompanySnapshot before, CompanySnapshot after) =>
        before.Checksums.Keys.Union(after.Checksums.Keys)
            .Where(t => before.Checksums.GetValueOrDefault(t) != after.Checksums.GetValueOrDefault(t))
            .Order(StringComparer.Ordinal).ToList();
}
