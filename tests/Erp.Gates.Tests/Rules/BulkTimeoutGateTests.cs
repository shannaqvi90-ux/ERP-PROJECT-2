using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// G3 (one command from a clean clone, on a busy machine): bulk work must never run under a short
/// command timeout. Critic p00 round 3: the 100,000-credential seed statement ran under Npgsql's
/// 30-second default and ./erp verify failed twice in a row on a saturated 4-CPU machine. Seeding
/// and imports now run on the bulk pool, whose connection string gives every command a long
/// timeout (Erp.Kernel.Data.ErpDataSources). This gate keeps it that way: code on the bulk path
/// (seeders, the bulk loader, anything that sends COPY) may not set a literal command timeout of
/// its own (that is how the 30-second default and then a too-short 600 crept in), nor open
/// connections or pools of its own (which would bypass the bulk pool).
/// </summary>
public sealed partial class BulkTimeoutGateTests
{
    [Fact]
    public void Bulk_code_never_sets_a_literal_command_timeout_or_opens_its_own_connection()
    {
        var files = BulkFiles().ToList();
        Assert.True(files.Count >= Ratchet.Min("rules.bulkSourceFilesChecked"),
            $"{files.Count} bulk source files checked; ratchet minimum {Ratchet.Min("rules.bulkSourceFilesChecked")}: " + string.Join(", ", files.Select(Relative)));
        var offenders = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }
                if (LiteralTimeoutRegex().IsMatch(line))
                {
                    offenders.Add($"{Relative(file)}:{i + 1}: literal command timeout ({line.Trim()}); bulk code inherits the bulk pool's timeout");
                }
                if (OwnConnectionRegex().IsMatch(line))
                {
                    offenders.Add($"{Relative(file)}:{i + 1}: opens its own connection or pool ({line.Trim()}); bulk code uses the unit of work's connection");
                }
            }
        }
        Assert.True(offenders.Count == 0, "Bulk code with its own timeout or connection:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Only_the_seed_runner_and_the_platform_build_the_application_pools()
    {
        var allowed = new[]
        {
            Path.Combine("src", "Kernel", "Erp.Kernel", "Data", "ErpDataSources.cs"),
            Path.Combine("src", "Kernel", "Erp.Kernel", "Hosting", "ErpPlatform.cs"),
            Path.Combine("src", "Kernel", "Erp.Kernel", "Seeding", "SeedRunner.cs"),
        };
        var offenders = MoneyGateTests.SourceFiles("src", "*.cs")
            .Where(f => !allowed.Contains(Relative(f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
            .Where(x => PoolRegex().IsMatch(x.line))
            .Select(x => $"{Relative(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Application connection pools built outside the reviewed places:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_seed_runner_puts_every_tenant_seed_on_the_bulk_pool()
    {
        var runner = File.ReadAllText(Repo.PathOf("src", "Kernel", "Erp.Kernel", "Seeding", "SeedRunner.cs"));
        var bulk = runner.IndexOf("ErpDataSources.BuildBulk(", StringComparison.Ordinal);
        var choose = runner.IndexOf("UseBulkConnectionAsync(", StringComparison.Ordinal);
        var begin = runner.IndexOf(".BeginAsync(", StringComparison.Ordinal);
        var seed = runner.IndexOf(".SeedAsync(", StringComparison.Ordinal);
        Assert.True(bulk >= 0 && choose > bulk && begin > choose && seed > begin,
            "SeedRunner must build the bulk pool, choose it for each tenant's session before binding the tenant, and only then run the seeders");
    }

    /// <summary>Seeders (every <c>ITenantSeeder</c>), the bulk loader and anything that sends COPY.</summary>
    internal static IEnumerable<string> BulkFiles() =>
        MoneyGateTests.SourceFiles("src", "*.cs")
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return SeederRegex().IsMatch(text) || text.Contains("BeginBinaryImport", StringComparison.Ordinal) ||
                       text.Contains("BeginTextImport", StringComparison.Ordinal) || text.Contains("BulkInsert.InsertAsync", StringComparison.Ordinal) ||
                       Path.GetFileName(f) == "BulkInsert.cs" || Path.GetFileName(f) == "SeedRunner.cs";
            })
            .Order(StringComparer.Ordinal);

    private static string Relative(string file) => Path.GetRelativePath(Repo.Root, file);

    [GeneratedRegex(@"CommandTimeout\s*=\s*\d|CommandTimeout\(\s*\d|SetCommandTimeout\(\s*\d|\.Timeout\s*=\s*TimeSpan\.From\w+\(\s*\d|Timeout\s*=\s*\d")]
    private static partial Regex LiteralTimeoutRegex();

    [GeneratedRegex(@"new\s+Npgsql(Connection|DataSourceBuilder)\s*\(|NpgsqlDataSource\.Create\s*\(")]
    private static partial Regex OwnConnectionRegex();

    [GeneratedRegex(@"new\s+NpgsqlDataSourceBuilder\s*\(|NpgsqlDataSource\.Create\s*\(|ErpDataSources\.Build(App|Bulk)\s*\(")]
    private static partial Regex PoolRegex();

    [GeneratedRegex(@":\s*ITenantSeeder\b|,\s*ITenantSeeder\b")]
    private static partial Regex SeederRegex();
}
