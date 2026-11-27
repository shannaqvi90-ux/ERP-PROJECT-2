using Erp.Gates.Tests.G1;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>Round 23: the HTTP attack no longer takes tenant A's own values for tenant B's, and
/// still catches a real leak of a derived label or of a counter-named row.</summary>
public sealed class MarkerJudgeSelfTests
{
    private const string BLabel = "Journal voucher · Al Noor Trading LLC CANARYB";
    private const string ALabel = "Journal voucher · Al Noor Trading LLC rtoken";

    private static Func<string, string?> Finder(params string[] markers) =>
        text => markers.FirstOrDefault(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static Func<string, string?> None => _ => null;

    [Fact]
    public void A_label_tenant_A_holds_itself_is_no_leak()
    {
        // Both tenants hold the same derived label (the old shared example name): tenant A's own row.
        var answer = $"[{{\"label\":\"{BLabel}\"}}]";
        Assert.Null(IsolationAttack.JudgeMarker(answer, Finder(BLabel), None, m => m == BLabel));
    }

    [Fact]
    public void A_planted_leak_of_a_derived_label_tenant_A_does_not_hold_is_caught()
    {
        var answer = $"[{{\"label\":\"{ALabel}\"}},{{\"label\":\"{BLabel}\"}}]";
        Assert.Equal(BLabel, IsolationAttack.JudgeMarker(answer, Finder(BLabel), None, m => m == ALabel));
    }

    [Fact]
    public void A_leak_is_still_found_after_an_own_value_in_the_same_answer()
    {
        const string own = "Sales invoice · Al Noor Trading LLC CANARYB";
        var answer = $"{own} and {BLabel}";
        Assert.Equal(BLabel, IsolationAttack.JudgeMarker(answer, Finder(own, BLabel), None, m => m == own));
    }

    [Fact]
    public void A_counter_named_row_of_tenant_B_is_caught_whatever_tenant_A_wrote()
    {
        // Tenant B holds a row named like an old counter name; tenant A's own writes carry the run
        // token and letters only, so they are never the marker and the leaked row is.
        var bRow = "attack-116772 role of tenant B";
        var own = AttackNames.Text(116772);
        Assert.DoesNotContain("116772", own, StringComparison.Ordinal);
        Assert.Null(IsolationAttack.JudgeMarker($"[\"{own}\"]", Finder("116772"), None, null));
        Assert.Equal(bRow, IsolationAttack.JudgeMarker($"[\"{own}\",\"{bRow}\"]", Finder(bRow), None, m => m == own));
    }

    [Fact]
    public void Written_names_carry_the_run_token_and_no_digits()
    {
        for (var n = 0; n < 200_000; n += 7919)
        {
            var text = AttackNames.Text(n);
            Assert.False(text.Any(char.IsDigit), text);
            Assert.Contains(AttackNames.RunToken, text, StringComparison.Ordinal);
        }
        Assert.NotEqual(AttackNames.Text(1), AttackNames.Text(2));
    }
}
