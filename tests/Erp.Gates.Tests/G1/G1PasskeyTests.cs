using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, passkeys. A passkey sign-in starts before any workspace is known: the device's user handle
/// names the workspace and user, the server looks for the passkey there and checks the device's
/// signature. The HTTP attack's generated bodies always carry an e-mail and a password, so they
/// take the password path; this attack goes down the passkey path itself. Tenant A, holding a
/// passkey of its own and knowing everything an attacker could learn about tenant B's (its
/// administrator's user id and the passkey's credential id, read here as the database superuser),
/// answers sign-in challenges with its own key under handles naming tenant B, its user, its
/// credential. Every answer must be the same refusal as for a handle naming nothing, tenant B's
/// passkeys may not change, and tenant A's signed-in calls on tenant B's passkeys (list, remove,
/// rename) must find nothing.
/// </summary>
public sealed class G1PasskeyTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    private static string Handle(Guid tenant, Guid user)
    {
        var bytes = new byte[32];
        tenant.TryWriteBytes(bytes.AsSpan(0, 16), bigEndian: true, out _);
        user.TryWriteBytes(bytes.AsSpan(16, 16), bigEndian: true, out _);
        return SoftwarePasskey.Base64Url(bytes);
    }

    private async Task<string> TenantBStateAsync()
    {
        await using var admin = await Env.OpenAdminAsync();
        // Tenant B's passkeys, every column (other gate checks of this environment sign tenant B
        // in and out meanwhile, so its sessions and sign-in history are not compared here; the
        // HTTP attack compares every table of tenant B).
        return await DbCatalog.ScalarAsync<string>(admin,
            "SELECT coalesce(string_agg(p::text, '|' ORDER BY p.id), '') FROM identity.passkeys p WHERE p.tenant_id = @b", ("b", Env.TenantB.Id));
    }

    [Fact]
    public async Task Tenant_A_cannot_sign_in_to_or_reach_tenant_Bs_passkeys_and_learns_nothing_of_them()
    {
        Guid victimUser;
        Guid victimPasskey;
        byte[] victimCredential;
        Guid attackerUser;
        await using (var admin = await Env.OpenAdminAsync())
        {
            (victimUser, victimPasskey, victimCredential) = (await DbCatalog.ReadAsync(admin,
                "SELECT user_id, id, credential_id FROM identity.passkeys WHERE tenant_id = @b ORDER BY created_at LIMIT 1",
                r => (r.GetGuid(0), r.GetGuid(1), (byte[])r.GetValue(2)), ("b", Env.TenantB.Id))).Single();
            attackerUser = await DbCatalog.ScalarAsync<Guid>(admin, "SELECT id FROM identity.users WHERE tenant_id = @a AND email_normalized = @e",
                ("a", Env.TenantA.Id), ("e", Env.Email(Env.TenantA, "admin")));
        }
        using var attacker = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var device = new SoftwarePasskey();
        Assert.Equal(HttpStatusCode.Created, (await device.RegisterAsync(attacker, "Attacker device")).StatusCode);
        var before = await TenantBStateAsync();

        var answers = new Dictionary<string, string>();
        async Task Try(string what, Func<string, JsonObject> answer)
        {
            using var anonymous = Env.CreateClient();
            var challenge = await SoftwarePasskey.ChallengeAsync(anonymous);
            foreach (var (client, who) in new[] { (anonymous, "anonymous"), (attacker, "tenant A signed in") })
            {
                using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new JsonObject { ["passkey"] = answer(challenge) });
                var text = Regex.Replace(await response.Content.ReadAsStringAsync(), "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"\"");
                answers[$"{what} ({who})"] = $"{(int)response.StatusCode} {text}";
                if (who == "anonymous")
                {
                    challenge = await SoftwarePasskey.ChallengeAsync(anonymous);
                }
            }
        }

        await Try("A's key, the handle names tenant B's user", c => device.Assertion(c, userHandle: Handle(Env.TenantB.Id, victimUser)));
        await Try("A's key and B's credential id, the handle names B's user", c => device.Assertion(c, userHandle: Handle(Env.TenantB.Id, victimUser), credentialId: victimCredential));
        await Try("A's key, the handle names A's own user in tenant B", c => device.Assertion(c, userHandle: Handle(Env.TenantB.Id, attackerUser)));
        await Try("A's key and B's credential id, the handle names A's own user", c => device.Assertion(c, userHandle: Handle(Env.TenantA.Id, attackerUser), credentialId: victimCredential));
        await Try("A's key, the handle names tenant B with a user that exists nowhere", c => device.Assertion(c, userHandle: Handle(Env.TenantB.Id, Guid.NewGuid())));
        await Try("A's key, the handle names a workspace that exists nowhere", c => device.Assertion(c, userHandle: Handle(Guid.NewGuid(), Guid.NewGuid())));

        var problems = answers.Where(a => !a.Value.StartsWith("401 ", StringComparison.Ordinal) || !a.Value.Contains("\"auth.passkeyFailed\"", StringComparison.Ordinal))
            .Select(a => $"{a.Key}: {a.Value}").ToList();
        if (answers.Values.Distinct().Count() > 1)
        {
            problems.Add("the refusals differ, so they tell tenant A what exists in tenant B:\n" + string.Join("\n", answers.Select(a => $"  {a.Key}: {a.Value}")));
        }

        // Tenant A's own calls on tenant B's passkeys find nothing.
        foreach (var (method, path) in new[]
                 {
                     ("GET", $"/api/identity/users/{victimUser}/passkeys"),
                     ("DELETE", $"/api/identity/users/{victimUser}/passkeys"),
                     ("DELETE", $"/api/identity/me/passkeys/{victimPasskey}"),
                     ("PUT", $"/api/identity/me/passkeys/{victimPasskey}"),
                 })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "PUT")
            {
                request.Content = JsonContent.Create(new { name = "Taken over" });
            }
            using var response = await attacker.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                problems.Add($"{method} {path} by tenant A answered {(int)response.StatusCode} (expected 404): {text}");
            }
            if (text.Contains(victimUser.ToString(), StringComparison.OrdinalIgnoreCase) || text.Contains(victimPasskey.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{method} {path} by tenant A carries tenant B's id: {text}");
            }
        }

        var after = await TenantBStateAsync();
        if (after != before)
        {
            problems.Add("tenant B's passkeys changed during the attack");
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{answers.Count} passkey answers under tenant B's handles judged, 4 calls on tenant B's passkeys");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(answers.Count >= Ratchet.Min("g1.passkeyAttacks"), $"g1.passkeyAttacks: {answers.Count}; ratchet minimum {Ratchet.Min("g1.passkeyAttacks")}");
    }
}
