using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.G2;
using Erp.Testing;

namespace Erp.Gates.Tests.Critic;

/// <summary>Critic p00 r12: proves each permission plant is live (and the product without it is right).</summary>
public sealed class CriticR12Proof(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private async Task<HttpClient> UserWith(HttpClient admin, string label, params string[] permissions)
    {
        var env = fixture.Env;
        var roleIds = new List<Guid>();
        if (permissions.Length > 0)
        {
            var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Critic {label}", nameAr = $"ناقد {label}", permissions });
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
            roleIds.Add((await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        var email = $"critic.{label}.{Guid.NewGuid():N}@{env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"Critic {label}", language = "en", password = ErpTestEnvironment.Password, roleIds });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return await env.SignInAsync(email);
    }

    /// <summary>P8: a user who may only create roles creates one, then may neither change nor delete it.</summary>
    [Fact]
    public async Task Creating_a_record_gives_no_right_to_change_or_delete_it()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        using var creator = await UserWith(admin, "createonly", "identity.roles.create");
        var created = await creator.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Made by creator", nameAr = "أنشأه المنشئ", permissions = Array.Empty<string>() });
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        var role = JsonDocument.Parse(createdBody).RootElement;
        var id = role.GetProperty("id").GetGuid();
        var answers = new List<string>();
        using (var read = await creator.GetAsync($"/api/identity/roles/{id}")) answers.Add($"GET role -> {(int)read.StatusCode} {(await read.Content.ReadAsStringAsync())[..Math.Min(160, (int)(read.Content.Headers.ContentLength ?? 0))]}");
        using (var put = await creator.PutAsJsonAsync($"/api/identity/roles/{id}", new { nameEn = "Changed by creator without update", nameAr = "غيّره المنشئ", permissions = Array.Empty<string>(), version = role.GetProperty("version").GetUInt32() }))
            answers.Add($"PUT role -> {(int)put.StatusCode} {await put.Content.ReadAsStringAsync()}");
        using (var copy = await creator.PostAsJsonAsync($"/api/identity/roles/{id}/copy", new { nameEn = "Copy", nameAr = "نسخة" }))
            answers.Add($"POST copy -> {(int)copy.StatusCode}");
        using (var del = await creator.DeleteAsync($"/api/identity/roles/{id}"))
            answers.Add($"DELETE role -> {(int)del.StatusCode} {await del.Content.ReadAsStringAsync()}");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.Where(a => !a.StartsWith("POST copy")).All(a => a.Contains("-> 403") || a.Contains("-> 404")), string.Join("\n", answers));
    }

    /// <summary>P7: holding identity.users.update alone does not open the users list.</summary>
    [Fact]
    public async Task Update_alone_does_not_open_reads()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        using var updater = await UserWith(admin, "updateonly", "identity.users.update");
        using var list = await updater.GetAsync("/api/identity/users?take=1");
        Assert.True(list.StatusCode == HttpStatusCode.Forbidden, $"GET users -> {(int)list.StatusCode}");
    }

    /// <summary>P6 and P9: a user with no roles is denied their own record and every kernel endpoint.</summary>
    [Fact]
    public async Task No_roles_user_is_denied_own_record_and_kernel_endpoints()
    {
        var env = fixture.Env;
        using var nobody = await env.SignInAsync(env.Email(env.TenantA, "noaccess"));
        var session = await nobody.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var self = session.GetProperty("user").GetProperty("id").GetString();
        var answers = new List<string>();
        foreach (var path in new[] { $"/api/identity/users/{self}", $"/api/tenancy/access/{self}", "/api/shell/badges" })
        {
            using var r = await nobody.GetAsync(path);
            answers.Add($"GET {path} -> {(int)r.StatusCode}");
        }
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", answers));
        Assert.True(answers.All(a => a.EndsWith("-> 403") || a.EndsWith("-> 404")), string.Join("\n", answers));
    }
    /// <summary>P10: a permission taken away stops working at once.</summary>
    [Fact]
    public async Task A_permission_taken_away_stops_working_at_once()
    {
        var env = fixture.Env;
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Critic revoke", nameAr = "ناقد سحب", permissions = new[] { "identity.users.read" } })).Content.ReadFromJsonAsync<JsonElement>();
        var email = $"critic.revoke.{Guid.NewGuid():N}@{env.TenantA.EmailDomain}";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Critic revoke", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } })).StatusCode);
        using var user = await env.SignInAsync(email);
        using (var before = await user.GetAsync("/api/identity/users?take=1")) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using (var change = await admin.PutAsJsonAsync($"/api/identity/roles/{role.GetProperty("id").GetGuid()}", new { nameEn = "Critic revoke", nameAr = "ناقد سحب", permissions = new[] { "identity.roles.read" }, version = role.GetProperty("version").GetUInt32() }))
            Assert.True(change.IsSuccessStatusCode, await change.Content.ReadAsStringAsync());
        using var after = await user.GetAsync("/api/identity/users?take=1");
        Assert.True(after.StatusCode == HttpStatusCode.Forbidden, $"GET users after the role lost identity.users.read -> {(int)after.StatusCode}");
    }
}
