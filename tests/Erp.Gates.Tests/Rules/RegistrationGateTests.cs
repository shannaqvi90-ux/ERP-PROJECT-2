using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// The pieces a module registers on the server (menu entries, lists) and on the web (screens in
/// <c>web/src/modules/*/routes.tsx</c>) describe the same product: navigation reaches every
/// screen, every menu entry opens a screen guarded by the same permission, registrations stay in
/// their module's namespace, and every registered list is served in the one page shape the list
/// framework reads (<c>{ items: [...], total }</c>, rows carrying every column).
/// </summary>
public sealed partial class RegistrationGateTests(GateFixture fixture)
{
    private sealed record WebRoute(string File, string Path, string TitleKey, string? Permission);

    [Fact]
    public void Navigation_reaches_every_screen_and_every_menu_entry_opens_a_screen_with_its_permission()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var routes = WebRoutes();
        var webStrings = WebStrings();
        var problems = new List<string>();
        Assert.NotEmpty(routes);

        foreach (var duplicate in routes.GroupBy(r => r.Path).Where(g => g.Count() > 1))
        {
            problems.Add($"web screen path {duplicate.Key} is defined {duplicate.Count()} times");
        }

        var menuEntries = 0;
        foreach (var module in catalog.Modules)
        {
            foreach (var entry in module.Menu)
            {
                menuEntries++;
                if (!entry.Key.StartsWith(module.Name + ".", StringComparison.Ordinal))
                {
                    problems.Add($"menu entry '{entry.Key}' of module {module.Name}: key must start with '{module.Name}.'");
                }
                if (!entry.LabelKey.StartsWith(module.Name + ".", StringComparison.Ordinal))
                {
                    problems.Add($"menu entry '{entry.Key}': label key '{entry.LabelKey}' must start with '{module.Name}.'");
                }
                if (!entry.Path.StartsWith("/" + module.Name + "/", StringComparison.Ordinal))
                {
                    problems.Add($"menu entry '{entry.Key}': path '{entry.Path}' must start with '/{module.Name}/'");
                }
                if (!catalog.IsPermission(entry.Permission))
                {
                    problems.Add($"menu entry '{entry.Key}': permission '{entry.Permission}' is not in any module's catalogue");
                }
                var route = routes.FirstOrDefault(r => r.Path == entry.Path);
                if (route is null)
                {
                    problems.Add($"menu entry '{entry.Key}': no web screen has path {entry.Path} (web/src/modules/*/routes.tsx)");
                }
                else if (route.Permission != entry.Permission)
                {
                    problems.Add($"menu entry '{entry.Key}' needs '{entry.Permission}' but screen {entry.Path} ({route.File}) needs '{route.Permission ?? "nothing"}'");
                }
            }
        }

