namespace Erp.Kernel.Security;

/// <summary>
/// An attack the tenant-isolation gate (G1) runs for a surface that is not a plain HTTP data
/// endpoint: exports, imports, jobs and files. The gate signs in as tenant A and hands the probe
/// tenant B's identifiers; the probe tries to reach B's data through its surface and reports any
/// value it obtained. The gate fails if any obtained value belongs to tenant B.
/// </summary>
public interface IIsolationProbe
{
    /// <summary>Surface kind this probe covers.</summary>
    SurfaceKind Kind { get; }

    /// <summary>Short name shown in gate output.</summary>
    string Name { get; }

    Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken);
}

/// <param name="Attacker">HTTP client signed in as a tenant A administrator.</param>
/// <param name="VictimTenantId">Tenant B.</param>
/// <param name="VictimIds">Every primary key that belongs to tenant B.</param>
/// <param name="VictimStrings">Canary strings that only tenant B's data contains.</param>
public sealed record IsolationProbeContext(
    HttpClient Attacker,
    Guid AttackerTenantId,
    Guid VictimTenantId,
    IReadOnlyCollection<Guid> VictimIds,
    IReadOnlyCollection<string> VictimStrings);

/// <param name="Attempts">Number of attempts made.</param>
/// <param name="Observed">Everything the probe saw (response bodies, file contents, job output);
/// the gate scans it for tenant B's identifiers and canaries.</param>
public sealed record IsolationProbeResult(int Attempts, IReadOnlyList<string> Observed);
