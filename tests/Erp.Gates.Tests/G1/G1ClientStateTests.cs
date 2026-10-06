using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 on the client, by inventory: the shell is a single-page app, one document in memory while
/// people sign in and out of different tenants on the same device. Module-level state in the web
/// sources (a top-level <c>let</c>, a top-level <c>new Map()</c> or <c>[]</c>, a class's static
/// field, a property put on <c>window</c>) lives as long as the document, so it can carry one
/// tenant's records to the next person, as a palette answer cache did in the round-2 critic's plant
/// P9. The product replaces the document whenever an identity ends (web/src/kernel/deviceState.ts),
/// and caches meant to hold data are made with <c>identityScoped()</c>; this gate makes every other
/// piece of module-level state a reviewed decision, listed in tests/Gates/client-module-state.txt
/// with the reason it can never hold tenant data.
///
/// The behavioural side is web/src/modules/shell/clientIsolation.test.tsx (one tab, B then A, with
/// the document kept in memory) and tests/e2e/specs/client-isolation.spec.ts (the real browser,
/// its storage and its JavaScript heap).
/// </summary>
public sealed partial class G1ClientStateTests
{
    private const string Allowlist = "tests/Gates/client-module-state.txt";

    /// <summary>One piece of module-level state: where, its name, and what it starts as.</summary>
    public sealed record Binding(string File, int Line, string Name, string Kind, string Initializer)
    {
        public string Key => $"{File} {Name}";

        public override string ToString() => $"{File}:{Line} {Kind} {Name} = {Initializer}";
    }

