using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>Which companies and branches a user may work in, the working company and branch,
/// and how the company scope follows them on every request.</summary>
public sealed class AccessTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Admin, Guid X, Guid Y, List<Guid> BranchesOfX)> SetUpAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?sort=code")).GetProperty("items").EnumerateArray()
            .Where(c => c.GetProperty("code").GetString() is "A1-CO" or "A2-CO").Select(c => c.GetProperty("id").GetGuid()).ToList();
        var branches = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{companies[0]}'")}")).GetProperty("items")
            .EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        return (admin, companies[0], companies[1], branches);
    }

    private async Task<(Guid Id, string Email)> NewUserAsync(HttpClient admin, string local, bool administrator)
    {
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray();
        var role = roles.First(r => r.GetProperty("isSystem").GetBoolean() == administrator).GetProperty("id").GetGuid();
        var email = $"{local}.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = local, language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return ((await Json(created)).GetProperty("id").GetGuid(), email);
    }

    [Fact]
    public async Task A_new_user_works_in_no_company_until_given_access_and_then_only_in_the_companies_given()
    {
        var (admin, x, y, branchesOfX) = await SetUpAsync();
        var (id, email) = await NewUserAsync(admin, "scoped", administrator: true);
        using (var before = await Env.SignInAsync(email))
        {
            Assert.Equal(0, (await before.GetFromJsonAsync<JsonElement>("/api/tenancy/companies")).GetProperty("total").GetInt32());
            var none = await before.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
            Assert.Equal(JsonValueKind.Null, none.GetProperty("companyId").ValueKind);
        }

        var granted = await admin.PutAsJsonAsync($"/api/tenancy/access/{id}", new
        {
            companies = new[] { new { companyId = x, allBranches = false, branchIds = new[] { branchesOfX[1] } } },
        });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var access = await Json(granted);
        Assert.Equal(branchesOfX[1], access.GetProperty("companies")[0].GetProperty("branchIds")[0].GetGuid());

        using var user = await Env.SignInAsync(email);
        var companies = await user.GetFromJsonAsync<JsonElement>("/api/tenancy/companies");
        Assert.Equal(x, companies.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/tenancy/companies/{y}")).StatusCode);
        Assert.Equal(0, (await user.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{y}'")}")).GetProperty("total").GetInt32());

        // Starts in the one company, in the one branch they may work in.
        var workplace = await user.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
        Assert.Equal(x, workplace.GetProperty("companyId").GetGuid());
        Assert.Equal(branchesOfX[1], workplace.GetProperty("branchId").GetGuid());
        Assert.Equal(branchesOfX[1], workplace.GetProperty("companies")[0].GetProperty("branches").EnumerateArray().Single().GetProperty("id").GetGuid());

        // Cannot switch to the other company or to a branch not given.
        var toY = await user.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = y, branchId = (Guid?)null });
        Assert.Equal("tenancyNotYourCompany", (await Json(toY)).GetProperty("errors").GetProperty("companyId")[0].GetProperty("code").GetString());
        var toBranch = await user.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = x, branchId = branchesOfX[0] });
        Assert.Equal("tenancyNotYourBranch", (await Json(toBranch)).GetProperty("errors").GetProperty("branchId")[0].GetProperty("code").GetString());

        // Cannot change a company it may not work in, even holding every permission.
        var body = new { code = "A2-CO", legalNameEn = "Taken", legalNameAr = "مأخوذة", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true, version = 1u };
        Assert.Equal(HttpStatusCode.NotFound, (await user.PutAsJsonAsync($"/api/tenancy/companies/{y}", body)).StatusCode);
    }

    [Fact]
    public async Task Administrators_give_only_their_own_companies_and_never_change_their_own_access()
    {
        var (admin, x, y, _) = await SetUpAsync();
        var (limitedId, limitedEmail) = await NewUserAsync(admin, "limited", administrator: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/access/{limitedId}",
            new { companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } } })).StatusCode);
        var (targetId, _) = await NewUserAsync(admin, "target", administrator: false);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}",
            new { companies = new[] { new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } } })).StatusCode);

        using var limited = await Env.SignInAsync(limitedEmail);
        // Sees only its own company among the target's access (Y is invisible to it).
        var seen = await limited.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}");
        Assert.Equal(0, seen.GetProperty("companies").GetArrayLength());
        Assert.Equal(x, seen.GetProperty("options").EnumerateArray().Single().GetProperty("id").GetGuid());

        // Granting Y is refused; granting X keeps the target's Y untouched.
        var grantY = await limited.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } } });
        Assert.Equal(HttpStatusCode.BadRequest, grantY.StatusCode);
        Assert.Equal("unknownIds", (await Json(grantY)).GetProperty("errors").GetProperty("companies")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await limited.PutAsJsonAsync($"/api/tenancy/access/{targetId}",
            new { companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } } })).StatusCode);
        var full = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{targetId}");
        Assert.Equal(new[] { x, y }.Order(), full.GetProperty("companies").EnumerateArray().Select(c => c.GetProperty("companyId").GetGuid()).Order());

        // Own access cannot be changed by oneself.
        var self = await limited.PutAsJsonAsync($"/api/tenancy/access/{limitedId}", new { companies = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("tenancy.cannotChangeOwnAccess", (await Json(self)).GetProperty("code").GetString());
        Assert.True((await limited.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{limitedId}")).GetProperty("isCaller").GetBoolean());

        // Branches must belong to the company they are listed under; a company may not be listed twice.
        var branchOfY = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{y}'")}")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var wrong = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { new { companyId = x, allBranches = false, branchIds = new[] { branchOfY } } } });
        Assert.Equal("tenancyAccessBranchOfOtherCompany", (await Json(wrong)).GetProperty("errors").GetProperty("companies")[0].GetProperty("code").GetString());
        var twice = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new
        {
            companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() }, new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } },
        });
        Assert.Equal("tenancyAccessCompanyTwice", (await Json(twice)).GetProperty("errors").GetProperty("companies")[0].GetProperty("code").GetString());
        var empty = await admin.PutAsJsonAsync($"/api/tenancy/access/{targetId}", new { companies = new[] { new { companyId = x, allBranches = false, branchIds = Array.Empty<Guid>() } } });
        Assert.Equal("tenancyAccessBranchesRequired", (await Json(empty)).GetProperty("errors").GetProperty("companies")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/tenancy/access/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_working_company_is_kept_across_sessions_and_falls_back_when_access_is_removed()
    {
        var (admin, x, y, _) = await SetUpAsync();
        var (id, email) = await NewUserAsync(admin, "switcher", administrator: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/access/{id}", new
        {
            companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() }, new { companyId = y, allBranches = true, branchIds = Array.Empty<Guid>() } },
        })).StatusCode);

        using (var user = await Env.SignInAsync(email))
        {
            Assert.Equal(x, (await user.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace")).GetProperty("companyId").GetGuid());
            var switched = await user.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = y, branchId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            var body = await Json(switched);
            Assert.Equal(y, body.GetProperty("companyId").GetGuid());
            Assert.NotEqual(JsonValueKind.Null, body.GetProperty("branchId").ValueKind);
        }
        using (var again = await Env.SignInAsync(email))
        {
            Assert.Equal(y, (await again.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace")).GetProperty("companyId").GetGuid());
        }

        // Access to Y removed: the next request works in X.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/access/{id}",
            new { companies = new[] { new { companyId = x, allBranches = true, branchIds = Array.Empty<Guid>() } } })).StatusCode);
        using var after = await Env.SignInAsync(email);
        Assert.Equal(x, (await after.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace")).GetProperty("companyId").GetGuid());
    }

    [Fact]
    public async Task The_access_list_pages_users_with_their_companies()
    {
        var (admin, _, _, _) = await SetUpAsync();
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access?search={Uri.EscapeDataString(Env.Email(Env.TenantA, "admin"))}");
        var row = page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("email").GetString() == Env.Email(Env.TenantA, "admin"));
        Assert.Equal(new[] { "A1-CO", "A2-CO" }, row.GetProperty("companies").EnumerateArray().Select(c => c.GetProperty("code").GetString()).Order());
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/access?take=10");
        Assert.True(all.GetProperty("total").GetInt32() > 10);
        Assert.Equal(10, all.GetProperty("items").GetArrayLength());
    }
}
