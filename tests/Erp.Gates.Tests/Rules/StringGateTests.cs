using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// CLAUDE.md rule 5: English and Arabic from the first screen. Every string file has an Arabic
/// twin with the same keys (and the reverse), no value is empty, Arabic values are written in
/// Arabic, every permission and module has labels, every menu entry has a label in the web
/// strings, and every problem or validation code the server can return has text.
/// </summary>
public sealed partial class StringGateTests(GateFixture fixture)
{
    [Fact]
    public void Server_string_files_have_matching_English_and_Arabic_keys()
    {
        var pairs = Pairs("src", "Resources");
        Assert.NotEmpty(pairs);
        var problems = pairs.SelectMany(p => Compare(p.English, p.Arabic)).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var count = pairs.Sum(p => Load(p.English).Count);
        Assert.True(count >= Ratchet.Min("rules.serverStrings"), $"{count} server strings; ratchet minimum {Ratchet.Min("rules.serverStrings")}");
    }

    [Fact]
    public void Web_string_files_have_matching_English_and_Arabic_keys()
    {
        var pairs = Pairs("web/src", "i18n");
        Assert.NotEmpty(pairs);
        var problems = pairs.SelectMany(p => Compare(p.English, p.Arabic)).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var count = pairs.Sum(p => Load(p.English).Count);
        Assert.True(count >= Ratchet.Min("rules.webStrings"), $"{count} web strings; ratchet minimum {Ratchet.Min("rules.webStrings")}");
    }

