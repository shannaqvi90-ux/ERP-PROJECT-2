namespace Erp.Testing;

/// <summary>
/// The trait of tests that measure time against a budget (the owner's bar: find one record among
/// 100,000 in well under a second). <c>./erp verify</c> runs them in a step of their own, after
/// every other .NET test, so the suite's own parallel tests never share the machine with the
/// measurement. Budgets are never loosened for load; the measurement runs alone instead.
/// </summary>
public static class TimingBudget
{
    public const string Trait = "Load";
    public const string Value = "Timing";
}
