namespace Erp.Kernel;

/// <summary>CRITIC p00 r19 plants: inert unless the CRITIC_PLANT environment variable names them.</summary>
public static class CriticPlant
{
    public static bool Is(string name) =>
        (Environment.GetEnvironmentVariable("CRITIC_PLANT") ?? "").Split(',').Contains(name, StringComparer.Ordinal);
}

/// <summary>CRITIC p00 r19 plant L19's memo of list totals.</summary>
public static class CriticTotals
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Total, DateTime At)> Memo = new();
}
