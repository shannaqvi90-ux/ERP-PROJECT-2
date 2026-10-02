using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.Rules;

/// <summary>CLAUDE.md rule 2: decimal types only, never floating point, for money — enforced
/// everywhere: no floating-point or <c>money</c> column in the database, no float or double in
/// server code, no parseFloat in the front end.</summary>
public sealed partial class MoneyGateTests(GateFixture fixture)
{
    [Fact]
    public async Task No_floating_point_or_money_columns_in_the_database()
    {
        await using var admin = await fixture.Env.OpenAdminAsync();
        var columns = await DbCatalog.ReadAsync(admin, $"""
            SELECT n.nspname || '.' || c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
              FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('r', 'p', 'v', 'm') AND a.attnum > 0 AND NOT a.attisdropped AND {DbCatalog.UserSchemaFilter}
               AND a.atttypid IN ('real'::regtype, 'double precision'::regtype, 'money'::regtype,
                                  'real[]'::regtype, 'double precision[]'::regtype)
            """, r => r.GetString(0));
        Assert.True(columns.Count == 0, "Floating-point or money-typed columns: " + string.Join(", ", columns));
    }

    [Fact]
    public void No_float_or_double_in_server_code()
    {
        var offenders = SourceFiles("src", "*.cs")
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && FloatRegex().IsMatch(StripStrings(x.line)))
            .Select(x => $"{Path.GetRelativePath(Repo.Root, x.file)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Floating-point types in server code:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void No_parseFloat_in_front_end_code()
    {
        var offenders = SourceFiles("web/src", "*.ts").Concat(SourceFiles("web/src", "*.tsx"))
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(x => ParseFloatRegex().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(Repo.Root, x.file)}:{x.i + 1}")
            .ToList();
        Assert.True(offenders.Count == 0, "parseFloat/Number.parseFloat in front-end code (money travels as decimal strings):\n" + string.Join("\n", offenders));
    }

    internal static IEnumerable<string> SourceFiles(string relative, string pattern) =>
        Directory.EnumerateFiles(Repo.PathOf(relative.Split('/')), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string StripStrings(string line) => StringLiteralRegex().Replace(line, "\"\"");

    [GeneratedRegex(@"\b(float|double|Single|Double)\b(?!\s*\()")]
    private static partial Regex FloatRegex();

    [GeneratedRegex(@"\bparseFloat\s*\(")]
    private static partial Regex ParseFloatRegex();

    [GeneratedRegex("\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial Regex StringLiteralRegex();
}
