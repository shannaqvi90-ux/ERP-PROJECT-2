using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Shell;
using Erp.Modules.Reports.Contracts;
using Erp.Modules.Reports.Pdf;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Reports;

/// <summary>
/// The report framework every module reuses. Modules register reports (parameters, columns,
/// groupings, totals and a source producing the rows) and printable lists (a row reader); this
/// module serves them under their own permissions as on-screen documents, PDF in English or
/// Arabic laid out right to left, and CSV or XLSX exports. It keeps no tables: everything it prints
/// is read through the owning module, inside the caller's tenant and company scope.
/// </summary>
public sealed class ReportsModule : ErpModule
{
    public override string Name => "reports";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. ReportsPermissions.All]);
        module.Services.AddSingleton<WebStrings>();
        module.Services.AddSingleton<PdfFonts>();
        module.Services.AddSingleton<PdfReportRenderer>();
        module.Services.AddScoped<ReportEngine>();
        module.Endpoints(ReportEndpoints.Map);
        module.Menu(new MenuEntry("reports.catalog", "reports.menu.catalog", "/reports/catalog", ReportsPermissions.CatalogRead, Order: 950));
        module.IsolationProbe<ReportsIsolationProbe>();
    }
}

/// <summary>
/// Tenant-isolation attack on reports and list printing (an export surface): signed in to tenant
/// A, every report runs with every tenant B id in every reference parameter, every printable list
/// prints with tenant B's ids and values in its search and filter, and every report and list runs
/// plain, each in every format and both languages, with and without tenant switch headers. Right
/// before each of tenant A's requests, tenant B (when the gate hands the probe tenant B's
/// administrator) prints or exports the very same thing on its own records, so a print store that
/// keeps documents without the tenant (critic p06 round 1, plant L2: files kept by download name)
/// holds tenant B's document when tenant A asks. Every answer tenant A gets goes back to the gate
/// whole; the gate reads PDFs and workbooks as text and looks for tenant B's ids and canaries.
/// </summary>
internal sealed class ReportsIsolationProbe(Erp.Kernel.Modules.ModuleCatalog catalog) : IIsolationProbe
{
    public SurfaceKind Kind => SurfaceKind.Export;

    public string Name => "reports and list printing";

    public async Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        var attempts = 0;
        var ids = context.VictimIds.Append(context.VictimTenantId).Distinct().Take(40).ToList();
        var texts = context.VictimStrings.Where(s => s.Length is > 2 and < 60).Take(12).ToList();
        var paths = new List<string>();
        foreach (var report in catalog.Reports.Select(r => r.Definition))
        {
            var references = report.Parameters.Where(p => p.Type == Erp.Kernel.Reports.ReportParameterType.Reference).ToList();
            if (references.Count == 0)
            {
                paths.Add($"/api/reports/run/{report.Key}?");
                foreach (var text in texts.Take(4))
                {
                    var textParameter = report.Parameters.FirstOrDefault(p => p.Type == Erp.Kernel.Reports.ReportParameterType.Text);
                    if (textParameter is not null)
                    {
                        paths.Add($"/api/reports/run/{report.Key}?{textParameter.Key}={Uri.EscapeDataString(text)}&");
                    }
                }
            }
            foreach (var parameter in references)
            {
                foreach (var id in ids)
                {
                    paths.Add($"/api/reports/run/{report.Key}?{parameter.Key}={id}&");
                }
            }
        }
        foreach (var (list, _) in catalog.PrintableLists)
        {
            paths.Add($"/api/reports/lists/{list.Key}?");
            foreach (var text in texts)
            {
                paths.Add($"/api/reports/lists/{list.Key}?search={Uri.EscapeDataString(text.Split(' ')[0])}&");
            }
            foreach (var column in list.Columns.Where(c => c.Filterable && c.Type == Erp.Kernel.Lists.ListColumnType.Reference))
            {
                foreach (var id in ids.Take(10))
                {
                    paths.Add($"/api/reports/lists/{list.Key}?filter={Uri.EscapeDataString($"{column.Key} eq '{id}'")}&");
                }
            }
        }
        var shapes = ReportEndpoints.Formats.SelectMany(format => Erp.Kernel.Localization.Languages.All.Select(language => (Format: format, Language: language))).ToList();
        var work = paths.SelectMany(path => shapes.Select(shape => $"{path}format={shape.Format}&language={shape.Language}")).ToList();
        var results = new (int Attempts, List<string> Observed, string? Failure)[work.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, work.Count), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (i, ct) =>
        {
            var uri = work[i];
            var seen = new List<string>();
            var tries = 0;
            try
            {
                if (context.Victim is { } victim)
                {
                    // Tenant B first, on its own records (its ids are its own): its answer is not observed.
                    using var own = await victim.GetAsync(uri, ct);
                    await own.Content.ReadAsByteArrayAsync(ct);
                    tries++;
                }
                foreach (var withHeaders in new[] { false, true })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    if (withHeaders)
                    {
                        request.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
                        request.Headers.TryAddWithoutValidation("X-Company-Id", ids[i % ids.Count].ToString());
                    }
                    using var response = await context.Attacker.SendAsync(request, ct);
                    tries++;
                    var body = await response.Content.ReadAsByteArrayAsync(ct);
                    seen.Add(IsolationProbeResult.Body(response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", body));
                    seen.Add(string.Join("\n", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")));
                }
                results[i] = (tries, seen, null);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // A request that fails or times out is reported to the gate with what was asked; the
                // answers already seen are still judged, and the other requests still run.
                results[i] = (tries, seen, $"GET {uri} after {tries} of 3 requests: {e.GetType().Name}: {e.Message}");
            }
        });
        var failures = new List<string>();
        foreach (var (tries, seen, failure) in results)
        {
            attempts += tries;
            observed.AddRange(seen);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }
        return new IsolationProbeResult(attempts, observed) { Failures = failures };
    }
}
