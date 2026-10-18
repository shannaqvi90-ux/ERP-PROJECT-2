using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, a role held in one company acts on nothing of another (critic p02 round 11: the accountant,
/// "Company manager" of the Dubai LLC and read-only in the JAFZA entity, rewrote the JAFZA entity's
/// trade licence, changed its branch and opened a new branch in it, all while working in Dubai; the
/// session's permissions were the working company's, its company scope every company she works in,
/// and no endpoint checked the permission in the record's own company).
///
/// The attacker holds the Administrator role (every permission) in company X alone, and only the
/// staff permissions (switching the working company, the own profile) in the whole workspace. They
/// work in X and in Y, every branch of each, and work in X. Every endpoint whose permission they
/// hold only in X receives company Y's records by name: Y's row ids in every route parameter (and a
/// user of X and Y where the route names a user, so the request reaches its handler), Y's id in
/// every company field of every body (a valid body otherwise), Y's id in the companyId query and in
/// a companyId filter. No row of company Y may change, no answer may carry Y's ids or Y-only text, a
/// read by Y's id answers as a read by an id that exists nowhere, and no answer is a server error.
/// The same requests aimed at company X itself are accepted (the controls), so the refusals are
/// about the company and not a malformed request.
/// </summary>
public static class CompanyRoleRecords
{
    /// <summary>The permissions the attacker holds in the whole workspace (the seeded staff role's).</summary>
    public static readonly string[] Everywhere = ["tenancy.workplace.read", "tenancy.workplace.switch", "identity.profile.update"];

    public sealed record Report(
        IReadOnlyList<string> Changed,
        IReadOnlyList<string> Leaks,
        IReadOnlyList<string> Oracles,
        IReadOnlyList<string> ServerErrors,
        IReadOnlyList<string> Named,
        int Endpoints,
        int Requests);

    public sealed class Setup
    {
        public required HttpClient Admin { get; init; }
        public required HttpClient Attacker { get; init; }
        public required Guid X { get; init; }
        public required Guid Y { get; init; }
        public required Guid AttackerId { get; init; }

        /// <summary>A user who works in X and Y and holds nothing: the user routes' target.</summary>
        public required Guid Target { get; init; }
    }

    /// <summary>The attacker and the target user, made by the tenant's administrator.</summary>
    public static async Task<Setup> PrepareAsync(ErpTestEnvironment env, string tag)
    {
        var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var companies = await GateCompanies.OfAsync(admin);
        Assert.True(companies.Ids.Count == 2, "the gate fixture needs two companies in tenant A");
        var (x, y) = (companies.Ids[0], companies.Ids[1]);
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var staff = await CreatedAsync(admin, "/api/identity/roles", new { nameEn = $"Staff everywhere {tag}", nameAr = $"موظفون في كل مكان {tag}", permissions = Everywhere });
        var email = $"g2.xrecords.{tag}@{env.TenantA.EmailDomain}";
        var attacker = await CreatedAsync(admin, "/api/identity/users", new
        {
            email, displayName = $"Administrator of X {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { staff }, companyRoles = new[] { new { roleId = administrator, companyId = x } },
        });
        await companies.GiveAccessAsync(attacker);
        var target = await CreatedAsync(admin, "/api/identity/users", new
        {
            email = $"g2.xrecords.target.{tag}@{env.TenantA.EmailDomain}", displayName = $"Works in X and Y {tag}", language = "en",
            password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>(),
        });
        await companies.GiveAccessAsync(target);
        var client = await env.SignInAsync(email);
        await SwitchAsync(client, x);
        return new Setup { Admin = admin, Attacker = client, X = x, Y = y, AttackerId = attacker, Target = target };
    }

    public static async Task<Report> RunAsync(ErpTestEnvironment env, Setup setup, Func<ApiEndpoint, bool>? only = null)
    {
        var tenant = env.TenantA.Id;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => !e.IsAnonymous && !Everywhere.Contains(e.Permission) && !e.Pattern.Contains("{*", StringComparison.Ordinal) &&
                        e.Name is not ("auth.signIn" or "auth.signOut") && (only is null || only(e)))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var before = await CompanySnapshot.TakeAsync(env, tenant, setup.Y, openApi.ExampleValues());
        Assert.True(before.Ids.Count >= 3, "company Y has too few rows to attack");
        // Up to four rows of each company table of Y (its company row first), in a stable order.
        await using var db = await env.OpenAdminAsync();
        var yIds = new List<string> { setup.Y.ToString() };
        foreach (var table in await DbCatalog.CompanyTablesAsync(db))
        {
            yIds.AddRange((await DbCatalog.ReadAsync(db, $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t AND company_id = @c ORDER BY id LIMIT 4",
                r => r.GetGuid(0), ("t", tenant), ("c", setup.Y))).Select(i => i.ToString()));
        }
        yIds = yIds.Distinct().ToList();
        var leaks = new List<string>();
        var oracles = new List<string>();
        var errors = new List<string>();
        var requests = 0;
        var client = setup.Attacker;

        async Task<(int Status, string Text)> SendAsync(string method, string path, JsonNode? body)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await client.SendAsync(request);
            requests++;
            var text = await response.Content.ReadAsStringAsync();
            var status = (int)response.StatusCode;
            var label = $"{method} {path}";
            if (status >= 500)
            {
                errors.Add($"{label}: {status} {Short(text)}");
            }
            if (before.FindMarker(text) is { } marker)
            {
                leaks.Add($"{label}: answered {status} carrying company Y's '{marker}'");
            }
            return (status, text);
        }

        foreach (var endpoint in endpoints)
        {
            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            JsonNode? Body(string tag) => schema is { } s ? G1WriteOracle.Valid(openApi, s, env, tag, setup.Y.ToString()) : null;
            if (endpoint.RouteParameters.Count == 0)
            {
                await SendAsync(endpoint.Method, endpoint.Pattern, Body($"r{requests}"));
                if (endpoint.Method == "GET")
                {
                    await SendAsync("GET", $"{endpoint.Pattern}?companyId={setup.Y}", null);
                    await SendAsync("GET", $"{endpoint.Pattern}?filter={Uri.EscapeDataString($"companyId eq '{setup.Y}'")}", null);
                }
                continue;
            }
            // A route naming a user reaches its handler with a user of X and Y (the body names Y).
            var values = endpoint.RouteParameters.Any(p => p.Contains("user", StringComparison.OrdinalIgnoreCase)) || endpoint.Pattern.Contains("/users/{", StringComparison.Ordinal)
                ? [setup.Target.ToString(), .. yIds]
                : yIds;
            foreach (var value in values)
            {
                var (status, _) = await SendAsync(endpoint.Method, endpoint.Path(_ => value), Body($"r{requests}"));
                if (endpoint.Method == "GET" && value != setup.Target.ToString())
                {
                    var nowhere = Guid.NewGuid().ToString();
                    var (control, _) = await SendAsync("GET", endpoint.Path(_ => nowhere), null);
                    if (control != status)
                    {
                        oracles.Add($"GET {endpoint.Pattern}: company Y's {value} answered {status}, an id that exists nowhere answered {control}");
                    }
                }
            }
        }

        // The writes the critic made by hand, by name, with what each must answer.
        var named = new List<string>();
        var branchY = await DbCatalog.ScalarAsync<Guid>(db, "SELECT id FROM tenancy.branches WHERE tenant_id = @t AND company_id = @c ORDER BY id LIMIT 1", ("t", tenant), ("c", setup.Y));
        var branchX = await DbCatalog.ScalarAsync<Guid>(db, "SELECT id FROM tenancy.branches WHERE tenant_id = @t AND company_id = @c ORDER BY id LIMIT 1", ("t", tenant), ("c", setup.X));
        if (only is null)
        {
            // A record the named checks start from; one that cannot be read is itself a finding
            // (the attack above may have removed it).
            async Task<JsonObject?> ReadAsync(HttpClient reader, string path)
            {
                using var response = await reader.GetAsync(path);
                if (!response.IsSuccessStatusCode)
                {
                    named.Add($"GET {path} answered {(int)response.StatusCode} after the attack: {Short(await response.Content.ReadAsStringAsync())}");
                    return null;
                }
                return await response.Content.ReadFromJsonAsync<JsonObject>();
            }
            async Task ExpectAsync(string what, string method, string path, Func<JsonObject?, JsonObject?> body, HttpStatusCode expected, HttpClient? reader = null)
            {
                var read = method is "POST" or "GET" ? null : await ReadAsync(reader ?? setup.Admin, path.EndsWith("/logo", StringComparison.Ordinal) ? path[..^5] : path);
                var (status, text) = await SendAsync(method, path, body(read));
                if (status != (int)expected)
                {
                    named.Add($"{what}: {method} {path} answered {status} (expected {(int)expected}): {Short(text)}");
                }
            }
            JsonObject? Edited(JsonObject? record, string field, string value)
            {
                if (record is null) return null;
                record[field] = value;
                return record;
            }
            var stamp = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            await ExpectAsync("changing company Y's trade licence", "PUT", $"/api/tenancy/companies/{setup.Y}", r => Edited(r, "tradeLicenceNumber", $"X-{stamp}"), HttpStatusCode.Forbidden);
            await ExpectAsync("replacing company Y's logo", "PUT", $"/api/tenancy/companies/{setup.Y}/logo",
                _ => new JsonObject { ["contentType"] = "image/png", ["data"] = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==" }, HttpStatusCode.Forbidden);
            await ExpectAsync("changing a branch of company Y", "PUT", $"/api/tenancy/branches/{branchY}", r => Edited(r, "city", $"Side {stamp}"), HttpStatusCode.Forbidden);
            await ExpectAsync("opening a branch in company Y", "POST", "/api/tenancy/branches",
                _ => new JsonObject { ["companyId"] = setup.Y, ["code"] = $"S{stamp}", ["nameEn"] = $"Side {stamp}", ["nameAr"] = $"جانبي {stamp}", ["country"] = "AE", ["isActive"] = true },
                HttpStatusCode.Forbidden);
            var access = await ReadAsync(setup.Admin, $"/api/tenancy/access/{setup.Target}");
            await ExpectAsync("taking company Y from a user's access", "PUT", $"/api/tenancy/access/{setup.Target}",
                _ => new JsonObject
                {
                    ["companies"] = new JsonArray(new JsonObject { ["companyId"] = setup.X, ["allBranches"] = true, ["branchIds"] = new JsonArray() }),
                    ["version"] = access?["version"]?.DeepClone(),
                }, HttpStatusCode.Forbidden);
            // The controls: the same writes in company X are accepted.
            await ExpectAsync("changing company X's trade licence (control)", "PUT", $"/api/tenancy/companies/{setup.X}", r => Edited(r, "tradeLicenceNumber", $"X-{stamp}"), HttpStatusCode.OK, client);
            await ExpectAsync("changing a branch of company X (control)", "PUT", $"/api/tenancy/branches/{branchX}", r => Edited(r, "city", $"Side {stamp}"), HttpStatusCode.OK, client);
            await ExpectAsync("opening a branch in company X (control)", "POST", "/api/tenancy/branches",
                _ => new JsonObject { ["companyId"] = setup.X, ["code"] = $"S{stamp}", ["nameEn"] = $"Side {stamp}", ["nameAr"] = $"جانبي {stamp}", ["country"] = "AE", ["isActive"] = true },
                HttpStatusCode.Created);
            // Reads of company Y answer as for a company that does not exist; the list shows X alone.
            await ExpectAsync("reading company Y", "GET", $"/api/tenancy/companies/{setup.Y}", _ => null, HttpStatusCode.NotFound, client);
            var list = await ReadAsync(client, "/api/tenancy/companies");
            var shown = (list?["items"] as JsonArray ?? []).Select(i => i?["id"]?.GetValue<Guid>()).ToList();
            if (shown.Count != 1 || shown[0] != setup.X)
            {
                named.Add($"the companies list of an administrator of X alone shows {string.Join(", ", shown)} (expected company X alone)");
            }
        }

        var after = await CompanySnapshot.TakeAsync(env, tenant, setup.Y, openApi.ExampleValues());
        return new Report(CompanySnapshot.Differences(before, after), leaks, oracles, errors, named, endpoints.Count, requests);
    }

    public static async Task SwitchAsync(HttpClient client, Guid company)
    {
        using var response = await client.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = company });
        Assert.True(response.IsSuccessStatusCode, $"switching to {company} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<Guid> CreatedAsync(HttpClient admin, string path, object body)
    {
        using var response = await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} as administrator answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static string Short(string text) => text.Length <= 240 ? text : text[..240] + "…";
}
