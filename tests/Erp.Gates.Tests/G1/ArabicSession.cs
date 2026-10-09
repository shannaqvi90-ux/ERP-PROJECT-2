using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The Arabic side of a session (critic p04 round 4). The request language comes from the
/// signed-in user's preference (else Accept-Language), so a session of an English-speaking user
/// only ever runs the English branch of every handler that reads it: error texts, permission
/// labels, the access summary, a printed document's default language. A leak kept only on the
/// Arabic branch (a cache or a file per language, a formatter memo) then passed every gate. These
/// helpers give the gates a session whose every request runs in Arabic with Arabic-Indic digits:
/// the seeded Arabic-speaking administrator of the tenant (<see cref="Local"/>), its digits
/// preference set to <c>arab</c>, and Accept-Language <c>ar-AE</c> on every request as well.
/// </summary>
public static partial class ArabicSession
{
    /// <summary>Local part of the e-mail of every tenant's Arabic-speaking administrator.</summary>
    public const string Local = "admin.ar";

    public const string PreferencesPath = "/api/identity/me/preferences";

    /// <summary>Signs in the tenant's Arabic administrator with a cookie and makes the session Arabic.</summary>
    public static async Task<HttpClient> SignInAsync(ErpTestEnvironment env, Erp.Kernel.Seeding.SeedTenant tenant)
    {
        var client = await env.SignInAsync(env.Email(tenant, Local));
        await PrepareAsync(client);
        return client;
    }

    /// <summary>The same with a bearer token.</summary>
    public static async Task<HttpClient> SignInWithTokenAsync(ErpTestEnvironment env, Erp.Kernel.Seeding.SeedTenant tenant)
    {
        var client = await env.SignInWithTokenAsync(env.Email(tenant, Local));
        await PrepareAsync(client);
        return client;
    }

