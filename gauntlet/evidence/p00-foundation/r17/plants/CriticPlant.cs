namespace Erp.Kernel;

/// <summary>CRITIC p00 r17 plants: inert unless the CRITIC_PLANT environment variable names them.</summary>
public static class CriticPlant
{
    public static bool Is(string name) =>
        (Environment.GetEnvironmentVariable("CRITIC_PLANT") ?? "").Split(',').Contains(name, StringComparer.Ordinal);
}
