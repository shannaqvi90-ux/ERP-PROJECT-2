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

/// <summary>
/// G2, privilege escalation by acting on users chosen by a search or a filter (critic p05 round
/// 5, plant P5; critic p03 round 5, finding R1). Every write without a route id whose body selects
/// rows the way a list does (a <c>search</c> or <c>filter</c> field) and that sits under a list of
/// users is found from the running app and its API description. A caller holding exactly that
/// endpoint's permission (plus reading users and roles) aims the selection, by search and by
/// filter, at exactly one stronger user, with every combination of the body's flags and the list's
/// own count of the selection in its count fields: the stronger user must stay exactly as they
/// were. The stronger users are
/// <list type="bullet">
/// <item>the Administrator;</item>
/// <item>users holding grants the caller lacks in every shape of <see cref="GrantTargets"/>,
/// through roles held in every company;</item>
/// <item>users holding one module's grants the caller lacks through a role held in one company
/// only, which the caller works in (critic p03 round 5, R1: a check reading only workspace-wide
/// roles let a helpdesk clerk deactivate a company manager);</item>
/// <item>a user holding a role, granting nothing the caller lacks, in a company the caller does
/// not work in (what it grants cannot be seen from the caller's companies). For these requests
/// the caller works in the first company alone.</item>
/// </list>
/// The same requests aimed at a user without roles must change that user, which proves the
/// selection reached them (also while the caller works in one company alone). Any other identity
/// write that selects like a list has no check yet and is reported until it gets one.
/// </summary>
public static class SetTakeover
{
    public sealed record Result(List<string> Problems, List<string> Checked, int Aimed, int CompanyAimed);

    /// <summary>Which stronger users to aim at: <see cref="Every"/> (the gate), or
    /// <see cref="PerModule"/> (one target per module and shape, for the self-tests).</summary>
    public enum Targets { Every, PerModule }

