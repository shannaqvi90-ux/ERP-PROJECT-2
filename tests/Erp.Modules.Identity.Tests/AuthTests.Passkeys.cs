using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>
/// Passkeys through the API with a software authenticator: adding one (only soon after signing
/// in, only an answer for this site, made by a verified person, to this session's challenge),
/// signing in with one from any start (no e-mail typed), and every way an answer is refused with
/// the same answer: another site or origin, a person not verified, a forged or reused challenge, a
/// tampered signature, a counter that goes back, a user handle naming another workspace or user,
/// a removed passkey, an inactive account.
/// </summary>
public sealed partial class AuthTests
{
    private const string PasskeyPassword = "Passkey-Pass-2026!";

    /// <summary>A user of tenant A with a role that lets them manage their own account, signed in now.</summary>
    private async Task<(string Email, Guid Id, HttpClient Client)> PasskeyUserAsync(string tag)
    {
        using var admin = await Env.SignInAsync(AdminA);
        var roles = await Json(await admin.GetAsync("/api/identity/roles?take=200"));
        var items = roles.ValueKind == JsonValueKind.Array ? roles : roles.GetProperty("items");
        var role = items.EnumerateArray()
            .Where(r => !r.GetProperty("isSystem").GetBoolean() && r.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "identity.profile.update"))
            .Select(r => r.GetProperty("id").GetGuid()).FirstOrDefault();
        var email = $"passkey.{tag}.{Guid.NewGuid():N}"[..28] + $"@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email, displayName = $"Passkey {tag}", language = "en", password = PasskeyPassword, roleIds = role == Guid.Empty ? Array.Empty<Guid>() : [role],
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();
        return (email, id, await Env.SignInAsync(email, PasskeyPassword));
    }

    private static string Scrub(string text) => Regex.Replace(text, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"\"");

    [Fact]
    public async Task A_passkey_is_added_soon_after_signing_in_and_signs_in_from_any_start_without_an_email()
    {
        var (email, id, client) = await PasskeyUserAsync("main");
        using var signedIn0 = client;
        using var device = new SoftwarePasskey();

        var mine = await Json(await client.GetAsync("/api/identity/me/passkeys"));
        Assert.Empty(mine.GetProperty("items").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("canAddUntil").ValueKind);

        var added = await device.RegisterAsync(client, "Work laptop");
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var passkey = await Json(added);
        Assert.Equal("Work laptop", passkey.GetProperty("name").GetString());
        mine = await Json(await client.GetAsync("/api/identity/me/passkeys"));
        Assert.Single(mine.GetProperty("items").EnumerateArray());

        // The same device cannot be added twice.
        Assert.Equal(HttpStatusCode.Conflict, (await device.RegisterAsync(client, "Again")).StatusCode);

        // Anonymous: the session answer carries a fresh challenge; the passkey alone signs in.
        using var anonymous = Env.CreateClient();
        var session = await Json(await anonymous.GetAsync("/api/auth/session"));
        Assert.False(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal("localhost", session.GetProperty("passkey").GetProperty("rpId").GetString());
        var signedIn = await device.SignInAsync(anonymous);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(email, (await Json(signedIn)).GetProperty("user").GetProperty("email").GetString());
        var now = await Json(await anonymous.GetAsync("/api/auth/session"));
        Assert.True(now.GetProperty("authenticated").GetBoolean());
        Assert.Equal(id, now.GetProperty("user").GetProperty("id").GetGuid());
        Assert.False(now.TryGetProperty("passkey", out _));

        // The use is recorded on the passkey and in the sign-in history; it is not an audited change.
        mine = await Json(await client.GetAsync("/api/identity/me/passkeys"));
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("items")[0].GetProperty("lastUsedAt").ValueKind);
        await using (var owner = await Env.OpenAdminAsync())
        {
            await using var audit = new Npgsql.NpgsqlCommand(
                "SELECT string_agg(action, ',' ORDER BY occurred_at) FROM audit.entries WHERE table_name = 'passkeys' AND record_id = @id", owner);
            audit.Parameters.AddWithValue("id", passkey.GetProperty("id").GetGuid());
            Assert.Equal("insert", await audit.ExecuteScalarAsync());
            await using var history = new Npgsql.NpgsqlCommand(
                "SELECT count(*) FROM identity.sign_in_attempts WHERE user_id = @id AND outcome = 'succeeded'", owner);
            history.Parameters.AddWithValue("id", id);
            Assert.True((long)(await history.ExecuteScalarAsync())! >= 2);
        }

        // Rename, then remove: a removed passkey no longer signs in.
        var renamed = await client.PutAsJsonAsync($"/api/identity/me/passkeys/{passkey.GetProperty("id").GetGuid()}", new { name = "Old laptop" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Old laptop", (await Json(renamed)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/identity/me/passkeys/{passkey.GetProperty("id").GetGuid()}")).StatusCode);
        using var later = Env.CreateClient();
        var refused = await device.SignInAsync(later);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("auth.passkeyFailed", (await Json(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_passkey_answer_is_refused_unless_every_check_holds_and_every_refusal_reads_the_same()
    {
        var (_, id, client) = await PasskeyUserAsync("checks");
        using var signedIn0 = client;
        using var device = new SoftwarePasskey(counting: true);
        Assert.Equal(HttpStatusCode.Created, (await device.RegisterAsync(client)).StatusCode);
        using var anonymous = Env.CreateClient();
        var handle = SoftwarePasskey.FromBase64Url(device.UserHandle!);
        string HandleOf(Guid tenant, Guid user)
        {
            var bytes = new byte[32];
            tenant.TryWriteBytes(bytes.AsSpan(0, 16), bigEndian: true, out _);
            user.TryWriteBytes(bytes.AsSpan(16, 16), bigEndian: true, out _);
            return SoftwarePasskey.Base64Url(bytes);
        }
        Assert.Equal(Env.TenantA.Id, new Guid(handle.AsSpan(0, 16), bigEndian: true));
        Assert.Equal(id, new Guid(handle.AsSpan(16, 16), bigEndian: true));
        Guid tenantBUser;
        await using (var owner = await Env.OpenAdminAsync())
        await using (var read = new Npgsql.NpgsqlCommand("SELECT id FROM identity.users WHERE tenant_id = @t ORDER BY email LIMIT 1", owner))
        {
            read.Parameters.AddWithValue("t", Env.TenantB.Id);
            tenantBUser = (Guid)(await read.ExecuteScalarAsync())!;
        }

        async Task<string> Send(JsonObject passkey)
        {
            var response = await anonymous.PostAsJsonAsync("/api/auth/sign-in", new JsonObject { ["passkey"] = passkey.DeepClone() });
            return $"{(int)response.StatusCode} {Scrub(await response.Content.ReadAsStringAsync())}";
        }
        // Each challenge from a client that has not signed in (a sign-in below leaves its cookie).
        async Task<string> Challenge()
        {
            using var fresh = Env.CreateClient();
            return await SoftwarePasskey.ChallengeAsync(fresh);
        }

        var refusals = new Dictionary<string, string>
        {
            ["another site"] = await Send(device.Assertion(await Challenge(), rpId: "evil.example")),
            ["another origin"] = await Send(device.Assertion(await Challenge(), origin: "http://evil.example")),
            ["person not verified"] = await Send(device.Assertion(await Challenge(), flags: 0x01)),
            ["nobody present"] = await Send(device.Assertion(await Challenge(), flags: 0x04)),
            ["a registration answer"] = await Send(device.Assertion(await Challenge(), type: "webauthn.create")),
            ["a challenge the server never issued"] = await Send(device.Assertion(SoftwarePasskey.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(58)))),
            ["the handle names tenant B, the user is A's"] = await Send(device.Assertion(await Challenge(), userHandle: HandleOf(Env.TenantB.Id, id))),
            ["the handle names a user of tenant B"] = await Send(device.Assertion(await Challenge(), userHandle: HandleOf(Env.TenantB.Id, tenantBUser))),
            ["the handle names another user of A"] = await Send(device.Assertion(await Challenge(), userHandle: HandleOf(Env.TenantA.Id, tenantBUser))),
            ["the handle names a workspace that exists nowhere"] = await Send(device.Assertion(await Challenge(), userHandle: HandleOf(Guid.NewGuid(), Guid.NewGuid()))),
            ["an unknown credential"] = await Send(device.Assertion(await Challenge(), credentialId: System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
        };
        var tampered = device.Assertion(await Challenge());
        var signature = SoftwarePasskey.FromBase64Url(tampered["signature"]!.GetValue<string>());
        signature[^1] ^= 0x01;
        tampered["signature"] = SoftwarePasskey.Base64Url(signature);
        refusals["a tampered signature"] = await Send(tampered);

        foreach (var (what, refusal) in refusals)
        {
            Assert.True(refusal.StartsWith("401 ", StringComparison.Ordinal) && refusal.Contains("\"auth.passkeyFailed\"", StringComparison.Ordinal), $"{what}: {refusal}");
        }
        // One answer for every refusal: nothing tells which workspace or user exists.
        Assert.Single(refusals.Values.Distinct());

        // The truthful answer signs in once; the same answer again is a replay.
        var answer = device.Assertion(await Challenge());
        Assert.StartsWith("200 ", await Send(answer));
        Assert.StartsWith("401 ", await Send(answer));
        // An older challenge than the last one answered is refused too.
        var older = await Challenge();
        Assert.StartsWith("200 ", await Send(device.Assertion(await Challenge())));
        Assert.Equal(refusals["another site"], await Send(device.Assertion(older)));
        // The device keeps a counter: one that does not move forward is a copied key.
        Assert.Equal(refusals["another site"], await Send(device.Assertion(await Challenge(), counter: 1)));
        Assert.StartsWith("200 ", await Send(device.Assertion(await Challenge())));

        // An inactive account does not sign in with its passkey.
        await fixture.ExecAsync("UPDATE identity.users SET is_active = false WHERE id = @id", ("id", id));
        Assert.Equal(refusals["another site"], await Send(device.Assertion(await Challenge())));
    }

    [Fact]
    public async Task Adding_a_passkey_needs_a_recent_sign_in_this_sessions_challenge_and_an_answer_for_this_site()
    {
        var (_, id, client) = await PasskeyUserAsync("adding");
        using var signedIn0 = client;
        using var device = new SoftwarePasskey();

        async Task<JsonObject> Options(HttpClient c) => (await c.PostAsync("/api/identity/me/passkeys/options", null) is var r && r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<JsonObject>()
            : throw new InvalidOperationException($"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"))!;
        async Task<HttpStatusCode> Register(JsonObject body) => (await client.PostAsJsonAsync("/api/identity/me/passkeys", body)).StatusCode;

        var options = await Options(client);
        Assert.Equal("required", options["authenticatorSelection"]!["userVerification"]!.GetValue<string>());
        Assert.Equal("none", options["attestation"]!.GetValue<string>());
        Assert.Contains(options["pubKeyCredParams"]!.AsArray(), p => p!["alg"]!.GetValue<int>() == -7);

        // Another site, another origin, nobody verified, a challenge of another session.
        Assert.Equal(HttpStatusCode.BadRequest, await Register(device.Registration(options, "x", rpId: "evil.example")));
        device.Origin = "http://evil.example";
        Assert.Equal(HttpStatusCode.BadRequest, await Register(device.Registration(await Options(client), "x")));
        device.Origin = SoftwarePasskey.DefaultOrigin;
        Assert.Equal(HttpStatusCode.BadRequest, await Register(device.Registration(await Options(client), "x", flags: 0x41)));
        using (var second = await Env.SignInAsync(Env.Email(Env.TenantA, "admin")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, await Register(device.Registration(await Options(second), "x")));
        }
        Assert.Equal(HttpStatusCode.BadRequest, await Register(new JsonObject { ["name"] = "", ["clientDataJson"] = "", ["attestationObject"] = "" }));
        Assert.Empty((await Json(await client.GetAsync("/api/identity/me/passkeys"))).GetProperty("items").EnumerateArray());

        // A session signed in more than a few minutes ago must sign in again first.
        await fixture.ExecAsync("UPDATE identity.sessions SET created_at = now() - interval '11 minutes' WHERE user_id = @id", ("id", id));
        Assert.Equal(JsonValueKind.Null, (await Json(await client.GetAsync("/api/identity/me/passkeys"))).GetProperty("canAddUntil").ValueKind);
        var late = await client.PostAsync("/api/identity/me/passkeys/options", null);
        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
        Assert.Equal("auth.recentSignInRequired", (await Json(late)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, await Register(device.Registration(options, "x")));
    }

    [Fact]
    public async Task Only_the_owner_changes_a_passkey_and_an_administrator_removes_another_users_passkeys()
    {
        var (_, ownerId, owner) = await PasskeyUserAsync("owner");
        var (_, _, other) = await PasskeyUserAsync("other");
        using var a = owner;
        using var b = other;
        using var device = new SoftwarePasskey();
        var created = await Json(await device.RegisterAsync(owner));
        var passkeyId = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/identity/me/passkeys/{passkeyId}", new { name = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/identity/me/passkeys/{passkeyId}")).StatusCode);
        // Without the permission to change users, nobody removes another user's passkeys.
        using (var noAccess = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.DeleteAsync($"/api/identity/users/{ownerId}/passkeys")).StatusCode);
        }

        using var admin = await Env.SignInAsync(AdminA);
        var listed = await Json(await admin.GetAsync($"/api/identity/users/{ownerId}/passkeys"));
        Assert.Equal(passkeyId, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.False(listed[0].TryGetProperty("publicKey", out _));
        var removed = await admin.DeleteAsync($"/api/identity/users/{ownerId}/passkeys");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(1, (await Json(removed)).GetProperty("removed").GetInt32());
        using var anonymous = Env.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.SignInAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/identity/users/{Guid.NewGuid()}/passkeys")).StatusCode);
    }
}
