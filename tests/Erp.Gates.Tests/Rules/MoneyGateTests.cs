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

    /// <summary>
    /// CLAUDE.md rule 2, every amount with its currency, the exchange rate used and the base-currency
    /// amount: a decimal column named <c>{prefix}_amount</c> (a money's amount, as
    /// <see cref="Erp.Kernel.Money.MoneyMapping"/> names it) sits beside <c>{prefix}_currency</c>,
    /// <c>{prefix}_exchange_rate</c>, <c>{prefix}_base_amount</c> and <c>{prefix}_base_currency</c> in the
    /// same table, each of them numeric or three letters, and a nullable amount (a money the record may
    /// leave out) has a check that names every one of them, so none is ever set without the others.
    /// </summary>
    [Fact]
    public async Task Every_amount_column_carries_its_currency_rate_and_base_amount()
    {
        await using var admin = await fixture.Env.OpenAdminAsync();
        var columns = await DbCatalog.ReadAsync(admin, $"""
            SELECT n.nspname || '.' || c.relname, a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull
              FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped AND {DbCatalog.UserSchemaFilter}
            """, r => (Table: r.GetString(0), Column: r.GetString(1), Type: r.GetString(2), Nullable: r.GetBoolean(3)));
        var checks = await DbCatalog.ReadAsync(admin, $"""
            SELECT n.nspname || '.' || c.relname, pg_get_constraintdef(k.oid)
              FROM pg_constraint k JOIN pg_class c ON c.oid = k.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE k.contype = 'c' AND {DbCatalog.UserSchemaFilter}
            """, r => (Table: r.GetString(0), Definition: r.GetString(1)));
        var problems = new List<string>();
        var amounts = 0;
        foreach (var amount in columns.Where(c => c.Column.EndsWith("_amount", StringComparison.Ordinal) && !c.Column.EndsWith("_base_amount", StringComparison.Ordinal)))
        {
            amounts++;
            var prefix = amount.Column[..^"_amount".Length];
            var where = $"{amount.Table}.{amount.Column}";
            if (!amount.Type.StartsWith("numeric", StringComparison.Ordinal))
            {
                problems.Add($"{where} is {amount.Type}, not numeric");
            }
            var siblings = new[] { ("_currency", "char"), ("_exchange_rate", "numeric"), ("_base_amount", "numeric"), ("_base_currency", "char") };
            foreach (var (suffix, kind) in siblings)
            {
                var sibling = columns.FirstOrDefault(c => c.Table == amount.Table && c.Column == prefix + suffix);
                if (sibling.Column is null)
                {
                    problems.Add($"{where} has no {prefix}{suffix} beside it (CLAUDE.md rule 2: an amount is stored with its currency, the rate used and the base-currency amount)");
                }
                else if (kind == "numeric" ? !sibling.Type.StartsWith("numeric", StringComparison.Ordinal) : !(sibling.Type.StartsWith("character(3)", StringComparison.Ordinal) || sibling.Type.StartsWith("character varying(3)", StringComparison.Ordinal)))
                {
                    problems.Add($"{amount.Table}.{prefix}{suffix} is {sibling.Type}");
                }
            }
            if (amount.Nullable && !checks.Any(k => k.Table == amount.Table && siblings.All(s => k.Definition.Contains(prefix + s.Item1, StringComparison.Ordinal)) && k.Definition.Contains(amount.Column, StringComparison.Ordinal)))
            {
                problems.Add($"{where} may be null, and no check constraint of {amount.Table} names it with its currency, rate and base amount (all set or all null)");
            }
        }
        Assert.True(problems.Count == 0, "Amounts without their currency, rate or base amount:\n" + string.Join("\n", problems));
        Assert.True(amounts >= Ratchet.Min("rules.moneyAmountColumnsChecked"), $"{amounts} amount columns checked; ratchet minimum {Ratchet.Min("rules.moneyAmountColumnsChecked")}");
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
