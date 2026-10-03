using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, source: the tenant comes only from the session. The few places that may choose a tenant,
/// change session settings, step around the tenant query filter or reach the database outside the
/// request's unit of work are reviewed one by one in tests/Gates/tenant-bypass-sources.txt (rule,
/// file, the number of uses reviewed, and reason); any other file under src/ that does one of these
/// things fails the gate, and so does a reviewed entry the file no longer needs. Uses are counted,
/// not just found: a reviewed file is reviewed for the uses that were read, so one more use in it
/// (critic p00 round 3, plant T1: a header-driven tenant switch whose set_config sat in the
/// already-reviewed ErpDbSession.cs) fails until someone reviews it and raises the count. The HTTP attack checks the same thing at run
/// time (every binding against the request's principal, every setting statement against its
/// sender); this check also covers code paths no request reaches.
/// </summary>
public sealed class G1TenantSourceTests
{
    public const string ReviewedFile = "tests/Gates/tenant-bypass-sources.txt";

    [Fact]
    public void Only_reviewed_files_choose_a_tenant_or_step_around_the_unit_of_work()
    {
        var files = TenantBypassScanner.SourceFiles().ToList();
        var result = TenantBypassScanner.Check(
            files.Select(f => (Path.GetRelativePath(Repo.Root, f).Replace('\\', '/'), File.ReadAllText(f))),
            Repo.ReadReviewedList(ReviewedFile));
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.FilesScanned} files scanned, {result.Matches} reviewed uses of {TenantBypassScanner.Rules.Count} rules");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.FilesScanned >= Ratchet.Min("g1.tenantSourceFilesScanned"),
            $"g1.tenantSourceFilesScanned: {result.FilesScanned}; ratchet minimum {Ratchet.Min("g1.tenantSourceFilesScanned")}");
        Assert.True(TenantBypassScanner.Rules.Count >= Ratchet.Min("g1.tenantSourceRules"),
            $"g1.tenantSourceRules: {TenantBypassScanner.Rules.Count}; ratchet minimum {Ratchet.Min("g1.tenantSourceRules")}");
    }

    [Fact]
    public void The_scanner_catches_every_planted_bypass()
    {
        // The shapes critics planted, and their obvious variants, in a file nobody reviewed.
        var planted = new (string Rule, string Code)[]
        {
            ("tenant-setting", "var sql = \"SELECT set_config('app.tenant_id', @t, true)\";"),
            ("set-config", "await Run(\"SELECT pg_catalog.set_config(@name, @value, true)\");"),
            ("sql-set", "await Run(\"SET LOCAL ROLE erp_owner\");"),
            ("sql-set", "await Run(\"\"\"\n    RESET ALL;\n    \"\"\");"),
            ("ignore-tenant-filter", "var all = await db.Users.IgnoreQueryFilters().ToListAsync();"),
            ("ignore-tenant-filter", "var all = db.Users.IgnoreQueryFilters([ModuleDbContext.TenantFilterName]);"),
            ("raw-ef-sql", "var rows = db.Users.FromSqlRaw(\"SELECT * FROM identity.users\");"),
            ("raw-ef-sql", "await db.Database.ExecuteSqlAsync($\"UPDATE x SET y = 1\");"),
            ("new-session", "await using var rogue = new ErpDbSession(dataSource);"),
            ("bind", "await session.BeginAsync(Guid.Parse(http.Request.Headers[\"X-Erp-Workspace\"]!), null, \"user\");"),
            ("unbound", "var connection = await session.OpenUnboundAsync();"),
            ("data-source", "app.MapGet(\"/x\", async (NpgsqlDataSource dataSource) => 1);"),
            ("connection-string", "var admin = configuration.GetConnectionString(\"Admin\");"),
            ("connection-string", "await using var c = new NpgsqlConnection(\"Host=db;Username=postgres\");"),
        };
        foreach (var (rule, code) in planted)
        {
            var result = TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", code)], []);
            Assert.True(result.Problems.Any(p => p.Contains($"[{rule}]", StringComparison.Ordinal)),
                $"rule {rule} missed: {code}\n" + string.Join("\n", result.Problems));
        }

        // A reviewed use passes; the same file without the use makes the entry stale.
        var reviewed = new List<(string Entry, string Reason)> { ("bind src/Modules/Planted/Planted.cs", "planted") };
        Assert.Empty(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", "await session.BeginAsync(id, null, \"seed\");")], reviewed).Problems);
        Assert.Contains(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", "var x = 1;")], reviewed).Problems,
            p => p.Contains("no longer", StringComparison.Ordinal));
        // A second use in a reviewed file fails until its count is reviewed (critic p00 round 3,
        // plant T1); an entry without a count reviews exactly one use.
        const string twoBinds = "await session.BeginAsync(id, null, \"seed\");\nawait session.BeginAsync(Guid.Parse(header), null, \"user\");";
        Assert.Contains(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", twoBinds)], reviewed).Problems,
            p => p.Contains("[bind]", StringComparison.Ordinal) && p.Contains("2 uses", StringComparison.Ordinal) && p.Contains(":2", StringComparison.Ordinal));
        Assert.Empty(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", twoBinds)],
            [("bind src/Modules/Planted/Planted.cs 2", "planted")]).Problems);
        // Fewer uses than reviewed: the count is stale and must come down (a later use would
        // otherwise slip in under the old count).
        Assert.Contains(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", "await session.BeginAsync(id, null, \"seed\");")],
            [("bind src/Modules/Planted/Planted.cs 2", "planted")]).Problems, p => p.Contains("lower the count", StringComparison.Ordinal));
        // Malformed counts are refused.
        Assert.Contains(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", "var x = 1;")],
            [("bind src/Modules/Planted/Planted.cs zero", "planted")]).Problems, p => p.Contains("must be", StringComparison.Ordinal));

        // Named filters other than the tenant's (a company filter) are not a tenant bypass.
        Assert.Empty(TenantBypassScanner.Check([("src/Modules/Planted/Planted.cs", "db.Workplaces.IgnoreQueryFilters([ModuleDbContext.CompanyFilterName])")], []).Problems);
    }
}

