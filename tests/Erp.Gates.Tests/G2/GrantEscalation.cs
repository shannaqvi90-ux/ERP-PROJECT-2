using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, privilege escalation through grants. Every endpoint whose request body carries a grant
/// field (roleIds, permissions) is found in the OpenAPI document. For each one, a user holding
/// exactly that endpoint's permission (plus reading roles and users) asks it to grant the
/// Administrator role and every permission in the catalogue: the answer must be 403 and nothing
/// may change. The same request granting only what the caller already holds must succeed, which
/// proves the 403 came from the grant check and not from a malformed request. Asking for
/// everything cannot tell a correct check from one that compares only part of the grants (critic
/// p03 round 3, plant P16: a role-create check narrowed to identity permissions minted a role
/// granting tenancy.tenant.update), so the same request also asks for what the caller lacks in
/// every shape of <see cref="GrantTargets"/>: each missing permission alone (and a role granting
/// it), a whole other module, the caller's own plus one more.
/// </summary>
public static class GrantEscalation
{
    public static readonly string[] GrantFields = ["roleIds", "permissions"];

    /// <param name="PartialTargets">Requests asking for grants the caller lacks without asking for everything.</param>
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Checked, int PartialTargets = 0);

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        var problems = new List<string>();
        var checkedEndpoints = new List<string>();
        var partialTargets = 0;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var everything = catalog.PermissionKeys.Order(StringComparer.Ordinal).ToList();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var administratorRole = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var targetRoles = new TargetRecords(admin, env);

        var grantEndpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => !e.IsAnonymous && e.HasBody)
            .Select(e => (Endpoint: e, Schema: openApi.RequestSchema(e.Method, e.Pattern)))
            .Where(x => x.Schema is { } s && s.TryGetProperty("properties", out var p) && GrantFields.Any(f => p.TryGetProperty(f, out _)))
            .Select(x => (x.Endpoint, Schema: x.Schema!.Value))
            .ToList();

        var n = 0;
        foreach (var (endpoint, schema) in grantEndpoints)
        {
            n++;
            var tag = $"{n}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant()}";
            var callerPermissions = new[] { endpoint.Permission, "identity.roles.read", "identity.users.read" }.Distinct().ToArray();
            var callerRole = await CreatedIdAsync(admin, "/api/identity/roles",
                new { nameEn = $"G2 caller {tag}", nameAr = $"مستدعي {tag}", permissions = callerPermissions });
            var email = $"g2.grant.{tag}@{env.TenantA.EmailDomain}";
            await CreatedIdAsync(admin, "/api/identity/users",
                new { email, displayName = $"G2 caller {tag}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { callerRole } });
            using var caller = await env.SignInAsync(email);
            var permissionsBefore = await SessionPermissionsAsync(caller);
            var administratorsBefore = await AdministratorCountAsync(admin);

            string path;
            JsonObject escalate;
            JsonObject control;
            Func<Task<string?>>? targetUnchanged = null;
            Func<GrantTargets.Target, Task<JsonObject>> asking;
            if (endpoint.RouteParameters.Count > 0)
            {
                // Update: a fresh target created by the administrator through the collection's POST.
                var collection = endpoint.Pattern[..endpoint.Pattern.LastIndexOf("/{", StringComparison.Ordinal)];
                var createSchema = openApi.RequestSchema("POST", collection);
                if (createSchema is not { } cs)
                {
                    problems.Add($"{endpoint}: no POST {collection} to create a target; extend the escalation gate for this endpoint");
                    continue;
                }
                var targetBody = ValidBody(openApi, cs, env, $"{tag}t");
                SetGrants(targetBody, [], ["identity.users.read"]);
                var targetId = await CreatedIdAsync(admin, collection, targetBody);
                path = endpoint.Path(_ => targetId.ToString());
                var item = await admin.GetFromJsonAsync<JsonObject>(path) ?? [];
                control = OnlySchemaFields(openApi, schema, item);
                escalate = OnlySchemaFields(openApi, schema, item);
                var currentRoles = item["roleIds"] is JsonArray roles ? roles.Select(r => r!.GetValue<Guid>()).ToList() : [];
                SetGrants(escalate, [.. currentRoles, administratorRole], everything);
                asking = async target =>
                {
                    var body = OnlySchemaFields(openApi, schema, item);
                    SetGrants(body, [.. currentRoles, await targetRoles.RoleAsync(target.Permissions)], target.Permissions);
                    return body;
                };
                var grantsBefore = GrantsOf(item);
                targetUnchanged = async () =>
                {
                    var now = GrantsOf(await admin.GetFromJsonAsync<JsonObject>(path) ?? []);
                    return now == grantsBefore ? null : $"target grants changed from {grantsBefore} to {now}";
                };
            }
            else
            {
                path = endpoint.Pattern;
                escalate = ValidBody(openApi, schema, env, $"{tag}e");
                SetGrants(escalate, [administratorRole], everything);
                control = ValidBody(openApi, schema, env, $"{tag}c");
                SetGrants(control, [callerRole], [endpoint.Permission]);
                var a = 0;
                asking = async target =>
                {
                    var body = ValidBody(openApi, schema, env, $"{tag}p{++a}");
                    SetGrants(body, [await targetRoles.RoleAsync(target.Permissions)], target.Permissions);
                    return body;
                };
            }

            var (status, text) = await SendAsync(caller, endpoint.Method, path, escalate);
            if (status != (int)HttpStatusCode.Forbidden)
            {
                problems.Add($"{endpoint}: granting the Administrator role / every permission as a user holding only [{string.Join(", ", callerPermissions)}] answered {status} (expected 403): {Short(text)}");
            }
            if (targetUnchanged is not null && await targetUnchanged() is { } changed)
            {
                problems.Add($"{endpoint}: {changed}");
            }
            if (await AdministratorCountAsync(admin) != administratorsBefore)
            {
                problems.Add($"{endpoint}: the number of Administrator users changed");
            }
            if (!(await SessionPermissionsAsync(caller)).SequenceEqual(permissionsBefore))
            {
                problems.Add($"{endpoint}: the caller's own permissions changed");
            }

            // Grants the caller lacks without asking for everything.
            foreach (var target in GrantTargets.For(everything, callerPermissions))
            {
                var (targetStatus, targetText) = await SendAsync(caller, endpoint.Method, path, await asking(target));
                if (targetStatus != (int)HttpStatusCode.Forbidden)
                {
                    problems.Add($"{endpoint}: asking for {target} as a user holding only [{string.Join(", ", callerPermissions)}] answered {targetStatus} (expected 403): {Short(targetText)}");
                }
                if (targetUnchanged is not null && await targetUnchanged() is { } targetChanged)
                {
                    problems.Add($"{endpoint}: asking for {target}: {targetChanged}");
                }
                if (!(await SessionPermissionsAsync(caller)).SequenceEqual(permissionsBefore))
                {
                    problems.Add($"{endpoint}: asking for {target}: the caller's own permissions changed");
                }
                partialTargets++;
            }
            if (await AdministratorCountAsync(admin) != administratorsBefore)
            {
                problems.Add($"{endpoint}: the number of Administrator users changed while asking for grants the caller lacks");
            }

            var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, path, control);
            if (controlStatus is < 200 or >= 300)
            {
                problems.Add($"{endpoint}: the same request granting only what the caller holds answered {controlStatus}, so the gate cannot tell a grant check from a malformed request: {Short(controlText)}");
            }
            checkedEndpoints.Add(endpoint.Key);
        }
        return new Result(problems, checkedEndpoints, partialTargets);
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, string method, string path, JsonObject body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, object body)
    {
        using var response = body is JsonObject json
            ? await admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") })
            : await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} as administrator answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<IReadOnlyList<string>> SessionPermissionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();

    private static async Task<int> AdministratorCountAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
        .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("userCount").GetInt32();

    private static string GrantsOf(JsonObject item) =>
        string.Join(" | ", GrantFields.Select(f => item[f] is JsonArray a ? string.Join(",", a.Select(x => x!.ToString()).Order(StringComparer.Ordinal)) : ""));

    internal static void SetGrants(JsonObject body, IEnumerable<Guid> roleIds, IEnumerable<string> permissions)
    {
        if (body.ContainsKey("roleIds")) body["roleIds"] = new JsonArray(roleIds.Distinct().Select(r => (JsonNode)JsonValue.Create(r)).ToArray());
        if (body.ContainsKey("permissions")) body["permissions"] = new JsonArray(permissions.Distinct().Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
    }

    /// <summary>A body that passes validation: unique names and e-mail, a valid password and
    /// language, flags on, no ids.</summary>
    internal static JsonObject ValidBody(OpenApiDocument openApi, JsonElement schema, ErpTestEnvironment? env, string tag)
    {
        var k = 0;
        return openApi.BuildBody(schema, (type, format, name) =>
        {
            k++;
            var lower = name?.ToLowerInvariant() ?? "";
            return type switch
            {
                "string" when format == "uuid" => null,
                "string" when lower.Contains("email") => $"g2.{tag}.{k}@{env?.TenantA.EmailDomain ?? "g2.example"}",
                "string" when lower == "password" => ErpTestEnvironment.Password,
                "string" when lower == "language" => "en",
                "string" => $"G2 {tag} {k}",
                "integer" => null,
                "boolean" => true,
                _ => null,
            };
        }) as JsonObject ?? [];
    }

    /// <summary>The item's values for the fields the request schema has.</summary>
    private static JsonObject OnlySchemaFields(OpenApiDocument openApi, JsonElement schema, JsonObject item)
    {
        var body = new JsonObject();
        if (openApi.Resolve(schema).TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                body[property.Name] = item[property.Name]?.DeepClone();
            }
        }
        return body;
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
