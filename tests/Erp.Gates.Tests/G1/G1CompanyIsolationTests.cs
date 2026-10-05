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
            $"{report.EndpointsAttacked} endpoints, {report.Requests} requests, {report.Markers} company Y markers, {report.DifferentialChecks} differential checks, {report.WriteOracleChecks} write oracle checks");
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
        Assert.True(report.WriteOracleChecks >= Ratchet.Min("g1.companyWriteOracleChecks"),
            $"g1.companyWriteOracleChecks: {report.WriteOracleChecks}; ratchet minimum {Ratchet.Min("g1.companyWriteOracleChecks")}");
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
    int DifferentialChecks,
    int WriteOracleChecks = 0);

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

        // Write oracles inside the tenant, before the attack writes anything of its own (critic p02 round 2: an administrator of company X alone
        // got 409 for a company code only company Y uses and 201 for a code that exists nowhere).
        // Every write with an identifying field (a code, an e-mail) is sent by the company X
        // administrator twice, once with a value company Y holds in that column and once with a
        // value of the same shape that exists nowhere: the two answers must have the same status.
        var oracleClient = attackers[0].Client;
        await G1WriteOracle.UseWorkingCompanyAsync(oracleClient);
        var writeOracleChecks = 0;
        foreach (var endpoint in endpoints.Where(e => e.Method is "POST" or "PUT" or "PATCH" && !e.IsAnonymous))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema ||
                !openApi.Resolve(schema).TryGetProperty("properties", out var properties))
            {
                continue;
            }
            var collection = endpoint.RouteParameters.Count == 0 ? null : endpoint.Pattern[..endpoint.Pattern.LastIndexOf("/{", StringComparison.Ordinal)];
            if (collection is not null && openApi.RequestSchema("POST", collection) is null)
            {
                continue;
            }
            foreach (var field in properties.EnumerateObject()
                         .Where(p => openApi.TypeOfSchema(p.Value) == "string" &&
                                     G1WriteOracle.IsIdentifying(p.Name, openApi.Resolve(p.Value).TryGetProperty("format", out var f) ? f.GetString() : null))
                         .Select(p => p.Name))
            {
                // Only values company Y alone holds (no other row of the tenant has them, so a
                // refusal can only come from company Y) that the attacker never wrote itself.
                foreach (var held in before.ValuesOf(field).Where(v => before.Strings.Contains(v) && !state.Stored.Contains(v)).Take(3))
                {
                    var tag = $"wo{writeOracleChecks}{Guid.NewGuid():N}"[..12];
                    var fresh = Reshape(held);
                    var (withY, withYText) = await G1WriteOracle.SendAsync(oracleClient, openApi, endpoint, collection, schema, env, $"{tag}y", field, held);
                    var (withFresh, withFreshText) = await G1WriteOracle.SendAsync(oracleClient, openApi, endpoint, collection, schema, env, $"{tag}f", field, fresh);
                    writeOracleChecks++;
                    if (withY is >= 200 and < 300)
                    {
                        // Now the attacker's own record holds the value: later writes may refuse it for that.
                        state.Stored.Add(held);
                    }
                    if (withY != withFresh)
                    {
                        state.Oracles.Add($"company X administrator → {endpoint} [{field}]: company Y's value answered {withY}, a value that exists nowhere answered {withFresh} ({Short(withYText)} / {Short(withFreshText)})");
                    }
                }
            }
        }
        // The same with the attacker's own company record: changing company X's code to company Y's
        // code must answer as a code that exists nowhere does.
        var ownCompany = await oracleClient.GetFromJsonAsync<JsonObject>($"/api/tenancy/companies/{x}");
        var yCompany = await tenantAdmin.GetFromJsonAsync<JsonObject>($"/api/tenancy/companies/{y}");
        if (ownCompany is not null && yCompany?["code"]?.GetValue<string>() is { } yCode)
        {
            var original = ownCompany["code"]?.GetValue<string>();
            async Task<int> ChangeCode(string code)
            {
                var current = await oracleClient.GetFromJsonAsync<JsonObject>($"/api/tenancy/companies/{x}") ?? [];
                current["code"] = code;
                using var answer = await oracleClient.PutAsJsonAsync($"/api/tenancy/companies/{x}", current);
                return (int)answer.StatusCode;
            }
            var toY = await ChangeCode(yCode);
            var toFresh = await ChangeCode(Reshape(yCode));
            writeOracleChecks++;
            if (toY != toFresh)
            {
                state.Oracles.Add($"company X administrator → PUT /api/tenancy/companies/{{X}} [code]: company Y's code answered {toY}, a code that exists nowhere answered {toFresh}");
            }
            if (original is not null && toFresh is >= 200 and < 300)
            {
                await ChangeCode(original);
            }
        }

        foreach (var endpoint in endpoints)
        {
            attacked++;
            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var documentedQueries = openApi.Parameters(endpoint.Method, endpoint.Pattern).Where(p => p.In == "query").Select(p => p.Name);
            var queries = documentedQueries.Concat(GuessedQueryNames).Distinct().ToList();
            var get = endpoint.Method == "GET";
            foreach (var (name, client) in attackers)
            {
                // Every request of this attacker on this endpoint is built first, in the order (and
                // with the bodies) of sending them one after another, then sent four at a time.
                var batch = new List<(string Method, string Path, JsonNode? Body, string Value, string? ControlPath, string? ControlValue)>();
                // Y's ids in every route parameter (bodies carry Y's ids in every id field).
                var routeValues = endpoint.RouteParameters.Count == 0 ? [""] : yIds.Take(40).ToList();
                var n = 0;
                foreach (var value in routeValues)
                {
                    batch.Add((endpoint.Method, endpoint.Path(_ => value), Body(env, openApi, schema, yIds, n++), value, null, null));
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
                            var body = Body(env, openApi, schema, yIds, n++);
                            var controlValue = get ? Guid.TryParse(value, out _) ? Guid.NewGuid().ToString() : Scramble(value) : null;
                            batch.Add((endpoint.Method, Uri(value), body, value, controlValue is null ? null : Uri(controlValue), controlValue));
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
                            // A body that passes validation in the attacker's own company, with Y's text in one
                            // field: the write is judged, not refused for a malformed address.
                            var body = (JsonNode)G1WriteOracle.Valid(openApi, bodySchema, env, $"ct{n++}", x.ToString());
                            if (body is JsonObject fields && fields.ContainsKey(field))
                            {
                                fields[field] = value;
                            }
                            else
                            {
                                body = openApi.BuildBody(bodySchema, (leaf, type, format, leafName) =>
                                    leafName == field && type == "string" && format != "uuid" ? value : openApi.Conform(leaf, Leaf(type, format, yIds, n++)));
                            }
                            batch.Add((endpoint.Method, endpoint.Path(_ => ""), body, value, null, null));
                        }
                    }
                }

                if (get)
                {
                    // Reads change nothing: each answer, and its control for a value that exists
                    // nowhere, is judged as it comes.
                    await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests }, async (item, _) =>
                    {
                        var answer = await state.SendAsync(name, client, item.Method, item.Path, item.Body, [item.Value]);
                        if (item.ControlPath is { } controlPath)
                        {
                            var other = await state.SendAsync(name, client, "GET", controlPath, null, [item.ControlValue!]);
                            state.Compare(name, $"GET {item.Path}", answer, other, item.Value, item.ControlValue!);
                        }
                    });
                }
                else
                {
                    // Writes: the answers are judged once all of them are in, after the Y texts the
                    // successful ones stored in the attacker's own records count as the attacker's
                    // own (the writes run at the same time, so an answer may already show a text a
                    // concurrent write stored).
                    var answers = new (int Status, string Text)[batch.Count];
                    await Parallel.ForEachAsync(Enumerable.Range(0, batch.Count), new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests }, async (i, _) =>
                        answers[i] = await state.SendRawAsync(client, batch[i].Method, batch[i].Path, batch[i].Body));
                    for (var i = 0; i < batch.Count; i++)
                    {
                        state.RecordWrite(batch[i].Method, answers[i].Status, [batch[i].Value]);
                    }
                    for (var i = 0; i < batch.Count; i++)
                    {
                        state.Judge(name, batch[i].Method, batch[i].Path, answers[i].Status, answers[i].Text, [batch[i].Value]);
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
            attacked, state.Requests, before.Markers.Count, state.DifferentialChecks, writeOracleChecks);
    }

    /// <summary>Every other body passes validation (critic p02 round 2, plant C2: the attack's
    /// generated bodies all failed e-mail validation, so a create in company Y was never completed
    /// and a handler that widened the scope before writing passed): valid values for every field,
    /// company Y itself in companyId and Y's ids in the other id fields. The rest carry Y's ids in
    /// every id leaf, valid or not.</summary>
    private static JsonNode? Body(ErpTestEnvironment env, OpenApiDocument openApi, JsonElement? schema, List<string> yIds, int n)
    {
        if (schema is not { } s)
        {
            return null;
        }
        if (n % 2 == 0)
        {
            var valid = G1WriteOracle.Valid(openApi, s, env, $"cv{n}", yIds[0]);
            foreach (var (name, value) in valid.ToList())
            {
                if (name != "companyId" && value is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out _))
                {
                    valid[name] = yIds[(n / 2) % yIds.Count];
                }
            }
            valid["companyId"] = yIds[0];
            return valid;
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

    /// <summary>A value of the same shape (letters for letters, digits for digits, case kept)
    /// that exists nowhere.</summary>
    private static string Reshape(string value)
    {
        var random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(value.Length);
        var at = value.IndexOf('@');
        return new string(value.Select((c, i) => at >= 0 && i >= at ? c
            : char.IsAsciiLetterUpper(c) ? (char)('A' + random[i] % 26)
            : char.IsAsciiLetterLower(c) ? (char)('a' + random[i] % 26)
            : char.IsDigit(c) ? (char)('0' + random[i] % 10) : c).ToArray());
    }

    private static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";

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
        public int Requests => _requests;
        public int DifferentialChecks => _differentialChecks;

        public int StoredSkips { get; private set; }

        /// <summary>Y texts the attacker wrote into its own records by successful writes.</summary>
        public HashSet<string> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly Lock _lock = new();
        private int _requests;
        private int _differentialChecks;

        public async Task<(int Status, string Text)> SendAsync(string attacker, HttpClient client, string method, string path, JsonNode? body, IReadOnlyCollection<string> sent)
        {
            var (status, text) = await SendRawAsync(client, method, path, body);
            Judge(attacker, method, path, status, text, sent);
            RecordWrite(method, status, sent);
            return (status, text);
        }

        /// <summary>Send and read the answer, without judging it.</summary>
        public async Task<(int Status, string Text)> SendRawAsync(HttpClient client, string method, string path, JsonNode? body)
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
            Interlocked.Increment(ref _requests);
            return ((int)response.StatusCode, text);
        }

        /// <summary>A successful write: the values it carried are stored where the attacker may read them.</summary>
        public void RecordWrite(string method, int status, IReadOnlyCollection<string> sent)
        {
            if (method is "POST" or "PUT" or "PATCH" && status is >= 200 and < 300)
            {
                lock (_lock)
                {
                    foreach (var value in sent.Where(v => v.Length > 0 && !Guid.TryParse(v, out _)))
                    {
                        Stored.Add(value);
                    }
                }
            }
        }

        /// <summary>Judges one answer for company Y's markers and server errors.</summary>
        public void Judge(string attacker, string method, string path, int status, string text, IReadOnlyCollection<string> sent)
        {
            // An echo of what this request sent is not a leak, and neither is a Y text the attacker
            // itself stored earlier in a record it may keep (a user it created, named with Y's
            // branch name, is listed by the access list): those are the attacker's own data.
            string[] stored;
            lock (_lock)
            {
                stored = [.. Stored];
            }
            var scrubbed = sent.Concat(stored).Where(v => v.Length > 0).Distinct().OrderByDescending(v => v.Length)
                .Aggregate(text, (t, v) => t.Replace(v, "<sent>", StringComparison.OrdinalIgnoreCase).Replace(JsonSerializer.Serialize(v)[1..^1], "<sent>", StringComparison.OrdinalIgnoreCase));
            if (victim.FindMarker(scrubbed) is { } marker)
            {
                lock (_lock) Leaks.Add($"{attacker} → {method} {path} → {status}: contains company Y marker {marker}");
            }
            if (status >= 500)
            {
                lock (_lock) ServerErrors.Add($"{attacker} → {method} {path} → {status}: {text[..Math.Min(200, text.Length)]}");
            }
        }

        public void Compare(string attacker, string label, (int Status, string Text) answer, (int Status, string Text) control, string value, string controlValue)
        {
            bool stored;
            lock (_lock)
            {
                stored = Stored.Contains(value);
            }
            if (stored)
            {
                // The attacker itself stored this text in a record of its own by an earlier valid
                // write (a user it created, named with Y's branch name): a search finds that record,
                // which tells it nothing about company Y. Its answers are still judged for leaks.
                lock (_lock) StoredSkips++;
                return;
            }
            Interlocked.Increment(ref _differentialChecks);
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
                lock (_lock) Oracles.Add($"{attacker} → {label}: {answer.Status} {left[..Math.Min(160, left.Length)]} but for a value that exists nowhere {control.Status} {right[..Math.Min(160, right.Length)]}");
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

    /// <summary>Company Y's values by column name (snake case), in every company table.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Company Y's values in the column a request field (camel case) is stored in.</summary>
    public IReadOnlyList<string> ValuesOf(string field)
    {
        var column = string.Concat(field.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));
        return Columns.GetValueOrDefault(column) ?? [];
    }

    public IReadOnlyList<string> Markers => _markers ??= Ids.Select(i => i.ToString()).Concat(Strings.Where(s => s.Length >= 8)).ToList();

    private IReadOnlyList<string>? _markers;
    private MarkerSearch? _search;

    public string? FindMarker(string text) => (_search ??= new MarkerSearch(Markers)).Find(text);

    /// <param name="publicValues">Published API example values: they identify no company.</param>
    public static async Task<CompanySnapshot> TakeAsync(ErpTestEnvironment env, Guid tenant, Guid company, IReadOnlySet<string> publicValues)
    {
        await using var admin = await env.OpenAdminAsync();
        var ids = new List<Guid> { company };
        var strings = new List<string>();
        var checksums = new Dictionary<string, string>();
        var byColumn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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
                var values = await DbCatalog.ReadAsync(admin,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c AND \"{column.Name}\" IS NOT NULL AND length(\"{column.Name}\") >= 2",
                    r => r.GetString(0), ("t", tenant), ("c", company));
                byColumn[column.Name] = [.. byColumn.GetValueOrDefault(column.Name) ?? [], .. values];
                strings.AddRange(values.Where(v => v.Length >= 4));
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
        return new CompanySnapshot
        {
            Ids = ids.Distinct().ToList(), Strings = unique, Checksums = checksums,
            Columns = byColumn.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal),
        };
    }

    public static IReadOnlyList<string> Differences(CompanySnapshot before, CompanySnapshot after) =>
        before.Checksums.Keys.Union(after.Checksums.Keys)
            .Where(t => before.Checksums.GetValueOrDefault(t) != after.Checksums.GetValueOrDefault(t))
            .Order(StringComparer.Ordinal).ToList();
}
