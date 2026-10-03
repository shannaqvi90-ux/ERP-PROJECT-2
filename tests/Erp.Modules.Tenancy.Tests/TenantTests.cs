using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Tenancy.Tests;

public sealed class TenancyFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The workspace screen's API: each signed-in user reads and renames only their own
/// workspace, with validation in the user's language and optimistic concurrency.</summary>
public sealed class TenantTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Each_user_reads_their_own_workspace()
    {
        foreach (var tenant in new[] { Env.TenantA, Env.TenantB })
        {
            using var viewer = await Env.SignInAsync(Env.Email(tenant, "viewer"));
            var response = await viewer.GetAsync("/api/tenancy/tenant");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);
            Assert.Equal(tenant.Id, body.GetProperty("id").GetGuid());
            Assert.Equal(tenant.Code, body.GetProperty("code").GetString());
            Assert.Equal("active", body.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Renaming_needs_both_names_and_the_version_and_refuses_a_stale_version()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var before = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant");
        var version = before.GetProperty("version").GetUInt32();

        var invalid = await admin.PutAsJsonAsync("/api/tenancy/tenant", new { nameEn = "", nameAr = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Json(invalid)).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("nameEn")[0].GetProperty("code").GetString());
        Assert.Equal("required", errors.GetProperty("nameAr")[0].GetProperty("code").GetString());
        Assert.Equal("required", errors.GetProperty("version")[0].GetProperty("code").GetString());

        var renamed = await admin.PutAsJsonAsync("/api/tenancy/tenant", new { nameEn = "  Alpha Trading Group LLC ", nameAr = "مجموعة ألفا للتجارة ذ.م.م", version });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var after = await Json(renamed);
        Assert.Equal("Alpha Trading Group LLC", after.GetProperty("nameEn").GetString());
        Assert.Equal("مجموعة ألفا للتجارة ذ.م.م", after.GetProperty("nameAr").GetString());
        Assert.Equal(before.GetProperty("code").GetString(), after.GetProperty("code").GetString());

        var stale = await admin.PutAsJsonAsync("/api/tenancy/tenant", new { nameEn = "Stale", nameAr = "قديم", version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency", (await Json(stale)).GetProperty("code").GetString());

        // The session shows the new name, and the other workspace is untouched.
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("Alpha Trading Group LLC", session.GetProperty("tenant").GetProperty("nameEn").GetString());
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        Assert.Equal(Env.TenantB.NameEn, (await other.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant")).GetProperty("nameEn").GetString());
    }

    [Fact]
    public async Task Reading_and_renaming_need_their_permissions()
    {
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        var current = await viewer.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant");
        var rename = await viewer.PutAsJsonAsync("/api/tenancy/tenant", new { nameEn = "Viewer rename", nameAr = "تغيير", version = current.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);

        using var noAccess = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
        Assert.Equal(HttpStatusCode.Forbidden, (await noAccess.GetAsync("/api/tenancy/tenant")).StatusCode);
    }
}
