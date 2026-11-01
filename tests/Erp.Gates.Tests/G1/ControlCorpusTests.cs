namespace Erp.Gates.Tests.G1;

/// <summary>
/// The differential oracle compares a tenant B value with a value that exists nowhere. A control
/// that happens to occur in tenant A's own data makes the two answers differ with no leak (four
/// times in rounds of p10 and p12). These tests prove a control that occurs in the data, in any
/// letter case, or in the attacker's own answer is never used, whatever the data holds.
/// </summary>
public sealed class ControlCorpusTests
{
    private static readonly string Letters = "abcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void A_control_is_never_a_text_the_attacker_holds_in_any_letter_case()
    {
        // The prefixes of tenant A's numbering sequences, in capitals, and a six-digit number inside
        // a longer contact number.
        var corpus = new ControlCorpus(["QT-0001\u0001SO-0001\u0001PO-0001\u0001CN-0001\u0001DN-0001\u0001RV-0001\u0001PV-0001\u0001JV-0001\u0001",
            "C-1007246\u0001C-1007247\u0001"]);
        for (var i = 0; i < 400; i++)
        {
            foreach (var value in new[] { "so-", "bq-", "QT", "580569", "100724", "ab", "12" })
            {
                var control = corpus.ControlFor(value + new string(' ', 0));
                Assert.False(corpus.Occurs(control), $"the control {control} for {value} occurs in the attacker's data");
            }
        }
    }

    [Fact]
    public void A_control_is_drawn_again_for_every_text_of_the_data_not_only_the_first_draw()
    {
        // Every two-letter text is in the data: no control of the value's own shape exists, so a
        // longer one of the same kind is made, and it is not in the data either.
        var all = string.Join('\u0001', from x in Letters from y in Letters select $"{x}{y}");
        var corpus = new ControlCorpus([all.ToUpperInvariant()]);
        var control = corpus.ControlFor("ab");
        Assert.True(control.Length > 2, control);
        Assert.False(corpus.Occurs(control), control);
        Assert.True(corpus.Fallbacks > 0);
    }

    [Fact]
    public void A_control_is_never_the_value_itself_nor_a_text_of_the_attackers_own_answer()
    {
        var corpus = new ControlCorpus(["unrelated"]);
        // The attacker's unfiltered answer holds every one-letter text: the control for a one
        // letter value must still not occur in it.
        var answer = string.Concat(Letters).ToLowerInvariant() + "{\"id\":\"x\"}";
        for (var i = 0; i < 200; i++)
        {
            var control = corpus.ControlFor("q", $"key{i}", answer);
            Assert.False(answer.Contains(control, StringComparison.OrdinalIgnoreCase), control);
            Assert.False(control.Equals("q", StringComparison.OrdinalIgnoreCase), control);
        }
    }

    [Fact]
    public void A_control_keeps_the_shape_of_the_value_where_the_data_allows_it()
    {
        var corpus = new ControlCorpus(["nothing alike"]);
        var control = corpus.ControlFor("a.b@c.example");
        Assert.Equal(13, control.Length);
        Assert.Equal(".", control[1].ToString());
        Assert.Equal("@", control[3].ToString());
        Assert.True(Guid.TryParse(corpus.ControlFor(Guid.NewGuid().ToString()), out _));
    }
}