    [Fact]
    public void Every_permission_module_and_menu_entry_has_labels()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var server = Pairs("src", "Resources").SelectMany(p => Load(p.English).Keys).ToHashSet();
        var web = Pairs("web/src", "i18n").SelectMany(p => Load(p.English).Keys).ToHashSet();
        var missing = catalog.Permissions.Select(p => p.LabelKey).Where(k => !server.Contains(k))
            .Concat(catalog.Modules.Select(m => $"module.{m.Name}").Where(k => !server.Contains(k)))
            .Concat(catalog.Menu.Select(m => m.LabelKey).Where(k => !web.Contains(k)).Select(k => $"web:{k}"))
            .Concat(catalog.Lists.SelectMany(l => l.Columns.Select(c => c.LabelKey).Append(l.LabelKey)).Where(k => !web.Contains(k)).Select(k => $"web:{k}"))
            .Concat(catalog.Lists.SelectMany(l => l.Columns.SelectMany(c => (c.Choices ?? []).Select(x => x.LabelKey))).Where(k => !web.Contains(k)).Select(k => $"web:{k}"))
            .Concat(catalog.Lists.SelectMany(l => (l.Presets ?? []).Select(p => p.LabelKey)).Where(k => !web.Contains(k)).Select(k => $"web:{k}"))
            .ToList();
        Assert.NotEmpty(catalog.Lists);
        Assert.True(missing.Count == 0, "Missing labels: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_problem_and_validation_code_in_server_code_has_text()
    {
        var keys = Pairs("src", "Resources").SelectMany(p => Load(p.English).Keys).ToHashSet();
        var missing = new List<string>();
        foreach (var file in MoneyGateTests.SourceFiles("src", "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in ProblemCodeRegex().Matches(text))
            {
                var key = $"problem.{match.Groups["code"].Value}";
                if (!keys.Contains(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
            foreach (Match match in ValidationCodeRegex().Matches(text))
            {
                var key = $"validation.{match.Groups["code"].Value}";
                if (!keys.Contains(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
            foreach (Match match in ListQueryCodeRegex().Matches(text))
            {
                var key = $"validation.{match.Groups["code"].Value}";
                if (!keys.Contains(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.True(missing.Count == 0, "Codes without text: " + string.Join(", ", missing.Distinct()));
    }

    private static List<(string English, string Arabic)> Pairs(string root, string folder) =>
        MoneyGateTests.SourceFiles(root, "en.json")
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == folder)
            .Select(f => (f, Path.Combine(Path.GetDirectoryName(f)!, "ar.json")))
            .ToList();

    private static Dictionary<string, string> Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];

    private static IEnumerable<string> Compare(string englishPath, string arabicPath)
    {
        var name = Path.GetRelativePath(Repo.Root, englishPath);
        if (!File.Exists(arabicPath))
        {
            yield return $"{name}: no Arabic twin ar.json";
            yield break;
        }
        var en = Load(englishPath);
        var ar = Load(arabicPath);
        foreach (var key in en.Keys.Except(ar.Keys)) yield return $"{name}: '{key}' has no Arabic text";
        foreach (var key in ar.Keys.Except(en.Keys)) yield return $"{name}: '{key}' has Arabic but no English text";
        foreach (var (key, value) in en.Where(p => string.IsNullOrWhiteSpace(p.Value))) yield return $"{name}: '{key}' English text is empty";
        foreach (var problem in PlaceholderAndPluralProblems(name, en, ar)) yield return problem;
        foreach (var (key, value) in ar)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return $"{name}: '{key}' Arabic text is empty";
            }
            // Keys under ".native." name a language in that language (the "English" button on the
            // Arabic screen), so they are the one place Arabic files hold Latin text.
            else if (!ArabicLetterRegex().IsMatch(value) && !key.Contains(".native.", StringComparison.Ordinal)
                     && !(en.TryGetValue(key, out var english) && !LatinLetterRegex().IsMatch(english)))
            {
                yield return $"{name}: '{key}' Arabic text has no Arabic letters: {value}";
            }
        }
    }

    /// <summary>English and Arabic texts of a key use the same placeholders, and every plural
    /// message covers the CLDR categories its language needs (Arabic has six).</summary>
    private static IEnumerable<string> PlaceholderAndPluralProblems(string name, Dictionary<string, string> en, Dictionary<string, string> ar)
    {
        foreach (var (key, english) in en)
        {
            if (!ar.TryGetValue(key, out var arabic))
            {
                continue;
            }
            var enNames = string.Join(",", Erp.Kernel.Localization.MessageFormat.Placeholders(english).Order(StringComparer.Ordinal));
            var arNames = string.Join(",", Erp.Kernel.Localization.MessageFormat.Placeholders(arabic).Order(StringComparer.Ordinal));
            if (enNames != arNames)
            {
                yield return $"{name}: '{key}' English uses {{{enNames}}} but Arabic uses {{{arNames}}}";
            }
            foreach (var (language, text) in new[] { ("en", english), ("ar", arabic) })
            {
                foreach (var plural in Erp.Kernel.Localization.MessageFormat.Plurals(text))
                {
                    var missing = Erp.Kernel.Localization.PluralRules.Categories[language].Except(plural.Selectors).ToList();
                    if (missing.Count > 0)
                    {
                        yield return $"{name}: '{key}' {language} plural {{{plural.Variable}}} lacks {string.Join(", ", missing)}";
                    }
                }
            }
        }
    }

    [Fact]
    public void Count_messages_use_plural_forms()
    {
        // A count shown with a number placeholder must be a plural message in both languages:
        // "{count} users" reads "1 users", and Arabic has six forms.
        var problems = new List<string>();
        foreach (var (english, arabic) in Pairs("src", "Resources").Concat(Pairs("web/src", "i18n")))
        {
            foreach (var (language, path) in new[] { ("en", english), ("ar", arabic) })
            {
                foreach (var (key, value) in Load(path))
                {
                    if (key.EndsWith(".count", StringComparison.Ordinal) && Erp.Kernel.Localization.MessageFormat.Plurals(value).Count == 0)
                    {
                        problems.Add($"{Path.GetRelativePath(Repo.Root, path)}: '{key}' shows a count without a plural message");
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicLetterRegex();

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex LatinLetterRegex();

    [GeneratedRegex("Problems\\.(?:Result|Write)\\([^;]*?StatusCodes\\.Status\\d+\\w*,\\s*\"(?<code>[a-zA-Z.]+)\"|Problems\\.(?:Forbidden|Conflict)\\(\\s*\\w+\\s*,\\s*\"(?<code>[a-zA-Z.]+)\"|Problems\\.Create\\([^;]*?StatusCodes\\.Status\\d+\\w*,\\s*\"(?<code>[a-zA-Z.]+)\"")]
    private static partial Regex ProblemCodeRegex();

    [GeneratedRegex("\\.(?:Add|Must)\\([^;]*?\"[a-zA-Z]+\",\\s*\"(?<code>[a-zA-Z]+)\"")]
    private static partial Regex ValidationCodeRegex();

    /// <summary>List query errors (filter, sort, paging, grouping) carry a validation code.</summary>
    [GeneratedRegex("new ListQueryException\\(\\s*(?:\"[a-zA-Z]+\"|Parameter)\\s*,\\s*\"(?<code>[a-zA-Z.]+)\"")]
    private static partial Regex ListQueryCodeRegex();
}
