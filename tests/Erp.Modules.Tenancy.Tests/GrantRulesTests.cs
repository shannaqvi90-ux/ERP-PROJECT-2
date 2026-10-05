using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>Company and branch access is a grant (critic p02 round 2): nobody takes access from a
/// stronger user or gives beyond their own; branch limits hold for every branch read and write; a
/// company code, unique across the workspace, is picked only by someone who sees every company.</summary>
public sealed class GrantRulesTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Access(Guid company, params Guid[] branches) =>
        new { companyId = company, allBranches = branches.Length == 0, branchIds = branches };

    private async Task<(HttpClient Admin, Guid AdminId, Guid X, Guid Y, List<Guid> BranchesOfX)> SetUpAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?sort=code")).GetProperty("items").EnumerateArray()
            .Where(c => c.GetProperty("code").GetString() is "A1-CO" or "A2-CO").Select(c => c.GetProperty("id").GetGuid()).ToList();
        var branches = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{companies[0]}'")}&sort=code")).GetProperty("items")
            .EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        return (admin, adminId, companies[0], companies[1], branches);
    }

    private async Task<(Guid Id, string Email)> NewUserAsync(HttpClient admin, string local, Guid[] roleIds, params object[] access)
    {
        var email = $"{local}.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = local, language = "en", password = ErpTestEnvironment.Password, roleIds });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();
        if (access.Length > 0)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAccessAsync(id, new { companies = access })).StatusCode);
        }
        return (id, email);
    }

    private static async Task<Guid> AdministratorRoleAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();

    [Fact]
    public async Task A_user_holding_only_the_access_permission_cannot_take_access_from_the_administrator()
    {
        var (admin, adminId, x, _, _) = await SetUpAsync();
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new
        {
            nameEn = $"Access clerk {Guid.NewGuid():N}"[..30], nameAr = "موظف صلاحيات " + Guid.NewGuid().ToString("N")[..6],
            permissions = new[] { "tenancy.access.read", "tenancy.access.update" },
        });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var (_, clerkEmail) = await NewUserAsync(admin, "clerk", [(await Json(role)).GetProperty("id").GetGuid()], Access(x));
        using var clerk = await Env.SignInAsync(clerkEmail);

        var seen = await clerk.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{adminId}");
        Assert.False(seen.GetProperty("canEdit").GetBoolean());
        Assert.Equal("tenancy.access.readOnly.permissions", seen.GetProperty("readOnlyReason").GetString());
        var strip = await clerk.PutAccessAsync(adminId, new { companies = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, strip.StatusCode);
        var problem = await Json(strip);
        Assert.Equal("tenancy.userBeyondOwn", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        // Still in every company.
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace")).GetProperty("companies").GetArrayLength();
        Assert.True(companies >= 2);

        // A user without roles is the clerk's to change, within X.
        var (plainId, _) = await NewUserAsync(admin, "plain", [], Access(x));
        Assert.Equal(HttpStatusCode.OK, (await clerk.PutAccessAsync(plainId, new { companies = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task A_branch_limited_administrator_sees_reads_changes_and_gives_only_their_own_branch()
    {
        var (admin, _, x, _, branchesOfX) = await SetUpAsync();
        var (mine, other) = (branchesOfX[0], branchesOfX[1]);
        var administrator = await AdministratorRoleAsync(admin);
        var (_, managerEmail) = await NewUserAsync(admin, "branchmanager", [administrator], Access(x, mine));
        using var manager = await Env.SignInAsync(managerEmail);

        // Only the own branch exists for them.
        var list = await manager.GetFromJsonAsync<JsonElement>("/api/tenancy/branches");
        Assert.Equal(new[] { mine }, list.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/tenancy/branches/{other}")).StatusCode);
        var otherBody = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches/{other}");
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PutAsJsonAsync($"/api/tenancy/branches/{other}", otherBody)).StatusCode);
        var company = (await manager.GetFromJsonAsync<JsonElement>("/api/tenancy/companies")).GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == x);
        Assert.Equal(1, company.GetProperty("branchCount").GetInt32());

        // Their own branch: the name may change, the code (unique in the company) may not.
        var own = (await manager.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/tenancy/branches/{mine}"))!;
        own["nameEn"] = "Renamed by the branch manager";
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/tenancy/branches/{mine}", own)).StatusCode);
        var renamed = (await manager.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/tenancy/branches/{mine}"))!;
        renamed["code"] = "BM-NEW";
        var recode = await manager.PutAsJsonAsync($"/api/tenancy/branches/{mine}", renamed);
        Assert.Equal(HttpStatusCode.Forbidden, recode.StatusCode);
        Assert.Equal("tenancy.branchNeedsEveryBranch", (await Json(recode)).GetProperty("code").GetString());
        var create = await manager.PostAsJsonAsync("/api/tenancy/branches", new { companyId = x, nameEn = "New shop", country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("tenancy.branchNeedsEveryBranch", (await Json(create)).GetProperty("code").GetString());

        // Giving: only the own branch.
        var (emptyId, _) = await NewUserAsync(admin, "empty", []);
        var options = await manager.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{emptyId}");
        var option = options.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == x);
        Assert.False(option.GetProperty("canGiveAllBranches").GetBoolean());
        Assert.Equal(new[] { mine }, option.GetProperty("branches").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()));
        var all = await manager.PutAccessAsync(emptyId, new { companies = new[] { Access(x) } });
        Assert.Equal(HttpStatusCode.Forbidden, all.StatusCode);
        Assert.Equal("tenancy.grantBeyondOwn", (await Json(all)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAccessAsync(emptyId, new { companies = new[] { Access(x, other) } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAccessAsync(emptyId, new { companies = new[] { Access(x, mine) } })).StatusCode);

        // Taking: never from someone who works in more branches of the company.
        var (everyId, _) = await NewUserAsync(admin, "every", [], Access(x));
        var narrow = await manager.PutAccessAsync(everyId, new { companies = new[] { Access(x, mine) } });
        Assert.Equal(HttpStatusCode.Forbidden, narrow.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAccessAsync(everyId, new { companies = Array.Empty<object>() })).StatusCode);
        var kept = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{everyId}");
        Assert.True(kept.GetProperty("companies")[0].GetProperty("allBranches").GetBoolean());
    }

    [Fact]
    public async Task Only_someone_who_works_in_every_company_creates_a_company_or_changes_a_company_code()
    {
        var (admin, _, x, y, _) = await SetUpAsync();
        var administrator = await AdministratorRoleAsync(admin);
        var (_, oneEmail) = await NewUserAsync(admin, "onecompany", [administrator], Access(x));
        using var one = await Env.SignInAsync(oneEmail);
        var codeOfY = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{y}")).GetProperty("code").GetString();

        // The same answer for a code only company Y uses and for a code used nowhere.
        foreach (var code in new[] { codeOfY, "ZZ-NOWHERE" })
        {
            var created = await one.PostAsJsonAsync("/api/tenancy/companies", new
            {
                code, legalNameEn = "Falcon Logistics LLC", legalNameAr = "فالكون للخدمات اللوجستية ذ.م.م", baseCurrency = "AED",
                fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true,
            });
            Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
            Assert.Equal("tenancy.companyNeedsEveryCompany", (await Json(created)).GetProperty("code").GetString());
        }
        var own = (await one.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/tenancy/companies/{x}"))!;
        var originalName = own["legalNameEn"]!.GetValue<string>();
        own["code"] = codeOfY;
        Assert.Equal(HttpStatusCode.Forbidden, (await one.PutAsJsonAsync($"/api/tenancy/companies/{x}", own)).StatusCode);
        own["code"] = "ZZ-NOWHERE";
        Assert.Equal(HttpStatusCode.Forbidden, (await one.PutAsJsonAsync($"/api/tenancy/companies/{x}", own)).StatusCode);
        // Other details of the own company are theirs to change.
        own.Remove("code");
        own["legalNameEn"] = originalName + " ";
        Assert.Equal(HttpStatusCode.OK, (await one.PutAsJsonAsync($"/api/tenancy/companies/{x}", own)).StatusCode);

        // The administrator works in every company: creates one and works in it; the counts follow.
        var made = await admin.PostAsJsonAsync("/api/tenancy/companies", new
        {
            legalNameEn = "Grant Rules Trading LLC", legalNameAr = "قواعد المنح للتجارة ذ.م.م", baseCurrency = "AED",
            fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true,
        });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        await using var owner = await Env.OpenAdminAsync();
        await using (var command = new Npgsql.NpgsqlCommand(
            "SELECT t.company_count = (SELECT count(*) FROM tenancy.companies c WHERE c.tenant_id = t.id) FROM tenancy.tenants t", owner))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                Assert.True(reader.GetBoolean(0), "a workspace's company count differs from its companies");
            }
        }
        await using (var command = new Npgsql.NpgsqlCommand("""
            SELECT count(*) FROM (SELECT tenant_id, user_id, count(*) AS n FROM tenancy.user_company_access GROUP BY tenant_id, user_id) a
              FULL JOIN tenancy.user_company_totals t ON t.tenant_id = a.tenant_id AND t.user_id = a.user_id
             WHERE coalesce(a.n, 0) <> coalesce(t.company_count, 0)
            """, owner))
        {
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        // The one-company administrator still may not: the new company is one more they lack.
        var again = await one.PostAsJsonAsync("/api/tenancy/companies", new { legalNameEn = "Another LLC", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
    }
    [Fact]
    public async Task Saving_access_needs_the_version_read_and_two_saves_never_overwrite_each_other()
    {
        var (admin, _, x, y, _) = await SetUpAsync();
        var (targetId, _) = await NewUserAsync(admin, "versioned", [], Access(x));
        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}");
        var version = read.GetProperty("version").GetUInt32();

        // No version: refused, nothing changes.
        var missing = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { Access(x), Access(y) } });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("required", (await Json(missing)).GetProperty("errors").GetProperty("version")[0].GetProperty("code").GetString());

        // The version read: saved, and the answer carries the new version.
        var saved = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { Access(x), Access(y) }, version });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var newVersion = (await Json(saved)).GetProperty("version").GetUInt32();
        Assert.NotEqual(version, newVersion);
        Assert.Equal(newVersion, await admin.AccessVersionAsync(targetId));

        // The old version again (a second administrator who read before the first saved): 409, nothing changes.
        var stale = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { Access(x) }, version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency", (await Json(stale)).GetProperty("code").GetString());
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}")).GetProperty("companies").GetArrayLength());

        // Two saves sent at the same moment with the same version: one wins, the other is told.
        var current = await admin.AccessVersionAsync(targetId);
        using var second = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var answers = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { Access(x) }, version = current }),
            second.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { Access(y) }, version = current }));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, answers.Select(a => a.StatusCode).Order());
        var winner = (await Json(answers.Single(a => a.StatusCode == HttpStatusCode.OK))).GetProperty("companies");
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}")).GetProperty("companies").GetArrayLength());
        Assert.Equal(winner.GetRawText(), (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}")).GetProperty("companies").GetRawText());

        // A user nobody gave access yet: two first saves at once, one wins.
        var (freshId, _) = await NewUserAsync(admin, "fresh", []);
        var first = await admin.AccessVersionAsync(freshId);
        var firsts = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/tenancy/access/{freshId}", new { companies = new[] { Access(x) }, version = first }),
            second.PutAsJsonAsync($"/api/tenancy/access/{freshId}", new { companies = new[] { Access(y) }, version = first }));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, firsts.Select(a => a.StatusCode).Order());
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{freshId}")).GetProperty("companies").GetArrayLength());
    }

    [Fact]
    public async Task The_access_version_tells_nothing_about_companies_the_caller_cannot_see()
    {
        var (admin, _, x, y, _) = await SetUpAsync();
        var (xAdminId, xAdminEmail) = await NewUserAsync(admin, "xversion", [await AdministratorRoleAsync(admin)], Access(x));
        var (targetId, _) = await NewUserAsync(admin, "seen", [], Access(x));
        using var xAdmin = await Env.SignInAsync(xAdminEmail);
        var before = await xAdmin.AccessVersionAsync(targetId);
        // The tenant administrator gives the target company Y, which the company X administrator cannot see.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAccessAsync(targetId, new { companies = new[] { Access(x), Access(y) } })).StatusCode);
        Assert.Equal(before, await xAdmin.AccessVersionAsync(targetId));
        // A change in company X is seen.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAccessAsync(targetId, new { companies = new[] { Access(x, (await BranchesOfAsync(admin, x))[0]), Access(y) } })).StatusCode);
        Assert.NotEqual(before, await xAdmin.AccessVersionAsync(targetId));
        _ = xAdminId;
    }

    [Fact]
    public async Task A_new_company_is_given_to_every_administrator_of_the_whole_workspace_and_to_nobody_holding_less()
    {
        var (admin, adminId, x, y, _) = await SetUpAsync();
        var administrator = await AdministratorRoleAsync(admin);
        // Critic p02 round 3: a second administrator created a company and the tenant Administrator
        // lost "every company", could no longer create companies, and could no longer be managed.
        var everyCompany = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200")).GetProperty("items").EnumerateArray()
            .Select(c => Access(c.GetProperty("id").GetGuid())).ToArray();
        var (_, secondEmail) = await NewUserAsync(admin, "second", [administrator], everyCompany);
        var (peerId, _) = await NewUserAsync(admin, "peer", [administrator], everyCompany);
        var (lesserId, _) = await NewUserAsync(admin, "lesser", [], everyCompany);
        var (oneCompanyId, _) = await NewUserAsync(admin, "onecompany", [administrator], Access(x));
        using var second = await Env.SignInAsync(secondEmail);
        var created = await second.PostAsJsonAsync("/api/tenancy/companies", new { code = "", legalNameEn = $"Peer Co {Guid.NewGuid():N}"[..20], legalNameAr = "شركة الأقران", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var company = (await Json(created)).GetProperty("id").GetGuid();

        async Task<bool> WorksIn(Guid user) =>
            (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{user}")).GetProperty("companies").EnumerateArray().Any(c => c.GetProperty("companyId").GetGuid() == company);
        Assert.True(await WorksIn(adminId), "the tenant Administrator, who worked in every company, works in the new one");
        Assert.True(await WorksIn(peerId), "another administrator of every company works in the new one");
        Assert.False(await WorksIn(lesserId), "a user holding fewer permissions than the creator is given it by hand, not automatically");
        Assert.False(await WorksIn(oneCompanyId), "an administrator of one company does not gain the new one");

        // The tenant Administrator still holds the whole workspace: creating a company works.
        var again = await admin.PostAsJsonAsync("/api/tenancy/companies", new { code = "", legalNameEn = $"Again Co {Guid.NewGuid():N}"[..20], legalNameAr = "شركة مرة أخرى", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        _ = y;
    }

    private static async Task<List<Guid>> BranchesOfAsync(HttpClient admin, Guid company) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{company}'")}&sort=code")).GetProperty("items")
            .EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
}
