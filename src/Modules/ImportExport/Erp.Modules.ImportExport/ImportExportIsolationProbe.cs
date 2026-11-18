using System.Net.Http.Json;
using System.Text;
using Erp.Kernel.Security;

namespace Erp.Modules.ImportExport;

/// <summary>
/// Tenant-isolation attack on import and export (an import surface and an export surface): tenant B reads its own uploaded files and
/// full exports (reads only), then, signed in to tenant A, every tenant B id is tried against every import and full export action
/// (read, mapping, run, error file, discard, download), with and without tenant switch headers; tenant A uploads a file that names
/// tenant B's ids and canaries as its values and runs it; tenant A's lists must hold none of B's. Every byte of every answer is
/// handed back to the gate, which looks for tenant B's ids and canaries.
/// </summary>
internal sealed class ImportExportIsolationProbe : IIsolationProbe
{
    private const string Contacts = "/api/importexport/contacts.contacts";

    public SurfaceKind Kind => SurfaceKind.Import;

    public string Name => "importexport";

    public async Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        var failures = new List<string>();
        var attempts = 0;

        // Tenant B uses the surface on its own records right before the attack, by reading only (the gate watches tenant B's rows
        // for changes nobody of B's made): its uploaded files, their error files and its full exports.
        if (context.Victim is { } victim)
        {
            attempts++;
            using var list = await victim.GetAsync($"{Contacts}/imports?take=10", cancellationToken);
            if (!list.IsSuccessStatusCode)
            {
                failures.Add($"tenant B could not read its own imports: {(int)list.StatusCode}");
            }
            else
            {
                using var document = System.Text.Json.JsonDocument.Parse(await list.Content.ReadAsStringAsync(cancellationToken));
                foreach (var row in document.RootElement.GetProperty("items").EnumerateArray().Take(5))
                {
                    var own = row.GetProperty("id").GetGuid();
                    foreach (var path in new[] { $"{Contacts}/imports/{own}", $"{Contacts}/imports/{own}/errors" })
                    {
                        attempts++;
                        using var read = await victim.GetAsync(path, cancellationToken);
                    }
                }
            }
            attempts++;
            using var exports = await victim.GetAsync("/api/importexport/tenant-exports", cancellationToken);
        }

        foreach (var id in context.VictimIds.Append(context.VictimTenantId).Distinct())
        {
            foreach (var withHeaders in new[] { false, true })
            {
                foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                {
                    (HttpMethod.Get, $"{Contacts}/imports/{id}", null),
                    (HttpMethod.Get, $"{Contacts}/imports/{id}/errors", null),
                    (HttpMethod.Put, $"{Contacts}/imports/{id}/mapping", new { columns = new[] { new { index = 0, field = "email" } }, version = 1u }),
                    (HttpMethod.Post, $"{Contacts}/imports/{id}/run", new { dryRun = false, onError = "skip" }),
                    (HttpMethod.Delete, $"{Contacts}/imports/{id}", null),
                    (HttpMethod.Get, $"/api/importexport/tenant-exports/{id}", null),
                    (HttpMethod.Post, $"/api/importexport/tenant-exports/{id}/download", null),
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

        // A file that names tenant B's ids as organisations and references: whatever the import reads or reports back is tenant A's.
        var ids = context.VictimIds.Take(5).Select(i => i.ToString()).ToList();
        var csv = "Name,Reference,Kind,Organisation,Email\r\n" + string.Join("\r\n", ids.Select((id, i) => $"Probe {i},{id[..8]},person,{id},{id[..6]}@probe.example")) + "\r\n";
        using (var upload = new HttpRequestMessage(HttpMethod.Post, $"{Contacts}/imports") { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)) })
        {
            upload.Headers.TryAddWithoutValidation("X-File-Name", "probe.csv");
            upload.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
            using var response = await context.Attacker.SendAsync(upload, cancellationToken);
            attempts++;
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            observed.Add($"{(int)response.StatusCode} {text}");
            if (response.IsSuccessStatusCode)
            {
                var session = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
                using var run = await context.Attacker.PostAsJsonAsync($"{Contacts}/imports/{session}/run", new { dryRun = true, onError = "skip" }, cancellationToken);
                attempts++;
                observed.Add($"{(int)run.StatusCode} {await run.Content.ReadAsStringAsync(cancellationToken)}");
                using var template = await context.Attacker.GetAsync($"{Contacts}/template", cancellationToken);
                attempts++;
                observed.Add($"{(int)template.StatusCode} {await template.Content.ReadAsStringAsync(cancellationToken)}");
                using var discard = await context.Attacker.DeleteAsync($"{Contacts}/imports/{session}", cancellationToken);
                attempts++;
            }
        }

        // Tenant A's own lists hold none of tenant B's files and exports.
        foreach (var path in new[] { $"{Contacts}/imports?take=30", "/api/importexport/tenant-exports" })
        {
            using var list = await context.Attacker.GetAsync(path, cancellationToken);
            attempts++;
            observed.Add($"{(int)list.StatusCode} {await list.Content.ReadAsStringAsync(cancellationToken)}");
        }
        return new IsolationProbeResult(attempts, observed) { Failures = failures };
    }
}
