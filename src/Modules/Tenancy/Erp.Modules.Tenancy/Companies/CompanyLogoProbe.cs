using System.Net.Http.Json;
using System.Text;
using Erp.Kernel.Security;

namespace Erp.Modules.Tenancy.Companies;

/// <summary>
/// Tenant-isolation attack on company logos (a file surface): signed in to tenant A, read,
/// replace and remove the logo of every tenant B id, including as a data URL and with tenant
/// switch headers. Every byte of every answer is handed back to the gate, which looks for tenant
/// B's ids and canaries (tenant B's seeded logos carry its canary in their bytes).
/// </summary>
internal sealed class CompanyLogoProbe : IIsolationProbe
{
    public SurfaceKind Kind => SurfaceKind.File;

    public string Name => "tenancy company logos";

    public async Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        var attempts = 0;
        foreach (var id in context.VictimIds.Append(context.VictimTenantId).Distinct())
        {
            var path = $"/api/tenancy/companies/{id}/logo";
            foreach (var withHeaders in new[] { false, true })
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, path);
                if (withHeaders)
                {
                    get.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
                    get.Headers.TryAddWithoutValidation("X-Company-Id", id.ToString());
                }
                using var response = await context.Attacker.SendAsync(get, cancellationToken);
                attempts++;
                observed.Add($"{(int)response.StatusCode} {Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken))}");
            }
            using (var put = await context.Attacker.PutAsJsonAsync(path, new { contentType = "image/png", data = CompanyLogo.ExamplePng }, cancellationToken))
            {
                attempts++;
                observed.Add($"{(int)put.StatusCode} {await put.Content.ReadAsStringAsync(cancellationToken)}");
            }
            using (var delete = await context.Attacker.DeleteAsync(path, cancellationToken))
            {
                attempts++;
                observed.Add($"{(int)delete.StatusCode} {await delete.Content.ReadAsStringAsync(cancellationToken)}");
            }
        }
        return new IsolationProbeResult(attempts, observed);
    }
}
