using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// CLAUDE.md rule 4 for the numbering tables (p10): a user can read, on screen, which sequence of which
/// company changed and what it changed to. A recorded choice is shown by its label, never its stored
/// code (the sequence's "Starts again" is stored as 0, 1 or 2), and every audited numbering row names
/// its record.
/// </summary>
public sealed class NumberingAuditGateTests(AuditFixture fixture) : IClassFixture<AuditFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static Dictionary<string, string> Strings(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Repo.PathOf("web", "src", "modules", "audit", "i18n", $"{language}.json")))!;

    /// <summary>Every value a choice column of the numbering tables can hold (its CHECK names a list or a range of whole numbers)
    /// and every registered kind of document has audit.choice.&lt;schema.table&gt;.&lt;column&gt;.&lt;value&gt; in both languages.</summary>
    [Fact]
    public async Task Every_recorded_choice_value_of_the_numbering_tables_has_a_label_in_both_languages()
    {
        await using var admin = await Env.OpenAdminAsync();
        var checks = await DbCatalog.ReadAsync(admin, """
            SELECT n.nspname || '.' || c.relname, pg_get_constraintdef(k.oid)
              FROM pg_constraint k JOIN pg_class c ON c.oid = k.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE k.contype = 'c' AND n.nspname = 'numbering'
            """, r => (Table: r.GetString(0), Definition: r.GetString(1)));
        var choices = new List<(string Table, string Column, string Value)>();
        foreach (var (table, definition) in checks)
        {
            var list = Regex.Match(definition, @"^CHECK \(+\(?(\w+)\)?(::text)? = ANY");
            if (list.Success)
            {
                choices.AddRange(Regex.Matches(definition, @"'([^']+)'::").Select(v => (table, list.Groups[1].Value, v.Groups[1].Value)));
                continue;
            }
            // A range of whole numbers on one small column: (reset >= 0) AND (reset <= 2).
            var range = Regex.Match(definition, @"^CHECK \(\(\((\w+) >= (\d+)\) AND \(\1 <= (\d+)\)\)\)$");
            if (range.Success && int.Parse(range.Groups[3].Value) - int.Parse(range.Groups[2].Value) <= 10)
            {
                for (var v = int.Parse(range.Groups[2].Value); v <= int.Parse(range.Groups[3].Value); v++)
                {
                    choices.Add((table, range.Groups[1].Value, v.ToString()));
                }
            }
        }
        Assert.Contains(choices, c => c is { Table: "numbering.sequences", Column: "reset" } && c.Value == "2");
        foreach (var type in Env.Factory.Services.GetServices<NumberedDocumentType>())
        {
            choices.Add(("numbering.sequences", "document_type", type.Key));
            choices.Add(("numbering.issued_numbers", "document_type", type.Key));
        }
        var problems = new List<string>();
        foreach (var language in new[] { "en", "ar" })
        {
            var strings = Strings(language);
            foreach (var (table, column, value) in choices)
            {
                var key = $"audit.choice.{table}.{column}.{value}";
                if (!strings.TryGetValue(key, out var label) || string.IsNullOrWhiteSpace(label) || label == value)
                {
                    problems.Add($"{key} has no label in {language} (web/src/modules/audit/i18n/{language}.json)");
                }
                else if (language == "ar" && !Regex.IsMatch(label, @"\p{IsArabic}"))
                {
                    problems.Add($"{key}: the Arabic label '{label}' has no Arabic letters");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Every audited numbering table has a trigger that names its rows (name_en, name_ar or code), so the audit log never
    /// reads "(empty)" for a record of numbering.</summary>
    [Fact]
    public async Task Every_audited_numbering_row_is_named_by_a_column_the_audit_trail_reads()
    {
        await using var admin = await Env.OpenAdminAsync();
        foreach (var table in new[] { "sequences", "counters", "issued_numbers" })
        {
            var columns = (await DbCatalog.ColumnsAsync(admin, new TableRef("numbering", table))).Select(c => c.Name).ToHashSet();
            Assert.True(columns.Contains("code") || columns.Contains("name_en"), $"numbering.{table} has no column the audit trail names its rows from (name_en or code)");
        }
    }
}
