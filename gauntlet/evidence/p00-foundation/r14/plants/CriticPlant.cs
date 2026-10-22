namespace Erp.Kernel;

/// <summary>CRITIC p00 r14: plants are inert unless CRITIC_PLANT names them (comma separated).</summary>
public static class CriticPlant
{
    public static bool Is(string name) =>
        (System.Environment.GetEnvironmentVariable("CRITIC_PLANT") ?? "").Split(',').Contains(name);
}
