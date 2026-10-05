using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: these tests create users and roles and change company access.</summary>
public sealed class CompanyGrantFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2, company and branch access is a grant. Every endpoint whose request body carries a company
/// grant field (<c>companies</c>: items of companyId, allBranches, branchIds) is found in the
/// OpenAPI document and aimed at a user in its route. Critic p02 round 2: a clerk whose role held
/// only "change company access" removed the tenant Administrator from a company, and a manager
/// limited to one branch gave someone every branch; nothing looked. So, for each such endpoint:
/// <list type="bullet">
/// <item>a caller holding only that endpoint's permission (and reading access) aims it at the
/// Administrator, who holds more permissions: 403, and the Administrator's access rows are
/// unchanged; the same request aimed at a user with no roles succeeds (the control);</item>
/// <item>an administrator who works in one company only aims it at the tenant Administrator, who
/// works in more companies: 403, rows unchanged; the control on a one-company user succeeds;</item>
/// <item>an administrator limited to one branch of a company may give only that branch: giving
/// every branch or another branch is refused, and so is narrowing or removing the access of a user
/// who holds every branch; giving the caller's own branch succeeds;</item>
/// <item>nobody changes their own access through it (critic p02 round 2, plant P2: only a module
/// test noticed when that check was removed).</item>
/// </list>
/// Each refusal is also tried by an attacker for whom only that one rule can apply (critic p02
/// round 3, plant P3: with the "user holds permissions the caller lacks" check removed this gate
/// still passed, because its only clerk worked in company X alone and the separate "user works in
/// companies the caller does not" rule refused first). So the clerk who holds only the access
/// permission is tried a second time working in every company and every branch: then only the
/// permission rule stands between them and the Administrator. Where the endpoint's request carries
/// a concurrency token (<c>version</c>), every request sends the version the caller just read, so a
/// refusal is the grant check's and never a stale version's; and a request with a stale version
/// must be refused with 409 and change nothing.
/// </summary>
public sealed class G2CompanyAccessGrantTests(CompanyGrantFixture fixture) : IClassFixture<CompanyGrantFixture>
{
    [Fact]
    public async Task Company_access_is_given_and_taken_only_within_what_the_caller_holds_and_never_from_a_stronger_user()
    {
        var result = await CompanyGrants.RunAsync(fixture.Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Endpoints.Count} company grant endpoint(s) ({string.Join(", ", result.Endpoints)}), {result.Checks} checks");
        Assert.Contains("PUT /api/tenancy/access/{userId:guid}", result.Endpoints);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.Checks >= Ratchet.Min("g2.companyGrantChecks"), $"{result.Checks} company grant checks; ratchet minimum {Ratchet.Min("g2.companyGrantChecks")}");
    }
}

/// <summary>The company grant check, reusable by the gate self-tests (see <see cref="G2CompanyAccessGrantTests"/>).</summary>
public static class CompanyGrants
{
    public const string GrantField = "companies";

    public sealed record Result(IReadOnlyList<string> Problems, int Checks, IReadOnlyList<string> Endpoints);

