using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2 for reports (critic p06 round 1, plants P1 and P2). A report is guarded by the permission
/// its definition names, and the G2 gate used to check only that the route declares that very
/// permission, so a report that prints companies' licences and tax numbers under the workplace
/// switcher's permission passed. Here the permission is judged by what the report prints: signed in
/// as a user holding exactly the report's permission (and again with each other read permission
/// added), every report runs in English and Arabic for real records, and every value of the
/// workspace's own data the document prints must also be shown by some endpoint those same
/// permissions open (the record's own screen, its list). A value no other endpoint shows them is
/// data the report's permission does not grant. The catalogue must list exactly the reports and
/// lists the caller may run, with only the columns and parameters it may see.
/// </summary>
public sealed class G2ReportDataTests(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    [Fact]
    public async Task Every_report_prints_only_data_its_permissions_show_elsewhere()
    {
        var result = await ReportDataCheck.RunAsync(fixture.Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Reports} reports, {result.PermissionSets} permission sets, {result.Runs} runs, {result.ValuesJudged} printed values judged");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems.Take(60)));
        Assert.True(result.Reports >= Ratchet.Min("rules.reportsChecked"), $"{result.Reports} reports judged by what they print");
        Assert.True(result.ValuesJudged >= Ratchet.Min("g2.reportValuesJudged"), $"g2.reportValuesJudged: {result.ValuesJudged}; ratchet minimum {Ratchet.Min("g2.reportValuesJudged")}");
        Assert.True(result.PermissionSets >= Ratchet.Min("g2.reportPermissionSets"), $"g2.reportPermissionSets: {result.PermissionSets}; ratchet minimum {Ratchet.Min("g2.reportPermissionSets")}");
    }

    [Fact]
    public async Task The_report_catalogue_lists_only_what_the_caller_may_run_and_see()
    {
        var problems = await ReportDataCheck.CatalogueProblemsAsync(fixture.Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}

/// <summary>The report data check, reusable so the gate's self-tests can prove it catches a report
/// planted under a permission that does not grant what it prints.</summary>
public static class ReportDataCheck
{
    public sealed record Result(IReadOnlyList<string> Problems, int Reports, int PermissionSets, int Runs, int ValuesJudged);

    private const int MaxPages = 40;

    public static async Task<Result> RunAsync(ErpTestEnvironment env, string? onlyReport = null)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = EndpointInventory.From(env.Factory.Services);
        var tenant = env.TenantA;
        using var admin = await env.SignInAsync(env.Email(tenant, "admin"));
        var reads = catalog.PermissionKeys.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var reports = catalog.Reports.Select(r => r.Definition).Where(d => onlyReport is null || d.Key == onlyReport).ToList();

        // Every permission set judged: the report's own alone, then with each other read permission.
        var sets = new List<(ReportDefinition Report, IReadOnlyList<string> Permissions)>();
        foreach (var report in reports)
        {
            sets.Add((report, [report.Permission]));
            foreach (var other in reads.Where(r => r != report.Permission))
            {
                sets.Add((report, [report.Permission, other]));
            }
        }
        // Users first (their rows are workspace data too), then the data, then what each shows.
        var users = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
        try
        {
            foreach (var permissions in sets.Select(s => s.Permissions).Concat(reads.Select(r => (IReadOnlyList<string>)[r])).DistinctBy(Key))
            {
                users[Key(permissions)] = await UserWithAsync(env, admin, permissions, "g2rd");
            }

            var data = await WorkspaceTextAsync(env, tenant.Id);
            var labels = ResourceStrings();
            var shown = new Dictionary<string, string>(StringComparer.Ordinal);
            async Task<string> ShownByAsync(string permission)
            {
                if (!shown.TryGetValue(permission, out var text))
                {
                    text = await CorpusAsync(users[Key([permission])], endpoints.Where(e => e.Method == "GET" && !e.IsAnonymous && e.Permission == permission));
                    shown[permission] = text;
                }
                return text;
            }
            var anonymous = await CorpusAsync(users[Key([reads[0]])], endpoints.Where(e => e.Method == "GET" && e.IsAnonymous));

            var problems = new List<string>();
            var printedBy = new HashSet<string>(StringComparer.Ordinal);
            var runs = 0;
            var judged = 0;
            foreach (var (report, permissions) in sets)
            {
                var client = users[Key(permissions)];
                var corpus = string.Join("\n", await Task.WhenAll(permissions.Select(ShownByAsync))) + "\n" + anonymous;
                foreach (var query in await QueriesAsync(admin, catalog, report))
                {
                    // A parameter that names another area's records must be refused to a caller
                    // who cannot read that area.
                    var refusedParameter = report.Parameters.FirstOrDefault(p => p.Permission is { } extra && !permissions.Contains(extra) &&
                                                                                 query.Contains(p.Key + "=", StringComparison.Ordinal));
                    foreach (var language in new[] { "en", "ar" })
                    {
                        var path = $"/api/reports/run/{report.Key}?{query}{(query.Length > 0 ? "&" : "")}format=json&language={language}";
                        using var response = await client.GetAsync(path);
                        runs++;
                        if (refusedParameter is not null)
                        {
                            if (response.StatusCode != HttpStatusCode.BadRequest)
                            {
                                problems.Add($"{report.Key} as [{string.Join(", ", permissions)}]: parameter '{refusedParameter.Key}' needs {refusedParameter.Permission} but {path} answered {(int)response.StatusCode}");
                            }
                            continue;
                        }
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            problems.Add($"{report.Key} as [{string.Join(", ", permissions)}]: {path} answered {(int)response.StatusCode}, so what it prints could not be judged");
                            continue;
                        }
                        var printed = PrintedText(await response.Content.ReadFromJsonAsync<JsonElement>());
                        foreach (var value in data)
                        {
                            if (!printed.Contains(value, StringComparison.Ordinal) || labels.Contains(value))
                            {
                                continue;
                            }
                            judged++;
                            printedBy.Add(report.Key);
                            if (!corpus.Contains(value, StringComparison.Ordinal))
                            {
                                problems.Add($"{report.Key} (permission {report.Permission}) as a user holding exactly [{string.Join(", ", permissions)}]: " +
                                             $"{path} prints '{value}', which no other endpoint those permissions open shows");
                            }
                        }
                    }
                }
            }
            foreach (var report in reports.Where(r => !printedBy.Contains(r.Key)))
            {
                problems.Add($"{report.Key}: no run printed anything of the workspace's data, so the check was blind to it");
            }
            return new Result(problems.Distinct().ToList(), reports.Count, sets.Count, runs, judged);
        }
        finally
        {
            foreach (var client in users.Values)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>The catalogue as users holding the catalogue's permission and exactly one other:
    /// it lists exactly the reports and printable lists whose permission they hold, without the
    /// columns and parameters that need a permission they lack.</summary>
    public static async Task<List<string>> CatalogueProblemsAsync(ErpTestEnvironment env)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var problems = new List<string>();
        var permissions = catalog.Reports.Select(r => r.Definition.Permission)
            .Concat(catalog.PrintableLists.Select(p => p.List.Permission))
            .Concat(catalog.PermissionKeys.Where(p => p.EndsWith(".read", StringComparison.Ordinal)))
            .Where(p => p != "reports.catalog.read").Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var permission in permissions)
        {
            var held = new[] { "reports.catalog.read", permission };
            using var client = await UserWithAsync(env, admin, held, "g2rc");
            var answer = await client.GetFromJsonAsync<JsonElement>("/api/reports/catalog?language=en");
            var items = answer.GetProperty("items").EnumerateArray().ToList();
            var listed = items.Select(i => i.GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToList();
            var expected = catalog.Reports.Where(r => held.Contains(r.Definition.Permission)).Select(r => r.Definition.Key).Order(StringComparer.Ordinal).ToList();
            if (!listed.SequenceEqual(expected))
            {
                problems.Add($"catalogue for [{string.Join(", ", held)}] lists reports [{string.Join(", ", listed)}], expected [{string.Join(", ", expected)}]");
            }
            var lists = answer.GetProperty("lists").EnumerateArray().Select(l => l.GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToList();
            var expectedLists = catalog.PrintableLists.Where(p => held.Contains(p.List.Permission)).Select(p => p.List.Key).Order(StringComparer.Ordinal).ToList();
            if (!lists.SequenceEqual(expectedLists))
            {
                problems.Add($"catalogue for [{string.Join(", ", held)}] lists printable lists [{string.Join(", ", lists)}], expected [{string.Join(", ", expectedLists)}]");
            }
            foreach (var item in items)
            {
                var definition = catalog.FindReport(item.GetProperty("key").GetString()!)!.Definition;
                var columns = item.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToList();
                foreach (var hidden in definition.Columns.Where(c => c.Permission is { } extra && !held.Contains(extra)).Where(c => columns.Contains(c.Key)))
                {
                    problems.Add($"catalogue for [{string.Join(", ", held)}] shows column '{hidden.Key}' of {definition.Key}, which needs {hidden.Permission}");
                }
                var parameters = item.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToList();
                foreach (var hidden in definition.Parameters.Where(p => p.Permission is { } extra && !held.Contains(extra)).Where(p => parameters.Contains(p.Key)))
                {
                    problems.Add($"catalogue for [{string.Join(", ", held)}] offers parameter '{hidden.Key}' of {definition.Key}, which needs {hidden.Permission}");
                }
            }
        }
        return problems;
    }

    private static string Key(IReadOnlyList<string> permissions) => string.Join("+", permissions.Order(StringComparer.Ordinal));

    private static int _users;

    /// <summary>A role granting exactly these permissions and a user holding only that role, signed in.</summary>
    private static async Task<HttpClient> UserWithAsync(ErpTestEnvironment env, HttpClient admin, IReadOnlyList<string> permissions, string prefix)
    {
        var n = Interlocked.Increment(ref _users);
        var name = $"{prefix} {n}";
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Only {name}", nameAr = $"فقط {name}", permissions });
        Assert.True(role.StatusCode == HttpStatusCode.Created, $"role for [{string.Join(", ", permissions)}]: {(int)role.StatusCode} {await role.Content.ReadAsStringAsync()}");
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"{prefix}-{n}@{env.TenantA.EmailDomain}";
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"G2 {name}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } });
        Assert.True(user.StatusCode == HttpStatusCode.Created, $"user for [{string.Join(", ", permissions)}]: {(int)user.StatusCode}");
        // The user works in every company of the workspace (all branches), so what a report
        // prints is decided by its permissions alone, not by the company scope.
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200")).GetProperty("items").EnumerateArray()
            .Select(c => new { companyId = c.GetProperty("id").GetGuid(), allBranches = true }).ToList();
        // Access saves carry the version read (p02): read it first, as the access screen does.
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}")).GetProperty("version").GetUInt32();
        using var access = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies, version });
        Assert.True(access.IsSuccessStatusCode, $"company access for [{string.Join(", ", permissions)}]: {(int)access.StatusCode} {await access.Content.ReadAsStringAsync()}");
        return await env.SignInAsync(email);
    }

    /// <summary>Every text value of the workspace's own rows (read with the superuser), at least four
    /// characters long: what a document might print. The audit trail is left out (its rows repeat
    /// every other table's values).</summary>
    private static async Task<IReadOnlyList<string>> WorkspaceTextAsync(ErpTestEnvironment env, Guid tenantId)
    {
        await using var connection = await env.OpenAdminAsync();
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in await DbCatalog.TenantTablesAsync(connection))
        {
            if (table.Schema == "audit")
            {
                continue;
            }
            foreach (var column in await DbCatalog.ColumnsAsync(connection, table))
            {
                if (column.Name == "tenant_id" || column.Type.EndsWith("[]", StringComparison.Ordinal) ||
                    !(column.Type.StartsWith("text", StringComparison.Ordinal) || column.Type.StartsWith("character varying", StringComparison.Ordinal) || column.Type == "citext"))
                {
                    continue;
                }
                values.UnionWith(await DbCatalog.ReadAsync(connection,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND length(\"{column.Name}\"::text) >= 4 LIMIT 5000",
                    r => r.GetString(0), ("t", tenantId)));
            }
        }
        return values.Select(v => v.Trim()).Where(v => v.Length >= 4).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every string of every resource file (labels, choices, messages): a value equal to
    /// one is a word the product prints itself, not the workspace's data.</summary>
    private static HashSet<string> ResourceStrings()
    {
        var strings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.json", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(Repo.Root, "web", "src"), "*.json", SearchOption.AllDirectories))
                     .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                                 f.Contains($"{Path.DirectorySeparatorChar}i18n{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Walk(document.RootElement, strings);
        }
        return strings;
    }

    /// <summary>The queries each report runs with: without parameters (when none is required) and
    /// with each of up to three of the workspace's records in each reference parameter.</summary>
    private static async Task<IReadOnlyList<string>> QueriesAsync(HttpClient admin, ModuleCatalog catalog, ReportDefinition report)
    {
        var queries = new List<string>();
        var required = report.Parameters.Where(p => p.Required).ToList();
        if (required.Count == 0)
        {
            queries.Add("");
        }
        foreach (var parameter in report.Parameters.Where(p => p.Type == ReportParameterType.Reference))
        {
            var list = catalog.FindList(parameter.Lookup!)!;
            var page = await admin.GetFromJsonAsync<JsonElement>($"{list.Endpoint}?take=3");
            foreach (var row in page.GetProperty("items").EnumerateArray())
            {
                var others = required.Where(r => r.Key != parameter.Key).ToList();
                if (others.Count > 0)
                {
                    continue;
                }
                queries.Add($"{parameter.Key}={row.GetProperty("id").GetString()}");
            }
        }
        return queries;
    }

    /// <summary>Everything the endpoints show (every string and number of every answer, lists paged
    /// to the end, every record's own GET for the ids the lists gave).</summary>
    private static async Task<string> CorpusAsync(HttpClient client, IEnumerable<ApiEndpoint> endpoints)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = endpoints.Where(e => !e.Pattern.StartsWith("/api/reports/", StringComparison.Ordinal) && !e.Pattern.Contains("{*", StringComparison.Ordinal)).ToList();
        foreach (var endpoint in all.Where(e => e.RouteParameters.Count == 0))
        {
            string? after = null;
            for (var page = 0; page < MaxPages; page++)
            {
                var path = endpoint.Path(_ => "") + "?take=200" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after));
                using var response = await client.GetAsync(path);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
                {
                    break;
                }
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Walk(json, shown, ids);
                after = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                if (after is null)
                {
                    break;
                }
            }
        }
        foreach (var endpoint in all.Where(e => e.RouteParameters.Count == 1))
        {
            foreach (var id in ids.Take(400).ToList())
            {
                using var response = await client.GetAsync(endpoint.Path(_ => id));
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    Walk(await response.Content.ReadFromJsonAsync<JsonElement>(), shown, ids);
                }
            }
        }
        return string.Join("\n", shown);
    }

    private static void Walk(JsonElement element, HashSet<string> into, HashSet<string>? ids = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ids is not null && property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(property.Value.GetString(), out _))
                    {
                        ids.Add(property.Value.GetString()!);
                    }
                    Walk(property.Value, into, ids);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, into, ids);
                break;
            case JsonValueKind.String:
                into.Add(element.GetString()!);
                break;
            case JsonValueKind.Number:
                into.Add(element.GetRawText());
                break;
        }
    }

    /// <summary>What a document prints of data: its letterhead (the issuing company), subject, the
    /// parameters' and facts' values, group labels, cells and totals. Not its title, labels or fixed
    /// texts (the product's own words), nor who printed it (the caller's own name).</summary>
    private static string PrintedText(JsonElement document)
    {
        var text = new StringBuilder();
        void Add(JsonElement e, string name)
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) text.Append(v.GetString()).Append('\n');
        }
        Add(document, "issuer");
        Add(document, "subject");
        foreach (var section in new[] { "parameters", "facts" })
        {
            foreach (var fact in document.GetProperty(section).EnumerateArray()) Add(fact, "text");
        }
        foreach (var group in document.GetProperty("groups").EnumerateArray())
        {
            Add(group, "label");
            foreach (var row in group.GetProperty("rows").EnumerateArray())
            {
                foreach (var cell in row.GetProperty("cells").EnumerateArray()) Add(cell, "text");
            }
        }
        foreach (var total in document.GetProperty("totals").EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object)) Add(total, "text");
        return text.ToString();
    }
}
