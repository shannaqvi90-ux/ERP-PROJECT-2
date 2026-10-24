using System.Text.Json;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;

namespace Erp.Modules.Numbering.Tests;

/// <summary>The pattern rules, from the examples the web screen's preview is held to as well.</summary>
public sealed class PatternTests
{
    private static readonly JsonElement Examples =
        JsonDocument.Parse(File.ReadAllText(Repo.PathOf("tests", "Erp.Modules.Numbering.Tests", "pattern-examples.json"))).RootElement;

    [Fact]
    public void Every_shared_example_formats_to_its_number()
    {
        var count = 0;
        foreach (var e in Examples.GetProperty("formats").EnumerateArray())
        {
            var number = NumberPattern.Format(e.GetProperty("prefix").GetString()!, e.GetProperty("suffix").GetString()!, e.GetProperty("padding").GetInt32(),
                DateOnly.Parse(e.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                e.GetProperty("branch").ValueKind == JsonValueKind.Null ? null : e.GetProperty("branch").GetString(), e.GetProperty("value").GetInt64());
            Assert.Equal(e.GetProperty("number").GetString(), number);
            count++;
        }
        Assert.True(count >= 7);
    }

    [Fact]
    public void Every_shared_example_falls_in_its_period()
    {
        foreach (var e in Examples.GetProperty("periods").EnumerateArray())
        {
            Assert.Equal(e.GetProperty("period").GetString(),
                NumberPattern.PeriodOf(e.GetProperty("reset").GetString()!, DateOnly.Parse(e.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Fact]
    public void Every_shared_check_finds_exactly_its_problems()
    {
        var count = 0;
        foreach (var e in Examples.GetProperty("checks").EnumerateArray())
        {
            var found = NumberPattern.Check(e.GetProperty("prefix").GetString(), e.GetProperty("suffix").GetString(), e.GetProperty("padding").GetInt32(),
                e.GetProperty("reset").GetString(), e.GetProperty("perBranch").GetBoolean()).Select(p => $"{p.Field} {p.Code}").ToList();
            var expected = e.GetProperty("problems").EnumerateArray().Select(p => $"{p[0].GetString()} {p[1].GetString()}").ToList();
            Assert.True(expected.SequenceEqual(found), $"{e}: expected [{string.Join(", ", expected)}], found [{string.Join(", ", found)}]");
            count++;
        }
        Assert.True(count >= 15);
    }

    [Fact]
    public void A_value_wider_than_its_digits_keeps_every_digit_and_the_padding_is_held_to_its_range()
    {
        Assert.Equal("A-1234567", NumberPattern.Format("A-", "", 3, new DateOnly(2026, 1, 1), null, 1_234_567));
        Assert.Equal("999999999999999999", NumberPattern.Format("", "", 12, new DateOnly(2026, 1, 1), null, NumberPattern.MaxValue));
        // A branch token with no branch prints nothing in its place (a preview before a branch exists).
        Assert.Equal("DN--001", NumberPattern.Format("DN-{BRANCH}-", "", 3, new DateOnly(2026, 1, 1), null, 1));
    }

    [Fact]
    public void Document_type_keys_are_module_and_kind_in_lower_camel_case()
    {
        Assert.True(NumberPattern.IsDocumentTypeKey("purchasing.order"));
        Assert.True(NumberPattern.IsDocumentTypeKey("sales.creditNote"));
        Assert.False(NumberPattern.IsDocumentTypeKey("Purchasing.order"));
        Assert.False(NumberPattern.IsDocumentTypeKey("purchasing"));
        Assert.False(NumberPattern.IsDocumentTypeKey("purchasing.order.line"));
        Assert.Empty(new NumberedDocumentType("purchasing.order", "x", "PO-{YYYY}-", 5, NumberReset.Yearly, false).Problems());
        Assert.NotEmpty(new NumberedDocumentType("purchasing.order", "x", "PO-", 5, NumberReset.Yearly, false).Problems());
    }
}
