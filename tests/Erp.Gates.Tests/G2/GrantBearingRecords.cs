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
/// G2, acting on a record that grants access. A record is grant-bearing when its collection is
/// created with a grant field (<c>roleIds</c>, <c>permissions</c>): a role, a user, anything a
/// later module adds that hands out access. Every endpoint that acts on one such record (anything
/// but GET under <c>collection/{id}</c>: edit, delete, copy, reset, end sessions, clear a pause) is
/// found from the running app's routing and OpenAPI document, not from a list. A caller holding
/// exactly that endpoint's permission (plus reading roles and users) aims it at a record granting
/// everything (the Administrator role for users, every permission in the catalogue for roles):
/// the answer must be 403 and the record must read back exactly as before (still there after a
/// delete). The same request aimed at a record granting only what the caller holds must succeed,
/// which proves the 403 came from the grant check. Without this, a user who may only delete roles
/// removes roles granting what they do not hold (critic p03 round 1, plant P2), which is how a
/// junior administrator strips the finance team of its access.
/// </summary>
public static class GrantBearingRecords
{
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Checked);

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        var problems = new List<string>();
        var checkedEndpoints = new List<string>();
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var everything = catalog.PermissionKeys.Order(StringComparer.Ordinal).ToList();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var administratorRole = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();

        var endpoints = EndpointInventory.From(env.Factory.Services);
        var families = endpoints
            .Where(e => e.Method == "POST" && !e.IsAnonymous && e.RouteParameters.Count == 0)
            .Select(e => (Create: e, Schema: openApi.RequestSchema(e.Method, e.Pattern)))
            .Where(x => x.Schema is { } s && openApi.Resolve(s).TryGetProperty("properties", out var p) && GrantEscalation.GrantFields.Any(f => p.TryGetProperty(f, out _)))
            .Select(x => (x.Create, Schema: x.Schema!.Value))
            .ToList();

        var n = 0;
        foreach (var (create, createSchema) in families)
        {
            var prefix = create.Pattern.TrimEnd('/') + "/{";
            var acting = endpoints.Where(e => e.Method is not ("GET" or "HEAD" or "OPTIONS") && !e.IsAnonymous && e.Pattern.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var endpoint in acting)
            {
                n++;
                var tag = $"{n}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant()}";
                var callerPermissions = new[] { endpoint.Permission, "identity.roles.read", "identity.users.read" }.Distinct().ToArray();
                var callerRole = await CreatedIdAsync(admin, "/api/identity/roles",
                    new JsonObject { ["nameEn"] = $"G2 holder {tag}", ["nameAr"] = $"حامل {tag}", ["permissions"] = new JsonArray(callerPermissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()) });
                var email = $"g2.holder.{tag}@{env.TenantA.EmailDomain}";
                await CreatedIdAsync(admin, "/api/identity/users",
                    new JsonObject { ["email"] = email, ["displayName"] = $"G2 holder {tag}", ["language"] = "en", ["password"] = ErpTestEnvironment.Password, ["mustChangePassword"] = false, ["roleIds"] = new JsonArray(JsonValue.Create(callerRole)) });
                using var caller = await env.SignInAsync(email);

                // A record granting everything.
                var strongBody = GrantEscalation.ValidBody(openApi, createSchema, env, $"{tag}s");
                GrantEscalation.SetGrants(strongBody, [administratorRole], everything);
                var strong = await CreatedIdAsync(admin, create.Pattern, strongBody);
                var strongPath = endpoint.Path(_ => strong.ToString());
                var itemPath = create.Pattern.TrimEnd('/') + "/" + strong;
                var before = await ReadAsync(admin, itemPath);

                var (status, text) = await SendAsync(caller, endpoint.Method, strongPath, await BodyAsync(admin, openApi, endpoint, itemPath, $"{tag}x"));
                if (status != (int)HttpStatusCode.Forbidden)
                {
                    problems.Add($"{endpoint}: aimed at a record granting everything by a user holding only [{string.Join(", ", callerPermissions)}] answered {status} (expected 403): {Short(text)}");
                }
                var after = await ReadAsync(admin, itemPath);
                if (after != before)
                {
                    problems.Add($"{endpoint}: the record granting everything changed: before {Short(before)}; after {Short(after)}");
                }

                // Control: a record granting only what the caller holds.
                var weakBody = GrantEscalation.ValidBody(openApi, createSchema, env, $"{tag}w");
                GrantEscalation.SetGrants(weakBody, [], [endpoint.Permission]);
                var weak = await CreatedIdAsync(admin, create.Pattern, weakBody);
                var weakItem = create.Pattern.TrimEnd('/') + "/" + weak;
                var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => weak.ToString()), await BodyAsync(admin, openApi, endpoint, weakItem, $"{tag}c"));
                if (controlStatus is < 200 or >= 300)
                {
                    problems.Add($"{endpoint}: aimed at a record granting only what the caller holds answered {controlStatus}, so the gate cannot tell a grant check from a malformed request: {Short(controlText)}");
                }
                checkedEndpoints.Add(endpoint.Key);
            }
        }
        return new Result(problems, checkedEndpoints);
    }

    /// <summary>The record as the administrator reads it (status and body), or its status when it
    /// has no GET.</summary>
    private static async Task<string> ReadAsync(HttpClient admin, string path)
    {
        using var response = await admin.GetAsync(path);
        return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";
    }

    /// <summary>A body that passes validation: for an edit, the record's own fields with its first
    /// plain text field changed (a real change, grants untouched); otherwise fresh names, a valid
    /// password and language, flags on.</summary>
    private static async Task<JsonObject?> BodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, string itemPath, string tag)
    {
        if (!endpoint.HasBody || openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return null;
        }
        var body = GrantEscalation.ValidBody(openApi, schema, env: null, tag);
        foreach (var (field, value) in body.ToList())
        {
            if (value is JsonArray array && array.All(x => x is null))
            {
                body[field] = new JsonArray();
            }
        }
        if (endpoint.Method is "PUT" or "PATCH")
        {
            using var response = await admin.GetAsync(itemPath);
            if (response.IsSuccessStatusCode && JsonNode.Parse(await response.Content.ReadAsStringAsync()) is JsonObject item)
            {
                var changed = false;
                foreach (var (field, generated) in body.ToList())
                {
                    if (item[field] is not { } current)
                    {
                        continue;
                    }
                    var plainText = current is JsonValue v && v.GetValueKind() == JsonValueKind.String && generated is JsonValue g && g.GetValueKind() == JsonValueKind.String &&
                                    !field.Contains("email", StringComparison.OrdinalIgnoreCase) && !field.Equals("language", StringComparison.OrdinalIgnoreCase) &&
                                    !field.Equals("numerals", StringComparison.OrdinalIgnoreCase) && !Guid.TryParse(v.GetValue<string>(), out _);
                    if (plainText && !changed)
                    {
                        body[field] = field.EndsWith("Ar", StringComparison.Ordinal) ? $"تعديل {tag}" : $"Edited {tag}";
                        changed = true;
                        continue;
                    }
                    body[field] = current.DeepClone();
                }
            }
        }
        return body;
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, string method, string path, JsonObject? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, JsonObject body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await admin.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} as administrator answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
