using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G2;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, existence oracles on writes. A write that refuses a value because another tenant uses it
/// tells the caller that the value exists over there: creating a user answers 409 for an address
/// tenant B uses and 201 for an unused one. A platform-wide unique index is one way to build that
/// (caught by <see cref="G1UniqueIndexTests"/>); a registry kept outside the tenant's rows is
/// another (critic p03 round 2, plant L4: a disk file of every address created, which the HTTP
/// attack missed and only the sign-in throttle test met by chance).
///
/// Every non-anonymous POST, PUT and PATCH whose request schema has an identifying text field
/// (an e-mail, a code) is found from routing and the OpenAPI document. Tenant B first uses a value
/// through that same endpoint (creates with it, or edits a record to it). Tenant A then sends the
/// same request twice: once with B's value and once with a value of the same shape that exists
/// nowhere. The two answers must have the same status. The same is done with a value B holds from
/// its seed data (its administrator's address), which no endpoint wrote.
///
/// Names are judged the same way (critic p03 round 3, plant L7: a role-name registry kept outside
/// the tenant's rows answered 409 for a name another workspace used, and every gate passed):
/// every text field whose name says it is a name (<c>nameEn</c>, <c>nameAr</c>,
/// <c>displayName</c>, <c>legalNameEn</c>, …) is used by tenant B through the endpoint and sent by
/// tenant A beside a name that exists nowhere. Seeded names are not sent: both tenants' seeds share
/// names such as "Administrator", which a workspace may rightly refuse as its own.
/// </summary>
public static class G1WriteOracle
{
    public sealed record Result(IReadOnlyList<string> Problems, int Checks, IReadOnlyList<string> Endpoints);

    public static bool IsIdentifying(string name, string? format)
    {
        if (format is "uuid" or "date" or "date-time")
        {
            return false;
        }
        var lower = name.ToLowerInvariant();
        return lower.Contains("email") || lower == "code" || lower.EndsWith("code", StringComparison.Ordinal) && !lower.Contains("password");
    }

    /// <summary>A name field: unique within a workspace at most, never across workspaces.</summary>
    public static bool IsName(string name, string? format) =>
        format is not ("uuid" or "date" or "date-time") && name.Contains("name", StringComparison.OrdinalIgnoreCase);

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        var problems = new List<string>();
        var endpointsChecked = new List<string>();
        var checks = 0;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        using var a = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var b = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        // A record that belongs to a company (a branch) is created in the caller's working company.
        WorkingCompany.AddOrUpdate(a, await WorkingCompanyAsync(a));
        WorkingCompany.AddOrUpdate(b, await WorkingCompanyAsync(b));

