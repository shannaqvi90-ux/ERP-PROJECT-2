using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, object level: an action on "me" acts on the caller only. Every write endpoint without a
/// route parameter acts on the caller (their preferences, their workplace, their session), on the
/// caller's tenant, or creates a record; none of them may act on another person because the
/// request names that person. A permission check alone cannot see this: the caller holds the
/// endpoint's permission, and the harm is in which record the handler picks (critic p04 round 3,
/// plant N1: an optional, documented <c>userId</c> on <c>PUT /me/preferences</c> let a viewer
/// change the Administrator's language while <c>PUT /users/{id}</c> answered 403).
///
/// For every such endpoint (found from the running app's routing, sign-in and sign-out included),
/// a caller holding exactly that endpoint's permission sends a valid request three times, naming a
/// victim (another user of the same tenant, signed in, holding the same role) in every subject
/// field a handler might honour: the body (the request schema's own id fields and the usual
/// subject names), the query string and the headers. Every row of every tenant table that
/// mentions the victim is compared before and after each request (read as the superuser), and the
/// victim's own view of their session: nothing may change. The same valid request without the
/// victim (the control) must succeed, so a refusal of the body never passes for a check.
/// </summary>
public static class SubjectInjection
{
    /// <summary>Names a handler might read the subject of an action from.</summary>
    public static readonly string[] SubjectFields =
    [
        "userId", "user", "id", "subjectId", "targetId", "targetUserId", "forUserId", "onBehalfOf", "onBehalfOfUserId",
        "actingAs", "actAs", "impersonate", "ownerId", "ownerUserId", "accountId", "memberId", "personId", "employeeId",
        "profileId", "principalId", "uid", "sub", "user_id", "target_user_id", "owner_id", "subject",
    ];

    /// <summary>Headers a handler might read the subject of an action from.</summary>
    public static readonly string[] SubjectHeaders =
    [
        "X-User-Id", "X-Subject-Id", "X-On-Behalf-Of", "X-Acting-As", "X-Act-As", "X-Impersonate", "X-Impersonate-User",
        "X-Target-User", "X-Target-User-Id", "X-Owner-Id", "X-Account-Id",
    ];

