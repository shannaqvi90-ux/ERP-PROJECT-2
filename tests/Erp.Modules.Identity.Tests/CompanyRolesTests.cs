using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Identity.Tests;

/// <summary>Its own environment: these tests assign roles per company and change where users start.</summary>
public sealed class CompanyRolesFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// Roles held in one company and the company a user starts work in, through the API: what the
/// record and the access view show, how assignments change and are audited, what deleting a role
/// or a user does to them, and the rules for another user's default company (only a company they
/// work in, never one's own, the version read).
/// </summary>
public sealed class CompanyRolesTests(CompanyRolesFixture fixture) : IClassFixture<CompanyRolesFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Admin, Guid X, Guid Y)> AdminAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.True(companies.Count >= 2, "the fixture needs two companies");
        return (admin, companies[0], companies[1]);
    }

    private static async Task<Guid> RoleAsync(HttpClient admin, string name, params string[] permissions)
    {
        var response = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = name, nameAr = $"دور {name}", permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> UserAsync(HttpClient admin, string local, object[] companyRoles, Guid[]? roleIds = null)
    {
        var response = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"{local}@{Env.TenantA.EmailDomain}", displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = roleIds ?? [], companyRoles,
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static async Task GiveAccessAsync(HttpClient admin, Guid user, params Guid[] companies)
    {
        var response = await admin.PutAsJsonAsync($"/api/tenancy/access/{user}", new { companies = companies.Select(c => new { companyId = c, allBranches = true, branchIds = Array.Empty<Guid>() }) });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Roles_in_one_company_are_created_shown_changed_and_explained()
    {
        var (admin, x, y) = await AdminAsync();
        var reader = await RoleAsync(admin, "Company reader", "identity.users.read");
        var created = await UserAsync(admin, "cr.shown", [new { roleId = reader, companyId = x }]);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(reader, created.GetProperty("companyRoles")[0].GetProperty("roleId").GetGuid());
        Assert.Equal(x, created.GetProperty("companyRoles")[0].GetProperty("companyId").GetGuid());
        Assert.False(created.GetProperty("rolesElsewhere").GetBoolean());

        // Moved to Y: the record changes version, and the audit trail keeps both rows' history.
        var before = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}");
        var moved = await admin.PutAsJsonAsync($"/api/identity/users/{id}", new
        {
            displayName = "cr.shown", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = before.GetProperty("version").GetUInt32(),
            companyRoles = new[] { new { roleId = reader, companyId = y } },
        });
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        var after = await Json(moved);
        Assert.Equal(y, after.GetProperty("companyRoles")[0].GetProperty("companyId").GetGuid());
        Assert.NotEqual(before.GetProperty("version").GetUInt32(), after.GetProperty("version").GetUInt32());
        Assert.Equal(after.GetProperty("version").GetUInt32(), (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}")).GetProperty("version").GetUInt32());

        // A stale version is refused, and leaving companyRoles out leaves them as they are.
        var stale = await admin.PutAsJsonAsync($"/api/identity/users/{id}", new
        {
            displayName = "cr.shown", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = before.GetProperty("version").GetUInt32(),
            companyRoles = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var renamed = await admin.PutAsJsonAsync($"/api/identity/users/{id}", new
        {
            displayName = "cr.renamed", language = "en", isActive = true, roleIds = Array.Empty<Guid>(), version = after.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(y, (await Json(renamed)).GetProperty("companyRoles")[0].GetProperty("companyId").GetGuid());

        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}/access");
        var role = Assert.Single(access.GetProperty("roles").EnumerateArray());
        Assert.Equal(y, role.GetProperty("companyId").GetGuid());
        var permission = Assert.Single(access.GetProperty("permissions").EnumerateArray());
        Assert.Equal("identity.users.read", permission.GetProperty("key").GetString());
        Assert.Equal(y, permission.GetProperty("grants")[0].GetProperty("companyId").GetGuid());
        Assert.Equal(y, Assert.Single(access.GetProperty("companies").EnumerateArray()).GetProperty("id").GetGuid());

        await using var owner = new NpgsqlConnection(Env.AdminConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.entries WHERE table_name = 'user_company_roles' AND tenant_id = @t AND changes::text LIKE '%' || @u || '%'", owner);
        command.Parameters.AddWithValue("t", Env.TenantA.Id);
        command.Parameters.AddWithValue("u", id.ToString());
        Assert.True((long)(await command.ExecuteScalarAsync())! >= 3, "inserting X, deleting X and inserting Y are audited");
    }

    [Fact]
    public async Task Invalid_company_roles_are_refused_with_field_errors()
    {
        var (admin, x, _) = await AdminAsync();
        var reader = await RoleAsync(admin, "Company reader 2", "identity.users.read");
        foreach (var (label, companyRoles) in new (string, object[])[]
                 {
                     ("unknown company", [new { roleId = reader, companyId = Guid.NewGuid() }]),
                     ("unknown role", [new { roleId = Guid.NewGuid(), companyId = x }]),
                     ("another workspace's company", [new { roleId = reader, companyId = Env.TenantB.Id }]),
                     ("missing role", [new { companyId = x }]),
                 })
        {
            var response = await admin.PostAsJsonAsync("/api/identity/users", new
            {
                email = $"cr.bad.{Guid.NewGuid():N}"[..20] + $"@{Env.TenantA.EmailDomain}", displayName = "Bad", language = "en", password = ErpTestEnvironment.Password,
                companyRoles,
            });
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{label}: {(int)response.StatusCode}");
            Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("companyRoles", out _), label);
        }
    }

    [Fact]
    public async Task Deleting_a_role_or_a_user_removes_their_company_assignments()
    {
        var (admin, x, y) = await AdminAsync();
        var temporary = await RoleAsync(admin, "Temporary company role", "identity.users.read");
        var user = (await UserAsync(admin, "cr.deleted", [new { roleId = temporary, companyId = x }, new { roleId = temporary, companyId = y }])).GetProperty("id").GetGuid();
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/roles/{temporary}")).GetProperty("userCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/identity/roles/{temporary}")).StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{user}");
        Assert.Equal(0, after.GetProperty("companyRoles").GetArrayLength());
        Assert.False(after.GetProperty("rolesElsewhere").GetBoolean());

        var kept = await RoleAsync(admin, "Kept company role", "identity.users.read");
        var leaver = (await UserAsync(admin, "cr.leaver", [new { roleId = kept, companyId = x }])).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/identity/users/{leaver}")).StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/roles/{kept}")).GetProperty("userCount").GetInt32());
    }

    [Fact]
    public async Task An_administrator_sets_where_another_user_starts_work()
    {
        var (admin, x, y) = await AdminAsync();
        var staff = await RoleAsync(admin, "Starters", "tenancy.workplace.read", "tenancy.workplace.switch");
        var user = await UserAsync(admin, "cr.starter", [], [staff]);
        var id = user.GetProperty("id").GetGuid();

        // No company yet: nothing to choose, and choosing one they do not work in is a field error.
        var none = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}/default-company");
        Assert.Equal(0, none.GetProperty("companies").GetArrayLength());
        var refused = await admin.PutAsJsonAsync($"/api/identity/users/{id}/default-company", new { companyId = x, version = none.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("identityNotTheirCompany", (await Json(refused)).GetProperty("errors").GetProperty("companyId")[0].GetProperty("code").GetString());

        await GiveAccessAsync(admin, id, x, y);
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}/default-company");
        Assert.Equal(2, current.GetProperty("companies").GetArrayLength());
        var set = await admin.PutAsJsonAsync($"/api/identity/users/{id}/default-company", new { companyId = y, version = current.GetProperty("version").GetUInt32() });
        Assert.True(set.IsSuccessStatusCode, await set.Content.ReadAsStringAsync());
        var chosen = await Json(set);
        Assert.Equal(y, chosen.GetProperty("companyId").GetGuid());

        // The user starts there.
        using var starter = await Env.SignInAsync($"cr.starter@{Env.TenantA.EmailDomain}");
        Assert.Equal(y, (await starter.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace")).GetProperty("companyId").GetGuid());

        // The version read is required to change it again; an old one is a conflict.
        var stale = await admin.PutAsJsonAsync($"/api/identity/users/{id}/default-company", new { companyId = x, version = current.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var cleared = await admin.PutAsJsonAsync($"/api/identity/users/{id}/default-company", new { companyId = (Guid?)null, version = chosen.GetProperty("version").GetUInt32() });
        Assert.True(cleared.IsSuccessStatusCode, await cleared.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, (await Json(cleared)).GetProperty("companyId").ValueKind);

        // Nobody sets their own here (they switch in the top bar).
        var self = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var selfId = self.GetProperty("user").GetProperty("id").GetGuid();
        var mine = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{selfId}/default-company");
        var own = await admin.PutAsJsonAsync($"/api/identity/users/{selfId}/default-company", new { companyId = y, version = mine.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
    }

    [Fact]
    public async Task The_seeded_accountant_manages_the_first_company_and_reads_the_second()
    {
        using var accountant = await Env.SignInAsync(Env.Email(Env.TenantA, "accountant"));
        var (admin, x, y) = await AdminAsync();
        using (admin)
        {
            var mine = (await accountant.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
            var record = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{mine}");
            Assert.Equal(2, record.GetProperty("companyRoles").GetArrayLength());
            await accountant.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = x });
            var inX = (await accountant.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
            await accountant.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = y });
            var inY = (await accountant.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
            Assert.Contains("tenancy.branches.update", inX);
            Assert.DoesNotContain("tenancy.branches.update", inY);
            Assert.Contains("tenancy.branches.read", inY);
        }
    }
}