        // Values tenant B holds from its seed data, each sent by tenant A at most once (tenant A's
        // own earlier write of an address would make its own workspace refuse it the next time).
        List<string> seedEmails;
        await using (var owner = await env.OpenAdminAsync())
        {
            seedEmails = await DbCatalog.ReadAsync(owner, "SELECT email FROM identity.users WHERE tenant_id = @t ORDER BY email LIMIT 200",
                r => r.GetString(0), ("t", env.TenantB.Id));
        }
        var sentByA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? SeedValue(string field)
        {
            var candidates = field.Contains("email", StringComparison.OrdinalIgnoreCase) ? seedEmails : [env.TenantB.Code];
            var value = candidates.FirstOrDefault(c => !sentByA.Contains(c));
            if (value is not null)
            {
                sentByA.Add(value);
            }
            return value;
        }

        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Method is "POST" or "PUT" or "PATCH" && !e.IsAnonymous)
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var n = 0;
        foreach (var endpoint in endpoints)
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema ||
                !openApi.Resolve(schema).TryGetProperty("properties", out var properties))
            {
                continue;
            }
            var fields = properties.EnumerateObject()
                .Where(p => openApi.TypeOfSchema(p.Value) == "string" &&
                            (IsIdentifying(p.Name, openApi.Resolve(p.Value).TryGetProperty("format", out var f) ? f.GetString() : null) ||
                             (IsName(p.Name, openApi.Resolve(p.Value).TryGetProperty("format", out var g) ? g.GetString() : null) &&
                              !openApi.Resolve(p.Value).TryGetProperty("enum", out _))))
                .Select(p => p.Name)
                .ToList();
            if (fields.Count == 0)
            {
                continue;
            }
            var collection = endpoint.RouteParameters.Count == 0 ? null : endpoint.Pattern[..endpoint.Pattern.LastIndexOf("/{", StringComparison.Ordinal)];
            if (collection is not null && openApi.RequestSchema("POST", collection) is null)
            {
                problems.Add($"{endpoint}: no POST {collection} to create a record to send it to; extend the write-oracle gate for this endpoint");
                continue;
            }
            foreach (var field in fields)
            {
                n++;
                var tag = $"{n}{Guid.NewGuid():N}"[..12];
                var used = ValueLike(field, $"oracle.{tag}", env.TenantB);
                var fresh = ValueLike(field, $"oracle.{tag}x", env.TenantB);

                // Tenant B uses the value through this very endpoint.
                var (victimStatus, victimText) = await SendAsync(b, openApi, endpoint, collection, schema, env, $"{tag}b", field, used);
                if (victimStatus is < 200 or >= 300)
                {
                    problems.Add($"{endpoint} [{field}]: tenant B could not use the value through the endpoint ({victimStatus}), so the gate cannot judge it: {Short(victimText)}");
                    continue;
                }
                var pairs = new List<(string Label, string Value)> { ("written by tenant B through this endpoint", used) };
                if (!IsName(field, null) && SeedValue(field) is { } seeded)
                {
                    pairs.Add(("from tenant B's seed data", seeded));
                }
                foreach (var (label, value) in pairs)
                {
                    var (withB, withBText) = await SendAsync(a, openApi, endpoint, collection, schema, env, $"{tag}u{checks}", field, value);
                    var (withFresh, withFreshText) = await SendAsync(a, openApi, endpoint, collection, schema, env, $"{tag}f{checks}", field, label.StartsWith("from", StringComparison.Ordinal) ? ValueLike(field, $"oracle.{tag}s", env.TenantB) : fresh);
                    checks++;
                    if (withB != withFresh)
                    {
                        problems.Add($"{endpoint} [{field}]: tenant A sending a value {label} answered {withB}, a value that exists nowhere answered {withFresh}: the answer tells tenant A what tenant B holds ({Short(withBText)} / {Short(withFreshText)})");
                    }
                }
            }
            endpointsChecked.Add(endpoint.Key);
        }
        return new Result(problems, checks, endpointsChecked);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, object> WorkingCompany = new();

    /// <summary>Remember the client's working company: records that belong to a company are
    /// created in it by <see cref="SendAsync"/>.</summary>
    internal static async Task UseWorkingCompanyAsync(HttpClient client) => WorkingCompany.AddOrUpdate(client, await WorkingCompanyAsync(client));

    private static async Task<object> WorkingCompanyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/tenancy/workplace");
        if (!response.IsSuccessStatusCode)
        {
            return "";
        }
        var workplace = await response.Content.ReadFromJsonAsync<JsonObject>();
        return workplace?["companyId"]?.GetValue<string>() ?? "";
    }

    /// <summary>An address in tenant B's domain, or a code of the same shape.</summary>
    private static string ValueLike(string field, string local, Erp.Kernel.Seeding.SeedTenant tenant)
    {
        if (field.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return $"{local}@{tenant.EmailDomain}";
        }
        if (IsName(field, null))
        {
            var mark = new string(local.Where(char.IsLetterOrDigit).ToArray());
            return field.EndsWith("Ar", StringComparison.Ordinal) ? $"اسم {mark}" : $"Name {mark}";
        }
        var code = new string(local.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return code.Length <= 12 ? code : code[^12..];
    }

    /// <summary>Send the endpoint a valid body with <paramref name="field"/> set to
    /// <paramref name="value"/>: a create as is; an edit on a record the same caller creates first
    /// through the collection's POST, carrying the record's own values otherwise.</summary>
    internal static async Task<(int Status, string Text)> SendAsync(HttpClient client, OpenApiDocument openApi, ApiEndpoint endpoint, string? collection,
        JsonElement schema, ErpTestEnvironment env, string tag, string field, string value)
    {
        string path;
        JsonObject body;
        if (collection is null)
        {
            path = endpoint.Pattern;
            body = Valid(openApi, schema, env, tag, CompanyOf(client));
            // A record of its own with no id in the route (the workspace's settings): an edit
            // carries its current values, the version among them.
            if (endpoint.Method is "PUT" or "PATCH")
            {
                using var current = await client.GetAsync(path);
                if (current.IsSuccessStatusCode && await current.Content.ReadFromJsonAsync<JsonObject>() is { } record)
                {
                    foreach (var (name, _) in body.ToList())
                    {
                        if (record[name] is { } own)
                        {
                            body[name] = own.DeepClone();
                        }
                    }
                }
            }
        }
        else
        {
            var createBody = Valid(openApi, openApi.RequestSchema("POST", collection)!.Value, env, $"{tag}t", CompanyOf(client));
            string id;
            using (var create = await client.SendAsync(Json(HttpMethod.Post, collection, createBody)))
            {
                var created = await create.Content.ReadAsStringAsync();
                if (create.StatusCode != HttpStatusCode.Created)
                {
                    return ((int)create.StatusCode, $"creating the record to edit failed: {created}");
                }
                id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString()!;
                path = endpoint.Path(_ => id);
            }
            // The record itself (an action under it, such as a copy, has no GET of its own).
            using var read = await client.GetAsync($"{collection}/{id}");
            // An edit carries the record's own values; an action under it (a copy) fresh ones.
            var item = read.IsSuccessStatusCode && endpoint.Method is "PUT" or "PATCH" ? await read.Content.ReadFromJsonAsync<JsonObject>() ?? [] : [];
            body = Valid(openApi, schema, env, tag, CompanyOf(client));
            foreach (var (name, _) in body.ToList())
            {
                if (item[name] is { } current)
                {
                    body[name] = current.DeepClone();
                }
            }
        }
        body[field] = value;
        using var response = await client.SendAsync(Json(new HttpMethod(endpoint.Method), path, body));
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string CompanyOf(HttpClient client) => WorkingCompany.TryGetValue(client, out var id) ? (string)id : "";

    internal static JsonObject Valid(OpenApiDocument openApi, JsonElement schema, ErpTestEnvironment env, string tag, string companyId)
    {
        var generic = GrantEscalation.ValidBody(openApi, schema, env, tag);
        // Every leaf also meets its documented constraints (an enum's value, a pattern's example,
        // a maximum length), and an identifying code gets a code-shaped value of its own, so a
        // create of a record with codes, choices and patterned fields (a company, a branch) passes
        // validation and its write is judged rather than refused.
        var k = 0;
        var body = openApi.BuildBody(schema, (leaf, type, format, name) =>
        {
            k++;
            var value = name is not null && generic[name] is JsonValue plain ? plain.DeepClone() : null;
            if (type == "string" && name is not null && IsIdentifying(name, format) && !name.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                value = JsonValue.Create(ValueLike(name, $"v{tag}{k}", env.TenantB));
            }
            else if (type == "string" && format == "uuid" && name == "companyId" && companyId.Length > 0)
            {
                value = JsonValue.Create(companyId);
            }
            else if (type == "string" && name is not null && !IsName(name, format) && !name.Contains("email", StringComparison.OrdinalIgnoreCase) &&
                     OpenApiDocument.Examples(openApi.Resolve(leaf)).FirstOrDefault(e => e.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.String } example)
            {
                // A documented expression (a list's column keys, sort, filter, grouping): the
                // first example is valid where generated text is not.
                value = JsonValue.Create(example.GetString());
            }
            else if (type == "integer" && value is null)
            {
                value = JsonValue.Create(1);
            }
            return openApi.Conform(leaf, value);
        }) as JsonObject ?? [];
        foreach (var (name, value) in body.ToList())
        {
            if (value is JsonArray array && array.All(x => x is null))
            {
                body[name] = new JsonArray();
            }
        }
        return body;
    }

    private static HttpRequestMessage Json(HttpMethod method, string path, JsonObject body) =>
        new(method, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";
}

/// <summary>Its own environment: the gate creates records in both tenants, some carrying the
/// other tenant's addresses, which would disturb tests that read the shared gate environment.</summary>
public sealed class WriteOracleFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The write-oracle gate against the product (its self-test plants a registry).</summary>
public sealed class G1WriteOracleTests(WriteOracleFixture fixture) : IClassFixture<WriteOracleFixture>
{
    [Fact]
    public async Task Writes_answer_the_same_for_another_tenants_values_as_for_values_that_exist_nowhere()
    {
        var result = await G1WriteOracle.RunAsync(fixture.Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Checks} differential write checks on {string.Join(", ", result.Endpoints)}");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.Contains("POST /api/identity/users", result.Endpoints);
        Assert.Contains("PUT /api/identity/users/{id:guid}", result.Endpoints);
        Assert.True(result.Checks >= Ratchet.Min("g1.writeOracleChecks"),
            $"{result.Checks} differential write checks; ratchet minimum {Ratchet.Min("g1.writeOracleChecks")}");
    }
}
