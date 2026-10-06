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
        // Company Y's own code reaches the company create (critic p02 round 3, plant C4), and every
        // identifying column of every company table reaches the creates whose body carries it.
        Assert.Contains("POST /api/tenancy/companies [code] <- tenancy.companies", report.WriteOracleSources);
        Assert.Contains("POST /api/tenancy/branches [code] <- tenancy.branches", report.WriteOracleSources);
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
    int WriteOracleChecks = 0)
{
    /// <summary>Every write the in-tenant write oracle sent company Y's values to, with the field
    /// and the table the value came from: "POST /api/tenancy/companies [code] &lt;- tenancy.companies".</summary>
    public IReadOnlyList<string> WriteOracleSources { get; init; } = [];
}

/// <summary>The company attack, reusable by the gate self-tests.</summary>
public static class CompanyAttack
{
    private static readonly string[] GuessedQueryNames = ["companyId", "branchId", "company", "branch", "id", "userId", "search", "q"];

    /// <summary>Which wall the attack tests: company Y against an administrator of company X, or
    /// one branch of company X against an administrator limited to another branch of it.</summary>
    public enum Layer { Company, Branch }

    public static Task<CompanyAttackReport> RunAsync(ErpTestEnvironment env) => RunAsync(env, Layer.Company);

    /// <summary>
    /// <paramref name="layer"/> Company: an administrator who may work only in company X (and the
    /// read-only user of company X) attack company Y. Branch (critic p02 round 3, plant C3: with
    /// tenancy's branch filter switched off, an administrator limited to one branch listed and
    /// renamed the company's other branches and every gate passed): an administrator limited to the
    /// first branch of company X (and the read-only user, limited to every branch of X but its last)
    /// attack a branch Z of company X that neither may work in: its row and every row of every tenant table that
    /// carries its <c>branch_id</c>.
    /// </summary>
    public static async Task<CompanyAttackReport> RunAsync(ErpTestEnvironment env, Layer layer)
    {
        var tenant = env.TenantA;
        var branchLayer = layer == Layer.Branch;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Name is not ("auth.signIn" or "auth.signOut") && !e.Pattern.Contains("{*", StringComparison.Ordinal))
            .ToList();

