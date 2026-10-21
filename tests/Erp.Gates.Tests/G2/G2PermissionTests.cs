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
    /// <summary>The routes the lists module generates under <c>/api/lists/&lt;list key&gt;/</c>: method and
    /// path after the list key. Each declares the list's own permission, the shared-view writes the
    /// sharing permission.</summary>
    private static readonly string[] DerivedListRoutes =
    [
        "GET definition", "GET views", "POST views", "GET views/{id:guid}", "PUT views/{id:guid}", "DELETE views/{id:guid}",
        "POST shared-views", "GET shared-views/{id:guid}", "PUT shared-views/{id:guid}", "DELETE shared-views/{id:guid}",
    ];

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
                // Only the routes the lists module generates for every list derive their permission
                // (critic p05 round 12, plant P11b: any other route under the list's prefix passed as
                // guarded by the list's read permission without review). A new route shape is added
                // here with its permission, or reviewed in a map.
                var suffix = $"{endpoint.Method} {rest[(list.Key.Length + 1)..]}";
                if (!DerivedListRoutes.Contains(suffix))
                {
                    problems.Add($"{endpoint.Key}: '{suffix}' is not a route every list has ({string.Join(", ", DerivedListRoutes)}); add its shape and permission to the derived rule or review it in a map");
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

    /// <summary>Through every session carrier (cookie and Bearer, and any carrier the kernel adds:
    /// <see cref="SessionTokenCarriers.All"/>).</summary>
    [Fact]
    public async Task A_user_whose_roles_grant_nothing_is_denied_everything_and_sees_no_menu()
    {
        var result = await CarrierMatrix.DeniedEverythingAsync(Env, Env.Email(Env.TenantA, "noaccess"), Permissioned);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        AssertEveryCarrier(result.Carriers);
        Assert.Equal(Permissioned.Count * result.Carriers.Count, result.Requests);
        AssertSelfReferencing(result, Permissioned.Count);
    }

    /// <summary>
    /// Records the caller made stay theirs after their roles are gone, and naming them opens
    /// nothing (critic p00 round 11: "people may always manage their own record" is the shortcut
    /// self-service screens invite). The user holds every list's permission and the sharing
    /// permission, saves a personal view in every list, and loses every role; then every
    /// permissioned endpoint answers 403 through every carrier, with random ids and with every id
    /// of their own, the saved views among them.
    /// </summary>
    [Fact]
    public async Task A_user_who_lost_every_role_is_denied_everything_on_the_records_they_made()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var email = $"g2.former.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        var permissions = catalog.Lists.Select(l => l.Permission).Distinct().Order(StringComparer.Ordinal).ToArray();
        var userId = await CreateUserHoldingAsync(admin, email, permissions, "Former list user", "مستخدم قوائم سابق");
        // The user works in the administrator's companies: the first through one named branch, the
        // second with every branch, so their companies and their branch are ids of their own.
        var companies = await GateCompanies.OfAsync(admin);
        Assert.True(companies.Ids.Count == 2, $"the gate workspace needs two companies; found {companies.Ids.Count}");
        var branches = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/branches?filter=" + Uri.EscapeDataString($"companyId eq '{companies.Ids[0]}'"))).GetProperty("items");
        var branch = branches.EnumerateArray().First(b => b.GetProperty("companyId").GetGuid() == companies.Ids[0]).GetProperty("id").GetGuid();
        var accessVersion = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}")).GetProperty("version").GetUInt32();
        using (var access = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new
               {
                   companies = new object[]
                   {
                       new { companyId = companies.Ids[0], allBranches = false, branchIds = new[] { branch } },
                       new { companyId = companies.Ids[1], allBranches = true, branchIds = Array.Empty<Guid>() },
                   },
                   version = accessVersion,
               }))
        {
            Assert.True(access.IsSuccessStatusCode, $"giving access: {(int)access.StatusCode} {await access.Content.ReadAsStringAsync()}");
        }
        using (var user = await Env.SignInAsync(email))
        {
            foreach (var list in catalog.Lists)
            {
                var columns = list.Columns.Take(2).Select(c => c.Key).ToArray();
                using var saved = await user.PostAsJsonAsync($"/api/lists/{list.Key}/views", PersonalBody(list, columns, $"Mine {list.Key}", false, null));
                Assert.True(saved.StatusCode == HttpStatusCode.Created, $"POST /api/lists/{list.Key}/views: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
            }
        }
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{userId}");
        using var stripped = await admin.PutAsJsonAsync($"/api/identity/users/{userId}", new
        {
            displayName = current.GetProperty("displayName").GetString(),
            language = "en",
            isActive = true,
            roleIds = Array.Empty<Guid>(),
            version = current.GetProperty("version").GetUInt32(),
        });
        Assert.True(stripped.IsSuccessStatusCode, $"removing the roles: {(int)stripped.StatusCode} {await stripped.Content.ReadAsStringAsync()}");

        var (token, signedIn) = await Env.SignInForTokenAsync(email);
        Assert.Equal(userId, signedIn);
        var own = await OwnIds.OfAsync(Env, userId, token);
        Assert.True(own.Count(o => o.Sources.Contains("lists.saved_views.id")) >= Math.Min(catalog.Lists.Count(), OwnIds.RowsPerTable),
            "The caller's own saved views are not among the ids tried: " + string.Join(", ", own));
        foreach (var mine in companies.Ids.Append(branch))
        {
            Assert.True(own.Any(o => o.Value == mine), $"The caller's company or branch {mine} is not among the ids tried: " + string.Join(", ", own));
        }

        var result = await CarrierMatrix.DeniedEverythingAsync(Env, email, Permissioned);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        AssertEveryCarrier(result.Carriers);
        AssertSelfReferencing(result, Permissioned.Count);
    }

    private static void AssertSelfReferencing(CarrierMatrix.Result result, int endpoints)
    {
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.SelfRequests} requests named the caller's own ids ({result.OwnIdsTried} ids) at {result.SelfEndpoints} endpoints");
        Assert.Equal(endpoints, result.SelfEndpoints);
        Assert.True(result.SelfRequests >= Ratchet.Min("g2.selfReferencingDenyRequests"),
            $"{result.SelfRequests} requests named the caller's own ids; ratchet minimum {Ratchet.Min("g2.selfReferencingDenyRequests")}");
        Assert.True(result.OwnIdsTried >= Ratchet.Min("g2.ownIdsTried"),
            $"{result.OwnIdsTried} own ids of the caller tried; ratchet minimum {Ratchet.Min("g2.ownIdsTried")}");
    }

    /// <summary>Through every session carrier, as above: one sign-in per permission, its token
    /// presented once as the cookie and once as a Bearer header.</summary>
    [Fact]
    public async Task Granting_exactly_one_permission_opens_exactly_the_endpoints_that_declare_it()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var problems = new List<string>();
        var checkedPermissions = 0;
        var requests = 0;
        var selfRequests = 0;
        var selfCarriers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in catalog.PermissionKeys.Order(StringComparer.Ordinal))
        {
            var email = $"g2.{permission.Replace('.', '-')}@{Env.TenantA.EmailDomain}";
            await CreateUserHoldingAsync(admin, email, [permission], $"Only {permission}", $"فقط {permission}");
            var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();
            // The requests naming the caller's own ids go through one carrier per permission, the
            // carriers taken in turn (the random-id matrix covers every carrier for every permission).
            var selfCarrier = SessionTokenCarriers.All[checkedPermissions % SessionTokenCarriers.All.Count].Name;
            var result = await CarrierMatrix.ExactlyOneAsync(Env, email, permission, expectedMenu, Permissioned, selfCarriers: [selfCarrier]);
            problems.AddRange(result.Problems);
            AssertEveryCarrier(result.Carriers);
            requests += result.Requests;
            selfRequests += result.SelfRequests;
            if (result.SelfRequests > 0)
            {
                selfCarriers.Add(selfCarrier);
            }
            checkedPermissions++;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{selfRequests} requests named the caller's own ids at endpoints of the held permission's module");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedPermissions >= Ratchet.Min("g2.permissions"));
        Assert.Equal(checkedPermissions * Permissioned.Count * SessionTokenCarriers.All.Count, requests);
        Assert.Equal(SessionTokenCarriers.All.Select(c => c.Name).Order(StringComparer.Ordinal), selfCarriers.Order(StringComparer.Ordinal));
        Assert.True(selfRequests >= Ratchet.Min("g2.selfReferencingGrantRequests"),
            $"{selfRequests} requests named the caller's own ids; ratchet minimum {Ratchet.Min("g2.selfReferencingGrantRequests")}");
    }

    /// <summary>
    /// Self-test (critic p00 round 11, plant P6): permission checks that pass a request naming the
    /// caller's own id. <c>user</c>: a route value equal to the caller's user id passes ("people
    /// may always manage their own record"); <c>session</c>: one equal to the caller's session id;
    /// <c>holder</c>: a caller holding any permission of the endpoint's module passes on their own
    /// user id. The deny-by-default matrix must report the first two and the grant-exactly-one
    /// matrix the third (the deny matrix staying clean for it), each only on the requests naming own
    /// ids; the random-id requests stay clean.
    /// </summary>
    [Theory]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("holder")]
    public async Task The_matrices_catch_a_permission_check_that_passes_requests_naming_the_callers_own_ids(string mode)
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        await using var planted = new ErpAppFactory(SelfBypassModule.Settings(Env.Factory.Settings, mode));
        // Fresh users: under the plant, the matrices' own requests may change or end their account.
        var nobody = $"g2.selfplant.{mode}.none.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await CreateUserHoldingAsync(admin, nobody, [], $"Nothing {mode}", $"لا شيء {mode}");
        var denied = await CarrierMatrix.DeniedEverythingAsync(Env, nobody, Permissioned, planted);
        // The grant-exactly-one matrix is needed for the plant only it can see.
        var exactlyOne = new CarrierMatrix.Result([], [], 0);
        if (mode == "holder")
        {
            const string permission = "identity.users.read";
            var holder = $"g2.selfplant.{mode}.one.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
            await CreateUserHoldingAsync(admin, holder, [permission], $"Read users {mode}", $"قراءة المستخدمين {mode}");
            var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();
            exactlyOne = await CarrierMatrix.ExactlyOneAsync(Env, holder, permission, expectedMenu, Permissioned, planted);
        }

        foreach (var (name, result) in new[] { ("deny by default", denied), ("grant exactly one", exactlyOne) })
        {
            var notSelf = result.Problems.Where(p => !p.Contains(" naming ", StringComparison.Ordinal)).ToList();
            Assert.True(notSelf.Count == 0, $"The {name} matrix reported more than the plant ({mode}):\n" + string.Join("\n", notSelf));
        }
        switch (mode)
        {
            case "user":
                Assert.Contains(denied.Problems, p => p.Contains("GET /api/identity/users/{id:guid} naming the caller's user id answered 200", StringComparison.Ordinal));
                Assert.Contains(denied.Problems, p => p.Contains("GET /api/tenancy/access/{userId:guid} naming the caller's user id answered 200", StringComparison.Ordinal));
                Assert.Contains(denied.Problems, p => p.Contains("PUT /api/identity/users/{id:guid} naming the caller's user id answered", StringComparison.Ordinal));
                foreach (var carrier in SessionTokenCarriers.All)
                {
                    Assert.Contains(denied.Problems, p => p.StartsWith($"[{carrier.Name}] GET /api/identity/users/{{id:guid}} naming the caller's user id", StringComparison.Ordinal));
                }
                break;
            case "session":
                Assert.Contains(denied.Problems, p => p.Contains("GET /api/identity/users/{id:guid} naming the caller's session answered 404", StringComparison.Ordinal));
                Assert.DoesNotContain(denied.Problems, p => p.Contains("naming the caller's user id", StringComparison.Ordinal));
                break;
            case "holder":
                Assert.True(denied.Problems.Count == 0, "The plant passes only callers holding a permission of the module; the deny matrix reported:\n" + string.Join("\n", denied.Problems));
                Assert.Contains(exactlyOne.Problems, p => p.Contains("identity.users.read: PUT /api/identity/users/{id:guid} naming the caller's user id answered", StringComparison.Ordinal));
                Assert.Contains(exactlyOne.Problems, p => p.Contains("identity.users.read: GET /api/identity/users/{id:guid}/sign-ins naming the caller's user id answered 200", StringComparison.Ordinal));
                Assert.DoesNotContain(exactlyOne.Problems, p => p.Contains("/api/tenancy/", StringComparison.Ordinal));
                break;
        }
        // The real host passes the same matrices: the deny-by-default and grant-exactly-one tests above.
    }

    /// <summary>A token the server never issued gets 401 from every permissioned endpoint through
    /// every carrier, and the session endpoint answers as nobody.</summary>
    [Fact]
    public async Task An_unknown_token_gets_401_from_every_permissioned_endpoint_through_every_carrier()
    {
        var result = await CarrierMatrix.UnknownTokenAsync(Env, Permissioned);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        AssertEveryCarrier(result.Carriers);
    }

    /// <summary>
    /// Each carrier's test client carries the session through that carrier alone, and the kernel
    /// honours it: the same user is signed in through each. Anything else a client might send a
    /// token in (a query string, another header, another cookie, a form field, another
    /// Authorization scheme) is not read: the kernel reads tokens only through its carrier list,
    /// which the matrices above cover.
    /// </summary>
    [Fact]
    public async Task Sessions_are_read_only_through_the_listed_carriers_and_each_has_a_working_test_client()
    {
        Assert.Equal([SessionTokenCarriers.BearerName, SessionTokenCarriers.CookieName], SessionTokenCarriers.All.Select(c => c.Name));
        var email = Env.Email(Env.TenantA, "noaccess");
        var users = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        foreach (var carrier in await Env.SignInPerCarrierAsync(email))
        {
            using (carrier)
            {
                using var session = await carrier.Client.GetAsync("/api/auth/session");
                Assert.True(session.StatusCode == HttpStatusCode.OK, $"[{carrier.Carrier}] GET /api/auth/session: {(int)session.StatusCode}");
                users.Add((await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("id").GetString()!);
                var sent = carrier.Client.DefaultRequestHeaders.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
                // The client sends the token one way only.
                Assert.Equal(carrier.Carrier == SessionTokenCarriers.BearerName, sent.ContainsKey("Authorization"));
                Assert.Equal(carrier.Carrier == SessionTokenCarriers.CookieName, sent.ContainsKey("Cookie"));
                token ??= carrier.Carrier == SessionTokenCarriers.BearerName ? sent["Authorization"]["Bearer ".Length..] : null;
            }
        }
        Assert.Single(users);
        Assert.NotNull(token);
        Assert.Throws<InvalidOperationException>(() => Env.ClientCarrying("query", token));

        using var anonymous = Env.CreateClient();
        foreach (var (label, build) in new (string, Func<HttpRequestMessage>)[]
                 {
                     ("query access_token", () => new HttpRequestMessage(HttpMethod.Get, $"/api/auth/session?access_token={token}")),
                     ("query token", () => new HttpRequestMessage(HttpMethod.Get, $"/api/auth/session?token={token}")),
                     ("query session", () => new HttpRequestMessage(HttpMethod.Get, $"/api/auth/session?session={token}")),
                     ("query erp_session", () => new HttpRequestMessage(HttpMethod.Get, $"/api/auth/session?erp_session={token}")),
                     ("header X-Erp-Session", () => WithHeader("X-Erp-Session", token)),
                     ("header X-Api-Key", () => WithHeader("X-Api-Key", token)),
                     ("header X-Auth-Token", () => WithHeader("X-Auth-Token", token)),
                     ("header X-Access-Token", () => WithHeader("X-Access-Token", token)),
                     ("header Session", () => WithHeader("Session", token)),
                     ("Authorization Token", () => WithHeader("Authorization", "Token " + token)),
                     ("Authorization bare", () => WithHeader("Authorization", token)),
                     ("Authorization Basic", () => WithHeader("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(email + ":" + token)))),
                     ("Proxy-Authorization Bearer", () => WithHeader("Proxy-Authorization", "Bearer " + token)),
                     ("cookie session", () => WithHeader("Cookie", "session=" + token)),
                     ("cookie erp_token", () => WithHeader("Cookie", "erp_token=" + token)),
                     ("cookie access_token", () => WithHeader("Cookie", "access_token=" + token)),
                 })
        {
            using var request = build();
            using var response = await anonymous.SendAsync(request);
            var who = await CarrierMatrix.SignedInAsync(response);
            Assert.True(who is null, $"A session token sent as {label} was honoured: {who}");
        }

        static HttpRequestMessage WithHeader(string name, string value)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
            request.Headers.TryAddWithoutValidation(name, value);
            return request;
        }
    }

    /// <summary>
    /// Self-test (critic p00 round 9, plant P3): a host in which any signed-in Bearer caller passes
    /// every permission check, over this environment's database. Both matrices must report it, for
    /// the Bearer carrier and for no other; the same matrices pass on the real host.
    /// </summary>
    [Fact]
    public async Task The_carrier_matrices_catch_a_permission_bypass_for_bearer_callers()
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        const string permission = "identity.users.read";
        var email = $"g2.plant.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await CreateUserHoldingAsync(admin, email, [permission], "Planted Bearer check", "فحص حامل مزروع");
        var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();

        await using var planted = new ErpAppFactory(BearerBypassModule.Settings(Env.Factory.Settings));
        var denied = await CarrierMatrix.DeniedEverythingAsync(Env, Env.Email(Env.TenantA, "noaccess"), Permissioned, planted, ownIds: false);
        var exactlyOne = await CarrierMatrix.ExactlyOneAsync(Env, email, permission, expectedMenu, Permissioned, planted, selfCarriers: []);
        foreach (var (name, result) in new[] { ("deny by default", denied), ("grant exactly one", exactlyOne) })
        {
            var bearer = result.Problems.Where(p => p.StartsWith($"[{SessionTokenCarriers.BearerName}]", StringComparison.Ordinal)).ToList();
            Assert.True(bearer.Count > 0, $"The {name} matrix missed the planted Bearer bypass");
            Assert.True(bearer.Count == result.Problems.Count, $"The {name} matrix reported more than the plant:\n" + string.Join("\n", result.Problems.Except(bearer)));
        }
        Assert.Contains(denied.Problems, p => p.StartsWith("[bearer] GET /api/identity/users:", StringComparison.Ordinal) && p.EndsWith(": 200", StringComparison.Ordinal));
        Assert.Contains(exactlyOne.Problems, p => p.StartsWith("[bearer] identity.users.read: ", StringComparison.Ordinal) && p.EndsWith("(expected 403)", StringComparison.Ordinal));
    }

    private static void AssertEveryCarrier(IReadOnlyList<string> carriers)
    {
        Assert.Equal(SessionTokenCarriers.All.Select(c => c.Name), carriers);
        Assert.True(carriers.Count >= Ratchet.Min("g2.credentialCarriers"),
            $"{carriers.Count} session carriers checked; ratchet minimum {Ratchet.Min("g2.credentialCarriers")}");
    }

    /// <summary>A user holding one role that grants <paramref name="permissions"/>, or no role at
    /// all when none are given; returns the user's id.</summary>
    private static async Task<Guid> CreateUserHoldingAsync(HttpClient admin, string email, string[] permissions, string roleEn, string roleAr)
    {
        var roleIds = new List<Guid>();
        if (permissions.Length > 0)
        {
            var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = roleEn, nameAr = roleAr, permissions });
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
            roleIds.Add((await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"G2 {string.Join(", ", permissions)}", language = "en", password = ErpTestEnvironment.Password, roleIds });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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

    /// <summary>Critic p00 round 5, plant P2: the permission is declared as metadata, but the
    /// endpoint's authorization is only <c>.RequireAuthorization()</c>, so any signed-in user
    /// passes. The host must refuse to start, before any request is made.</summary>
    [Fact]
    public void The_host_refuses_a_permission_declared_as_metadata_that_no_policy_enforces()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: null,
            shape: e => e.WithMetadata(new RequiresPermissionAttribute("rogue.things.read")).RequireAuthorization())));
        Assert.Contains("no authorization policy on it requires that permission", error.Message, StringComparison.Ordinal);

        // A policy that requires another permission does not count either.
        error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: null, listPermission: "rogue.things.write",
            shape: e => e.WithMetadata(new RequiresPermissionAttribute("rogue.things.read"))
                .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(SessionAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser().AddRequirements(new PermissionRequirement("rogue.things.write")).Build()))));
        Assert.Contains("no authorization policy on it requires that permission", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_refuses_a_permissioned_endpoint_that_also_allows_anonymous_callers()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildHost(new RogueModule(declare: null,
            shape: e => e.RequirePermission("rogue.things.read").AllowAnonymous())));
        Assert.Contains("allows anonymous callers, so it is never checked", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_permissioned_endpoint_is_enforced_by_a_policy_requiring_its_permission()
    {
        var unenforced = Env.Factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<RequiresPermissionAttribute>() is { } p && !ErpPlatform.EnforcesPermission(e, p.Permission))
            .Select(e => e.DisplayName)
            .ToList();
        Assert.True(unenforced.Count == 0, "Declared but not enforced: " + string.Join(", ", unenforced));
        Assert.True(Permissioned.Count >= Ratchet.Min("g2.permissionedEndpoints"));
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
        var result = await ListViewChecksAsync(Env, "");
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Lists} lists, {result.Refusals} refusals checked on list endpoints, {result.AllowedWrites} own-view writes " +
            $"({result.FieldVariants} with one more field of the view schemas), {result.VisibilityChecks} view listings judged");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.Refusals >= Ratchet.Min("g2.listEndpointRefusals"), $"g2.listEndpointRefusals: {result.Refusals}; ratchet minimum {Ratchet.Min("g2.listEndpointRefusals")}");
        Assert.True(result.AllowedWrites >= Ratchet.Min("g2.listOwnViewWrites"), $"g2.listOwnViewWrites: {result.AllowedWrites}; ratchet minimum {Ratchet.Min("g2.listOwnViewWrites")}");
        Assert.True(result.VisibilityChecks >= Ratchet.Min("g2.listViewListingsJudged"), $"g2.listViewListingsJudged: {result.VisibilityChecks}; ratchet minimum {Ratchet.Min("g2.listViewListingsJudged")}");
        Assert.True(result.FieldVariants >= Ratchet.Min("g2.listViewFieldVariants"), $"g2.listViewFieldVariants: {result.FieldVariants}; ratchet minimum {Ratchet.Min("g2.listViewFieldVariants")}");
        Assert.True(result.ReaderRouteWrites >= Ratchet.Min("g2.listReaderRouteWrites"), $"g2.listReaderRouteWrites: {result.ReaderRouteWrites}; ratchet minimum {Ratchet.Min("g2.listReaderRouteWrites")}");
    }

    /// <summary>
    /// Self-test (critic p05 round 9, plant P8): a host over this environment's database where a
    /// personal view saved with <c>isShared: true</c> becomes the workspace's shared view (and its
    /// shared default when <c>isDefault</c> is true), although the personal route declares only the
    /// list's read permission (<see cref="SharedViewBypassModule"/>). The field is in the view the API
    /// answers, so the check sends it to the personal routes, and the reader's write must show as a
    /// change to the workspace's views.
    /// </summary>
    [Fact]
    public async Task The_list_view_check_catches_a_personal_route_that_shares_a_view()
    {
        await using var planted = Env.OverHost(SharedViewBypassModule.Settings(Env));
        var result = await ListViewChecksAsync(planted, "-p8", l => l.Key == "identity.users");
        Assert.Contains(result.Problems, p => p.StartsWith("identity.users: a reader's writes to its own personal views changed the administrator's or the workspace's views", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Contains("[isShared", StringComparison.Ordinal));
    }

    /// <summary>
    /// Self-test (critic p05 round 12, plant P11b): a host over this environment's database with one
    /// more route, POST /api/lists/identity.users/views/{id}/share, that makes a shared copy of the
    /// caller's own personal view under the list's read permission (<see cref="ShareCopyPlantModule"/>).
    /// A reader's writes through every route of the list must show it as a new shared view.
    /// </summary>
    [Fact]
    public async Task The_list_view_check_catches_any_route_that_lets_a_reader_share_its_own_view()
    {
        await using var planted = Env.OverHost(ShareCopyPlantModule.Settings(Env));
        var result = await ListViewChecksAsync(planted, "-p11b", l => l.Key == "identity.users");
        Assert.Contains(result.Problems, p => p.StartsWith("identity.users: a reader without lists.views.share changed the workspace's views through the list's write routes", StringComparison.Ordinal) &&
                                              p.Contains("POST /api/lists/identity.users/views/{id:guid}/share 200", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith("identity.users: after a reader without lists.views.share sent every write route", StringComparison.Ordinal));
    }

    /// <summary>What <see cref="ListViewChecksAsync"/> found and how much it sent.</summary>
    internal sealed record ListViewResult(List<string> Problems, int Lists, int Refusals, int AllowedWrites, int VisibilityChecks, int FieldVariants, int ReaderRouteWrites = 0);

    /// <summary>The list endpoint checks of <see cref="Every_list_endpoint_needs_the_lists_own_permission_and_shared_view_writes_need_sharing"/>
    /// on <paramref name="Env"/>'s host (the lists <paramref name="only"/> keeps); <paramref name="tag"/>
    /// keeps the roles and users it creates apart from another run's over the same database.</summary>
    internal static async Task<ListViewResult> ListViewChecksAsync(ErpTestEnvironment Env, string tag, Func<Erp.Kernel.Lists.ListDefinition, bool>? only = null)
    {
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var Endpoints = EndpointInventory.From(Env.Factory.Services);
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var anonymous = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);

        async Task<HttpClient> UserWith(string label, params string[] permissions)
        {
            label += tag;
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
        var allowedWrites = 0;
        var visibilityChecks = 0;
        var fieldVariants = 0;
        var readerRouteWrites = 0;
        var lists = 0;
        foreach (var list in catalog.Lists.Where(l => only?.Invoke(l) ?? true))
        {
            lists++;
            using var reader = await UserWith($"reader-{list.Key.Replace('.', '-')}", list.Permission);
            var columns = list.Columns.Take(1).Select(c => c.Key).ToArray();
            // Both of the administrator's views are defaults (critic p05 round 7, plant P6: a reader's
            // own personal default also cleared the workspace's shared default), so a write that
            // reaches past the caller's own views shows in the administrator's snapshot. The personal
            // default is saved first: a shared default replaces shared defaults only, and both must
            // be defaults in the snapshot the later writes are judged against.
            var personal = await (await admin.PostAsJsonAsync($"/api/lists/{list.Key}/views", new { name = $"Mine {list.Key}{tag}", columns, isDefault = true })).Content.ReadFromJsonAsync<JsonElement>();
            var shared = await (await admin.PostAsJsonAsync($"/api/lists/{list.Key}/shared-views", new { name = $"Team {list.Key}{tag}", columns, isDefault = true })).Content.ReadFromJsonAsync<JsonElement>();
            var before = await ViewsTextAsync(admin, list.Key);
            foreach (var (what, view) in new[] { ("personal", personal), ("shared", shared) })
            {
                Assert.True(before.Contains($"{view.GetProperty("id").GetString()}:{view.GetProperty("name").GetString()}:{view.GetProperty("version").GetUInt32()}:True", StringComparison.Ordinal),
                    $"{list.Key}: the administrator's {what} view is not a default before the checks: {before}");
            }

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
            var sharersOwn = await (await sharer.PostAsJsonAsync($"/api/lists/{list.Key}/views", new { name = $"Sharer's own {list.Key}{tag}", columns })).Content.ReadFromJsonAsync<JsonElement>();
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

            // What a caller may do touches only the caller's own views (critic p05 round 7, plant P6).
            // A reader without the share permission, and a sharer, save and change personal views of
            // their own with every body field and both values of isDefault; the administrator's
            // shared and personal defaults stay exactly as they were.
            foreach (var (who, client) in new[] { ("a reader", reader), ("a sharer", sharer) })
            {
                foreach (var isDefault in new[] { true, false, true })
                {
                    using var created = await client.PostAsJsonAsync($"/api/lists/{list.Key}/views", PersonalBody(list, columns, $"{who} {isDefault} {Guid.NewGuid():N}", isDefault, null));
                    allowedWrites++;
                    if (created.StatusCode != HttpStatusCode.Created)
                    {
                        problems.Add($"POST /api/lists/{list.Key}/views answered {(int)created.StatusCode} to {who} saving a personal view with isDefault {isDefault}");
                        continue;
                    }
                    var own = await created.Content.ReadFromJsonAsync<JsonElement>();
                    foreach (var change in new[] { !isDefault, isDefault })
                    {
                        using var changed = await client.PutAsJsonAsync($"/api/lists/{list.Key}/views/{own.GetProperty("id").GetString()}",
                            PersonalBody(list, columns, own.GetProperty("name").GetString()!, change, own.GetProperty("version").GetUInt32()));
                        allowedWrites++;
                        if (!changed.IsSuccessStatusCode)
                        {
                            problems.Add($"PUT /api/lists/{list.Key}/views/{{id}} answered {(int)changed.StatusCode} to {who} changing its own view to isDefault {change}");
                            break;
                        }
                        own = await changed.Content.ReadFromJsonAsync<JsonElement>();
                    }
                }
                // Every field a view can carry, one at a time on top of the full body (critic p05
                // round 9, plant P8: an isShared field honoured by POST /views made a reader's view the
                // workspace's shared default): the properties of every view endpoint's request schema
                // and of the view the API answers, each with every value of its type, sent to both
                // personal routes. A personal route keeps the view the caller's own and personal.
                var variantsSent = new List<string>();
                foreach (var (field, value) in ViewFieldVariants(openApi, list, shared, personal))
                {
                    var label = $"[{field}={value?.ToJsonString() ?? "null"}]";
                    var body = PersonalBody(list, columns, $"{who} {field} {Guid.NewGuid():N}", false, null);
                    body[field] = value;
                    using var created = await client.PostAsJsonAsync($"/api/lists/{list.Key}/views", body);
                    fieldVariants++;
                    variantsSent.Add("POST " + label);
                    if ((int)created.StatusCode >= 500)
                    {
                        problems.Add($"POST /api/lists/{list.Key}/views {label} answered {(int)created.StatusCode} to {who}");
                    }
                    if (created.StatusCode != HttpStatusCode.Created)
                    {
                        continue;
                    }
                    var own = await created.Content.ReadFromJsonAsync<JsonElement>();
                    if (own.GetProperty("isShared").GetBoolean() || !own.GetProperty("isMine").GetBoolean())
                    {
                        problems.Add($"POST /api/lists/{list.Key}/views {label} saved for {who} a view that is not its own personal view");
                    }
                    var change = PersonalBody(list, columns, own.GetProperty("name").GetString()!, false, own.GetProperty("version").GetUInt32());
                    change[field] = field.Equals("version", StringComparison.OrdinalIgnoreCase) ? change[field] : value;
                    using var changed = await client.PutAsJsonAsync($"/api/lists/{list.Key}/views/{own.GetProperty("id").GetString()}", change);
                    fieldVariants++;
                    variantsSent.Add("PUT " + label);
                    if ((int)changed.StatusCode >= 500)
                    {
                        problems.Add($"PUT /api/lists/{list.Key}/views/{{id}} {label} answered {(int)changed.StatusCode} to {who}");
                    }
                    if (changed.IsSuccessStatusCode && await changed.Content.ReadFromJsonAsync<JsonElement>() is var saved &&
                        (saved.GetProperty("isShared").GetBoolean() || !saved.GetProperty("isMine").GetBoolean()))
                    {
                        problems.Add($"PUT /api/lists/{list.Key}/views/{{id}} {label} left {who}'s view no longer its own personal view");
                    }
                }
                var mine = await ViewsTextAsync(admin, list.Key);
                if (mine != before)
                {
                    problems.Add($"{list.Key}: {who}'s writes to its own personal views changed the administrator's or the workspace's views:\nbefore {before}\nafter  {mine}" +
                                 $"\n(field variants sent: {string.Join(" ", variantsSent)})");
                }
                var listedToCaller = await client.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/views");
                foreach (var item in listedToCaller.GetProperty("items").EnumerateArray()
                             .Where(v => v.GetProperty("isShared").GetBoolean() && !before.Contains(v.GetProperty("id").GetString()!, StringComparison.Ordinal)))
                {
                    problems.Add($"{list.Key}: after {who}'s writes to its own personal views, GET /views lists a shared view the workspace did not have ({item.GetProperty("name").GetString()})");
                }
            }

            // Every write route of the list, whatever its method and shape (critic p05 round 12, plant
            // P11b: POST /views/{id}/share made a shared copy of the caller's own view and declared
            // only the list's read permission; the writes above go only to POST /views and PUT
            // /views/{id}, so the behavioural check passed). A reader without the share permission
            // sends each one with its own personal view's id in every route parameter and a full
            // body; the workspace's views stay exactly as they were and no shared view appears.
            var readersOwn = await (await reader.PostAsJsonAsync($"/api/lists/{list.Key}/views", PersonalBody(list, columns, $"Reader's own {list.Key}{tag}", false, null)))
                .Content.ReadFromJsonAsync<JsonElement>();
            var routesSent = new List<string>();
            foreach (var endpoint in listEndpoints.Where(e => e.Method != "GET").OrderBy(e => e.Method == "DELETE" ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                using var request = ViewRequest(endpoint, readersOwn, columns, endpoint.Method == "POST" ? $"Reader's write {Guid.NewGuid():N}" : null);
                if (endpoint.HasBody && endpoint.Method != "POST")
                {
                    var body = PersonalBody(list, columns, readersOwn.GetProperty("name").GetString() + " (changed)", false, readersOwn.GetProperty("version").GetUInt32());
                    request.Content = JsonContent.Create(body);
                }
                using var response = await reader.SendAsync(request);
                readerRouteWrites++;
                routesSent.Add($"{endpoint.Method} {endpoint.Pattern} {(int)response.StatusCode}");
                if ((int)response.StatusCode >= 500)
                {
                    problems.Add($"{endpoint} answered {(int)response.StatusCode} to a reader of {list.Key} sending its own view");
                }
            }
            var afterReaderRoutes = await ViewsTextAsync(admin, list.Key);
            if (afterReaderRoutes != before)
            {
                problems.Add($"{list.Key}: a reader without lists.views.share changed the workspace's views through the list's write routes with its own view:\nbefore {before}\nafter  {afterReaderRoutes}" +
                             $"\n(routes sent: {string.Join("; ", routesSent)})");
            }
            foreach (var item in (await reader.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/views")).GetProperty("items").EnumerateArray()
                         .Where(v => v.GetProperty("isShared").GetBoolean() && !before.Contains(v.GetProperty("id").GetString()!, StringComparison.Ordinal)))
            {
                problems.Add($"{list.Key}: after a reader without lists.views.share sent every write route with its own view, GET /views lists a shared view the workspace did not have ({item.GetProperty("name").GetString()}; routes sent: {string.Join("; ", routesSent)})");
            }

            // Who sees which views (critic p05 round 7, plant P7: every user's personal views were
            // listed to any reader): GET /views answers shared views and the caller's own only.
            var personalIds = new Dictionary<string, string>(StringComparer.Ordinal) { [personal.GetProperty("id").GetString()!] = "the administrator's", [sharersOwn.GetProperty("id").GetString()!] = "the sharer's" };
            foreach (var (who, client, ownId) in new[] { ("the administrator", admin, personal.GetProperty("id").GetString()!), ("the sharer", sharer, sharersOwn.GetProperty("id").GetString()!), ("a reader", reader, "") })
            {
                var listed = await client.GetFromJsonAsync<JsonElement>($"/api/lists/{list.Key}/views");
                visibilityChecks++;
                foreach (var item in listed.GetProperty("items").EnumerateArray())
                {
                    var id = item.GetProperty("id").GetString()!;
                    if (!item.GetProperty("isShared").GetBoolean() && !item.GetProperty("isMine").GetBoolean())
                    {
                        problems.Add($"GET /api/lists/{list.Key}/views listed to {who} a personal view that is not its own ({id})");
                    }
                    if (personalIds.TryGetValue(id, out var whose) && id != ownId)
                    {
                        problems.Add($"GET /api/lists/{list.Key}/views listed to {who} {whose} personal view");
                    }
                }
                if (!listed.GetProperty("items").EnumerateArray().Any(v => v.GetProperty("id").GetString() == shared.GetProperty("id").GetString()))
                {
                    problems.Add($"GET /api/lists/{list.Key}/views did not list the workspace's shared view to {who}");
                }
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
                using var request = ViewRequest(endpoint, view, columns, endpoint.Method == "POST" ? $"Again {list.Key}{tag}" : null);
                using var response = await admin.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    problems.Add($"{endpoint} answered {(int)response.StatusCode} to the administrator, so the refusals above may not have been the permission check");
                }
            }
        }
        return new ListViewResult(problems, lists, refusals, allowedWrites, visibilityChecks, fieldVariants, readerRouteWrites);
    }

    /// <summary>
    /// Every field a saved view can carry, each with every value of its type: the properties of the
    /// request schemas of the list's view endpoints (personal and shared) and of the view GET answers.
    /// Booleans both ways; ids those of the workspace's shared view and the administrator's personal
    /// view; text the shared view's own; column lists every column; the concurrency token is copied.
    /// A property the check cannot give a value is a blind spot, reported by the caller as a field
    /// sent as null.
    /// </summary>
    private static IEnumerable<(string Field, System.Text.Json.Nodes.JsonNode? Value)> ViewFieldVariants(OpenApiDocument openApi, Erp.Kernel.Lists.ListDefinition list,
        JsonElement shared, JsonElement personal)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var prefix = $"/api/lists/{list.Key}";
        foreach (var (method, pattern) in new[] { ("POST", "/views"), ("PUT", "/views/{id}"), ("POST", "/shared-views"), ("PUT", "/shared-views/{id}") })
        {
            if (openApi.RequestSchema(method, prefix + pattern) is { } schema && schema.TryGetProperty("properties", out var props))
            {
                foreach (var property in props.EnumerateObject())
                {
                    properties.TryAdd(property.Name, property.Value);
                }
            }
        }
        if (openApi.TryGetOperation("GET", prefix + "/views/{id}", out var get) && get.TryGetProperty("responses", out var responses) &&
            responses.TryGetProperty("200", out var ok) && ok.TryGetProperty("content", out var content) &&
            content.TryGetProperty("application/json", out var json) && json.TryGetProperty("schema", out var answered) &&
            openApi.Resolve(answered).TryGetProperty("properties", out var answeredProps))
        {
            foreach (var property in answeredProps.EnumerateObject())
            {
                properties.TryAdd(property.Name, property.Value);
            }
        }
        Assert.True(properties.ContainsKey("isShared"), $"{list.Key}: the view schemas name no isShared: the field variants are blind");
        foreach (var (name, schema) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (FieldVariants.ConcurrencyTokens.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            var resolved = openApi.Resolve(schema);
            var format = resolved.TryGetProperty("format", out var f) ? f.GetString() : null;
            var values = (openApi.TypeOfSchema(resolved), format) switch
            {
                ("boolean", _) => new System.Text.Json.Nodes.JsonNode?[] { true, false },
                ("string", "uuid") => [shared.GetProperty("id").GetString(), personal.GetProperty("id").GetString()],
                ("string", "date-time") => [DateTimeOffset.UtcNow.AddDays(-1).ToString("O")],
                ("array", _) => [new System.Text.Json.Nodes.JsonArray(list.Columns.Select(c => (System.Text.Json.Nodes.JsonNode?)c.Key).ToArray())],
                ("integer", _) or ("number", _) => [1],
                ("string", _) => [shared.TryGetProperty(name, out var own) && own.ValueKind == JsonValueKind.String ? own.GetString() : $"Field {name}"],
                _ => [null],
            };
            foreach (var value in values)
            {
                yield return (name, value);
            }
        }
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

    /// <summary>A personal view body with every field set: columns, a sort, a search, a filter and a
    /// grouping the list accepts, and the given isDefault.</summary>
    private static Dictionary<string, object?> PersonalBody(Erp.Kernel.Lists.ListDefinition list, string[] columns, string name, bool isDefault, uint? version) => new()
    {
        ["name"] = name,
        ["columns"] = columns,
        ["sort"] = list.Columns.FirstOrDefault(c => c.Sortable) is { } sortable ? "-" + sortable.Key : null,
        ["search"] = list.SearchFields.Count > 0 ? "a" : null,
        ["filter"] = list.Columns.FirstOrDefault(c => c.Filterable) is { } filterable ? $"{filterable.Key} is not null" : null,
        ["groupBy"] = list.Columns.FirstOrDefault(c => c.Groupable)?.Key,
        ["isDefault"] = isDefault,
        ["version"] = version,
    };

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

    private sealed class RogueModule(string? declare, bool register = true, string? listPermission = null, Action<RouteHandlerBuilder>? shape = null) : ErpModule
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
                if (shape is not null)
                {
                    shape(endpoint);
                }
                else if (declare is not null)
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
