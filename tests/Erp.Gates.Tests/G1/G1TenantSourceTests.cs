using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, source: the tenant comes only from the session. The few places that may choose a tenant,
/// change session settings, step around the tenant query filter or reach the database outside the
/// request's unit of work are reviewed one by one in tests/Gates/tenant-bypass-sources.txt (rule,
/// file and reason); any other file under src/ that does one of these things fails the gate, and
/// so does a reviewed entry the file no longer needs. The HTTP attack checks the same thing at run
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
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || Rules.All(r => r.Name != parts[0]))
            {
                problems.Add($"{G1TenantSourceTests.ReviewedFile}: '{entry}' must be '<rule> <path>' with a rule among {string.Join(", ", Rules.Select(r => r.Name))}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{G1TenantSourceTests.ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed.Add(entry);
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        var matches = 0;
        foreach (var (path, text) in files)
        {
            scanned++;
            foreach (var rule in Rules)
            {
                var match = rule.Pattern.Match(text);
                if (!match.Success)
                {
                    continue;
                }
                var key = $"{rule.Name} {path}";
                if (allowed.Contains(key))
                {
                    used.Add(key);
                    matches++;
                    continue;
                }
                var line = text[..match.Index].Count(c => c == '\n') + 1;
                problems.Add($"{path}:{line} {rule.What} [{rule.Name}]: only reviewed files may; review it in {G1TenantSourceTests.ReviewedFile} or let the session's tenant do the work");
            }
        }
        var paths = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in allowed.Where(a => !used.Contains(a)))
        {
            var path = stale.Split(' ')[1];
            problems.Add(paths.Contains(path)
                ? $"{G1TenantSourceTests.ReviewedFile}: '{stale}' no longer matches; remove the entry"
                : $"{G1TenantSourceTests.ReviewedFile}: '{stale}' names a file that no longer exists; remove the entry");
        }
        return new Result(problems, scanned, matches);
    }
}