    [Fact]
    public void Every_piece_of_module_level_state_in_the_web_sources_is_reviewed()
    {
        var reviewed = Repo.ReadReviewedList(Allowlist);
        var problems = reviewed.Where(r => r.Reason.Length < 20).Select(r => $"{Allowlist}: '{r.Entry}' needs a reason (after #) saying why it never holds tenant data").ToList();
        var allowed = reviewed.Select(r => r.Entry).ToHashSet(StringComparer.Ordinal);

        var files = 0;
        var inspected = 0;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in WebSources())
        {
            files++;
            var relative = Path.GetRelativePath(Repo.Root, path).Replace('\\', '/');
            var (bindings, stateful) = Inspect(relative, File.ReadAllText(path));
            inspected += bindings;
            foreach (var binding in stateful)
            {
                found.Add(binding.Key);
                if (!allowed.Contains(binding.Key))
                {
                    problems.Add($"{binding}: module-level state lives as long as the document and can carry one tenant's data to the next person on the device. Make it identityScoped() (kernel/deviceState), move it into a component, or review it in {Allowlist}.");
                }
            }
        }
        problems.AddRange(allowed.Where(a => !found.Contains(a)).Select(a => $"{Allowlist}: '{a}' is no longer module-level state in the web sources; remove the stale entry"));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(files >= Ratchet.Min("g1.clientSourceFilesScanned"), $"{files} web source files scanned; ratchet minimum {Ratchet.Min("g1.clientSourceFilesScanned")}");
        Assert.True(inspected >= Ratchet.Min("g1.clientModuleBindingsInspected"), $"{inspected} module-level bindings inspected; ratchet minimum {Ratchet.Min("g1.clientModuleBindingsInspected")}");
    }

    /// <summary>Self-test: the critic's plant P9 and its variants are found; the safe forms are not.</summary>
    [Fact]
    public void The_inventory_finds_planted_module_state()
    {
        const string planted = """
            import { identityScoped } from "../../kernel/deviceState";
            const shownPerSource = 8;
            const debounceMs = 120;
            const answered = new Map<string, SourceState>();
            export let lastQuery = "";
            var hits: string[] = [];
            const seen: Record<string, PaletteItem[]> = {};
            const recentRows = [] as UserRow[];
            const memo = memoize((q: string) => q);
            const scoped = identityScoped<string, SourceState>();
            const pattern = /^[a-z]+$/;
            const later =
              new WeakMap<object, string>();
            export const isEmail = (value: string) => pattern.test(value);
            const Context = createContext<Api | null>(null);
            class Store {
              static rows: UserRow[] = [];
              static readonly limit = 5;
            }
            function remember(rows: UserRow[]) {
              window.__erpRows = rows;
              globalThis.lastTenant = rows[0];
              const local = new Map();
            }
            """;
        var (_, stateful) = Inspect("web/src/modules/shell/Planted.tsx", planted);
        var names = stateful.Select(b => b.Name).ToList();
        Assert.Equal(["answered", "lastQuery", "hits", "seen", "recentRows", "memo", "later", "Store.rows", "window.__erpRows", "globalThis.lastTenant"], names);

        // P9 exactly as the round-2 critic planted it, in the product's palette source.
        const string palette = "web/src/modules/shell/CommandPalette.tsx";
        var product = File.ReadAllText(Repo.PathOf(palette.Split('/')));
        Assert.Contains("const debounceMs = 120;", product);
        var plantedPalette = product.Replace("const debounceMs = 120;", "const debounceMs = 120;\nconst answered = new Map<string, SourceState>();", StringComparison.Ordinal);
        Assert.Empty(Inspect(palette, product).Stateful);
        Assert.Equal(["answered"], Inspect(palette, plantedPalette).Stateful.Select(b => b.Name));
    }

    private const string CarrierAllowlist = "tests/Gates/client-carriers.txt";

    /// <summary>
    /// The browser carriers that outlive a document in a tab or a browser: what a screen puts in
    /// them reaches the next document, which may belong to the next person on the device. Cookies
    /// a script can read and <c>window.name</c> survive the document replacement that ends an
    /// identity; history entries (addresses with search text and record ids) stay in the tab after
    /// sign-out; storage, IndexedDB, Cache Storage, service workers and broadcast channels outlive
    /// the document by design (critic p04 round 3, plants C1 and C2).
    /// </summary>
    public static readonly IReadOnlyList<(string Kind, Regex Pattern)> Carriers =
    [
        ("cookie", new Regex(@"\bdocument\s*\.\s*cookie\b|\bcookieStore\b", RegexOptions.Compiled)),
        ("window-name", new Regex(@"\b(window|self|globalThis|top|parent|opener|frames)\s*\.\s*name\b|\bdefaultView\b", RegexOptions.Compiled)),
        ("history", new Regex(@"\b(pushState|replaceState)\b", RegexOptions.Compiled)),
        ("local-storage", new Regex(@"\blocalStorage\b", RegexOptions.Compiled)),
        ("session-storage", new Regex(@"\bsessionStorage\b", RegexOptions.Compiled)),
        ("indexed-db", new Regex(@"\bindexedDB\b", RegexOptions.Compiled)),
        ("cache-storage", new Regex(@"\bcaches\s*\.", RegexOptions.Compiled)),
        ("service-worker", new Regex(@"\bserviceWorker\b|\bSharedWorker\b", RegexOptions.Compiled)),
        ("broadcast", new Regex(@"\bBroadcastChannel\b", RegexOptions.Compiled)),
    ];

    /// <summary>Every use of a carrier that outlives the document, per file and kind, with how often.</summary>
    public static IReadOnlyList<string> CarrierUses(string file, string source)
    {
        var stripped = StripComments(source);
        var uses = new List<string>();
        foreach (var (kind, pattern) in Carriers)
        {
            var count = pattern.Matches(stripped).Count;
            if (count > 0)
            {
                uses.Add($"{file} {kind} {count}");
            }
        }
        return uses;
    }

    /// <summary>
    /// Every use of a carrier that outlives the document is a reviewed decision, listed with how
    /// often the file uses it (a new use in a reviewed file is a new decision too) and the reason
    /// it never carries one person's tenant data to the next: the kernel forgets it when the
    /// identity ends (kernel/deviceState), stamps it for the identity (kernel/historyGuard), or it
    /// holds a device setting only. A cookie written by a screen, a palette cache in
    /// <c>window.name</c> (the critic's plants C2 and C1) fail here before any browser runs.
    /// </summary>
    [Fact]
    public void Every_use_of_a_carrier_that_outlives_the_document_is_reviewed()
    {
        var reviewed = Repo.ReadReviewedList(CarrierAllowlist);
        var problems = reviewed.Where(r => r.Reason.Length < 20).Select(r => $"{CarrierAllowlist}: '{r.Entry}' needs a reason (after #) saying why it never carries one person's data to the next").ToList();
        var allowed = reviewed.Select(r => r.Entry).ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in WebSources())
        {
            var relative = Path.GetRelativePath(Repo.Root, path).Replace('\\', '/');
            foreach (var use in CarrierUses(relative, File.ReadAllText(path)))
            {
                found.Add(use);
                if (!allowed.Contains(use))
                {
                    problems.Add($"{use}: this carrier outlives the document and can hand one person's data to the next on the device. Forget it when the identity ends (kernel/deviceState), keep the data in memory, or review it in {CarrierAllowlist}.");
                }
            }
        }
        problems.AddRange(allowed.Where(a => !found.Contains(a)).Select(a => $"{CarrierAllowlist}: '{a}' no longer matches the web sources; update or remove the stale entry"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        var kinds = found.Select(f => f.Split(' ')[1]).Distinct().Count();
        Assert.True(Carriers.Count >= Ratchet.Min("g1.clientCarrierKinds"), $"{Carriers.Count} carrier kinds inspected; ratchet minimum {Ratchet.Min("g1.clientCarrierKinds")}");
        Assert.True(found.Count >= Ratchet.Min("g1.clientCarrierUsesReviewed"), $"{found.Count} carrier uses reviewed ({kinds} kinds in use); ratchet minimum {Ratchet.Min("g1.clientCarrierUsesReviewed")}");
    }

    /// <summary>Self-test: the critic's round-3 plants C1 and C2, in the product's palette source, are found.</summary>
    [Fact]
    public void The_carrier_inventory_finds_the_planted_cookie_and_window_name()
    {
        const string palette = "web/src/modules/shell/CommandPalette.tsx";
        var product = File.ReadAllText(Repo.PathOf(palette.Split('/')));
        Assert.Empty(CarrierUses(palette, product));

        const string runStart = "    if (!entry) return;\n";
        Assert.Contains(runStart, product);
        var c2 = product.Replace(runStart, runStart + "    if (entry.id.includes(\":\")) document.cookie = `erp.recentRecord=${encodeURIComponent(entry.title)}; path=/; max-age=31536000`;\n", StringComparison.Ordinal);
        Assert.Equal([$"{palette} cookie 1"], CarrierUses(palette, c2));

        const string asked = "    if (asked.length === 0) return;\n";
        Assert.Contains(asked, product);
        var c1 = product.Replace(asked, asked + "    const holder = document.defaultView!;\n    holder.name = JSON.stringify(remote);\n", StringComparison.Ordinal);
        Assert.Equal([$"{palette} window-name 1"], CarrierUses(palette, c1));

        var stored = product.Replace(asked, asked + "    window.localStorage.setItem(\"erp.palette\", trimmed);\n    navigator.serviceWorker.register(\"/sw.js\");\n", StringComparison.Ordinal);
        Assert.Equal([$"{palette} local-storage 1", $"{palette} service-worker 1"], CarrierUses(palette, stored));
    }

    private static IEnumerable<string> WebSources() =>
        Directory.EnumerateFiles(Repo.PathOf("web", "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".test.ts", StringComparison.Ordinal) && !f.EndsWith(".test.tsx", StringComparison.Ordinal) && !f.EndsWith(".d.ts", StringComparison.Ordinal))
            .Where(f => !f.Replace('\\', '/').Contains("/web/src/test/", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// The module-level bindings of a file (how many) and those that are state: anything that is
    /// not a constant (string, number, regular expression), a function, a React context, an
    /// import.meta.glob of code, or an identity-scoped map. Plus static fields of classes that are
    /// not readonly, and properties put on window, globalThis or self.
    /// </summary>
    public static (int Bindings, List<Binding> Stateful) Inspect(string file, string source)
    {
        var lines = StripComments(source).Split('\n');
        var bindings = 0;
        var stateful = new List<Binding>();
        string? currentClass = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (ClassRegex().Match(line) is { Success: true } cls)
            {
                currentClass = cls.Groups["name"].Value;
            }
            else if (line.StartsWith('}'))
            {
                currentClass = null;
            }
            if (currentClass is not null && StaticFieldRegex().Match(line) is { Success: true } field)
            {
                bindings++;
                stateful.Add(new Binding(file, i + 1, $"{currentClass}.{field.Groups["name"].Value}", "static", field.Groups["init"].Value.Trim()));
                continue;
            }
            foreach (Match global in GlobalAssignRegex().Matches(line))
            {
                bindings++;
                stateful.Add(new Binding(file, i + 1, $"{global.Groups["target"].Value}.{global.Groups["name"].Value}", "global", line.Trim()));
            }
            if (TopLevelRegex().Match(line) is not { Success: true } top)
            {
                continue;
            }
            bindings++;
            var kind = top.Groups["kind"].Value;
            var initializer = top.Groups["init"].Value.Trim();
            // The value on the next line (const x =\n  value).
            for (var j = i + 1; initializer.Length == 0 && j < lines.Length; j++)
            {
                initializer = lines[j].Trim();
            }
            if (kind is "let" or "var" || !(IsConstantInitializer(initializer) || IsModuleRegistration(file, top.Groups["name"].Value)))
            {
                stateful.Add(new Binding(file, i + 1, top.Groups["name"].Value, kind, Shorten(initializer)));
            }
        }
        return (bindings, stateful);
    }

    private static bool IsConstantInitializer(string init) =>
        init.Length == 0 // a declaration without a value (a type only), never assigned: const has a value
        || StringLiteralRegex().IsMatch(init)
        || NumberRegex().IsMatch(init)
        || init is "true" or "true;" or "false" or "false;"
        || RegexLiteralRegex().IsMatch(init)
        || FunctionRegex().IsMatch(init)
        || init.StartsWith("createContext", StringComparison.Ordinal)
        || init.StartsWith("forwardRef", StringComparison.Ordinal)
        || init.StartsWith("import.meta.glob", StringComparison.Ordinal)
        || init.StartsWith("identityScoped", StringComparison.Ordinal);

    /// <summary>
    /// A module's registration with the shell, by convention: <c>routes</c> in
    /// web/src/modules/&lt;module&gt;/routes.tsx and <c>extensions</c> in
    /// web/src/modules/&lt;module&gt;/extensions.ts(x). The kernel collects them from code when the
    /// bundle loads (paths, permissions, components, functions); data a palette source fetches is
    /// asked for each time, never kept in them.
    /// </summary>
    private static bool IsModuleRegistration(string file, string name) =>
        (name == "routes" && ModuleFileRegex().Match(file) is { Success: true } r && r.Groups["file"].Value == "routes.tsx")
        || (name == "extensions" && ModuleFileRegex().Match(file) is { Success: true } e && e.Groups["file"].Value is "extensions.ts" or "extensions.tsx");

    [GeneratedRegex(@"^web/src/modules/[^/]+/(?<file>[^/]+)$")]
    private static partial Regex ModuleFileRegex();

    private static string Shorten(string text) => text.Length > 80 ? text[..80] + "…" : text;

    /// <summary>
    /// Comments removed, line structure kept. A small scanner, so that "/*" or "//" inside strings,
    /// template literals and regular expressions (an import.meta.glob pattern, a URL) is not taken
    /// for a comment.
    /// </summary>
    public static string StripComments(string source)
    {
        var output = new System.Text.StringBuilder(source.Length);
        var i = 0;
        var lastSignificant = '\0';
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                output.Append('\n', source.AsSpan(i, end - i).Count('\n'));
                i = end;
                continue;
            }
            if (c is '"' or '\'' or '`' || (c == '/' && "(,=:[!&|?{};+-*%~^".Contains(lastSignificant)) || (c == '/' && lastSignificant == '\0'))
            {
                // A string, template or regular expression literal: copied as is up to its end.
                var quote = c;
                var inClass = false;
                output.Append(c);
                i++;
                while (i < source.Length)
                {
                    var d = source[i];
                    output.Append(d);
                    i++;
                    if (d == '\\' && i < source.Length)
                    {
                        output.Append(source[i]);
                        i++;
                        continue;
                    }
                    if (quote == '/' && d == '[') inClass = true;
                    else if (quote == '/' && d == ']') inClass = false;
                    else if (d == quote && !(quote == '/' && inClass)) break;
                    else if (d == '\n' && quote is '"' or '\'' or '/') break;
                }
                lastSignificant = 'a';
                continue;
            }
            output.Append(c);
            if (!char.IsWhiteSpace(c)) lastSignificant = c;
            i++;
        }
        return output.ToString();
    }

    [GeneratedRegex(@"^(export\s+)?(?<kind>const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(:[^=]+)?=\s*(?<init>.*)$")]
    private static partial Regex TopLevelRegex();

    [GeneratedRegex(@"^(export\s+)?(default\s+)?(abstract\s+)?class\s+(?<name>[A-Za-z_$][\w$]*)")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^\s+(public\s+|private\s+|protected\s+)?static\s+(?!readonly\b)(?!async\b)(?!get\b)(?!set\b)(?<name>[A-Za-z_$#][\w$]*)\s*(:[^=(]+)?=\s*(?<init>.*)$")]
    private static partial Regex StaticFieldRegex();

    [GeneratedRegex(@"\b(?<target>window|globalThis|self)\.(?!location\b)(?<name>[A-Za-z_$][\w$]*)\s*=(?!=)")]
    private static partial Regex GlobalAssignRegex();

    [GeneratedRegex(@"^(""[^""]*""|'[^']*'|`[^`$]*`)\s*(as\s+const)?\s*;?$")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"^-?\d[\d_]*(\.\d+)?\s*;?$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^/.+/[dgimsuyv]*\s*;?$")]
    private static partial Regex RegexLiteralRegex();

    [GeneratedRegex(@"^(async\s+)?(function\b|\(.*\)\s*(:\s*[^=]+)?=>|\(\s*$|\([^)]*$|[A-Za-z_$][\w$]*\s*=>|<[^>]+>\s*\()")]
    private static partial Regex FunctionRegex();

}
