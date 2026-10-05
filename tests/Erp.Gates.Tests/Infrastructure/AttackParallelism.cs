namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// How many of an attack's requests are in flight at once where their order does not matter
/// (requests of one endpoint, one field or one list query; tenant B's touches of an endpoint still
/// come before and after them). The attacks wait mostly on round trips to PostgreSQL, not on the
/// processor (measured: under one processor busy per attack process), so more requests in flight
/// shorten the run without changing a single request or judgement. ERP_GATE_PARALLEL_REQUESTS
/// overrides it (at least 1) for measuring.
/// </summary>
public static class AttackParallelism
{
    public static int Requests { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("ERP_GATE_PARALLEL_REQUESTS"), out var n) && n >= 1 ? n : 8;
}
