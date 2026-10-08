using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Companies for the G2 acting-on-grants gates. Roles can be held in one company and a user has a
/// default company, so a probe that changes either needs companies its caller works in (a role is
/// assigned, and a default chosen, only in the caller's own companies) and, for a default company,
/// one the user works in too. The gates take the first two companies the administrator works in and
/// give their callers, and the users they act on as the control, access to both.
/// </summary>
public sealed class GateCompanies
{
    private GateCompanies(HttpClient admin, IReadOnlyList<Guid> ids)
    {
        Admin = admin;
        Ids = ids;
    }

    private HttpClient Admin { get; }

    /// <summary>Up to two companies the administrator works in (empty in a workspace without companies).</summary>
    public IReadOnlyList<Guid> Ids { get; }

    /// <summary>The company roles are assigned in by the probes, or null without companies.</summary>
    public Guid? First => Ids.Count > 0 ? Ids[0] : null;

    public static async Task<GateCompanies> OfAsync(HttpClient admin)
    {
        using var response = await admin.GetAsync("/api/identity/companies");
        var ids = new List<Guid>();
        if (response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<JsonArray>() is { } companies)
        {
            ids.AddRange(companies.Take(2).Select(c => c!["id"]!.GetValue<Guid>()));
        }
        return new GateCompanies(admin, ids);
    }

    /// <summary>Let the user work in both companies (every branch). Answers false when the record
    /// is not a user (the access endpoint does not know it), so callers can hand any record id.</summary>
    public async Task<bool> GiveAccessAsync(Guid userId)
    {
        if (Ids.Count == 0)
        {
            return false;
        }
        using var current = await Admin.GetAsync($"/api/tenancy/access/{userId}");
        if (!current.IsSuccessStatusCode)
        {
            return false;
        }
        var body = new JsonObject
        {
            ["companies"] = new JsonArray(Ids.Select(id => (JsonNode)new JsonObject { ["companyId"] = id, ["allBranches"] = true, ["branchIds"] = new JsonArray() }).ToArray()),
        };
        // A version, when the access record carries one, is sent back as read.
        if (await current.Content.ReadFromJsonAsync<JsonObject>() is { } read && read["version"] is { } version)
        {
            body["version"] = version.DeepClone();
        }
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tenancy/access/{userId}") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await Admin.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PUT /api/tenancy/access/{userId} as administrator answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        return true;
    }

    /// <summary>Let the user work in <paramref name="companyId"/> alone (every branch).</summary>
    public async Task LimitAccessAsync(Guid userId, Guid companyId)
    {
        using var current = await Admin.GetAsync($"/api/tenancy/access/{userId}");
        var body = new JsonObject
        {
            ["companies"] = new JsonArray(new JsonObject { ["companyId"] = companyId, ["allBranches"] = true, ["branchIds"] = new JsonArray() }),
        };
        if (current.IsSuccessStatusCode && await current.Content.ReadFromJsonAsync<JsonObject>() is { } read && read["version"] is { } version)
        {
            body["version"] = version.DeepClone();
        }
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/tenancy/access/{userId}") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await Admin.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PUT /api/tenancy/access/{userId} as administrator answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>A company id other than <paramref name="current"/>, for a single-field change of a
    /// company field (a default company); null when there is none.</summary>
    public JsonNode? Other(JsonNode? current) =>
        Ids.Where(id => current is not JsonValue v || !Guid.TryParse(v.ToString(), out var c) || c != id)
            .Select(id => (JsonNode?)JsonValue.Create(id)).FirstOrDefault();

    /// <summary>A company role entry.</summary>
    public static JsonObject CompanyRole(Guid roleId, Guid companyId) => new() { ["roleId"] = roleId, ["companyId"] = companyId };

    /// <summary>True for a field naming one company (a default company), which single-field
    /// requests change to another company.</summary>
    public static bool IsCompanyField(string field) => field.EndsWith("companyId", StringComparison.OrdinalIgnoreCase);
}
