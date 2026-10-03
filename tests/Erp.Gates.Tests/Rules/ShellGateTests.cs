using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// CLAUDE.md rule 5 for the app shell: English and Arabic from the first screen, mirrored right to
/// left. These checks keep every later screen honest:
/// <list type="bullet">
/// <item>Stylesheets and inline styles use logical directions only (inline-start/end), never
/// left/right, so Arabic screens mirror without a second stylesheet.</item>
/// <item>Screens format numbers, amounts and dates only through the kernel formatter, so the
/// screen language and the user's digit choice (Latin or Arabic-Indic) apply everywhere.</item>
/// <item>Every navigation group a module puts its menu entries in has an English and Arabic
/// heading.</item>
/// </list>
/// </summary>
public sealed partial class ShellGateTests(GateFixture fixture)
{
    [Fact]
    public void Stylesheets_and_inline_styles_use_logical_directions_only()
    {
        var problems = new List<string>();
        var declarations = 0;
        foreach (var file in MoneyGateTests.SourceFiles("web/src", "*.css"))
        {
            var css = CommentRegex().Replace(File.ReadAllText(file), m => new string('\n', m.Value.Count(c => c == '\n')));
            foreach (Match match in DeclarationRegex().Matches(css))
            {
                declarations++;
                var property = match.Groups["prop"].Value.ToLowerInvariant();
                var value = match.Groups["value"].Value.Trim().ToLowerInvariant();
                var line = css[..match.Index].Count(c => c == '\n') + 1;
                var where = $"{Path.GetRelativePath(Repo.Root, file)}:{line}";
                if (PhysicalPropertyRegex().IsMatch(property))
                {
                    problems.Add($"{where}: '{property}' is a physical direction; use the logical property (inline-start/inline-end)");
                }
                else if (property is "float" or "clear" or "text-align" or "caption-side" && PhysicalValueRegex().IsMatch(value))
                {
                    problems.Add($"{where}: '{property}: {value}' is a physical direction; use start/end or inline-start/inline-end");
                }
                else if (property is "margin" or "padding" or "inset" or "border-width" or "border-style" or "border-color" or "border-radius")
                {
                    var parts = SplitValue(value);
                    // Four values: top right bottom left. Different right and left do not mirror.
                    if (parts.Count == 4 && parts[1] != parts[3])
                    {
                        problems.Add($"{where}: '{property}: {value}' sets left and right differently; use {property}-inline/{property}-block (or logical corner radii)");
                    }
                    if (property == "border-radius" && parts.Count >= 2 && parts.Count <= 4 && !parts.Contains("/") && parts.Distinct().Count() > 1)
                    {
                        problems.Add($"{where}: 'border-radius: {value}' rounds left and right corners differently; use border-start-start-radius and the other logical corners");
                    }
                }
            }
        }
        foreach (var file in MoneyGateTests.SourceFiles("web/src", "*.tsx").Concat(MoneyGateTests.SourceFiles("web/src", "*.ts")))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (InlinePhysicalRegex().IsMatch(lines[i]))
                {
                    problems.Add($"{Path.GetRelativePath(Repo.Root, file)}:{i + 1}: inline style with a physical direction (left/right); use a logical property");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(declarations >= Ratchet.Min("rules.cssDeclarationsChecked"), $"{declarations} CSS declarations checked; ratchet minimum {Ratchet.Min("rules.cssDeclarationsChecked")}");
    }

    /// <summary>Files allowed to format numbers and dates directly: the formatter itself and the
    /// message formatter, which receives the formatter's locale (digits included).</summary>
    private static readonly string[] ReviewedFormatters = ["web/src/kernel/format.ts", "web/src/kernel/messageFormat.ts"];

    [Fact]
    public void Screens_format_numbers_amounts_and_dates_through_the_kernel_formatter()
    {
        var problems = new List<string>();
        var files = 0;
        foreach (var file in MoneyGateTests.SourceFiles("web/src", "*.ts").Concat(MoneyGateTests.SourceFiles("web/src", "*.tsx")))
        {
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            if (relative.EndsWith(".test.ts", StringComparison.Ordinal) || relative.EndsWith(".test.tsx", StringComparison.Ordinal) || relative.StartsWith("web/src/test/", StringComparison.Ordinal))
            {
                continue;
            }
            files++;
            if (ReviewedFormatters.Contains(relative))
            {
                continue;
            }
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (DirectFormattingRegex().Match(lines[i]) is { Success: true } match)
                {
                    problems.Add($"{relative}:{i + 1}: '{match.Value.Trim()}' formats without the screen's language and digits; use useI18n().format");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(files >= Ratchet.Min("rules.webFormattingFilesChecked"), $"{files} web source files checked; ratchet minimum {Ratchet.Min("rules.webFormattingFilesChecked")}");
    }

    /// <summary>
    /// Every request field that sets an interface language or a digit system lists exactly the
    /// supported values in the OpenAPI document, so API clients (and the isolation gate's valid
    /// bodies, which take documented values) never have to guess them.
    /// </summary>
    [Fact]
    public async Task Language_and_digit_fields_document_their_allowed_values()
    {
        using var client = fixture.Env.CreateClient();
        var document = await OpenApiDocument.LoadAsync(client);
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["language"] = [.. Erp.Kernel.Localization.Languages.All],
            ["numerals"] = [.. Erp.Kernel.Localization.NumeralSystems.All],
        };
        var problems = new List<string>();
        var checkedFields = 0;
        foreach (var endpoint in EndpointInventory.From(fixture.Env.Factory.Services).Where(e => e.InOpenApi && e.HasBody))
        {
            if (document.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema ||
                !document.Resolve(schema).TryGetProperty("properties", out var properties))
            {
                continue;
            }
            foreach (var property in properties.EnumerateObject())
            {
                if (!expected.TryGetValue(property.Name, out var values))
                {
                    continue;
                }
                checkedFields++;
                var field = document.Resolve(property.Value);
                var listed = field.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
                    : [];
                if (!listed.Order().SequenceEqual(values.Order()))
                {
                    problems.Add($"{endpoint} {property.Name}: documents [{string.Join(", ", listed)}], supports [{string.Join(", ", values)}]");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // The preferences endpoint (language and digits) and the user endpoints (language).
        Assert.True(checkedFields >= 4, $"only {checkedFields} language or digit fields found in request bodies");
    }

    [Fact]
    public void Every_navigation_group_has_an_English_and_Arabic_heading()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var en = Load("web/src/modules/shell/i18n/en.json");
        var ar = Load("web/src/modules/shell/i18n/ar.json");
        var groups = catalog.Menu.Select(m => m.Group).OfType<string>().Distinct().ToList();
        Assert.NotEmpty(groups);
        var missing = groups.Where(g => !en.ContainsKey($"shell.group.{g}") || !ar.ContainsKey($"shell.group.{g}")).ToList();
        Assert.True(missing.Count == 0, "Menu groups without a heading (add shell.group.<group> to web/src/modules/shell/i18n): " + string.Join(", ", missing));
    }

    private static Dictionary<string, string> Load(string relative) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Repo.PathOf(relative.Split('/')))) ?? [];

    /// <summary>Space-separated parts of a value; spaces inside parentheses (rgba(…)) do not split.</summary>
    private static List<string> SplitValue(string value)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in value.Replace("!important", "", StringComparison.Ordinal).Trim())
        {
            if (c == '(') depth++;
            if (c == ')') depth--;
            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (current.Length > 0) parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"(?<![\w-])(?<prop>-?[a-zA-Z][a-zA-Z-]*)\s*:\s*(?<value>[^;{}]+)(?=;|\})")]
    private static partial Regex DeclarationRegex();

    [GeneratedRegex(@"(^|-)(left|right)(-|$)")]
    private static partial Regex PhysicalPropertyRegex();

    [GeneratedRegex(@"\b(left|right)\b")]
    private static partial Regex PhysicalValueRegex();

    [GeneratedRegex(@"\b(margin|padding|border)(Left|Right)\b|\b(left|right)\s*:\s*[\d""'`]|textAlign\s*:\s*[""'](left|right)[""']|\bfloat\s*:\s*[""'](left|right)")]
    private static partial Regex InlinePhysicalRegex();

    [GeneratedRegex(@"\.toLocale(Date|Time)?String\s*\(|\.toFixed\s*\(|\bnew\s+Intl\.(NumberFormat|DateTimeFormat|RelativeTimeFormat)\b")]
    private static partial Regex DirectFormattingRegex();

}
