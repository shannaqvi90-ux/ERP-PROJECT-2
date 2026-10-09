using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Callers whose grants come from a role held in one company (critic p03 round 6, plant Pc: an
/// "all that match" check that let a grant the caller holds in ONE company cover the same role
/// held in ANY company passed every gate, because every takeover caller held its grants through
/// workspace-wide roles). The caller holds, through one role assigned in company X and nothing
/// workspace-wide, the endpoint's permission, reading users and roles, and every grant of the
/// targets it is aimed at; it works in X and Y and starts in X. Aimed at users holding the same
/// grants in Y, or in every company, it must leave them alone; aimed at users holding them in X it
/// must act (the control: the same role, in the company where the caller holds it).
/// </summary>
public static class CompanyCallers
{
    /// <summary>A caller holding <c>Permissions</c> only through a role in company X.</summary>
    public sealed record Caller(Guid Id, string Email, Guid RoleId, Guid X, Guid Y, IReadOnlyList<string> Permissions);

    /// <summary>What the caller holds in X: <paramref name="own"/> (the endpoint's permission and
    /// reading users and roles) plus every grant of <paramref name="targets"/>.</summary>
    public static IReadOnlyList<string> HeldInX(IEnumerable<string> own, IEnumerable<GrantTargets.Target> targets) =>
        own.Concat(targets.SelectMany(t => t.Permissions)).Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>Make the caller (as <paramref name="admin"/>, who works in both companies), starting
    /// in X. Null without two companies.</summary>
    public static async Task<Caller?> CreateAsync(HttpClient admin, ErpTestEnvironment env, GateCompanies companies, string local,
        IReadOnlyList<string> permissions)
    {
        if (companies.Ids.Count < 2)
        {
            return null;
        }
        var (x, y) = (companies.Ids[0], companies.Ids[1]);
        var roleId = await CreatedIdAsync(admin, "/api/identity/roles", new JsonObject
        {
            ["nameEn"] = $"In one company {local}",
            ["nameAr"] = $"في شركة واحدة {local}",
            ["permissions"] = new JsonArray(permissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
        });
        var email = $"{local}@{env.TenantA.EmailDomain}";
        var id = await CreatedIdAsync(admin, "/api/identity/users", new JsonObject
        {
            ["email"] = email,
            ["displayName"] = $"Company caller {local}",
            ["language"] = "en",
            ["password"] = ErpTestEnvironment.Password,
            ["mustChangePassword"] = false,
            ["roleIds"] = new JsonArray(),
            ["companyRoles"] = new JsonArray(GateCompanies.CompanyRole(roleId, x)),
        });
        await companies.GiveAccessAsync(id);
        // Starts in X whatever the order of the companies' codes.
        var current = await admin.GetFromJsonAsync<JsonObject>($"/api/identity/users/{id}/default-company") ?? [];
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/identity/users/{id}/default-company")
               {
                   Content = new StringContent(new JsonObject { ["companyId"] = x, ["version"] = current["version"]?.DeepClone() }.ToJsonString(), Encoding.UTF8, "application/json"),
               })
        using (var response = await admin.SendAsync(request))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PUT /api/identity/users/{id}/default-company as administrator answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
        return new Caller(id, email, roleId, x, y, permissions);
    }

    /// <summary>Sign the caller in and check the session holds <paramref name="endpointPermission"/>
    /// (working in X) and that no workspace-wide role gives it anything.</summary>
    public static async Task<HttpClient> SignInAsync(ErpTestEnvironment env, Caller caller, string endpointPermission)
    {
        var client = await env.SignInAsync(caller.Email);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var held = session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToHashSet(StringComparer.Ordinal);
        if (!held.Contains(endpointPermission))
        {
            client.Dispose();
            throw new InvalidOperationException($"the caller holding [{string.Join(", ", caller.Permissions)}] in company {caller.X} only does not hold {endpointPermission} in its session: {session}");
        }
        return client;
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
}
