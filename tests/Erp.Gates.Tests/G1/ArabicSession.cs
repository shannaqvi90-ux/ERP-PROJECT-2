using System.Net;
using System.Net.Http.Json;
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
public static class ArabicSession
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
    /// the same answer in English).</summary>
    public static int ArabicLetters(string text) => text.Count(c => c is >= '؀' and <= 'ۿ');
}
