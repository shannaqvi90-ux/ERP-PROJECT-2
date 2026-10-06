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
/// prints with tenant B's ids and values in its search and filter, each in every format and both
/// languages, with tenant switch headers. Every answer goes back to the gate whole; the gate reads
/// PDFs and workbooks as text and looks for tenant B's ids and canaries.
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
        foreach (var path in paths)
        {
            foreach (var (format, language) in new[] { ("json", "en"), ("pdf", "ar"), ("pdf", "en"), ("csv", "en"), ("xlsx", "ar") })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}format={format}&language={language}");
                if (attempts % 2 == 1)
                {
                    request.Headers.TryAddWithoutValidation("X-Tenant-Id", context.VictimTenantId.ToString());
                    request.Headers.TryAddWithoutValidation("X-Company-Id", ids[attempts % ids.Count].ToString());
                }
                using var response = await context.Attacker.SendAsync(request, cancellationToken);
                attempts++;
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                observed.Add(IsolationProbeResult.Body(response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", body));
                observed.Add(string.Join("\n", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")));
            }
        }
        return new IsolationProbeResult(attempts, observed);
    }
}