    /// <summary>
    /// Creates, through the product's own endpoints, an Arabic-speaking administrator of the
    /// tenant who works in every company and every branch, and answers the new user's e-mail.
    /// <see cref="Local"/> is limited to one branch of the last company in the gate seed (so every
    /// access table holds rows of both companies), and a write of a record every company shares
    /// (the workspace itself, <c>IWorkspaceWide</c>, p02 round 7) is refused to anyone who does not
    /// hold the whole workspace. Without this user no Arabic session could make that write, and the
    /// Arabic branch of its handler's success path would never run for either tenant. The user holds
    /// the same roles as the tenant's administrator; its names carry the tenant's code and
    /// <paramref name="run"/>, so they are this tenant's alone. <see cref="SignInWorkspaceWideAsync"/>
    /// checks that it holds the whole workspace.
    /// </summary>
    public static async Task<string> CreateWorkspaceWideAsync(ErpTestEnvironment env, Erp.Kernel.Seeding.SeedTenant tenant, HttpClient admin, string run)
    {
        var email = $"{WorkspaceWideLocal}.{run.ToLowerInvariant()}@{tenant.EmailDomain}";
        List<Guid> roleIds;
        await using (var connection = await env.OpenAdminAsync())
        {
            roleIds = await Infrastructure.DbCatalog.ReadAsync(connection,
                "SELECT ur.role_id FROM identity.user_roles ur JOIN identity.users u ON u.id = ur.user_id AND u.tenant_id = ur.tenant_id " +
                "WHERE u.tenant_id = @t AND u.email_normalized = @e",
                r => r.GetGuid(0), ("t", tenant.Id), ("e", env.Email(tenant, "admin").ToLowerInvariant()));
        }
        if (roleIds.Count == 0)
        {
            throw new InvalidOperationException($"Tenant {tenant.Code}'s administrator holds no role to copy.");
        }
        using var created = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email,
            displayName = $"Workspace Arabic administrator {tenant.Code} {run}",
            displayNameAr = $"مدير مساحة العمل بالعربية {tenant.Code} {run}",
            language = "ar",
            password = ErpTestEnvironment.Password,
            mustChangePassword = false,
            roleIds,
        });
        var createdText = await created.Content.ReadAsStringAsync();
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST /api/identity/users for {email} answered {(int)created.StatusCode}: {createdText}");
        }
        var userId = JsonDocument.Parse(createdText).RootElement.GetProperty("id").GetGuid();

        // Every company the administrator works in (the access record's options), every branch.
        var read = await admin.GetFromJsonAsync<JsonObject>($"/api/tenancy/access/{userId}")
                   ?? throw new InvalidOperationException($"GET /api/tenancy/access/{userId} answered nothing.");
        var companies = (read["options"] as JsonArray ?? []).Select(o => o!["id"]!.DeepClone()).ToList();
        var body = new JsonObject
        {
            ["companies"] = new JsonArray(companies.Select(id => (JsonNode)new JsonObject { ["companyId"] = id, ["allBranches"] = true, ["branchIds"] = new JsonArray() }).ToArray()),
            ["version"] = read["version"]?.DeepClone(),
        };
        using var granted = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", body);
        if (!granted.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PUT /api/tenancy/access/{userId} answered {(int)granted.StatusCode}: {await granted.Content.ReadAsStringAsync()}");
        }
        return email;
    }

    /// <summary>Local part (before the run suffix) of the e-mails of the workspace-wide Arabic
    /// administrators the gates create (<see cref="CreateWorkspaceWideAsync"/>).</summary>
    public const string WorkspaceWideLocal = "admin.ar.workspace";

    /// <summary>Signs in a user made by <see cref="CreateWorkspaceWideAsync"/> with a cookie, or with
    /// <paramref name="bearer"/> a bearer token, and makes the session Arabic. Throws when the user
    /// does not hold the whole workspace (<c>everyCompany</c> of <c>GET /api/tenancy/tenant</c>).</summary>
    public static async Task<HttpClient> SignInWorkspaceWideAsync(ErpTestEnvironment env, string email, bool bearer = false)
    {
        var client = bearer ? await env.SignInWithTokenAsync(email) : await env.SignInAsync(email);
        if (await HoldsWholeWorkspaceAsync(client) != true)
        {
            client.Dispose();
            throw new InvalidOperationException($"{email} was given every company and every branch but does not hold the whole workspace (everyCompany of GET /api/tenancy/tenant).");
        }
        await PrepareAsync(client);
        return client;
    }

    /// <summary>Whether an answer is the refusal of a write of a record every company shares to
    /// someone who does not work in every company and branch (the endpoint's
    /// <c>tenancy.workspaceNeedsEveryCompany</c> or the kernel's <c>workspaceNeedsEveryCompany</c>).</summary>
    public static bool IsWorkspaceRefusal(int status, string text) =>
        status == (int)HttpStatusCode.Forbidden && WorkspaceRefusalCode().IsMatch(text);

    [System.Text.RegularExpressions.GeneratedRegex("\"code\"\\s*:\\s*\"(?:[a-z]+\\.)?workspaceNeedsEveryCompany\"")]
    private static partial System.Text.RegularExpressions.Regex WorkspaceRefusalCode();

    /// <summary>Whether the signed-in user holds the whole workspace (<c>everyCompany</c> of
    /// <c>GET /api/tenancy/tenant</c>); null when the answer does not say.</summary>
    public static async Task<bool?> HoldsWholeWorkspaceAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/tenancy/tenant");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        return json?["everyCompany"] is JsonValue v && v.TryGetValue<bool>(out var holds) ? holds : null;
    }

    /// <summary>An anonymous client whose every request asks for Arabic.</summary>
    public static HttpClient Anonymous(ErpTestEnvironment env)
    {
        var client = env.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar-AE");
        return client;
    }

    /// <summary>Sets the signed-in user's language to Arabic and digits to Arabic-Indic (an attack
    /// or a write variant may have changed them) and asks for Arabic on every request.</summary>
    public static async Task PrepareAsync(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.AcceptLanguage.Any())
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar-AE");
        }
        using var response = await client.PutAsJsonAsync(PreferencesPath, new { language = "ar", numerals = "arab" });
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The Arabic session's preferences could not be set: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>Whether the session is signed in and answers in Arabic with Arabic-Indic digits.</summary>
    public static async Task<bool> IsArabicAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/session");
        var text = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.OK && IsArabicSessionText(text);
    }

    /// <summary>Whether a <c>/api/auth/session</c> answer is a signed-in Arabic session with Arabic-Indic digits.</summary>
    public static bool IsArabicSessionText(string text) =>
        text.Contains("\"authenticated\":true", StringComparison.Ordinal) &&
        text.Contains("\"language\":\"ar\"", StringComparison.Ordinal) &&
        text.Contains("\"numerals\":\"arab\"", StringComparison.Ordinal);

    /// <summary>Letters of the Arabic script in a text (an answer in Arabic has more of them than
    /// the same answer in English), written as they are or as JSON escapes (<c>\u0627</c>), which
    /// is how the API and the gates' normalised answers carry them.</summary>
    public static int ArabicLetters(string text) =>
        text.Count(c => c is >= '\u0600' and <= '\u06FF') + EscapedArabic().Count(text);

    [System.Text.RegularExpressions.GeneratedRegex(@"\\u06[0-9A-Fa-f]{2}")]
    private static partial System.Text.RegularExpressions.Regex EscapedArabic();
}