        var menuPaths = catalog.Menu.Select(m => m.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            if (!webStrings.Contains(route.TitleKey))
            {
                problems.Add($"screen {route.Path} ({route.File}): title key '{route.TitleKey}' is not in the web strings");
            }
            if (route.Path == "/")
            {
                continue; // The home screen: the brand link in the top bar leads there.
            }
            if (route.Permission is null)
            {
                problems.Add($"screen {route.Path} ({route.File}) declares no permission; every screen but home needs one");
            }
            else if (!catalog.IsPermission(route.Permission))
            {
                problems.Add($"screen {route.Path} ({route.File}): permission '{route.Permission}' is not in any module's catalogue");
            }
            if (!menuPaths.Contains(route.Path))
            {
                problems.Add($"screen {route.Path} ({route.File}) is not reachable: no module menu entry has that path");
            }
        }
        Assert.True(routes.Any(r => r.Path == "/"), "No home screen (path /) is registered");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(menuEntries >= Ratchet.Min("rules.menuEntriesChecked"), $"{menuEntries} menu entries checked; ratchet minimum {Ratchet.Min("rules.menuEntriesChecked")}");
    }

    [Fact]
    public async Task Every_registered_list_is_served_as_a_page_whose_rows_carry_its_columns()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var client = fixture.Env.CreateClient();
        var document = await OpenApiDocument.LoadAsync(client);
        var problems = new List<string>();
        var lists = 0;
        foreach (var list in catalog.Lists)
        {
            lists++;
            if (!document.TryGetOperation("GET", list.Endpoint, out var operation))
            {
                problems.Add($"list '{list.Key}': GET {list.Endpoint} is not in the OpenAPI document");
                continue;
            }
            if (!operation.TryGetProperty("responses", out var responses) ||
                !responses.TryGetProperty("200", out var ok) ||
                !ok.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("application/json", out var json) ||
                !json.TryGetProperty("schema", out var schemaRef))
            {
                problems.Add($"list '{list.Key}': GET {list.Endpoint} documents no JSON 200 response");
                continue;
            }
            var page = document.Resolve(schemaRef);
            if (!page.TryGetProperty("properties", out var pageProperties) ||
                !pageProperties.TryGetProperty("items", out var itemsRef) ||
                !pageProperties.TryGetProperty("total", out var totalRef))
            {
                problems.Add($"list '{list.Key}': GET {list.Endpoint} must return a page {{ items, total }}, not {(HasType(page, "array") ? "a bare array" : "an object without items and total")}");
                continue;
            }
            var items = document.Resolve(itemsRef);
            if (!HasType(items, "array") || !items.TryGetProperty("items", out var rowRef))
            {
                problems.Add($"list '{list.Key}': 'items' of GET {list.Endpoint} is not an array");
                continue;
            }
            if (!HasType(document.Resolve(totalRef), "integer"))
            {
                problems.Add($"list '{list.Key}': 'total' of GET {list.Endpoint} is not an integer");
            }
            var row = document.Resolve(rowRef);
            var rowProperties = row.TryGetProperty("properties", out var p) ? p.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal) : [];
            foreach (var column in list.Columns.Where(c => !rowProperties.Contains(c.Key)))
            {
                problems.Add($"list '{list.Key}': column '{column.Key}' is not a property of the rows GET {list.Endpoint} returns");
            }
            if (list.SearchFields.Count > 0 && document.Parameters("GET", list.Endpoint).All(x => x.In != "query" || x.Name != list.SearchParameter))
            {
                problems.Add($"list '{list.Key}': search fields are registered but GET {list.Endpoint} has no '{list.SearchParameter}' query parameter");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(lists >= Ratchet.Min("rules.listsChecked"), $"{lists} lists checked; ratchet minimum {Ratchet.Min("rules.listsChecked")}");
    }

    private static bool HasType(JsonElement schema, string type) =>
        schema.TryGetProperty("type", out var t) &&
        (t.ValueKind == JsonValueKind.String ? t.GetString() == type : t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(x => x.GetString() == type));

    private static List<WebRoute> WebRoutes()
    {
        var modules = Repo.PathOf("web", "src", "modules");
        var routes = new List<WebRoute>();
        foreach (var file in Directory.EnumerateFiles(modules, "routes.tsx", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            var found = 0;
            foreach (Match match in RouteObjectRegex().Matches(text))
            {
                found++;
                var body = match.Value;
                var path = FieldRegex("path").Match(body);
                var title = FieldRegex("titleKey").Match(body);
                var permission = FieldRegex("permission").Match(body);
                Assert.True(path.Success && title.Success, $"{relative}: a screen without a literal path or titleKey: {body}");
                routes.Add(new WebRoute(relative, path.Groups["v"].Value, title.Groups["v"].Value, permission.Success ? permission.Groups["v"].Value : null));
            }
            Assert.True(found > 0, $"{relative}: no screens found; write each screen as {{ path: \"…\", titleKey: \"…\", permission: \"…\", component: … }}");
        }
        return routes;
    }

    private static HashSet<string> WebStrings() =>
        Directory.EnumerateFiles(Repo.PathOf("web", "src", "modules"), "en.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "i18n")
            .SelectMany(f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!.Keys)
            .ToHashSet(StringComparer.Ordinal);

    private static Regex FieldRegex(string name) => new($"\\b{name}\\s*:\\s*\"(?<v>[^\"]*)\"");

    [GeneratedRegex(@"\{[^{}]*\bpath\s*:[^{}]*\}")]
    private static partial Regex RouteObjectRegex();
}
