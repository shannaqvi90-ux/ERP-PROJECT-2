using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: these tests create roles, users and company access.</summary>
public sealed class CompanyRoleFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2, roles held in one company ("accountant in the Dubai LLC, read-only in the JAFZA entity").
/// <list type="bullet">
/// <item>For every permission of the catalogue, a user holding it only in company X has it while
/// working in X and is denied it while working in Y: every endpoint that declares it answers 403
/// in Y, the session and the menu leave it out, and the sign-in answer (the first screen) agrees
/// with the company the user starts in.</item>
/// <item>An administrator of company X alone grants only within X: no role definition (roles hold
/// in every company), no role in every company, no role in Y; and acts on no one who holds
/// anything outside X.</item>
/// <item>A user's roles in a company the caller does not work in are not shown to the caller and
/// make the user untouchable for them: what those roles grant cannot be seen.</item>
/// </list>
/// </summary>
public sealed class G2CompanyRoleTests(CompanyRoleFixture fixture) : IClassFixture<CompanyRoleFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static readonly string[] Switching = ["tenancy.workplace.read", "tenancy.workplace.switch"];

    [Fact]
    public async Task A_permission_held_in_one_company_is_denied_while_working_in_another()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = await GateCompanies.OfAsync(admin);
        Assert.True(companies.Ids.Count == 2, "the gate fixture needs two companies in tenant A");
        var (x, y) = (companies.Ids[0], companies.Ids[1]);
        var staff = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Company role staff", nameAr = "موظفو أدوار الشركات", permissions = Switching });
        var catalogue = Env.Factory.Services.GetRequiredService<ModuleCatalog>().PermissionKeys.Where(p => !Switching.Contains(p)).Order(StringComparer.Ordinal).ToList();
        var endpoints = EndpointInventory.From(Env.Factory.Services).Where(e => !e.IsAnonymous).ToList();
        // The menu of a user without the company role, working in Y.
        var baseline = await SessionWithAsync(staff, admin, companies, y, "g2.company.base");
        var problems = new List<string>();
        var checks = 0;
        var n = 0;
        foreach (var permission in catalogue)
        {
            n++;
            var role = await CreateAsync(admin, "/api/identity/roles", new { nameEn = $"Only in X {n}", nameAr = $"في س فقط {n}", permissions = new[] { permission } });
            var email = $"g2.company.{n}@{Env.TenantA.EmailDomain}";
            var user = await CreateAsync(admin, "/api/identity/users", new
            {
                email, displayName = $"Company role {n}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
                roleIds = new[] { staff }, companyRoles = new[] { new { roleId = role, companyId = x } },
            });
            await companies.GiveAccessAsync(user);
            using var client = await Env.SignInAsync(email);

            await SwitchAsync(client, x);
            var inX = await SessionAsync(client);
            if (!inX.Permissions.Contains(permission))
            {
                problems.Add($"{permission}: held in company X, missing from the session while working in X");
            }
            var mine = endpoints.Where(e => e.Permission == permission).ToList();
            foreach (var endpoint in mine.Where(e => e.Method == "GET" && e.RouteParameters.Count == 0))
            {
                using var open = await client.GetAsync(endpoint.Pattern);
                if (open.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    problems.Add($"{permission}: {endpoint} answered {(int)open.StatusCode} while working in X, where the role is held");
                }
                checks++;
            }

            await SwitchAsync(client, y);
            var inY = await SessionAsync(client);
            if (inY.Permissions.Contains(permission))
            {
                problems.Add($"{permission}: held only in company X, still in the session while working in Y");
            }
            foreach (var entry in inY.Menu.Except(baseline.Menu))
            {
                problems.Add($"{permission}: menu entry {entry} shown while working in Y");
            }
            foreach (var endpoint in mine)
            {
                using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(_ => Guid.NewGuid().ToString()));
                if (endpoint.HasBody)
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }
                using var denied = await client.SendAsync(request);
                if (denied.StatusCode != HttpStatusCode.Forbidden)
                {
                    problems.Add($"{permission}: {endpoint} answered {(int)denied.StatusCode} while working in Y (expected 403)");
                }
                checks++;
            }

            // The sign-in answer is the first screen: it shows what the company the user starts in allows.
            var startY = await SignInAnswerAsync(email);
            if (startY.Permissions.Contains(permission))
            {
                problems.Add($"{permission}: the sign-in answer of a user starting in Y holds it");
            }
            await SwitchAsync(client, x);
            var startX = await SignInAnswerAsync(email);
            if (!startX.Permissions.Contains(permission))
            {
                problems.Add($"{permission}: the sign-in answer of a user starting in X lacks it");
            }
            checks += 4;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{catalogue.Count} permissions held in one company, {checks} checks");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checks >= Ratchet.Min("g2.companyRoleChecks"), $"{checks} company role checks; ratchet minimum {Ratchet.Min("g2.companyRoleChecks")}");
    }

    [Fact]
    public async Task An_administrator_of_one_company_grants_and_acts_only_within_it()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = await GateCompanies.OfAsync(admin);
        var (x, y) = (companies.Ids[0], companies.Ids[1]);
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var staff = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Company admin staff", nameAr = "موظفو مدير الشركة", permissions = Switching });
        var reader = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Company admin reader", nameAr = "قارئ مدير الشركة", permissions = new[] { "identity.users.read" } });

        async Task<Guid> UserAsync(string local, Guid[] everywhere, object[] inCompanies, bool access = true)
        {
            var id = await CreateAsync(admin, "/api/identity/users", new
            {
                email = $"{local}@{Env.TenantA.EmailDomain}", displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
                roleIds = everywhere, companyRoles = inCompanies,
            });
            if (access)
            {
                await companies.GiveAccessAsync(id);
            }
            return id;
        }

        var problems = new List<string>();
        var checks = 0;
        void Expect(string what, HttpStatusCode expected, HttpStatusCode actual, string text)
        {
            checks++;
            if (actual != expected)
            {
                problems.Add($"{what}: answered {(int)actual} (expected {(int)expected}): {(text.Length > 300 ? text[..300] : text)}");
            }
        }

        // The administrator of company X alone, working in X.
        await UserAsync("g2.xadmin", [staff], [new { roleId = administrator, companyId = x }]);
        using var xAdmin = await Env.SignInAsync($"g2.xadmin@{Env.TenantA.EmailDomain}");
        await SwitchAsync(xAdmin, x);
        var plain = await UserAsync("g2.xplain", [], []);
        var heldInY = await UserAsync("g2.xheldy", [], [new { roleId = reader, companyId = y }]);
        var heldEverywhere = await UserAsync("g2.xheldall", [reader], []);
        var heldInX = await UserAsync("g2.xheldx", [], [new { roleId = reader, companyId = x }]);

        var (s, t) = await SendAsync(xAdmin, HttpMethod.Post, "/api/identity/roles", new { nameEn = "X admin made", nameAr = "صنعه مدير س", permissions = new[] { "identity.users.read" } });
        Expect("an administrator of X only creating a role (roles hold in every company)", HttpStatusCode.Forbidden, s, t);
        foreach (var (label, roleIds, companyRoles) in new (string, Guid[], object[])[]
                 {
                     ("giving the Administrator role in Y", [], [new { roleId = administrator, companyId = y }]),
                     ("giving a reader role in Y", [], [new { roleId = reader, companyId = y }]),
                     ("giving a reader role in every company", [reader], []),
                     ("giving the Administrator role in every company", [administrator], []),
                 })
        {
            (s, t) = await UpdateAsync(admin, xAdmin, plain, roleIds, companyRoles);
            Expect($"an administrator of X only {label}", HttpStatusCode.Forbidden, s, t);
        }
        (s, t) = await UpdateAsync(admin, xAdmin, plain, [], [new { roleId = reader, companyId = x }]);
        Expect("an administrator of X giving a reader role in X (control)", HttpStatusCode.OK, s, t);
        foreach (var (label, target) in new[] { ("in Y", heldInY), ("in every company", heldEverywhere) })
        {
            (s, t) = await SendAsync(xAdmin, HttpMethod.Post, $"/api/identity/users/{target}/password", new { password = "Taken-Over-Password-1", mustChangePassword = false });
            Expect($"an administrator of X resetting the password of someone holding a role {label}", HttpStatusCode.Forbidden, s, t);
            (s, t) = await UpdateAsync(admin, xAdmin, target, null, null, displayName: "Taken over");
            Expect($"an administrator of X renaming someone holding a role {label}", HttpStatusCode.Forbidden, s, t);
        }
        (s, t) = await SendAsync(xAdmin, HttpMethod.Post, $"/api/identity/users/{heldInX}/password", new { password = "Within-X-Password-1", mustChangePassword = false });
        Expect("an administrator of X resetting the password of someone holding a role in X (control)", HttpStatusCode.OK, s, t);

        // Working in Y, the same administrator holds nothing.
        await SwitchAsync(xAdmin, y);
        using (var list = await xAdmin.GetAsync("/api/identity/users"))
        {
            Expect("an administrator of X listing users while working in Y", HttpStatusCode.Forbidden, list.StatusCode, "");
        }

        // An administrator of every company who works only in X does not see a user's roles in Y,
        // and leaves that user alone.
        var narrow = await UserAsync("g2.xonly", [administrator], [], access: false);
        await GiveAccessAsync(admin, narrow, [x]);
        using var narrowAdmin = await Env.SignInAsync($"g2.xonly@{Env.TenantA.EmailDomain}");
        var seen = await narrowAdmin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{heldInY}");
        checks++;
        if (seen.GetProperty("companyRoles").GetArrayLength() != 0 || !seen.GetProperty("rolesElsewhere").GetBoolean())
        {
            problems.Add($"a caller who works only in X sees {seen.GetProperty("companyRoles")} and rolesElsewhere {seen.GetProperty("rolesElsewhere")} for a user holding a role in Y");
        }
        var access = await narrowAdmin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{heldInY}/access");
        checks++;
        if (access.GetProperty("roles").GetArrayLength() != 0 || access.GetProperty("companies").GetArrayLength() != 0 || !access.GetProperty("rolesElsewhere").GetBoolean())
        {
            problems.Add($"a caller who works only in X sees the access of a user holding a role in Y: {access}");
        }
        (s, t) = await SendAsync(narrowAdmin, HttpMethod.Post, $"/api/identity/users/{heldInY}/password", new { password = "Taken-Over-Password-2", mustChangePassword = false });
        Expect("a caller who works only in X resetting the password of a user holding a role in Y", HttpStatusCode.Forbidden, s, t);
        // A user holding a role in X who also works in Y is beyond them too (critic p02 round 8: a
        // new password would let them sign in to Y as that user).
        (s, t) = await SendAsync(narrowAdmin, HttpMethod.Post, $"/api/identity/users/{heldInX}/password", new { password = "Within-X-Password-2", mustChangePassword = false });
        Expect("a caller who works only in X resetting the password of a user holding a role in X who also works in Y", HttpStatusCode.Forbidden, s, t);
        var onlyInX = await UserAsync("g2.xheldxonly", [], [new { roleId = reader, companyId = x }], access: false);
        await GiveAccessAsync(admin, onlyInX, [x]);
        (s, t) = await SendAsync(narrowAdmin, HttpMethod.Post, $"/api/identity/users/{onlyInX}/password", new { password = "Within-X-Password-3", mustChangePassword = false });
        Expect("a caller who works only in X resetting the password of a user holding a role in X who works only there (control)", HttpStatusCode.OK, s, t);

        TestContext.Current.TestOutputHelper?.WriteLine($"{checks} checks");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private sealed record SessionView(IReadOnlySet<string> Permissions, IReadOnlySet<string> Menu);

    private static SessionView View(JsonElement session) => new(
        session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal),
        session.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToHashSet(StringComparer.Ordinal));

    private static async Task<SessionView> SessionAsync(HttpClient client) => View(await client.GetFromJsonAsync<JsonElement>("/api/auth/session"));

    /// <summary>The session of a user holding only <paramref name="role"/>, working in <paramref name="company"/>: the menu a user without the company role sees.</summary>
    private async Task<SessionView> SessionWithAsync(Guid role, HttpClient admin, GateCompanies companies, Guid company, string local)
    {
        var email = $"{local}@{Env.TenantA.EmailDomain}";
        var id = await CreateAsync(admin, "/api/identity/users", new { email, displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { role } });
        await companies.GiveAccessAsync(id);
        using var client = await Env.SignInAsync(email);
        await SwitchAsync(client, company);
        return await SessionAsync(client);
    }

    private async Task<SessionView> SignInAnswerAsync(string email)
    {
        using var client = Env.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = ErpTestEnvironment.Password });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"signing in as {email} answered {(int)response.StatusCode}: {text}");
        return View(JsonDocument.Parse(text).RootElement);
    }

    private static async Task SwitchAsync(HttpClient client, Guid company)
    {
        using var response = await client.PutAsJsonAsync("/api/tenancy/workplace", new { companyId = company });
        Assert.True(response.IsSuccessStatusCode, $"switching to {company} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task GiveAccessAsync(HttpClient admin, Guid user, Guid[] companies)
    {
        // The access screen sends back the version it read (409 when stale).
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{user}")).GetProperty("version").GetUInt32();
        using var response = await admin.PutAsJsonAsync($"/api/tenancy/access/{user}", new { companies = companies.Select(c => new { companyId = c, allBranches = true, branchIds = Array.Empty<Guid>() }), version });
        Assert.True(response.IsSuccessStatusCode, $"giving access answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Edit a user as <paramref name="caller"/>: their own values, with the roles and
    /// company roles given (null: as they are) and, optionally, another name.</summary>
    private static async Task<(HttpStatusCode, string)> UpdateAsync(HttpClient admin, HttpClient caller, Guid user, Guid[]? roleIds, object[]? companyRoles, string? displayName = null)
    {
        var current = await admin.GetFromJsonAsync<JsonObject>($"/api/identity/users/{user}") ?? [];
        var body = new JsonObject
        {
            ["displayName"] = displayName ?? current["displayName"]!.GetValue<string>(),
            ["language"] = current["language"]!.GetValue<string>(),
            ["isActive"] = current["isActive"]!.GetValue<bool>(),
            ["roleIds"] = roleIds is null ? current["roleIds"]!.DeepClone() : JsonSerializer.SerializeToNode(roleIds),
            ["version"] = current["version"]!.DeepClone(),
        };
        if (companyRoles is not null)
        {
            body["companyRoles"] = JsonSerializer.SerializeToNode(companyRoles);
        }
        return await SendAsync(caller, HttpMethod.Put, $"/api/identity/users/{user}", body);
    }

    private static async Task<(HttpStatusCode, string)> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json") };
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateAsync(HttpClient admin, string path, object body)
    {
        var (status, text) = await SendAsync(admin, HttpMethod.Post, path, body);
        if (status != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} as administrator answered {(int)status}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }
}