/// <summary>Finds code that chooses a tenant or works around the unit of work.</summary>
public static class TenantBypassScanner
{
    public sealed record Rule(string Name, string What, Regex Pattern);

    public sealed record Result(IReadOnlyList<string> Problems, int FilesScanned, int Matches);

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static readonly IReadOnlyList<Rule> Rules =
    [
        new("tenant-setting", "names the tenant settings app.tenant_id / app.tenant_tx", new(@"app\.tenant_(?:id|tx)\b", Options)),
        new("set-config", "calls set_config (changes a session setting)", new(@"\bset_config\s*\(", Options | RegexOptions.IgnoreCase)),
        new("sql-set", "sends SET, RESET or DISCARD (changes a session setting or role)",
            new(@"(?:""|^)\s*(?:SET|RESET|DISCARD)\s+(?:LOCAL\b|SESSION\b|ROLE\b|ALL\b|TRANSACTION\b|row_security\b|search_path\b|app\.|""|;)", Options | RegexOptions.IgnoreCase | RegexOptions.Multiline)),
        new("ignore-tenant-filter", "switches off the tenant query filter",
            new(@"IgnoreQueryFilters\s*\(\s*\)|IgnoreQueryFilters\s*\([^)]*(?:TenantFilterName|""tenant"")", Options)),
        new("raw-ef-sql", "runs raw SQL through EF Core", new(@"\b(?:FromSql|FromSqlRaw|FromSqlInterpolated|SqlQuery|SqlQueryRaw|ExecuteSql|ExecuteSqlRaw|ExecuteSqlInterpolated)(?:Async)?\s*[<(]", Options)),
        new("new-session", "builds a unit of work outside dependency injection", new(@"\bnew\s+ErpDbSession\s*\(", Options)),
        new("bind", "binds a unit of work to a tenant", new(@"\.BeginAsync\s*\(", Options)),
        new("unbound", "opens the connection without a tenant", new(@"\bOpenUnboundAsync\s*\(", Options)),
        new("data-source", "uses the database outside the request's unit of work", new(@"\bNpgsqlDataSource\b", Options)),
        new("connection-string", "opens its own database connection", new(@"\bGetConnectionString\s*\(|\bnew\s+NpgsqlConnection\s*\(|ConnectionStrings:", Options)),
    ];

    public static IEnumerable<string> SourceFiles()
    {
        var root = Repo.PathOf("src");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar);
                return !parts.Contains("bin") && !parts.Contains("obj");
            })
            .Order(StringComparer.Ordinal);
    }

    /// <param name="source">Repository-relative path and text of each file.</param>
    /// <param name="reviewed">Entries <c>rule path</c> with their reasons.</param>
    public static Result Check(IEnumerable<(string Path, string Text)> source, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var files = source.ToList();
        var problems = new List<string>();
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = 1;
            if (parts.Length is < 2 or > 3 || Rules.All(r => r.Name != parts[0]) ||
                (parts.Length == 3 && (!int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out count) || count < 1)))
            {
                problems.Add($"{G1TenantSourceTests.ReviewedFile}: '{entry}' must be '<rule> <path> [<uses reviewed, 1 when left out>]' with a rule among {string.Join(", ", Rules.Select(r => r.Name))}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{G1TenantSourceTests.ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed[$"{parts[0]} {parts[1]}"] = count;
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        var matches = 0;
        foreach (var (path, text) in files)
        {
            scanned++;
            foreach (var rule in Rules)
            {
                var found = rule.Pattern.Matches(text);
                if (found.Count == 0)
                {
                    continue;
                }
                var key = $"{rule.Name} {path}";
                var lines = string.Join(", ", found.Select(m => $"{path}:{text[..m.Index].Count(c => c == '\n') + 1}"));
                if (allowed.TryGetValue(key, out var reviewedUses))
                {
                    used.Add(key);
                    if (found.Count == reviewedUses)
                    {
                        matches += found.Count;
                    }
                    else if (found.Count > reviewedUses)
                    {
                        problems.Add($"{path}: {found.Count} uses where {G1TenantSourceTests.ReviewedFile} reviewed {reviewedUses}: {rule.What} [{rule.Name}] at {lines}; " +
                                     "review the new use and raise the count, or let the session's tenant do the work");
                    }
                    else
                    {
                        problems.Add($"{G1TenantSourceTests.ReviewedFile}: '{key}' reviews {reviewedUses} uses but the file has {found.Count} ({lines}); lower the count");
                    }
                    continue;
                }
                problems.Add($"{lines} {rule.What} [{rule.Name}]: only reviewed files may; review it in {G1TenantSourceTests.ReviewedFile} or let the session's tenant do the work");
            }
        }
        var paths = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in allowed.Keys.Where(a => !used.Contains(a)))
        {
            var path = stale.Split(' ')[1];
            problems.Add(paths.Contains(path)
                ? $"{G1TenantSourceTests.ReviewedFile}: '{stale}' no longer matches; remove the entry"
                : $"{G1TenantSourceTests.ReviewedFile}: '{stale}' names a file that no longer exists; remove the entry");
        }
        return new Result(problems, scanned, matches);
    }
}
