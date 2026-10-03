using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Correcting accounts: an invitation sent to a mistyped address is fixed or deleted, a
/// user carries a name in Arabic script, and roles are changed only by someone who holds
/// everything they grant.</summary>
public sealed class UserCorrectionsTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private string AdminA => Env.Email(Env.TenantA, "admin");

    private string NewEmail(string local) => $"{local}.{Guid.NewGuid():N}"[..(local.Length + 9)] + $"@{Env.TenantA.EmailDomain}";

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await Json(response);
    }

    private async Task<HttpClient> SignedInWithAsync(HttpClient admin, string[] permissions)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var role = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Holder {tag}", nameAr = $"حامل {tag}", permissions }));
        var email = NewEmail("holder");
        await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = $"Holder {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { role.GetProperty("id").GetGuid() } }));
        return await Env.SignInAsync(email);
    }

    [Fact]
    public async Task A_user_who_never_signed_in_is_deleted_and_anyone_who_signed_in_stays()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var invited = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users", new { email = NewEmail("typo"), displayName = "Typo", language = "en", roleIds = Array.Empty<Guid>() }));
        var id = invited.GetProperty("id").GetGuid();

        // Without the permission: refused.
        using (var updater = await SignedInWithAsync(admin, ["identity.users.read", "identity.users.update"]))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await updater.DeleteAsync($"/api/identity/users/{id}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/identity/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/identity/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/identity/users/{id}")).StatusCode);

        // The audit trail keeps the deletion.
        await using var owner = await Env.OpenAdminAsync();
        await using (var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE table_schema = 'identity' AND table_name = 'users' AND record_id = @id AND action = 'delete'", owner))
        {
            command.Parameters.AddWithValue("id", id);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }

        // Someone who has signed in keeps their record: deactivate instead.
        var email = NewEmail("signed");
        var signed = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Signed", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() }));
        using (await Env.SignInAsync(email))
        {
        }
        var refused = await admin.DeleteAsync($"/api/identity/users/{signed.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("identity.userHasSignedIn", (await Json(refused)).GetProperty("code").GetString());

        // Nobody deletes themselves.
        var me = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/identity/users/{me}")).StatusCode);
    }

    [Fact]
    public async Task The_sign_in_address_of_another_user_is_corrected_and_stays_unique_in_the_workspace()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var wrong = NewEmail("wrong");
        var user = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email = wrong, displayName = "Hessa", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() }));
        var id = user.GetProperty("id").GetGuid();
        var right = NewEmail("Right");

        var saved = await admin.PutAsJsonAsync($"/api/identity/users/{id}",
            new { email = right, displayName = "Hessa", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = user.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await Json(saved);
        Assert.Equal(right, after.GetProperty("email").GetString());

        // The new address signs in (any case); the old one no longer does.
        using (await Env.SignInAsync(right.ToUpperInvariant()))
        {
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Env.SignInAsync(wrong));

        // Another user's address is taken; an invalid one is refused field by field.
        var taken = await admin.PutAsJsonAsync($"/api/identity/users/{id}",
            new { email = AdminA, displayName = "Hessa", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = after.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("identity.emailTaken", (await Json(taken)).GetProperty("code").GetString());
        var invalid = await admin.PutAsJsonAsync($"/api/identity/users/{id}",
            new { email = "not-an-address", displayName = "Hessa", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = after.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await Json(invalid)).GetProperty("errors").TryGetProperty("email", out _));

        // Nobody changes their own sign-in address through administration.
        var me = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{(await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid()}");
        var own = await admin.PutAsJsonAsync($"/api/identity/users/{me.GetProperty("id").GetGuid()}",
            new { email = NewEmail("me"), displayName = me.GetProperty("displayName").GetString(), language = "en", isActive = true, roleIds = me.GetProperty("roleIds"), version = me.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
    }

    [Fact]
    public async Task A_user_has_a_name_in_Arabic_that_the_list_filters()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var user = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/users",
            new { email = NewEmail("majid"), displayName = $"Majid Pillai {tag}", displayNameAr = $"ماجد بيلاي {tag}", language = "ar", roleIds = Array.Empty<Guid>() }));
        Assert.Equal($"ماجد بيلاي {tag}", user.GetProperty("displayNameAr").GetString());

        var found = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users?filter={Uri.EscapeDataString($"displayNameAr contains 'ماجد بيلاي {tag}'")}");
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        Assert.Equal(user.GetProperty("id").GetGuid(), found.GetProperty("items")[0].GetProperty("id").GetGuid());

        // Words in any order, any part of the name: three letters of each are enough.
        var quick = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users?search={Uri.EscapeDataString($"pil maj {tag}")}");
        Assert.Equal(1, quick.GetProperty("total").GetInt32());

        // Cleared with an empty value; left alone when the field is not sent.
        var id = user.GetProperty("id").GetGuid();
        var kept = await Json(await admin.PutAsJsonAsync($"/api/identity/users/{id}",
            new { displayName = $"Majid Pillai {tag}", language = "ar", isActive = true, roleIds = Array.Empty<Guid>(), version = user.GetProperty("version").GetUInt32() }));
        Assert.Equal($"ماجد بيلاي {tag}", kept.GetProperty("displayNameAr").GetString());
        var cleared = await Json(await admin.PutAsJsonAsync($"/api/identity/users/{id}",
            new { displayName = $"Majid Pillai {tag}", displayNameAr = "", language = "ar", isActive = true, roleIds = Array.Empty<Guid>(), version = kept.GetProperty("version").GetUInt32() }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("displayNameAr").ValueKind);
    }

    [Fact]
    public async Task A_role_granting_more_than_the_caller_holds_is_neither_renamed_copied_nor_deleted_by_them()
    {
        using var admin = await Env.SignInAsync(AdminA);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var strong = await CreatedAsync(await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = $"Password desk {tag}", nameAr = $"مكتب {tag}", permissions = new[] { "identity.users.read", "identity.users.resetPassword" } }));
        var id = strong.GetProperty("id").GetGuid();
        using var keeper = await SignedInWithAsync(admin, ["identity.roles.read", "identity.roles.update", "identity.roles.create", "identity.roles.delete", "identity.users.read"]);

        var rename = await keeper.PutAsJsonAsync($"/api/identity/roles/{id}",
            new { nameEn = $"Leavers {tag}", nameAr = $"مغادرون {tag}", permissions = strong.GetProperty("permissions"), version = strong.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal("identity.roleBeyondOwn", (await Json(rename)).GetProperty("code").GetString());
        var copy = await keeper.PostAsJsonAsync($"/api/identity/roles/{id}/copy", new { nameEn = $"Copy {tag}", nameAr = $"نسخة {tag}" });
        Assert.Equal(HttpStatusCode.Forbidden, copy.StatusCode);
        var delete = await keeper.DeleteAsync($"/api/identity/roles/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal("identity.roleBeyondOwn", (await Json(delete)).GetProperty("code").GetString());
        Assert.Equal($"Password desk {tag}", (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/roles/{id}")).GetProperty("nameEn").GetString());
    }
}