    public enum Carrier { Body, Query, Header }

    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Checked, int Injections);

    /// <summary>Write endpoints without a route parameter: they act on the caller, the caller's
    /// tenant, or create.</summary>
    public static IReadOnlyList<ApiEndpoint> Endpoints(ErpTestEnvironment env) =>
        EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Method != "GET" && e.Method != "HEAD" && e.RouteParameters.Count == 0 && e.InOpenApi)
            .ToList();

    public static async Task<Result> RunAsync(ErpTestEnvironment env, Func<ApiEndpoint, bool>? only = null)
    {
        var problems = new List<string>();
        var checkedEndpoints = new List<string>();
        var injections = 0;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var n = 0;
        foreach (var endpoint in Endpoints(env).Where(e => only?.Invoke(e) ?? true))
        {
            n++;
            var tag = $"{n}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant()}";
            // The caller and the victim hold the same role: exactly the endpoint's permission (none
            // for an anonymous endpoint), so the victim is someone the endpoint could act for.
            // Plus the reads of the same collection (a list's shared views need the list itself:
            // a list the caller cannot read does not exist for them).
            var permissions = endpoint.IsAnonymous ? Array.Empty<string>() : CompanionReads(env, endpoint).Prepend(endpoint.Permission).Distinct().ToArray();
            var roleId = await CreatedIdAsync(admin, "/api/identity/roles", new { nameEn = $"Subject {tag}", nameAr = $"صاحب {tag}", permissions });
            var callerEmail = $"subject.caller.{tag}@{env.TenantA.EmailDomain}";
            var victimEmail = $"subject.victim.{tag}@{env.TenantA.EmailDomain}";
            var callerId = await CreatedIdAsync(admin, "/api/identity/users", new { email = callerEmail, displayName = $"Subject caller {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
            // The caller works in every company, so a company-scoped action is not refused for
            // want of a company (the victim keeps the default).
            await GrantEveryCompanyAsync(admin, callerId);
            // The victim reads Arabic: a request that changed the victim's language to the body's
            // English would show in the victim's row.
            var victimId = await CreatedIdAsync(admin, "/api/identity/users", new { email = victimEmail, displayName = $"Subject victim {tag}", language = "ar", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
            using var victim = await env.SignInAsync(victimEmail);

            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            // The control: the same valid request, naming nobody, from a session of its own. A
            // field the endpoint refuses as invalid (a sort or filter syntax the generic values
            // cannot meet) is left out, and the injected requests leave it out too.
            var leftOut = new HashSet<string>(StringComparer.Ordinal);
            var controlPassed = false;
            var controlReport = "";
            for (var attempt = 0; attempt < 4 && !controlPassed; attempt++)
            {
                using var controlCaller = await env.SignInAsync(callerEmail);
                var controlBody = Without(await BodyAsync(admin, openApi, endpoint, schema, env, $"{tag}c{attempt}", callerEmail), leftOut);
                var (controlStatus, controlText) = await SendAsync(controlCaller, endpoint, controlBody, null, null);
                controlPassed = controlStatus is >= 200 and < 300;
                controlReport = $"{controlStatus}: {Short(controlText)}";
                if (!controlPassed && controlStatus == (int)HttpStatusCode.BadRequest && InvalidFields(controlText) is { Count: > 0 } invalid && invalid.Any(leftOut.Add))
                {
                    continue;
                }
                if (!controlPassed)
                {
                    break;
                }
            }
            if (!controlPassed)
            {
                problems.Add($"{endpoint}: the valid request without a subject answered {controlReport}, so the gate cannot tell a refusal from a check; extend SubjectInjection's body for this endpoint");
                continue;
            }

            foreach (var carrier in Enum.GetValues<Carrier>())
            {
                using var caller = await env.SignInAsync(callerEmail);
                var body = Without(await BodyAsync(admin, openApi, endpoint, schema, env, $"{tag}{carrier.ToString()[0]}", callerEmail), leftOut);
                string? query = null;
                Dictionary<string, string>? headers = null;
                switch (carrier)
                {
                    case Carrier.Body:
                        body ??= [];
                        foreach (var field in SubjectFieldsOf(openApi, schema))
                        {
                            body[field] = victimId.ToString();
                        }
                        break;
                    case Carrier.Query:
                        query = string.Join("&", SubjectFields.Select(f => $"{Uri.EscapeDataString(f)}={victimId}"));
                        break;
                    case Carrier.Header:
                        headers = SubjectHeaders.ToDictionary(h => h, _ => victimId.ToString());
                        break;
                }

                var viewBefore = await VictimViewAsync(victim);
                var before = await FootprintAsync(env, victimId);
                var (status, text) = await SendAsync(caller, endpoint, body, query, headers);
                var after = await FootprintAsync(env, victimId);
                var viewAfter = await VictimViewAsync(victim);
                injections++;
                if (status >= 500)
                {
                    problems.Add($"{endpoint} [{carrier}]: naming another user answered {status}: {Short(text)}");
                }
                foreach (var table in before.Keys.Union(after.Keys).Where(t => before.GetValueOrDefault(t) != after.GetValueOrDefault(t)).Order(StringComparer.Ordinal))
                {
                    problems.Add($"{endpoint} [{carrier}]: a caller holding [{string.Join(", ", permissions)}] named another user of the tenant and that user's rows in {table} changed ({before.GetValueOrDefault(table) ?? "none"} -> {after.GetValueOrDefault(table) ?? "none"}); answered {status}");
                }
                if (viewBefore != viewAfter)
                {
                    problems.Add($"{endpoint} [{carrier}]: the named user's own session changed: {Short(viewBefore)} -> {Short(viewAfter)}");
                }
            }
            checkedEndpoints.Add(endpoint.Key);
        }
        return new Result(problems, checkedEndpoints, injections);
    }

    /// <summary>The permissions of the reads next to the endpoint (GET endpoints of the same parent path).</summary>
    private static IEnumerable<string> CompanionReads(ErpTestEnvironment env, ApiEndpoint endpoint)
    {
        var parent = endpoint.Pattern[..endpoint.Pattern.LastIndexOf('/')];
        return EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Method == "GET" && !e.IsAnonymous && e.RouteParameters.Count == 0 && e.Pattern.StartsWith(parent + "/", StringComparison.Ordinal) && e.Pattern.LastIndexOf('/') == parent.Length)
            .Select(e => e.Permission)
            .Distinct()
            .Order(StringComparer.Ordinal);
    }

    /// <summary>The body's subject fields: the usual names, and every id the request schema
    /// declares at its top level (an optional, documented userId is plant N1 exactly).</summary>
    private static IEnumerable<string> SubjectFieldsOf(OpenApiDocument openApi, JsonElement? schema)
    {
        var fields = new List<string>(SubjectFields);
        if (schema is { } s && openApi.Resolve(s).TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                var resolved = openApi.Resolve(property.Value);
                var format = resolved.TryGetProperty("format", out var f) ? f.GetString() : null;
                if (format == "uuid" && !fields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    fields.Add(property.Name);
                }
            }
        }
        return fields;
    }

    /// <summary>A valid body: the caller's own sign-in for signing in; for an edit of a record
    /// without an id (the tenant, the workplace), the record's current values as the
    /// administrator reads them; otherwise the write oracle's valid values (fresh names and codes,
    /// the administrator's working company, in which the caller also works).</summary>
    private static async Task<JsonObject?> BodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, JsonElement? schema, ErpTestEnvironment env, string tag, string callerEmail)
    {
        if (schema is not { } s)
        {
            return endpoint.HasBody ? [] : null;
        }
        if (endpoint.Pattern == "/api/auth/sign-in")
        {
            return new JsonObject { ["email"] = callerEmail, ["password"] = ErpTestEnvironment.Password };
        }
        var company = (await admin.GetFromJsonAsync<JsonObject>("/api/tenancy/workplace"))?["companyId"]?.GetValue<string>() ?? "";
        var body = G1WriteOracle.Valid(openApi, s, env, tag, company);
        if (endpoint.Method is "PUT" or "PATCH")
        {
            using var current = await admin.GetAsync(endpoint.Pattern);
            if (current.IsSuccessStatusCode && JsonNode.Parse(await current.Content.ReadAsStringAsync()) is JsonObject item)
            {
                foreach (var (name, _) in body.ToList())
                {
                    if (item[name] is { } value)
                    {
                        body[name] = value.DeepClone();
                    }
                }
            }
        }
        return body;
    }

    private static JsonObject? Without(JsonObject? body, IReadOnlySet<string> fields)
    {
        if (body is null)
        {
            return null;
        }
        foreach (var field in fields)
        {
            foreach (var (name, _) in body.ToList().Where(p => p.Key.Equals(field, StringComparison.OrdinalIgnoreCase)))
            {
                body.Remove(name);
            }
        }
        return body;
    }

    /// <summary>The fields a validation problem names (its errors' keys).</summary>
    private static List<string> InvalidFields(string problem)
    {
        try
        {
            return JsonNode.Parse(problem)?["errors"] is JsonObject errors ? errors.Select(e => e.Key).ToList() : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task GrantEveryCompanyAsync(HttpClient admin, Guid userId)
    {
        var companies = (await admin.GetFromJsonAsync<JsonObject>("/api/tenancy/companies?take=200"))?["items"] as JsonArray ?? [];
        // An access save carries the version read (tenancy refuses a missing or stale one).
        var read = await admin.GetFromJsonAsync<JsonObject>($"/api/tenancy/access/{userId}");
        var body = new JsonObject
        {
            ["companies"] = new JsonArray(companies.Select(c => (JsonNode)new JsonObject { ["companyId"] = c!["id"]!.DeepClone(), ["allBranches"] = true }).ToArray()),
            ["version"] = read?["version"]?.DeepClone(),
        };
        using var response = await admin.PutAsync($"/api/tenancy/access/{userId}", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PUT /api/tenancy/access/{userId} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, ApiEndpoint endpoint, JsonObject? body, string? query, IReadOnlyDictionary<string, string>? headers)
    {
        var path = endpoint.Pattern + (query is null ? "" : $"?{query}");
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>What the victim sees of themselves: the user part of their session.</summary>
    private static async Task<string> VictimViewAsync(HttpClient victim)
    {
        var session = await victim.GetFromJsonAsync<JsonElement>("/api/auth/session");
        return session.TryGetProperty("user", out var user)
            ? $"{session.GetProperty("authenticated").GetBoolean()} {user.GetRawText()}"
            : $"{session.GetProperty("authenticated").GetBoolean()}";
    }

    /// <summary>Per tenant table: the count and a checksum of the rows that mention the user's id
    /// anywhere (the user's own row, their sessions, their access, anything they own).</summary>
    public static async Task<Dictionary<string, string>> FootprintAsync(ErpTestEnvironment env, Guid userId)
    {
        await using var connection = await env.OpenAdminAsync();
        var footprint = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in await DbCatalog.TenantTablesAsync(connection))
        {
            var value = await DbCatalog.ScalarAsync<string>(connection,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE t::text LIKE '%' || @u || '%'",
                ("u", userId.ToString()));
            if (!value.StartsWith("0:", StringComparison.Ordinal))
            {
                footprint[table.Qualified] = value;
            }
        }
        return footprint;
    }

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, object body)
    {
        using var response = await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