        await using var admin = await env.OpenAdminAsync();
        var companies = await DbCatalog.ReadAsync(admin, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenant.Id));
        Assert.True(companies.Count >= 2, "tenant A needs two companies");
        var (x, y) = (companies[0], companies[1]);
        var xBranches = await DbCatalog.ReadAsync(admin, "SELECT id FROM tenancy.branches WHERE tenant_id = @t AND company_id = @c ORDER BY id",
            r => r.GetGuid(0), ("t", tenant.Id), ("c", x));
        Assert.True(!branchLayer || xBranches.Count >= 2, "company X needs two branches for the branch attack");
        var ownBranch = xBranches.FirstOrDefault();
        // Branch Z: the first branch of company X (in the order they were opened) that the seeded
        // read-only user may not work in, so both attackers lack it (not a branch an earlier
        // attack in the same environment created).
        var viewerBranches = await DbCatalog.ReadAsync(admin,
            "SELECT b.branch_id FROM tenancy.user_branch_access b JOIN identity.users u ON u.id = b.user_id AND u.tenant_id = b.tenant_id " +
            " WHERE b.tenant_id = @t AND b.company_id = @c AND u.email_normalized = @e", r => r.GetGuid(0),
            ("t", tenant.Id), ("c", x), ("e", env.Email(tenant, "viewer").ToLowerInvariant()));
        var z = xBranches.Skip(1).FirstOrDefault(b => !viewerBranches.Contains(b));
        Assert.True(!branchLayer || z != Guid.Empty, "company X needs a branch (other than its first) that its read-only user may not work in");

        // An administrator (every permission) who may work only in company X, or only in its first branch.
        using var tenantAdmin = await env.SignInAsync(env.Email(tenant, "admin"));
        var administratorRole = (await tenantAdmin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var email = $"{(branchLayer ? "branch" : "company")}.x.{Guid.NewGuid():N}@{tenant.EmailDomain}";
        var created = await tenantAdmin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = branchLayer ? "Branch-limited administrator" : "Company X administrator", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { administratorRole } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var scopedUser = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var granted = await tenantAdmin.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
            new { companies = new[] { new { companyId = x, allBranches = !branchLayer, branchIds = branchLayer ? new[] { ownBranch } : Array.Empty<Guid>() } }, version = await AccessVersionAsync(tenantAdmin, scopedUser) });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        if (branchLayer)
        {
            // The read-only user must lack branch Z too, or it would attack its own data.
            var viewerHoldsZ = await DbCatalog.ScalarAsync<bool>(admin,
                "SELECT EXISTS (SELECT 1 FROM tenancy.user_company_access a JOIN identity.users u ON u.id = a.user_id AND u.tenant_id = a.tenant_id " +
                " WHERE a.tenant_id = @t AND a.company_id = @c AND u.email_normalized = @e AND (a.all_branches OR EXISTS (SELECT 1 FROM tenancy.user_branch_access b " +
                "   WHERE b.tenant_id = a.tenant_id AND b.user_id = a.user_id AND b.branch_id = @z)))",
                ("t", tenant.Id), ("c", x), ("e", env.Email(tenant, "viewer").ToLowerInvariant()), ("z", z));
            Assert.False(viewerHoldsZ, "the read-only user of company X must be limited to branches other than branch Z");
        }

        var examples = openApi.ExampleValues();
        var before = branchLayer ? await CompanySnapshot.TakeBranchAsync(env, tenant.Id, z, examples) : await CompanySnapshot.TakeAsync(env, tenant.Id, y, examples);
        Assert.True(before.Ids.Count >= (branchLayer ? 1 : 5), $"the victim {(branchLayer ? "branch" : "company")} has too few rows to attack");
        Assert.True(!branchLayer || before.Strings.Count >= 2, "branch Z has too few values of its own to recognise (code, names)");
        var label = branchLayer ? "branch-limited administrator" : "company X administrator";
        var victimName = branchLayer ? "branch Z" : "company Y";
        var viewerLabel = branchLayer ? "branch-limited read-only user" : "company X read-only user";
        var attackers = new List<(string Name, HttpClient Client)>
        {
            (label, await env.SignInAsync(email)),
            (viewerLabel, await env.SignInAsync(env.Email(tenant, "viewer"))),
        };
        var state = new State(before, victimName);
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
        var writeOracleSources = new SortedSet<string>(StringComparer.Ordinal);
        // Every write with an identifying field receives up to three of the victim's values from
        // every company table with that column (critic p02 round 3, plant C4: the first three
        // values found were all branch codes, so company Y's own code never reached the company
        // create and its 409 went unseen). A value goes first to the writes of its own collection
        // (company Y's code to the company create before a branch create could store it in company
        // X), and only while no row of the tenant but the victim's holds it, checked in the
        // database right before sending, so a refusal can only come from the victim.
        var work = new List<(ApiEndpoint Endpoint, string? Collection, JsonElement Schema, string Field, string Table, string Value)>();
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
                foreach (var (table, values) in before.ValuesByTable(field))
                {
                    work.AddRange(values.Where(before.Strings.Contains).Select(v => (endpoint, collection, schema, field, table, v)));
                }
            }
        }
        static bool Home(ApiEndpoint endpoint, string? collection, string table) =>
            (collection ?? endpoint.Pattern).TrimEnd('/').Split('/')[^1] == table.Split('.')[^1];
        var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
        var sentTo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (endpoint, collection, schema, field, table, held) in work.OrderBy(w => Home(w.Endpoint, w.Collection, w.Table) ? 0 : 1))
        {
            var source = $"{endpoint} [{field}] <- {table}";
            if (perSource.GetValueOrDefault(source) >= 3 || sentTo.Contains($"{endpoint} [{field}] {held}") ||
                !await before.OnlyVictimHoldsAsync(env, tenant.Id, held))
            {
                continue;
            }
            perSource[source] = perSource.GetValueOrDefault(source) + 1;
            sentTo.Add($"{endpoint} [{field}] {held}");
            writeOracleSources.Add(source);
            var tag = $"wo{writeOracleChecks}{Guid.NewGuid():N}"[..12];
            var fresh = Reshape(held);
            var (withY, withYText) = await G1WriteOracle.SendAsync(oracleClient, openApi, endpoint, collection, schema, env, $"{tag}y", field, held);
            var (withFresh, withFreshText) = await G1WriteOracle.SendAsync(oracleClient, openApi, endpoint, collection, schema, env, $"{tag}f", field, fresh);
            writeOracleChecks++;
            if (withY is >= 200 and < 300)
            {
                // Now the attacker's own record holds the value: later writes skip it (the
                // database check above), and answers showing it are the attacker's own data.
                state.Stored.Add(held);
            }
            if (withY != withFresh)
            {
                state.Oracles.Add($"{label} → {endpoint} [{field}]: {victimName}'s value answered {withY}, a value that exists nowhere answered {withFresh} ({Short(withYText)} / {Short(withFreshText)})");
            }
        }
        // The same with the attacker's own record: changing company X's code (or the code of the
        // attacker's own branch) to the victim's code must answer as a code that exists nowhere does.
        var ownPath = branchLayer ? $"/api/tenancy/branches/{ownBranch}" : $"/api/tenancy/companies/{x}";
        var ownRecord = await oracleClient.GetFromJsonAsync<JsonObject>(ownPath);
        var victimRecord = await tenantAdmin.GetFromJsonAsync<JsonObject>(branchLayer ? $"/api/tenancy/branches/{z}" : $"/api/tenancy/companies/{y}");
        if (ownRecord is not null && victimRecord?["code"]?.GetValue<string>() is { } yCode)
        {
            var original = ownRecord["code"]?.GetValue<string>();
            async Task<int> ChangeCode(string code)
            {
                var current = await oracleClient.GetFromJsonAsync<JsonObject>(ownPath) ?? [];
                current["code"] = code;
                using var answer = await oracleClient.PutAsJsonAsync(ownPath, current);
                return (int)answer.StatusCode;
            }
            var toY = await ChangeCode(yCode);
            var toFresh = await ChangeCode(Reshape(yCode));
            writeOracleChecks++;
            writeOracleSources.Add($"PUT {(branchLayer ? "/api/tenancy/branches/{id:guid}" : "/api/tenancy/companies/{id:guid}")} [code] <- own record");
            if (toY != toFresh)
            {
                state.Oracles.Add($"{label} → PUT {ownPath} [code]: {victimName}'s code answered {toY}, a code that exists nowhere answered {toFresh}");
            }
            if (original is not null && toFresh is >= 200 and < 300)
            {
                await ChangeCode(original);
            }
        }
        else
        {
            state.Oracles.Add($"{label}: could not read its own record {ownPath} or the victim's code (the own-record code oracle would be blind)");
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
                    batch.Add((endpoint.Method, endpoint.Path(_ => value), Body(env, openApi, schema, yIds, n++, branchLayer ? x : null), value, null, null));
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
                            var body = Body(env, openApi, schema, yIds, n++, branchLayer ? x : null);
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

        // Direct escalations: give someone access to Y (or branch Z), switch to it, open it.
        // Each grant with the version just read: the refusal must be about the victim, not a stale version.
        var escalations = new List<string>();
        var scopedClient = attackers[0].Client;
        var viewerId = (await tenantAdmin.GetFromJsonAsync<JsonElement>($"/api/identity/users?search={Uri.EscapeDataString(env.Email(tenant, "viewer"))}"))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        if (branchLayer)
        {
            var grantZ = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{viewerId}",
                new { companies = new object[] { new { companyId = x, allBranches = false, branchIds = new[] { ownBranch, z } } }, version = await AccessVersionAsync(scopedClient, viewerId) });
            var grantZText = await grantZ.Content.ReadAsStringAsync();
            if (grantZ.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"giving branch Z answered {(int)grantZ.StatusCode}, expected 400: {Short(grantZText)}");
            else if (!grantZText.Contains("tenancyAccessBranchOfOtherCompany", StringComparison.Ordinal)) escalations.Add($"giving branch Z was refused for another reason than branch Z: {Short(grantZText)}");
            var grantSelfZ = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
                new { companies = new object[] { new { companyId = x, allBranches = false, branchIds = new[] { ownBranch, z } } }, version = await AccessVersionAsync(scopedClient, scopedUser) });
            if (grantSelfZ.IsSuccessStatusCode) escalations.Add($"giving itself branch Z answered {(int)grantSelfZ.StatusCode}");
            var grantAll = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
                new { companies = new object[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } }, version = await AccessVersionAsync(scopedClient, scopedUser) });
            if (grantAll.IsSuccessStatusCode) escalations.Add($"giving itself every branch of company X answered {(int)grantAll.StatusCode}");
            var switchZ = await scopedClient.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = x, branchId = (Guid?)z });
            if (switchZ.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"switching to branch Z answered {(int)switchZ.StatusCode}, expected 400");
            var openZ = await scopedClient.GetAsync($"/api/tenancy/branches/{z}");
            if (openZ.StatusCode != HttpStatusCode.NotFound) escalations.Add($"opening branch Z answered {(int)openZ.StatusCode}, expected 404");
            var workplaceZ = await scopedClient.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
            if (workplaceZ.GetProperty("companies").EnumerateArray().SelectMany(c => c.GetProperty("branches").EnumerateArray()).Any(b => b.GetProperty("id").GetGuid() == z))
                escalations.Add("the workplace switcher offers branch Z");
        }
        else
        {
            var grantY = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{viewerId}",
                new { companies = new object[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() }, new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } }, version = await AccessVersionAsync(scopedClient, viewerId) });
            if (grantY.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"granting company Y answered {(int)grantY.StatusCode}, expected 400");
            else if (!(await grantY.Content.ReadAsStringAsync()).Contains("unknownIds", StringComparison.Ordinal)) escalations.Add($"granting company Y was refused for another reason than company Y: {await grantY.Content.ReadAsStringAsync()}");
            var grantSelf = await scopedClient.PutAsJsonAsync($"/api/tenancy/access/{scopedUser}",
                new { companies = new[] { new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } }, version = await AccessVersionAsync(scopedClient, scopedUser) });
            if (grantSelf.IsSuccessStatusCode) escalations.Add($"granting itself company Y answered {(int)grantSelf.StatusCode}");
            var switchY = await scopedClient.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = y, branchId = (Guid?)null });
            if (switchY.StatusCode != HttpStatusCode.BadRequest) escalations.Add($"switching to company Y answered {(int)switchY.StatusCode}, expected 400");
            var openY = await scopedClient.GetAsync($"/api/tenancy/companies/{y}");
            if (openY.StatusCode != HttpStatusCode.NotFound) escalations.Add($"opening company Y answered {(int)openY.StatusCode}, expected 404");
            var workplace = await scopedClient.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
            if (workplace.GetProperty("companies").EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == y))
                escalations.Add("the workplace switcher offers company Y");
        }
        var session = await scopedClient.GetFromJsonAsync<JsonElement>("/api/auth/session");
        if (session.GetProperty("permissions").GetArrayLength() == 0) escalations.Add($"the {label} holds no permissions (the attack would be blind)");

        var after = branchLayer ? await CompanySnapshot.TakeBranchAsync(env, tenant.Id, z, examples) : await CompanySnapshot.TakeAsync(env, tenant.Id, y, examples);
        foreach (var (_, client) in attackers)
        {
            client.Dispose();
        }
        return new CompanyAttackReport(state.Leaks, state.Oracles, state.ServerErrors, CompanySnapshot.Differences(before, after), escalations,
            attacked, state.Requests, before.Markers.Count, state.DifferentialChecks, writeOracleChecks)
        {
            WriteOracleSources = [.. writeOracleSources],
        };
    }

    /// <summary>The version of a user's company access as <paramref name="client"/> reads it (0 when unreadable).</summary>
    internal static async Task<uint> AccessVersionAsync(HttpClient client, Guid userId)
    {
        using var response = await client.GetAsync($"/api/tenancy/access/{userId}");
        return response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("version", out var v) ? v.GetUInt32() : 0;
    }

    /// <summary>Every other body passes validation (critic p02 round 2, plant C2: the attack's
    /// generated bodies all failed e-mail validation, so a create in company Y was never completed
    /// and a handler that widened the scope before writing passed): valid values for every field,
    /// company Y itself in companyId and Y's ids in the other id fields. The rest carry Y's ids in
    /// every id leaf, valid or not. In the branch attack (<paramref name="ownCompany"/> set) the
    /// victim is branch Z of the attacker's own company: companyId is that company and branchId is
    /// branch Z.</summary>
    private static JsonNode? Body(ErpTestEnvironment env, OpenApiDocument openApi, JsonElement? schema, List<string> yIds, int n, Guid? ownCompany = null)
    {
        if (schema is not { } s)
        {
            return null;
        }
        if (n % 2 == 0)
        {
            var valid = G1WriteOracle.Valid(openApi, s, env, $"cv{n}", ownCompany?.ToString() ?? yIds[0]);
            foreach (var (name, value) in valid.ToList())
            {
                if (name != "companyId" && value is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out _))
                {
                    valid[name] = yIds[(n / 2) % yIds.Count];
                }
            }
            valid["companyId"] = ownCompany?.ToString() ?? yIds[0];
            if (ownCompany is not null)
            {
                valid["branchId"] = yIds[0];
            }
            return valid;
        }
        var body = openApi.BuildBody(s, (leaf, type, format, _) => openApi.Conform(leaf, Leaf(type, format, yIds, n))) as JsonObject ?? [];
        body["companyId"] = ownCompany?.ToString() ?? yIds[n % yIds.Count];
        if (ownCompany is not null)
        {
            body["branchId"] = yIds[n % yIds.Count];
        }
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

    private sealed class State(CompanySnapshot victim, string victimName)
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
            // Images as their bytes, printed documents and exports as their decoded text.
            var text = await ResponseText.ReadAsync(response);
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

        /// <summary>Judges one answer for the victim's (company Y's or branch Z's) markers and server errors.</summary>
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
                lock (_lock) Leaks.Add($"{attacker} → {method} {path} → {status}: contains {victimName} marker {marker}");
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

    /// <summary>Company Y's values by table, then by column name (snake case).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> TableColumns { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>();

    private static string ColumnOf(string field) =>
        string.Concat(field.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString()));

    /// <summary>Company Y's values in the column a request field (camel case) is stored in.</summary>
    public IReadOnlyList<string> ValuesOf(string field) => Columns.GetValueOrDefault(ColumnOf(field)) ?? [];

    /// <summary>Company Y's values in that column, table by table (in table order).</summary>
    public IReadOnlyList<(string Table, IReadOnlyList<string> Values)> ValuesByTable(string field)
    {
        var column = ColumnOf(field);
        return TableColumns.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Where(t => t.Value.ContainsKey(column))
            .Select(t => (t.Key, t.Value[column]))
            .ToList();
    }

    public IReadOnlyList<string> Markers => _markers ??= Ids.Select(i => i.ToString()).Concat(Strings.Where(s => s.Length >= 8)).ToList();

    /// <summary>The victim's id and, per table, the condition that picks the victim's rows in it
    /// (<c>@v</c> is the victim's id).</summary>
    public Guid VictimId { get; init; }

    public IReadOnlyDictionary<string, string> VictimRows { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The text a row holds for the uniqueness checks: every column of a textual type (text, codes,
    /// names, JSON, text arrays), lower-cased, never ids, numbers or timestamps. Comparing the whole
    /// row's text made the checks depend on chance: a short value of company Y such as its PO box
    /// "20002" appears in another row's random id or in a timestamp's microseconds in some runs and
    /// not others, so the attack's values (and the ratchet's request count) changed from run to run.
    /// Null when the table has no textual column.
    /// </summary>
    internal static string? TextOfRow(IEnumerable<ColumnInfo> columns)
    {
        static bool Textual(string type)
        {
            var baseType = type.EndsWith("[]", StringComparison.Ordinal) ? type[..^2] : type;
            return baseType.StartsWith("character", StringComparison.Ordinal) || baseType is "text" or "json" or "jsonb" or "citext" or "name";
        }
        var parts = columns.Where(c => Textual(c.Type)).Select(c => $"lower(t.\"{c.Name}\"::text)").ToList();
        return parts.Count == 0 ? null : $"concat_ws(chr(31), {string.Join(", ", parts)})";
    }

    /// <summary>True when no row of the tenant outside the victim's rows (and the audit trail)
    /// holds <paramref name="value"/> in a textual column now, read with the superuser.</summary>
    public async Task<bool> OnlyVictimHoldsAsync(ErpTestEnvironment env, Guid tenant, string value)
    {
        await using var admin = await env.OpenAdminAsync();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            var own = VictimRows.GetValueOrDefault(table.Qualified);
            if (TextOfRow(await DbCatalog.ColumnsAsync(admin, table)) is not { } text)
            {
                continue;
            }
            if (await DbCatalog.ScalarAsync<bool>(admin,
                    $"SELECT EXISTS (SELECT 1 FROM {table.Qualified} t WHERE tenant_id = @t" + (own is null ? "" : $" AND NOT coalesce(({own}), false)") +
                    $" AND strpos({text}, lower(@value)) > 0)",
                    ("t", tenant), ("v", VictimId), ("value", value)))
            {
                return false;
            }
        }
        return true;
    }

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
        var byTable = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
        var tables = await DbCatalog.CompanyTablesAsync(admin);
        foreach (var table in tables)
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            ids.AddRange(await DbCatalog.ReadAsync(admin, $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c ORDER BY id",
                r => r.GetGuid(0), ("t", tenant), ("c", company)));
            checksums[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE tenant_id = @t AND company_id = @c",
                ("t", tenant), ("c", company));
            var tableColumns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var column in columns.Where(c => c.Type.StartsWith("character varying", StringComparison.Ordinal) || c.Type == "text"))
            {
                var values = await DbCatalog.ReadAsync(admin,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c AND \"{column.Name}\" IS NOT NULL AND length(\"{column.Name}\") >= 2",
                    r => r.GetString(0), ("t", tenant), ("c", company));
                byColumn[column.Name] = [.. byColumn.GetValueOrDefault(column.Name) ?? [], .. values];
                tableColumns[column.Name] = values;
                strings.AddRange(values.Where(v => v.Length >= 4));
            }
            byTable[table.Qualified] = tableColumns;
        }
        // Only text no other row of the tenant holds (outside the audit trail) identifies company Y.
        var others = new List<string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            var isCompanyTable = tables.Contains(table);
            if (TextOfRow(await DbCatalog.ColumnsAsync(admin, table)) is not { } text)
            {
                continue;
            }
            others.AddRange(await DbCatalog.ReadAsync(admin,
                $"SELECT {text} FROM {table.Qualified} t WHERE tenant_id = @t" + (isCompanyTable ? " AND company_id <> @c" : ""),
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
            TableColumns = byTable,
            VictimId = company,
            VictimRows = tables.ToDictionary(t => t.Qualified, _ => "company_id = @v"),
        };
    }

    /// <summary>Everything that identifies one branch's data, read with the superuser: the branch's
    /// own row and the rows of every tenant table that carry its <c>branch_id</c> (whatever module
    /// adds one), their ids, their texts that appear in no other row of the tenant, and a checksum
    /// of them.</summary>
    public static async Task<CompanySnapshot> TakeBranchAsync(ErpTestEnvironment env, Guid tenant, Guid branch, IReadOnlySet<string> publicValues)
    {
        await using var admin = await env.OpenAdminAsync();
        var ids = new List<Guid> { branch };
        var strings = new List<string>();
        var checksums = new Dictionary<string, string>();
        var byColumn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var byTable = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
        // (table, the condition that picks the branch's rows)
        var sources = new List<(TableRef Table, string Where)> { (new TableRef("tenancy", "branches"), "id = @b") };
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            if ((await DbCatalog.ColumnsAsync(admin, table)).Any(c => c.Name == "branch_id"))
            {
                sources.Add((table, "branch_id = @b"));
            }
        }
        foreach (var (table, where) in sources)
        {
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            ids.AddRange(await DbCatalog.ReadAsync(admin, $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t AND {where} ORDER BY id",
                r => r.GetGuid(0), ("t", tenant), ("b", branch)));
            checksums[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE tenant_id = @t AND {where}",
                ("t", tenant), ("b", branch));
            var tableColumns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var column in columns.Where(c => c.Type.StartsWith("character varying", StringComparison.Ordinal) || c.Type == "text"))
            {
                var values = await DbCatalog.ReadAsync(admin,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND {where} AND \"{column.Name}\" IS NOT NULL AND length(\"{column.Name}\") >= 2",
                    r => r.GetString(0), ("t", tenant), ("b", branch));
                byColumn[column.Name] = [.. byColumn.GetValueOrDefault(column.Name) ?? [], .. values];
                tableColumns[column.Name] = values;
                strings.AddRange(values.Where(v => v.Length >= 4));
            }
            byTable[table.Qualified] = tableColumns;
        }
        // Only text no other row of the tenant holds (outside the audit trail) identifies the branch.
        var others = new List<string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            var own = sources.Where(x => x.Table == table).Select(x => x.Where).FirstOrDefault();
            if (TextOfRow(await DbCatalog.ColumnsAsync(admin, table)) is not { } text)
            {
                continue;
            }
            others.AddRange(await DbCatalog.ReadAsync(admin,
                $"SELECT {text} FROM {table.Qualified} t WHERE tenant_id = @t" + (own is null ? "" : $" AND NOT coalesce(({own}), false)"),
                r => r.GetString(0), ("t", tenant), ("b", branch)));
        }
        var unique = strings.Distinct(StringComparer.Ordinal)
            .Where(s => !publicValues.Contains(s.Trim()))
            .Where(s => !others.Any(o => o.Contains(s.ToLowerInvariant(), StringComparison.Ordinal)))
            .ToList();
        return new CompanySnapshot
        {
            Ids = ids.Distinct().ToList(), Strings = unique, Checksums = checksums,
            Columns = byColumn.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal),
            TableColumns = byTable,
            VictimId = branch,
            VictimRows = sources.ToDictionary(x => x.Table.Qualified, x => x.Where.Replace("@b", "@v", StringComparison.Ordinal)),
        };
    }

    public static IReadOnlyList<string> Differences(CompanySnapshot before, CompanySnapshot after) =>
        before.Checksums.Keys.Union(after.Checksums.Keys)
            .Where(t => before.Checksums.GetValueOrDefault(t) != after.Checksums.GetValueOrDefault(t))
            .Order(StringComparer.Ordinal).ToList();
}
