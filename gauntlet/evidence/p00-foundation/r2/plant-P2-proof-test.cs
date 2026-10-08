using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Gates.Tests.CriticProof;

public sealed class CriticProofTests(Erp.Gates.Tests.G2.G2Fixture fixture) : IClassFixture<Erp.Gates.Tests.G2.G2Fixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_read_only_user_reactivates_a_deactivated_user()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Reader", nameAr = "قارئ", permissions = new[] { "identity.users.read" } })).Content.ReadFromJsonAsync<JsonElement>();
        var reader = $"reader@{Env.TenantA.EmailDomain}";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users", new { email = reader, displayName = "Reader", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } })).StatusCode);
        var victim = await (await admin.PostAsJsonAsync("/api/identity/users", new { email = $"leaver@{Env.TenantA.EmailDomain}", displayName = "Leaver", language = "en", password = ErpTestEnvironment.Password, roleIds = Array.Empty<Guid>() })).Content.ReadFromJsonAsync<JsonElement>();
        var id = victim.GetProperty("id").GetGuid();
        var off = await admin.PutAsJsonAsync($"/api/identity/users/{id}", new { displayName = "Leaver", language = "en", isActive = false, roleIds = Array.Empty<Guid>(), version = victim.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        using var client = await Env.SignInAsync(reader);
        using var response = await client.PostAsync($"/api/identity/users/{id}/reactivate", null);
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}");
        TestContext.Current.TestOutputHelper?.WriteLine($"reactivate by a user holding only identity.users.read: {(int)response.StatusCode}; isActive now {after.GetProperty("isActive")}");
        Assert.True(after.GetProperty("isActive").GetBoolean());
    }
}
