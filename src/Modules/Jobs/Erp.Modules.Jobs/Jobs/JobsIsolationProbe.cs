using System.Net.Http.Json;
using System.Text;
using Erp.Kernel.Security;

namespace Erp.Modules.Jobs.Jobs;

/// <summary>
/// Tenant-isolation attack on background jobs (a job surface): tenant B starts a job of its own, then,
/// signed in to tenant A, every tenant B id is tried against every job and schedule action: read,
/// output, cancel, rerun, update, with and without tenant switch headers, and the list filtered on
/// each id. Every byte of every answer is handed back to the gate, which looks for tenant B's ids and
/// canaries (tenant B's seeded jobs, outputs and schedules carry its canary).
/// </summary>
internal sealed class JobsIsolationProbe : IIsolationProbe
{
    public SurfaceKind Kind => SurfaceKind.Job;

    public string Name => "jobs";

    public async Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        var failures = new List<string>();
        var attempts = 0;

        // Tenant B's own job right before the attack, so something of B's is queued while A asks.
        if (context.Victim is { } victim)
        {
            using var started = await victim.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.selfCheck", payload = new { steps = 1, pauseMilliseconds = 0 } }, cancellationToken);
            attempts++;
            if (!started.IsSuccessStatusCode)
            {
                failures.Add($"tenant B could not start its own job: {(int)started.StatusCode}");
            }
        }

        foreach (var id in context.VictimIds.Append(context.VictimTenantId).Distinct())
        {
            foreach (var withHeaders in new[] { false, true })
            {
                foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                {
                    (HttpMethod.Get, $"/api/jobs/jobs/{id}", null),
                    (HttpMethod.Get, $"/api/jobs/jobs/{id}/output", null),
                    (HttpMethod.Post, $"/api/jobs/jobs/{id}/cancel", null),
                    (HttpMethod.Post, $"/api/jobs/jobs/{id}/rerun", null),
                    (HttpMethod.Get, $"/api/jobs/schedules/{id}", null),
                    (HttpMethod.Put, $"/api/jobs/schedules/{id}", new { name = "taken over", cron = "* * * * *", isEnabled = true, version = 1u }),
                    (HttpMethod.Get, $"/api/jobs/jobs?filter={Uri.EscapeDataString($"id eq '{id}'")}&take=5", null),
                })
                {
                    using var request = new HttpRequestMessage(method, path);
                    if (body is not null)
                    {
                        request.Content = JsonContent.Create(body);
                    }
                    if (withHeaders)
                    {
                        request.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
                    }
                    using var response = await context.Attacker.SendAsync(request, cancellationToken);
                    attempts++;
                    observed.Add($"{(int)response.StatusCode} {Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken))}");
                }
            }
        }

        // Starting a job for another tenant by naming it in the body or the headers.
        using (var start = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/jobs") { Content = JsonContent.Create(new { type = "jobs.selfCheck", tenantId = context.VictimTenantId, payload = new { steps = 1 } }) })
        {
            start.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
            using var response = await context.Attacker.SendAsync(start, cancellationToken);
            attempts++;
            observed.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        // Tenant A's own list holds none of tenant B's jobs.
        using (var list = await context.Attacker.GetAsync("/api/jobs/jobs?take=200", cancellationToken))
        {
            attempts++;
            observed.Add($"{(int)list.StatusCode} {await list.Content.ReadAsStringAsync(cancellationToken)}");
        }
        return new IsolationProbeResult(attempts, observed) { Failures = failures };
    }
}
