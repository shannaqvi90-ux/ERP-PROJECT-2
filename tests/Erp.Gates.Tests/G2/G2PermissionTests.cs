using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Hosting;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: these tests create roles and users.</summary>
public sealed class G2Fixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2. Every endpoint declares one permission or sits on the reviewed anonymous allowlist. A user
/// whose roles grant nothing is denied everything; granting exactly one permission opens exactly
/// the endpoints that declare it and nothing else, and the shell menu shows only what is granted.
/// </summary>
public sealed class G2PermissionTests(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private IReadOnlyList<ApiEndpoint> Endpoints => EndpointInventory.From(Env.Factory.Services);

    private IReadOnlyList<ApiEndpoint> Permissioned => Endpoints.Where(e => !e.IsAnonymous).ToList();

    [Fact]
    public void Every_endpoint_declares_exactly_one_permission_or_is_reviewed_anonymous()
    {
        var problems = PermissionDeclarationProblems(Endpoints);
        var allowlist = Repo.ReadReviewedList("tests/Gates/anonymous-allowlist.txt");
        Assert.All(allowlist, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason), $"{a.Entry} needs a reason"));
        var anonymous = Endpoints.Where(e => e.IsAnonymous).Select(e => e.Key).Distinct().Order(StringComparer.Ordinal).ToList();
        var listed = allowlist.Select(a => a.Entry).Order(StringComparer.Ordinal).ToList();
        var notListed = anonymous.Except(listed).ToList();
        var stale = listed.Except(anonymous).ToList();
        if (notListed.Count > 0) problems.Add("Anonymous endpoints missing from tests/Gates/anonymous-allowlist.txt: " + string.Join(", ", notListed));
        if (stale.Count > 0) problems.Add("Allowlist entries with no such anonymous endpoint: " + string.Join(", ", stale));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(anonymous.Count <= Ratchet.Max("g2.anonymousEndpoints"),
            $"{anonymous.Count} anonymous endpoints; ratchet maximum {Ratchet.Max("g2.anonymousEndpoints")}");
        Assert.True(Permissioned.Count >= Ratchet.Min("g2.permissionedEndpoints"),
            $"{Permissioned.Count} permissioned endpoints; ratchet minimum {Ratchet.Min("g2.permissionedEndpoints")}");
    }

    /// <summary>Each endpoint declares exactly one permission, or is anonymous through the
    /// reviewed helper only; nothing lets ASP.NET Core skip authorization behind a permission.</summary>
    public static List<string> PermissionDeclarationProblems(IEnumerable<ApiEndpoint> endpoints)
    {
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint.IsAnonymous && endpoint.Permissions.Count > 0)
            {
                problems.Add($"{endpoint}: both anonymous and permissioned");
            }
            else if (endpoint.AllowsAnonymous && !endpoint.IsAnonymous)
            {
                // .AllowAnonymous() (or [AllowAnonymous]) skips authorization, so a permission the
                // endpoint also declares is never checked: only the reviewed helper may do this.
                problems.Add($"{endpoint}: allows anonymous callers (IAllowAnonymous) without AllowAnonymousReviewed; any permission it declares ({string.Join(", ", endpoint.Permissions)}) is skipped");
            }
            else if (endpoint.IsAnonymous && !endpoint.AllowsAnonymous)
            {
                problems.Add($"{endpoint}: carries an anonymous reason but ASP.NET Core still requires a signed-in caller; use AllowAnonymousReviewed");
            }
            else if (!endpoint.IsAnonymous && endpoint.Permissions.Count != 1)
            {
                problems.Add($"{endpoint}: declares {endpoint.Permissions.Count} permissions");
            }
        }
        return problems;
    }

    [Fact]
    public void Every_endpoint_under_a_reviewed_prefix_declares_exactly_its_reviewed_permission()
    {
        var maps = ReviewedPermissionMap.Load();
        Assert.Contains(maps, m => m.File == "identity.txt");
        Assert.Contains(maps, m => m.File == "tenancy.txt" && m.Prefixes.Contains("/api/tenancy/"));
        var (problems, checkedCount) = ReviewedPermissionMap.Check(Endpoints, maps);
        TestContext.Current.TestOutputHelper?.WriteLine($"{checkedCount} endpoints compared with {maps.Count} reviewed map(s)");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedCount >= Ratchet.Min("g2.reviewedPermissionEndpoints"),
            $"{checkedCount} endpoints compared with the reviewed map; ratchet minimum {Ratchet.Min("g2.reviewedPermissionEndpoints")}");
    }

    /// <summary>
    /// No module's routes escape review (critic p02 round 2, plant P1: nothing reviewed the
    /// permission of any /api/tenancy endpoint, so the logo replacement guarded by
    /// tenancy.workplace.switch passed). Every endpoint under <c>/api/&lt;module&gt;/</c> of a
    /// registered module must sit under a prefix of a reviewed map. The lists module's endpoints
    /// are generated per registered list (<c>/api/lists/&lt;list key&gt;/…</c>): each must declare
    /// exactly that list's own permission, and the shared-view writes the sharing permission.
    /// </summary>
    [Fact]
    public void Every_module_route_is_under_a_reviewed_map_or_derives_its_permission_from_its_list()
    {
        var maps = ReviewedPermissionMap.Load();
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var problems = new List<string>();
        var covered = 0;
        foreach (var endpoint in Endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var module = catalog.Modules.FirstOrDefault(m => endpoint.Pattern.StartsWith($"/api/{m.Name}/", StringComparison.Ordinal));
            if (module is null)
            {
                continue;
            }
            if (module.Name == "lists")
            {
                var rest = endpoint.Pattern["/api/lists/".Length..];
                var list = catalog.Lists.Where(l => rest.StartsWith(l.Key + "/", StringComparison.Ordinal)).MaxBy(l => l.Key.Length);
                if (list is null)
                {
                    problems.Add($"{endpoint.Key}: under /api/lists/ but names no registered list; review it in a map");
                    continue;
                }
                var shareWrite = rest[(list.Key.Length + 1)..].StartsWith("shared-views", StringComparison.Ordinal) && endpoint.Method != "GET";
                var expected = shareWrite ? "lists.views.share" : list.Permission;
                var declared = endpoint.IsAnonymous ? "anonymous" : string.Join(",", endpoint.Permissions);
                if (declared != expected)
                {
                    problems.Add($"{endpoint.Key} declares {declared}; a list endpoint of {list.Key} must declare {expected}");
                }
                covered++;
                continue;
            }
            if (module.Name == "reports" && ReportRoutePermission(catalog, endpoint.Pattern) is { } report)
            {
                // A report's run route and a printable list's print route declare exactly the
                // permission of the data they print: the report's own, or the list's.
                var declared = endpoint.IsAnonymous ? "anonymous" : string.Join(",", endpoint.Permissions);
                if (report.Expected is null)
                {
                    problems.Add($"{endpoint.Key}: under {report.Prefix} but names no registered {report.Kind}; review it in a map");
                }
                else if (declared != report.Expected)
                {
                    problems.Add($"{endpoint.Key} declares {declared}; a {report.Kind} route must declare {report.Expected}");
                }
                covered++;
                continue;
            }
            if (!maps.Any(m => m.Prefixes.Any(p => endpoint.Pattern.StartsWith(p, StringComparison.Ordinal))))
            {
                problems.Add($"{endpoint.Key} (module {module.Name}) is under no reviewed map in {ReviewedPermissionMap.Folder}; add tests/Gates/endpoint-permissions/{module.Name}.txt");
                continue;
            }
            covered++;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{covered} module endpoints reviewed by a map or derived from their list");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Contains(Endpoints, e => e.Pattern.StartsWith("/api/tenancy/", StringComparison.Ordinal));
        Assert.True(covered >= Ratchet.Min("g2.moduleRoutesReviewed"), $"{covered} module endpoints reviewed; ratchet minimum {Ratchet.Min("g2.moduleRoutesReviewed")}");
    }

    /// <summary>The permission a route under <c>/api/reports/run/&lt;report key&gt;</c> or
    /// <c>/api/reports/lists/&lt;list key&gt;</c> must declare (null when it names no registered
    /// report or printable list); null for any other route of the reports module (reviewed in a map).</summary>
    public static (string Prefix, string Kind, string? Expected)? ReportRoutePermission(ModuleCatalog catalog, string pattern)
    {
        const string run = "/api/reports/run/";
        const string lists = "/api/reports/lists/";
        if (pattern.StartsWith(run, StringComparison.Ordinal))
        {
            var key = pattern[run.Length..].Split('/')[0];
            return (run, "report", catalog.FindReport(key)?.Definition.Permission);
        }
        if (pattern.StartsWith(lists, StringComparison.Ordinal))
        {
            var key = pattern[lists.Length..].Split('/')[0];
            return (lists, "printable list", catalog.PrintableLists.Where(p => p.List.Key == key).Select(p => p.List.Permission).FirstOrDefault());
        }
        return null;
    }

    /// <summary>Self-test (critic p03 round 2, plant P2): the sign-in history guarded by
    /// identity.users.read while identity.signIns.read is still used elsewhere; an endpoint missing
    /// from the map; a map line matching nothing.</summary>
    [Fact]
    public void The_reviewed_permission_map_catches_a_weaker_permission_an_unreviewed_endpoint_and_a_stale_line()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        using var app = builder.Build();
        app.MapGet("/api/planted/users/{id:guid}/sign-ins", () => "planted").RequirePermission("identity.users.read");
        app.MapPost("/api/planted/users/{id:guid}/unblock", () => "planted").RequirePermission("identity.signIns.read");
        app.MapPost("/api/planted/users/{id:guid}/export", () => "planted").RequirePermission("identity.users.read");
        app.MapGet("/api/elsewhere/report", () => "planted").RequirePermission("identity.users.read");
        var endpoints = EndpointInventory.From(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
        var map = ReviewedPermissionMap.Parse("planted.txt",
        [
            "prefix /api/planted/",
            "GET /api/planted/users/{id:guid}/sign-ins identity.signIns.read # history is more sensitive than the record",
            "POST /api/planted/users/{id:guid}/unblock identity.users.update # changes the account",
            "DELETE /api/planted/users/{id:guid} identity.users.delete # removed since",
        ]);
        var (problems, checkedCount) = ReviewedPermissionMap.Check(endpoints, [map]);
        Assert.Equal(3, checkedCount);
        Assert.Contains(problems, p => p.StartsWith("GET /api/planted/users/{id:guid}/sign-ins declares identity.users.read; the reviewed map planted.txt says identity.signIns.read", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("POST /api/planted/users/{id:guid}/unblock declares identity.signIns.read", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("POST /api/planted/users/{id:guid}/export declares identity.users.read but is not in the reviewed map", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("planted.txt: DELETE /api/planted/users/{id:guid} matches no endpoint", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("/api/elsewhere/", StringComparison.Ordinal));
        Assert.Equal(4, problems.Count);

        // A line without a reason is refused.
        Assert.NotEmpty(ReviewedPermissionMap.Parse("bare.txt", ["prefix /api/planted/", "GET /api/planted/x identity.users.read"]).Problems);
    }

    /// <summary>Self-test (critic plant P6b): .AllowAnonymous() stacked on RequirePermission, with
    /// the endpoint metadata exactly as ASP.NET Core builds it, is reported.</summary>
    [Fact]
    public void The_declaration_check_catches_AllowAnonymous_stacked_on_a_permission()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        using var app = builder.Build();
        app.MapPut("/api/planted/preferences", () => "planted").RequirePermission("identity.profile.update").AllowAnonymous();
        app.MapGet("/api/planted/reviewed", () => "planted").AllowAnonymousReviewed("A reviewed anonymous endpoint for the self-test.");
        app.MapGet("/api/planted/guarded", () => "planted").RequirePermission("identity.users.read");
        var endpoints = EndpointInventory.From(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints));
        var problems = PermissionDeclarationProblems(endpoints);
        Assert.Single(problems);
        Assert.Contains("PUT /api/planted/preferences", problems[0], StringComparison.Ordinal);
        Assert.Contains("identity.profile.update", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void No_endpoint_that_changes_data_is_guarded_by_a_read_permission()
    {
        var result = ReadPermissionWrites.Check(Endpoints);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.StateChangingChecked >= Ratchet.Min("g2.stateChangingEndpointsChecked"),
            $"g2.stateChangingEndpointsChecked: {result.StateChangingChecked}; ratchet minimum {Ratchet.Min("g2.stateChangingEndpointsChecked")}");
    }

    [Fact]
    public async Task Requests_that_only_read_cannot_write()
    {
        // Every GET runs in a read-only transaction: PostgreSQL refuses a write in it, whoever
        // bound the transaction, so a read permission (and a forged cross-site GET, which the CSRF
        // defence lets through) can never change data.
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = HttpMethods.Get;
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = http;
        try
        {
            var unit = scope.ServiceProvider.GetRequiredService<Erp.Kernel.Data.ErpDbSession>();
            await unit.BeginAsync(Env.TenantA.Id, null, "user");
            await using var write = new Npgsql.NpgsqlCommand("UPDATE tenancy.tenants SET name_en = name_en", unit.Connection, unit.Transaction);
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => write.ExecuteNonQueryAsync());
            Assert.Equal(Npgsql.PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        // The same unit of work for a POST writes normally.
        await using var postScope = Env.Factory.Services.CreateAsyncScope();
        var postAccessor = postScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        postAccessor.HttpContext = new DefaultHttpContext { RequestServices = postScope.ServiceProvider, Request = { Method = HttpMethods.Post } };
        try
        {
            var unit = postScope.ServiceProvider.GetRequiredService<Erp.Kernel.Data.ErpDbSession>();
            await unit.BeginAsync(Env.TenantA.Id, null, "user");
            await using var write = new Npgsql.NpgsqlCommand("UPDATE tenancy.tenants SET name_en = name_en", unit.Connection, unit.Transaction);
            Assert.Equal(1, await write.ExecuteNonQueryAsync());
            await unit.RollbackAsync();
        }
        finally
        {
            postAccessor.HttpContext = null;
        }
    }

    [Fact]
    public void Every_declared_permission_is_in_the_catalogue_and_every_catalogue_permission_is_used()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var declared = Permissioned.Select(e => e.Permission).ToHashSet();
        var unknown = declared.Where(p => !catalog.IsPermission(p)).ToList();
        var unused = catalog.PermissionKeys.Where(p => !declared.Contains(p)).ToList();
        Assert.True(unknown.Count == 0, "Endpoints declare permissions missing from the catalogue: " + string.Join(", ", unknown));
        Assert.True(unused.Count == 0, "Catalogue permissions no endpoint uses (dead permissions confuse role editors): " + string.Join(", ", unused));
        Assert.True(catalog.PermissionKeys.Count >= Ratchet.Min("g2.permissions"),
            $"{catalog.PermissionKeys.Count} permissions; ratchet minimum {Ratchet.Min("g2.permissions")}");
    }

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_permissioned_endpoint()
    {
        using var client = Env.CreateClient();
        var problems = new List<string>();
        foreach (var endpoint in Permissioned)
        {
            using var response = await client.SendAsync(Request(endpoint));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                problems.Add($"{endpoint}: {(int)response.StatusCode}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task A_user_whose_roles_grant_nothing_is_denied_everything_and_sees_no_menu()
    {
        using var client = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
        var problems = new List<string>();
        foreach (var endpoint in Permissioned)
        {
            using var response = await client.SendAsync(Request(endpoint));
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                problems.Add($"{endpoint}: {(int)response.StatusCode}");
            }
        }
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(0, session.GetProperty("permissions").GetArrayLength());
        Assert.Equal(0, session.GetProperty("menu").GetArrayLength());
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Granting_exactly_one_permission_opens_exactly_the_endpoints_that_declare_it()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var problems = new List<string>();
        var checkedPermissions = 0;
        foreach (var permission in catalog.PermissionKeys.Order(StringComparer.Ordinal))
        {
            var email = $"g2.{permission.Replace('.', '-')}@{Env.TenantA.EmailDomain}";
            var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Only {permission}", nameAr = $"فقط {permission}", permissions = new[] { permission } });
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
            var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"G2 {permission}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } });
            Assert.Equal(HttpStatusCode.Created, user.StatusCode);

            using var client = await Env.SignInAsync(email);
            var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
            var granted = session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
            if (granted is not [var only] || only != permission)
            {
                problems.Add($"{permission}: session lists [{string.Join(", ", granted)}]");
            }
            var menu = session.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToList();
            var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();
            if (!menu.SequenceEqual(expectedMenu))
            {
                problems.Add($"{permission}: menu shows [{string.Join(", ", menu)}], expected [{string.Join(", ", expectedMenu)}]");
            }

            foreach (var endpoint in Permissioned)
            {
                using var response = await client.SendAsync(Request(endpoint));
                var opened = response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
                var shouldOpen = endpoint.Permission == permission;
                if (opened != shouldOpen)
                {
                    problems.Add($"{permission}: {endpoint} answered {(int)response.StatusCode} (expected {(shouldOpen ? "allowed" : "403")})");
                }
            }
            checkedPermissions++;
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedPermissions >= Ratchet.Min("g2.permissions"));
    }

    [Fact]
    public async Task Users_cannot_escalate_their_own_privileges()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var limited = new[] { "identity.roles.read", "identity.roles.create", "identity.roles.update", "identity.users.read", "identity.users.update" };
        var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Limited admin", nameAr = "مدير محدود", permissions = limited }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var email = $"limited@{Env.TenantA.EmailDomain}";
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Limited", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var self = await created.Content.ReadFromJsonAsync<JsonElement>();

        using var client = await Env.SignInAsync(email);
        var roles = (await client.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var administrator = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean());
        var viewer = await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?search=viewer@");
        var viewerUser = viewer.GetProperty("items")[0];

        // Create a role granting a permission the caller lacks.
        var escalateRole = await client.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Escalate", nameAr = "تصعيد", permissions = new[] { "identity.roles.delete" } });
        Assert.Equal(HttpStatusCode.Forbidden, escalateRole.StatusCode);

        // Add a permission the caller lacks to their own role.
        var ownRole = await client.PutAsJsonAsync($"/api/identity/roles/{role.GetProperty("id").GetGuid()}",
            new { nameEn = "Limited admin", nameAr = "مدير محدود", permissions = limited.Append("identity.users.create"), version = role.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, ownRole.StatusCode);

        // Give another user the Administrator role.
        var promote = await client.PutAsJsonAsync($"/api/identity/users/{viewerUser.GetProperty("id").GetGuid()}", new
        {
            displayName = viewerUser.GetProperty("displayName").GetString(),
            language = "en",
            isActive = true,
            roleIds = viewerUser.GetProperty("roleIds").EnumerateArray().Select(r => r.GetGuid()).Append(administrator.GetProperty("id").GetGuid()),
            version = viewerUser.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);

        // Give themselves the Administrator role.
        var selfPromote = await client.PutAsJsonAsync($"/api/identity/users/{self.GetProperty("id").GetGuid()}", new
        {
            displayName = "Limited",
            language = "en",
            isActive = true,
            roleIds = new[] { role.GetProperty("id").GetGuid(), administrator.GetProperty("id").GetGuid() },
            version = self.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, selfPromote.StatusCode);

        // Change the Administrator system role.
        var system = await client.PutAsJsonAsync($"/api/identity/roles/{administrator.GetProperty("id").GetGuid()}",
            new { nameEn = "Administrator", nameAr = "مدير النظام", permissions = limited, version = administrator.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Forbidden, system.StatusCode);

        // Nothing changed for the caller.
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(limited.Order(StringComparer.Ordinal), session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
    }

    [Fact]
    public async Task Creating_a_user_cannot_grant_more_than_the_caller_holds()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var limited = new[] { "identity.users.create", "identity.users.read", "identity.roles.read" };
        var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "User creator", nameAr = "منشئ المستخدمين", permissions = limited }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var broader = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Role deleter", nameAr = "حاذف الأدوار", permissions = new[] { "identity.users.read", "identity.roles.delete" } }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var email = $"creator@{Env.TenantA.EmailDomain}";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Creator", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } })).StatusCode);

        using var client = await Env.SignInAsync(email);
        var roles = (await client.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var administrator = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var before = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?take=1")).GetProperty("total").GetInt32();

        foreach (var (label, roleIds) in new[]
                 {
                     ("the Administrator role", new[] { administrator }),
                     ("a role with a permission the caller lacks", new[] { broader.GetProperty("id").GetGuid() }),
                     ("its own role plus the Administrator role", new[] { role.GetProperty("id").GetGuid(), administrator }),
                 })
        {
            var minted = $"minted.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
            var response = await client.PostAsJsonAsync("/api/identity/users",
                new { email = minted, displayName = "Minted", language = "en", password = ErpTestEnvironment.Password, roleIds });
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Creating a user with {label} answered {(int)response.StatusCode}");
            Assert.Equal("identity.grantBeyondOwn", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Env.SignInAsync(minted));
        }
        Assert.Equal(before, (await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?take=1")).GetProperty("total").GetInt32());

        // Within the caller's own grants it works.
        var allowed = await client.PostAsJsonAsync("/api/identity/users",
            new { email = $"helper@{Env.TenantA.EmailDomain}", displayName = "Helper", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task No_endpoint_grants_more_than_the_caller_holds()
    {
        var result = await GrantEscalation.RunAsync(Env);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.Checked.Count >= Ratchet.Min("g2.grantEndpointsChecked"),
            $"{result.Checked.Count} grant endpoints checked ({string.Join(", ", result.Checked)}); ratchet minimum {Ratchet.Min("g2.grantEndpointsChecked")}");
        Assert.True(result.PartialTargets >= Ratchet.Min("g2.grantEscalationPartialTargets"),
            $"{result.PartialTargets} requests asking for grants the caller lacks; ratchet minimum {Ratchet.Min("g2.grantEscalationPartialTargets")}");
    }

    [Fact]
    public void The_host_refuses_to_start_with_an_endpoint_that_declares_no_permission()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: null)));
        Assert.Contains("declares no permission", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_refuses_to_start_with_an_endpoint_whose_permission_is_not_in_the_catalogue()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: "rogue.things.read", register: false)));
        Assert.Contains("not in any module's catalogue", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_refuses_a_list_whose_endpoint_declares_another_permission()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: "rogue.things.read", listPermission: "rogue.things.write")));
        Assert.Contains("the list says 'rogue.things.write'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_registered_list_is_readable_with_its_permission_alone()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        foreach (var list in catalog.Lists)
        {
            var email = $"g2.list.{list.Key.Replace('.', '-')}@{Env.TenantA.EmailDomain}";
            var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"List {list.Key}", nameAr = $"قائمة {list.Key}", permissions = new[] { list.Permission } }))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users",
                new { email, displayName = $"List {list.Key}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } })).StatusCode);
            using var reader = await Env.SignInAsync(email);
            using var allowed = await reader.GetAsync(list.Endpoint);
            Assert.True(allowed.IsSuccessStatusCode, $"{list.Key}: {list.Endpoint} answered {(int)allowed.StatusCode} to a user holding {list.Permission}");
            using var noAccess = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
            using var denied = await noAccess.GetAsync(list.Endpoint);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            // The list's definition and the user's own views come with the list's permission alone;
            // sharing a view with everyone needs the share permission as well.
            foreach (var path in new[] { $"/api/lists/{list.Key}/definition", $"/api/lists/{list.Key}/views" })
            {
                using var open = await reader.GetAsync(path);
                Assert.True(open.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)open.StatusCode} to a user holding {list.Permission}");
                using var closed = await noAccess.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
            }
            var definition = await reader.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/definition");
            Assert.False(definition.GetProperty("canShare").GetBoolean(), $"{list.Key}: the definition offers sharing to a user who may not share");
            var columns = list.Columns.Take(1).Select(c => c.Key).ToArray();
            using var personal = await reader.PostAsJsonAsync($"/api/lists/{list.Key}/views", new { name = "Mine", columns });
            Assert.True(personal.StatusCode == HttpStatusCode.Created, $"{list.Key}: saving a personal view answered {(int)personal.StatusCode}");
            using var share = await reader.PostAsJsonAsync($"/api/lists/{list.Key}/shared-views", new { name = "Everyone's", columns });
            Assert.Equal(HttpStatusCode.Forbidden, share.StatusCode);
        }
    }

    [Fact]
    public async Task Sharing_a_view_needs_the_share_permission_and_the_lists_own_permission()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var list = catalog.Lists.First();
        var columns = list.Columns.Take(1).Select(c => c.Key).ToArray();

        async Task<HttpClient> UserWith(string label, params string[] permissions)
        {
            var role = await (await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Share {label}", nameAr = $"مشاركة {label}", permissions }))
                .Content.ReadFromJsonAsync<JsonElement>();
            var email = $"g2.share.{label}@{Env.TenantA.EmailDomain}";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users",
                new { email, displayName = $"Share {label}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role.GetProperty("id").GetGuid() } })).StatusCode);
            return await Env.SignInAsync(email);
        }

        // The share permission alone does not reveal a list the user cannot read.
        using var shareOnly = await UserWith("only", "lists.views.share");
        using (var created = await shareOnly.PostAsJsonAsync($"/api/lists/{list.Key}/shared-views", new { name = "Everyone's", columns }))
        {
            Assert.Equal(HttpStatusCode.NotFound, created.StatusCode);
        }
        using (var definition = await shareOnly.GetAsync($"/api/lists/{list.Key}/definition"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, definition.StatusCode);
        }

        // With both, the view is shared and every reader of the list sees it.
        using var sharer = await UserWith("both", "lists.views.share", list.Permission);
        Assert.True((await sharer.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/definition")).GetProperty("canShare").GetBoolean());
        using var shared = await sharer.PostAsJsonAsync($"/api/lists/{list.Key}/shared-views", new { name = "Everyone's", columns });
        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
        var views = await admin.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/views");
        Assert.Contains(views.GetProperty("items").EnumerateArray(), v => v.GetProperty("name").GetString() == "Everyone's" && v.GetProperty("isShared").GetBoolean());
    }

    /// <summary>
    /// Every endpoint of a registered list (its definition and its saved views, personal and
    /// shared) also needs that list's own permission, whatever permission it declares. A caller
    /// holding only the share permission (critic p05 round 1, plant P2: shared-view DELETE without
    /// the list's permission), and a reader of the list without the share permission, try every
    /// such endpoint against real views of the workspace: each is refused (403 or 404) and no view
    /// changes. The administrator then sends the same requests and they succeed, so the bodies and
    /// ids were valid and the refusals were the permission checks.
    /// </summary>
    [Fact]
    public async Task Every_list_endpoint_needs_the_lists_own_permission_and_shared_view_writes_need_sharing()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

        async Task<HttpClient> UserWith(string label, params string[] permissions)
        {
            var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Views {label}", nameAr = $"عروض {label}", permissions });
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
            var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var email = $"g2.views.{label}@{Env.TenantA.EmailDomain}";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/identity/users",
                new { email, displayName = $"Views {label}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } })).StatusCode);
            return await Env.SignInAsync(email);
        }

        using var shareOnly = await UserWith("share-only", "lists.views.share");
        var problems = new List<string>();
        var refusals = 0;
        foreach (var list in catalog.Lists)
        {
            using var reader = await UserWith($"reader-{list.Key.Replace('.', '-')}", list.Permission);
            var columns = list.Columns.Take(1).Select(c => c.Key).ToArray();
            var shared = await (await admin.PostAsJsonAsync($"/api/lists/{list.Key}/shared-views", new { name = $"Team {list.Key}", columns })).Content.ReadFromJsonAsync<JsonElement>();
            var personal = await (await admin.PostAsJsonAsync($"/api/lists/{list.Key}/views", new { name = $"Mine {list.Key}", columns })).Content.ReadFromJsonAsync<JsonElement>();
            var before = await ViewsTextAsync(admin, list.Key);

            var listEndpoints = Endpoints.Where(e => e.Pattern.StartsWith($"/api/lists/{list.Key}/", StringComparison.Ordinal)).ToList();
            Assert.True(listEndpoints.Count >= 4, $"{list.Key}: only {listEndpoints.Count} endpoints under /api/lists/{list.Key}/");
            foreach (var endpoint in listEndpoints)
            {
                var sharedRoute = endpoint.Pattern.Contains("/shared-views", StringComparison.Ordinal);
                var view = sharedRoute ? shared : personal;
                HttpRequestMessage Build() => ViewRequest(endpoint, view, columns);

                // Without the list's own permission: refused, whatever the endpoint declares.
                using (var response = await shareOnly.SendAsync(Build()))
                {
                    refusals++;
                    if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
                    {
                        problems.Add($"{endpoint} answered {(int)response.StatusCode} to a user holding only lists.views.share, not {list.Permission}");
                    }
                }
                // With the list's permission but not the share permission: shared views are
                // read-only (and another user's personal view does not exist for this reader).
                if (sharedRoute && endpoint.Method != "GET")
                {
                    using var response = await reader.SendAsync(Build());
                    refusals++;
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint} answered {(int)response.StatusCode} to a reader of {list.Key} without lists.views.share");
                    }
                }
                if (!sharedRoute && endpoint.Method is "PUT" or "DELETE" or "GET" && endpoint.RouteParameters.Count > 0)
                {
                    using var response = await reader.SendAsync(Build());
                    refusals++;
                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        problems.Add($"{endpoint} answered {(int)response.StatusCode} to another reader of {list.Key} for the administrator's personal view");
                    }
                }
            }
            // Crossed ids (critic p05 round 4, plant P4: the personal routes also matched shared views,
            // so a reader without the share permission renamed and deleted the workspace's shared
            // view through PUT/DELETE /views/{id}). A personal route answers for the caller's own
            // personal views only, and a shared route for shared views only: every route with an id
            // is sent the other kind of view, by a reader and by a sharer, and none of it exists there.
            using var sharer = await UserWith($"sharer-{list.Key.Replace('.', '-')}", "lists.views.share", list.Permission);
            var sharersOwn = await (await sharer.PostAsJsonAsync($"/api/lists/{list.Key}/views", new { name = $"Sharer's own {list.Key}", columns })).Content.ReadFromJsonAsync<JsonElement>();
            foreach (var endpoint in listEndpoints.Where(e => e.RouteParameters.Count > 0))
            {
                var sharedRoute = endpoint.Pattern.Contains("/shared-views", StringComparison.Ordinal);
                var crossings = sharedRoute
                    ? new[] { ("a sharer", sharer, personal, "the administrator's personal view"), ("a sharer", sharer, sharersOwn, "the sharer's own personal view"), ("a reader", reader, personal, "the administrator's personal view") }
                    : new[] { ("a reader", reader, shared, "the workspace's shared view"), ("a sharer", sharer, shared, "the workspace's shared view") };
                foreach (var (who, client, view, what) in crossings)
                {
                    using var response = await client.SendAsync(ViewRequest(endpoint, view, columns));
                    refusals++;
                    // A reader without the share permission may also be refused before the lookup.
                    var refused = response.StatusCode == HttpStatusCode.NotFound ||
                                  (who == "a reader" && sharedRoute && endpoint.Method != "GET" && response.StatusCode == HttpStatusCode.Forbidden);
                    if (!refused)
                    {
                        problems.Add($"{endpoint} answered {(int)response.StatusCode} to {who} of {list.Key} for {what}; " +
                                     (sharedRoute ? "a shared route serves shared views only" : "a personal route serves the caller's own personal views only"));
                    }
                }
            }
            var after = await ViewsTextAsync(admin, list.Key);
            if (after != before)
            {
                problems.Add($"{list.Key}: the workspace's views changed while refused callers tried them:\nbefore {before}\nafter  {after}");
            }
            using (var own = await sharer.GetAsync($"/api/lists/{list.Key}/views/{sharersOwn.GetProperty("id").GetString()}"))
            {
                var current = own.IsSuccessStatusCode ? await own.Content.ReadFromJsonAsync<JsonElement>() : default;
                if (!own.IsSuccessStatusCode || current.GetProperty("name").GetString() != sharersOwn.GetProperty("name").GetString() ||
                    current.GetProperty("isShared").GetBoolean() || current.GetProperty("version").GetUInt32() != sharersOwn.GetProperty("version").GetUInt32())
                {
                    problems.Add($"{list.Key}: the sharer's own personal view changed or went while shared routes were sent its id (answered {(int)own.StatusCode})");
                }
            }

            // Not blind: the same requests succeed for the administrator (writes last, delete at the end).
            foreach (var endpoint in listEndpoints.OrderBy(e => e.Method switch { "GET" => 0, "PUT" => 1, "POST" => 2, _ => 3 }))
            {
                var view = endpoint.Pattern.Contains("/shared-views", StringComparison.Ordinal) ? shared : personal;
                if (endpoint.Method == "PUT")
                {
                    // A refused caller that got through above may have deleted the view: report
                    // every problem found so far rather than stopping at the missing view.
                    using var current = await admin.GetAsync(endpoint.Path(_ => view.GetProperty("id").GetString()!));
                    if (!current.IsSuccessStatusCode)
                    {
                        problems.Add($"{endpoint}: the administrator's view answered {(int)current.StatusCode} before the administrator's own change (did a refused caller remove it?)");
                        continue;
                    }
                    view = await current.Content.ReadFromJsonAsync<JsonElement>();
                }
                using var request = ViewRequest(endpoint, view, columns, endpoint.Method == "POST" ? $"Again {list.Key}" : null);
                using var response = await admin.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    problems.Add($"{endpoint} answered {(int)response.StatusCode} to the administrator, so the refusals above may not have been the permission check");
                }
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{catalog.Lists.Count()} lists, {refusals} refusals checked on list endpoints");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(refusals >= Ratchet.Min("g2.listEndpointRefusals"), $"g2.listEndpointRefusals: {refusals}; ratchet minimum {Ratchet.Min("g2.listEndpointRefusals")}");
    }

    private static HttpRequestMessage ViewRequest(ApiEndpoint endpoint, JsonElement view, string[] columns, string? name = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(_ => view.GetProperty("id").GetString()!));
        if (endpoint.HasBody)
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = name ?? (endpoint.Method == "PUT" ? view.GetProperty("name").GetString() + " (changed)" : "Taken over"),
                ["columns"] = columns,
                ["version"] = view.TryGetProperty("version", out var version) ? version.GetUInt32() : null,
            };
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private static async Task<string> ViewsTextAsync(HttpClient admin, string list)
    {
        var views = await admin.GetFromJsonAsync<JsonElement>($"/api/lists/{list}/views");
        return string.Join(" | ", views.GetProperty("items").EnumerateArray()
            .Select(v => $"{v.GetProperty("id").GetString()}:{v.GetProperty("name").GetString()}:{v.GetProperty("version").GetUInt32()}:{v.GetProperty("isDefault").GetBoolean()}"));
    }

    [Fact]
    public async Task Undeclared_endpoints_fall_back_to_deny()
    {
        var policy = await Env.Factory.Services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync();
        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is DenyAllRequirement);
    }

    private static void BuildHost(ErpModule module)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["ConnectionStrings:App"] = "Host=localhost;Username=erp_app;Password=x;Database=erp";
        builder.AddErpPlatform([module]);
        var app = builder.Build();
        app.UseErpPlatform();
    }

    private sealed class RogueModule(string? declare, bool register = true, string? listPermission = null) : ErpModule
    {
        public override string Name => "rogue";

        public override void Register(ModuleBuilder module)
        {
            if (register)
            {
                module.Permissions("rogue.things.read");
            }
            if (listPermission is not null)
            {
                module.Permissions(listPermission);
                module.List(new Erp.Kernel.Lists.ListDefinition("rogue.things", "rogue.things.title", listPermission, "/api/rogue/declared",
                    [new Erp.Kernel.Lists.ListColumn("name", "rogue.things.name", Erp.Kernel.Lists.ListColumnType.Text)], ["name"]));
            }
            module.Endpoints(group =>
            {
                var endpoint = group.MapGet("/things", () => "secret");
                if (declare is not null)
                {
                    endpoint.RequirePermission(declare);
                }
                group.MapGet("/declared", () => "ok").RequirePermission("rogue.things.read");
            });
        }
    }

    /// <summary>A request that would succeed if authorised: random ids, an empty JSON body.</summary>
    internal static HttpRequestMessage Request(ApiEndpoint endpoint)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(_ => Guid.NewGuid().ToString()));
        if (endpoint.HasBody)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }
        return request;
    }
}