    private sealed record Access(Guid CompanyId, bool AllBranches, Guid[] BranchIds);

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => !e.IsAnonymous && e.HasBody && e.RouteParameters.Count == 1)
            .Where(e => openApi.RequestSchema(e.Method, e.Pattern) is { } s && openApi.Resolve(s).TryGetProperty("properties", out var p) && p.TryGetProperty(GrantField, out _))
            .ToList();

        var tenant = env.TenantA;
        List<Guid> companies;
        Dictionary<Guid, List<Guid>> branches;
        await using (var owner = await env.OpenAdminAsync())
        {
            companies = await DbCatalog.ReadAsync(owner, "SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY id", r => r.GetGuid(0), ("t", tenant.Id));
            branches = (await DbCatalog.ReadAsync(owner, "SELECT company_id, id FROM tenancy.branches WHERE tenant_id = @t ORDER BY id",
                    r => (Company: r.GetGuid(0), Branch: r.GetGuid(1)), ("t", tenant.Id)))
                .GroupBy(x => x.Company).ToDictionary(g => g.Key, g => g.Select(x => x.Branch).ToList());
        }
        Assert.True(companies.Count >= 2, "tenant A needs two companies");
        var x = companies[0];
        Assert.True(branches.GetValueOrDefault(x)?.Count >= 2, "company X needs two branches");
        var (bx1, bx2) = (branches[x][0], branches[x][1]);

        using var admin = await env.SignInAsync(env.Email(tenant, "admin"));
        var adminId = (await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        var administratorRole = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var problems = new List<string>();
        var checks = 0;
        var n = 0;

        foreach (var endpoint in endpoints)
        {
            var schema = openApi.Resolve(openApi.RequestSchema(endpoint.Method, endpoint.Pattern)!.Value);
            var item = openApi.Resolve(schema.GetProperty("properties").GetProperty(GrantField));
            if (!(item.TryGetProperty("items", out var items) && openApi.Resolve(items).TryGetProperty("properties", out var itemProperties) &&
                  itemProperties.TryGetProperty("companyId", out _) && itemProperties.TryGetProperty("allBranches", out _) && itemProperties.TryGetProperty("branchIds", out _)))
            {
                problems.Add($"{endpoint}: its {GrantField} items are not (companyId, allBranches, branchIds); extend the company grant gate for this shape");
                continue;
            }
            var readPermission = EndpointInventory.From(env.Factory.Services)
                .FirstOrDefault(e => e.Method == "GET" && e.Pattern == endpoint.Pattern)?.Permission;
            string Tag() => $"{++n}{Guid.NewGuid():N}"[..10];

            async Task<(Guid Id, string Email)> UserAsync(string label, Guid[] roleIds, Access[] access)
            {
                var tag = Tag();
                var email = $"cg.{label}.{tag}@{tenant.EmailDomain}";
                var id = await CreatedIdAsync(admin, "/api/identity/users",
                    new { email, displayName = $"Grant {label} {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds });
                if (access.Length > 0)
                {
                    var (status, text) = await SendAsync(admin, endpoint, id, access, readPermission is null ? null : await VersionAsync(admin, endpoint, id));
                    if (status != 200) throw new InvalidOperationException($"the administrator could not give {label} its access: {status} {text}");
                }
                return (id, email);
            }

            async Task Expect(string label, HttpClient caller, Guid target, Access[] body, bool allowed, bool refusedAsInvalid = false)
            {
                checks++;
                var before = await RowsAsync(target);
                // The version the caller reads now: a refusal must come from the grant check, never from a stale version.
                var version = readPermission is null ? null : await VersionAsync(caller, endpoint, target);
                var (status, text) = await SendAsync(caller, endpoint, target, body, version);
                var after = await RowsAsync(target);
                if (allowed)
                {
                    if (status is < 200 or >= 300)
                    {
                        problems.Add($"{endpoint} {label}: answered {status}, expected success (the control that proves the refusals come from the grant check): {Short(text)}");
                    }
                    return;
                }
                // 403 (the grant check); 400 only where the caller cannot even see what it names.
                if (status != 403 && !(refusedAsInvalid && status == 400))
                {
                    problems.Add($"{endpoint} {label}: answered {status}, expected 403{(refusedAsInvalid ? " or 400" : "")}: {Short(text)}");
                }
                if (before != after)
                {
                    problems.Add($"{endpoint} {label}: the target's access rows changed: {before} → {after}");
                }
            }

            // 1. A caller holding only this endpoint's permission (and reading access), in company X.
            var juniorRole = await CreatedIdAsync(admin, "/api/identity/roles", new
            {
                nameEn = $"Access clerk {Tag()}", nameAr = $"موظف صلاحيات {Tag()}",
                permissions = new[] { endpoint.Permission, readPermission ?? endpoint.Permission }.Distinct().ToArray(),
            });
            var junior = await UserAsync("junior", [juniorRole], [new Access(x, true, [])]);
            using var juniorClient = await env.SignInAsync(junior.Email);
            await Expect("by a user holding only the access permission, removing the Administrator from company X", juniorClient, adminId, [], false);
            await Expect("by a user holding only the access permission, limiting the Administrator to one branch of X", juniorClient, adminId, [new Access(x, false, [bx1])], false);
            var plain = await UserAsync("plain", [], [new Access(x, true, [])]);
            await Expect("by a user holding only the access permission, limiting a user without roles to one branch of X (control)", juniorClient, plain.Id, [new Access(x, false, [bx1])], true);
            await Expect("by a user holding only the access permission, removing a user without roles from X (control)", juniorClient, plain.Id, [], true);

            // 1b. The same permissions, working in every company and every branch: the company and
            // branch rules cannot refuse, only the permission rule can (critic p02 round 3, plant P3).
            var everywhere = companies.Select(c => new Access(c, true, [])).ToArray();
            var withoutX = everywhere.Where(a => a.CompanyId != x).ToArray();
            var xNarrowed = everywhere.Select(a => a.CompanyId == x ? new Access(x, false, [bx1]) : a).ToArray();
            var clerk = await UserAsync("clerk", [juniorRole], everywhere);
            using var clerkClient = await env.SignInAsync(clerk.Email);
            await Expect("by a user holding only the access permission who works in every company, removing the Administrator from company X", clerkClient, adminId, withoutX, false);
            await Expect("by a user holding only the access permission who works in every company, limiting the Administrator to one branch of X", clerkClient, adminId, xNarrowed, false);
            await Expect("by a user holding only the access permission who works in every company, taking every company from the Administrator", clerkClient, adminId, [], false);
            var plainEverywhere = await UserAsync("plaineverywhere", [], everywhere);
            await Expect("by a user holding only the access permission who works in every company, limiting a user without roles to one branch of X (control)", clerkClient, plainEverywhere.Id, xNarrowed, true);
            await Expect("by a user holding only the access permission who works in every company, removing a user without roles from X (control)", clerkClient, plainEverywhere.Id, withoutX, true);

            // 1c. A stale version is refused and changes nothing (two administrators editing one
            // user's access must not overwrite each other silently).
            if (HasVersion(openApi, schema) && readPermission is not null)
            {
                checks++;
                var stale = await UserAsync("stale", [], everywhere);
                var staleVersion = await VersionAsync(admin, endpoint, stale.Id);
                var (moved, movedText) = await SendAsync(admin, endpoint, stale.Id, withoutX, staleVersion);
                if (moved != 200) problems.Add($"{endpoint}: the administrator could not change a user's access with the version just read: {moved} {Short(movedText)}");
                var beforeStale = await RowsAsync(stale.Id);
                var (staleStatus, staleText) = await SendAsync(admin, endpoint, stale.Id, everywhere, staleVersion);
                if (staleStatus != 409) problems.Add($"{endpoint}: a request carrying the version read before another change answered {staleStatus}, expected 409: {Short(staleText)}");
                if (await RowsAsync(stale.Id) != beforeStale) problems.Add($"{endpoint}: a request carrying a stale version changed the user's access rows");
            }
            else if (readPermission is not null)
            {
                // A user's access is read and saved as a record (a GET at the same route): its save must carry the version read.
                problems.Add($"{endpoint}: the request carries no concurrency token (version) although the access is read at the same route; two administrators editing one user's access would overwrite each other");
            }

            // 2. Nobody changes their own access.
            await Expect("by a user on themselves (unchanged access)", juniorClient, junior.Id, [new Access(x, true, [])], false);
            await Expect("by a user on themselves (removing it)", juniorClient, junior.Id, [], false);

            // 3. An administrator of company X alone, against the tenant Administrator (more companies).
            var xAdmin = await UserAsync("xadmin", [administratorRole], [new Access(x, true, [])]);
            using var xAdminClient = await env.SignInAsync(xAdmin.Email);
            await Expect("by an administrator of company X alone, removing the tenant Administrator (who works in more companies) from X", xAdminClient, adminId, [], false);
            await Expect("by an administrator of company X alone, limiting the tenant Administrator to one branch of X", xAdminClient, adminId, [new Access(x, false, [bx2])], false);
            await Expect("by an administrator, on themselves", xAdminClient, xAdmin.Id, [], false);
            var oneCompany = await UserAsync("onecompany", [], [new Access(x, true, [])]);
            await Expect("by an administrator of company X alone, removing a user of X alone (control)", xAdminClient, oneCompany.Id, [], true);

            // 4. An administrator limited to branch 1 of company X gives and takes only that branch.
            var branchAdmin = await UserAsync("branchadmin", [administratorRole], [new Access(x, false, [bx1])]);
            using var branchClient = await env.SignInAsync(branchAdmin.Email);
            var empty = await UserAsync("empty", [], []);
            await Expect("by an administrator limited to branch 1 of X, giving every branch of X", branchClient, empty.Id, [new Access(x, true, [])], false);
            await Expect("by an administrator limited to branch 1 of X, giving branch 2 of X", branchClient, empty.Id, [new Access(x, false, [bx2])], false, refusedAsInvalid: true);
            await Expect("by an administrator limited to branch 1 of X, giving branches 1 and 2 of X", branchClient, empty.Id, [new Access(x, false, [bx1, bx2])], false, refusedAsInvalid: true);
            await Expect("by an administrator limited to branch 1 of X, giving branch 1 of X (control)", branchClient, empty.Id, [new Access(x, false, [bx1])], true);
            var everyBranch = await UserAsync("everybranch", [], [new Access(x, true, [])]);
            await Expect("by an administrator limited to branch 1 of X, narrowing a user of every branch of X to branch 1", branchClient, everyBranch.Id, [new Access(x, false, [bx1])], false);
            await Expect("by an administrator limited to branch 1 of X, removing a user of every branch of X", branchClient, everyBranch.Id, [], false);
            var bothBranches = await UserAsync("bothbranches", [], [new Access(x, false, [bx1, bx2])]);
            await Expect("by an administrator limited to branch 1 of X, taking branch 2 from a user of branches 1 and 2", branchClient, bothBranches.Id, [new Access(x, false, [bx1])], false);
        }

        // The tenant Administrator still works in every company, unharmed.
        var adminRows = await RowsAsync(adminId);
        if (companies.Any(c => !adminRows.Contains($"{c}:all", StringComparison.Ordinal)))
        {
            problems.Add($"the tenant Administrator lost access: {adminRows}");
        }
        return new Result(problems, checks, endpoints.Select(e => e.Key).ToList());

        Task<string> RowsAsync(Guid user) => CompanyGrants.RowsAsync(env, user);
    }

    /// <summary>The target's company and branch access rows, read with the superuser.</summary>
    private static async Task<string> RowsAsync(ErpTestEnvironment env, Guid user)
    {
        await using var owner = await env.OpenAdminAsync();
        var companies = await DbCatalog.ReadAsync(owner,
            "SELECT company_id::text || ':' || CASE WHEN all_branches THEN 'all' ELSE 'some' END FROM tenancy.user_company_access WHERE user_id = @u ORDER BY company_id",
            r => r.GetString(0), ("u", user));
        var branchRows = await DbCatalog.ReadAsync(owner, "SELECT branch_id::text FROM tenancy.user_branch_access WHERE user_id = @u ORDER BY branch_id",
            r => r.GetString(0), ("u", user));
        return string.Join(",", companies) + " | " + string.Join(",", branchRows);
    }

    private static bool HasVersion(OpenApiDocument openApi, JsonElement schema) =>
        openApi.Resolve(schema).TryGetProperty("properties", out var properties) && properties.TryGetProperty("version", out _);

    /// <summary>The <c>version</c> the GET at the endpoint's own route answers for the target, read
    /// by <paramref name="client"/> (null when the read is refused or carries none).</summary>
    private static async Task<JsonNode?> VersionAsync(HttpClient client, ApiEndpoint endpoint, Guid target)
    {
        using var response = await client.GetAsync(endpoint.Path(_ => target.ToString()));
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var read = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
        return read?["version"]?.DeepClone();
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, ApiEndpoint endpoint, Guid target, Access[] access, JsonNode? version = null)
    {
        var body = new JsonObject
        {
            [GrantField] = new JsonArray(access.Select(a => (JsonNode)new JsonObject
            {
                ["companyId"] = a.CompanyId,
                ["allBranches"] = a.AllBranches,
                ["branchIds"] = new JsonArray(a.BranchIds.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
            }).ToArray()),
        };
        if (version is not null)
        {
            body["version"] = version.DeepClone();
        }
        using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(_ => target.ToString()))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, object body)
    {
        using var response = await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
