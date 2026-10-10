using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 round 9, plant P4: a user manager who holds less than the Administrator asks
/// "Sign out everywhere" with removePasskeys=true on the Administrator. Must be 403 and change nothing.</summary>
public sealed partial class AuthTests
{
    [Fact]
    public async Task CriticR9Proof_a_weaker_user_manager_cannot_sign_out_and_strip_the_administrators_passkeys()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var adminId = (await Json(await admin.GetAsync("/api/auth/session"))).GetProperty("user").GetProperty("id").GetGuid();
        using var adminDevice = new SoftwarePasskey();
        Assert.Equal(HttpStatusCode.Created, (await adminDevice.RegisterAsync(admin, "Admin laptop")).StatusCode);
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new
        {
            nameEn = $"R9 helpdesk {Guid.NewGuid():N}"[..24], nameAr = $"مكتب مساعدة {Guid.NewGuid():N}"[..22],
            permissions = new[] { "identity.users.read", "identity.users.update", "identity.profile.update" },
        });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await Json(role)).GetProperty("id").GetGuid();
        var email = $"r9.helpdesk.{Guid.NewGuid():N}"[..28] + $"@{Env.TenantA.EmailDomain}";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email, displayName = "R9 helpdesk", language = "en", password = PasskeyPassword, mustChangePassword = false, roleIds = new[] { roleId },
        })).StatusCode);
        using var helpdesk = await Env.SignInAsync(email, PasskeyPassword);

        var passkeysBefore = await admin.GetStringAsync($"/api/identity/users/{adminId}/passkeys");
        var plain = await helpdesk.PostAsync($"/api/identity/users/{adminId}/sessions/revoke", null);
        var withPasskeys = await helpdesk.PostAsync($"/api/identity/users/{adminId}/sessions/revoke?removePasskeys=true", null);
        var body = await withPasskeys.Content.ReadAsStringAsync();
        Console.WriteLine($"CRITIC-R9 plain revoke on the Administrator: {(int)plain.StatusCode}; with removePasskeys=true: {(int)withPasskeys.StatusCode} {body}");
        using var check = await Env.SignInAsync(AdminA);
        var passkeysAfter = await check.GetStringAsync($"/api/identity/users/{adminId}/passkeys");
        Console.WriteLine($"CRITIC-R9 admin passkeys before: {passkeysBefore[..Math.Min(120, passkeysBefore.Length)]} after: {passkeysAfter}");
        Console.WriteLine($"CRITIC-R9 admin's earlier session still signed in: {(await Json(await admin.GetAsync("/api/auth/session"))).GetProperty("authenticated").GetBoolean()}");
        var adminStillIn = (await Json(await admin.GetAsync("/api/auth/session"))).GetProperty("authenticated").GetBoolean();
        Assert.True(plain.StatusCode == HttpStatusCode.Forbidden && withPasskeys.StatusCode == HttpStatusCode.Forbidden && passkeysBefore == passkeysAfter && adminStillIn,
            $"plain revoke on the Administrator: {(int)plain.StatusCode}; with removePasskeys=true: {(int)withPasskeys.StatusCode} {body}; " +
            $"Administrator's passkeys before: {passkeysBefore}; after: {passkeysAfter}; Administrator's earlier session still signed in: {adminStillIn}");
    }
}