    /// <param name="env">The environment.</param>
    /// <param name="userLists">Endpoints of lists whose rows are users: a set-based write under
    /// one of them is checked, counted through that list. Default: the users list.</param>
    /// <param name="only">Only these set-based writes, every other one left out (default: every one found).</param>
    /// <param name="targets">Which stronger users to aim at.</param>
    /// <param name="freshAdministrator">Aim at a new user holding the Administrator role rather than
    /// the seeded Administrator (an environment other tests still sign in to as that administrator).</param>
    public static async Task<Result> RunAsync(ErpTestEnvironment env, IReadOnlyList<string>? userLists = null, Func<ApiEndpoint, bool>? only = null,
        Targets targets = Targets.Every, bool freshAdministrator = false)
    {
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var catalogue = catalog.PermissionKeys.ToList();
        var usersList = catalog.Lists.Single(l => l.Endpoint == "/api/identity/users").Endpoint;
        var lists = userLists ?? [usersList];
        var problems = new List<string>();
        var setEndpoints = new List<(ApiEndpoint Endpoint, JsonElement Schema, string List)>();
        foreach (var endpoint in EndpointInventory.From(env.Factory.Services).Where(e => e.HasBody && !e.IsAnonymous && e.RouteParameters.Count == 0))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema || !SelectsLikeAList(schema) || (only is not null && !only(endpoint)))
            {
                continue;
            }
            if (lists.FirstOrDefault(l => endpoint.Pattern.StartsWith(l + "/", StringComparison.Ordinal)) is { } list)
            {
                // Counted as the users list counts them (a list of users elsewhere selects the same rows).
                setEndpoints.Add((endpoint, schema, list.StartsWith("/api/identity/", StringComparison.Ordinal) ? list : usersList));
            }
            else if (endpoint.Pattern.StartsWith("/api/identity/", StringComparison.Ordinal))
            {
                problems.Add($"{endpoint}: changes identity records chosen by a search or filter, and the takeover gate has no check for that kind of record yet; extend it");
            }
        }

        using var seeded = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var adminId = (await seeded.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        // The gate reads and prepares through an Administrator of its own, so a change that reaches
        // the seeded Administrator (the target) is reported as such, not as the gate losing its session.
        var administratorRole = (await seeded.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var observerEmail = $"setobserver.{Guid.NewGuid():N}"[..24] + $"@{env.TenantA.EmailDomain}";
        var observerId = await CreatedIdAsync(seeded, "/api/identity/users", new { email = observerEmail, displayName = "Set takeover observer", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { administratorRole } });
        // The observer works in the seeded Administrator's first two companies, where the probes
        // assign roles in one company and give access.
        await (await GateCompanies.OfAsync(seeded)).GiveAccessAsync(observerId);
        using var admin = await env.SignInAsync(observerEmail);
        var administrator = (Id: adminId, Email: env.Email(env.TenantA, "admin"));
        if (freshAdministrator)
        {
            var email = $"setadmin.{Guid.NewGuid():N}"[..21] + $"@{env.TenantA.EmailDomain}";
            administrator = (await CreatedIdAsync(admin, "/api/identity/users", new { email, displayName = "Set takeover Administrator", language = "en", roleIds = new[] { administratorRole } }), email);
        }
        var records = new TargetRecords(admin, env);
        var companies = await GateCompanies.OfAsync(admin);
        var checkedEndpoints = new List<string>();
        var aimed = 0;
        var companyAimed = 0;
        var n = 0;
        foreach (var (endpoint, schema, list) in setEndpoints)
        {
            var tag = $"s{++n}{Guid.NewGuid():N}"[..10];
            var permissions = new[] { endpoint.Permission, "identity.users.read", "identity.roles.read" }.Distinct().ToArray();
            var roleId = await CreatedIdAsync(admin, "/api/identity/roles", new { nameEn = $"Set takeover {tag}", nameAr = $"استيلاء جماعي {tag}", permissions });
            var callerEmail = $"settakeover.{tag}@{env.TenantA.EmailDomain}";
            var callerId = await CreatedIdAsync(admin, "/api/identity/users", new { email = callerEmail, displayName = $"Set takeover {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
            await companies.GiveAccessAsync(callerId);
            using var caller = await env.SignInAsync(callerEmail);

            var weakEmail = $"settarget.{tag}@{env.TenantA.EmailDomain}";
            var weakId = await CreatedIdAsync(admin, "/api/identity/users", new { email = weakEmail, displayName = $"Set target {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() });
            await companies.GiveAccessAsync(weakId);
            // A second one for the requests made while the caller works in one company alone.
            var limitedEmail = $"setlimited.{tag}@{env.TenantA.EmailDomain}";
            var limitedId = await CreatedIdAsync(admin, "/api/identity/users", new { email = limitedEmail, displayName = $"Set limited target {tag}", language = "en", roleIds = Array.Empty<Guid>() });

            // Stronger users: (id, label, e-mail, held in one company, held where the caller does not work).
            var strong = new List<(Guid Id, string Label, string Email, bool InCompany, bool Elsewhere)> { (administrator.Id, "the Administrator", administrator.Email, false, false) };
            foreach (var target in targets == Targets.Every ? GrantTargets.For(catalogue, permissions) : GrantTargets.PerModule(catalogue, permissions))
            {
                var id = await records.UserAsync(target.Permissions);
                strong.Add((id, $"a user holding {target}", await EmailOfAsync(admin, id), false, false));
            }
            if (companies.First is { } inCompany)
            {
                foreach (var target in GrantTargets.PerModule(catalogue, permissions))
                {
                    var id = await records.UserInCompanyAsync(target.Permissions, inCompany, companies);
                    strong.Add((id, $"a user holding {target} in one company only", await EmailOfAsync(admin, id), true, false));
                }
                if (companies.Ids.Count > 1)
                {
                    var id = await records.UserInCompanyAsync(["identity.users.read"], companies.Ids[1], companies, fresh: true);
                    strong.Add((id, "a user holding a role in a company the caller does not work in", await EmailOfAsync(admin, id), true, true));
                }
            }
            var variants = FlagVariants(schema);
            foreach (var (selector, select) in new (string, Func<string, (string? Search, string? Filter)>)[]
                     {
                         ("by search", e => (e, null)),
                         ("by filter", e => (null, Erp.Kernel.Lists.ListFilterText.Eq("email", e))),
                     })
            {
                var weakChanged = false;
                var weakChangedWorkingInOne = false;
                foreach (var flags in variants)
                {
                    foreach (var (strongId, label, strongEmail, inOneCompany, elsewhere) in strong)
                    {
                        if (elsewhere)
                        {
                            await companies.LimitAccessAsync(callerId, companies.First!.Value);
                        }
                        try
                        {
                            var (search, filter) = select(strongEmail);
                            var count = await ListCountAsync(caller, list, search, filter);
                            if (count != 1)
                            {
                                problems.Add($"{endpoint} {selector}: the users list answers {count} users for {label}'s address as the caller reads it (expected exactly 1); the gate cannot aim at them alone");
                                continue;
                            }
                            var before = await ReadUserAsync(admin, strongId);
                            var (status, text) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => ""), SetBody(schema, search, filter, flags, count));
                            aimed++;
                            if (inOneCompany)
                            {
                                companyAimed++;
                            }
                            var after = await ReadUserAsync(admin, strongId);
                            if (after != before)
                            {
                                problems.Add($"{endpoint} {selector} [{Describe(flags)}]: aimed at {label} by a user holding only [{string.Join(", ", permissions)}] answered {status} and changed them: " +
                                             $"before {Short(before)}; after {Short(after)}; answer {Short(text)}");
                            }
                            if (status >= 500)
                            {
                                problems.Add($"{endpoint} {selector} [{Describe(flags)}]: aimed at {label} answered {status}: {Short(text)}");
                            }
                            if (elsewhere)
                            {
                                // Control: working in the first company alone, the caller still reaches the user without roles.
                                var (limitedSearch, limitedFilter) = select(limitedEmail);
                                var limitedBefore = await ReadUserAsync(admin, limitedId);
                                var (limitedStatus, limitedText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => ""), SetBody(schema, limitedSearch, limitedFilter, flags, await ListCountAsync(caller, list, limitedSearch, limitedFilter)));
                                if (limitedStatus is < 200 or >= 300)
                                {
                                    problems.Add($"{endpoint} {selector} [{Describe(flags)}]: working in one company, aimed at a user without roles answered {limitedStatus}, so the gate cannot tell the check of hidden roles from a malformed request: {Short(limitedText)}");
                                }
                                weakChangedWorkingInOne |= await ReadUserAsync(admin, limitedId) != limitedBefore;
                            }
                        }
                        finally
                        {
                            if (elsewhere)
                            {
                                await companies.GiveAccessAsync(callerId);
                            }
                        }
                    }

                    // Control: the same request aimed at the user without roles.
                    var (controlSearch, controlFilter) = select(weakEmail);
                    var controlCount = await ListCountAsync(caller, list, controlSearch, controlFilter);
                    var controlBefore = await ReadUserAsync(admin, weakId);
                    var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => ""), SetBody(schema, controlSearch, controlFilter, flags, controlCount));
                    if (controlStatus is < 200 or >= 300)
                    {
                        problems.Add($"{endpoint} {selector} [{Describe(flags)}]: aimed at a user without roles answered {controlStatus}, so the gate cannot tell the access check from a malformed request: {Short(controlText)}");
                    }
                    weakChanged |= await ReadUserAsync(admin, weakId) != controlBefore;
                }
                if (!weakChanged)
                {
                    problems.Add($"{endpoint} {selector}: no request changed the user without roles, so the gate cannot tell that the selection reached anyone");
                }
                if (strong.Any(s => s.Elsewhere) && !weakChangedWorkingInOne)
                {
                    problems.Add($"{endpoint} {selector}: working in one company, no request changed the user without roles, so the gate cannot tell that the selection reached anyone");
                }
            }
            checkedEndpoints.Add(endpoint.Key);
        }
        return new Result(problems, checkedEndpoints, aimed, companyAimed);
    }

    /// <summary>True for a body that selects rows as a list does (a search or filter field).</summary>
    internal static bool SelectsLikeAList(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties) &&
        properties.EnumerateObject().Any(p => p.Name.Equals("search", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("filter", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every boolean field of the body all false, all true, and each one alone the other
    /// way, in that order (a body without flags has one variant).</summary>
    private static List<Dictionary<string, bool>> FlagVariants(JsonElement schema)
    {
        var flags = schema.GetProperty("properties").EnumerateObject().Where(p => IsType(p.Value, "boolean")).Select(p => p.Name).ToList();
        var variants = new List<Dictionary<string, bool>>
        {
            flags.ToDictionary(f => f, _ => false, StringComparer.Ordinal),
            flags.ToDictionary(f => f, _ => true, StringComparer.Ordinal),
        };
        foreach (var flag in flags.Where(_ => flags.Count > 1))
        {
            variants.Add(flags.ToDictionary(f => f, f => f == flag, StringComparer.Ordinal));
            variants.Add(flags.ToDictionary(f => f, f => f != flag, StringComparer.Ordinal));
        }
        return variants.DistinctBy(Describe).ToList();
    }

    private static string Describe(Dictionary<string, bool> flags) =>
        string.Join(", ", flags.Select(f => $"{f.Key}={(f.Value ? "true" : "false")}"));

    private static bool IsType(JsonElement schema, string type) =>
        schema.TryGetProperty("type", out var t) &&
        (t.ValueKind == JsonValueKind.String ? t.GetString() == type : t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(x => x.GetString() == type));

    /// <summary>The body: the selection, the flags, and in every integer field naming a count the
    /// number of rows the list itself answers for the selection (what a user confirms); every other
    /// field left out.</summary>
    private static JsonObject SetBody(JsonElement schema, string? search, string? filter, Dictionary<string, bool> flags, int count)
    {
        var body = new JsonObject();
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            if (property.Name.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                body[property.Name] = search;
            }
            else if (property.Name.Equals("filter", StringComparison.OrdinalIgnoreCase))
            {
                body[property.Name] = filter;
            }
            else if (flags.TryGetValue(property.Name, out var flag))
            {
                body[property.Name] = flag;
            }
            else if (IsType(property.Value, "integer") && property.Name.Contains("count", StringComparison.OrdinalIgnoreCase))
            {
                body[property.Name] = count;
            }
        }
        return body;
    }

    /// <summary>The list's own count of a selection, as the caller is answered.</summary>
    private static async Task<int> ListCountAsync(HttpClient caller, string endpoint, string? search, string? filter)
    {
        var uri = $"{endpoint}?take=1" + (search is null ? "" : "&search=" + Uri.EscapeDataString(search)) + (filter is null ? "" : "&filter=" + Uri.EscapeDataString(filter));
        return (await caller.GetFromJsonAsync<JsonElement>(uri)).GetProperty("total").GetInt32();
    }

    private static async Task<string> EmailOfAsync(HttpClient admin, Guid id) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{id}")).GetProperty("email").GetString()!;

    /// <summary>A user as the administrator reads them, with what they can do and where they start.</summary>
    private static async Task<string> ReadUserAsync(HttpClient admin, Guid id)
    {
        using var record = await admin.GetAsync($"/api/identity/users/{id}");
        using var access = await admin.GetAsync($"/api/identity/users/{id}/access");
        using var workplace = await admin.GetAsync($"/api/identity/users/{id}/default-company");
        return $"{(int)record.StatusCode} {await record.Content.ReadAsStringAsync()} | {(int)access.StatusCode} {await access.Content.ReadAsStringAsync()} | " +
               $"{(int)workplace.StatusCode} {await workplace.Content.ReadAsStringAsync()}";
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, string method, string path, JsonObject? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
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
