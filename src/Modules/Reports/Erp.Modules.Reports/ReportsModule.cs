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
        module.Services.AddSingleton(services => ReportLimits.From(services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()));
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
/// <para>
/// Combined (critic p06 round 5, plant L7b: a record PDF kept for the inline viewer by the record and
/// the disposition, with no tenant; it passed every gate because tenant B's record ids never went
/// with a non-default print option): tenant B finds its own records for every reference parameter
/// and filterable reference column (through the parameter's lookup list) and prints every report
/// and list with them and every print option together — format, language, digits, disposition,
/// time zone, grouping, the report's own choices, flags and dates, a list's columns, sort and
/// search — in a covering set where every value of every option meets every value of any two other
/// option and every value of a third at least once (<see cref="Covering"/>). Tenant A then sends each of those requests exactly
/// as tenant B sent them, tenant B's ids included, with and without tenant switch headers. Anything
/// a store keyed on any part of the request without the tenant holds for them is tenant B's.
/// </para>
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
        if (context.Victim is { } owner)
        {
            work.AddRange(await CombinedAsync(owner, texts, cancellationToken));
        }
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

    /// <summary>Every combination of the values of any three print options is sent: a record kept by
    /// three of them together (L7b: the record, the PDF format and the inline disposition) is filled
    /// by tenant B and then asked for by tenant A.</summary>
    internal const int CombinedStrength = 3;

    /// <summary>Most of tenant B's own records tried per reference parameter or column.</summary>
    private const int RecordsPerParameter = 3;

    /// <summary>
    /// Tenant B's own requests with every print option together: per report and printable list, the
    /// rows of a covering set of strength three over its options, each option's values being tenant B's own
    /// record ids (a required reference has no "absent"), the documented choices and the values other
    /// than the default. Every request names its format and language.
    /// </summary>
    private async Task<IReadOnlyList<string>> CombinedAsync(HttpClient victim, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var owned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        async Task<IReadOnlyList<string>> OwnIdsAsync(string? listKey)
        {
            if (listKey is null || catalog.FindList(listKey) is not { } list)
            {
                return [];
            }
            if (!owned.TryGetValue(listKey, out var found))
            {
                using var response = await victim.GetAsync($"{list.Endpoint}?take={RecordsPerParameter}", cancellationToken);
                found = [];
                if (response.IsSuccessStatusCode)
                {
                    using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (json.RootElement.TryGetProperty("items", out var items))
                    {
                        found = items.EnumerateArray().Where(i => i.TryGetProperty("id", out _)).Select(i => i.GetProperty("id").ToString()).Take(RecordsPerParameter).ToList();
                    }
                }
                owned[listKey] = found;
            }
            return found;
        }
        static IReadOnlyList<string?> Optional(IEnumerable<string> values) => [null, .. values];
        IEnumerable<(string Name, IReadOnlyList<string?> Values)> Options(IEnumerable<string> groupable) =>
        [
            ("format", [.. ReportEndpoints.Formats]),
            ("language", [.. Erp.Kernel.Localization.Languages.All]),
            ("numerals", [.. Erp.Kernel.Localization.NumeralSystems.All]),
            ("disposition", ["attachment", "inline"]),
            ("timeZone", [null, "UTC"]),
            ("groupBy", Optional(groupable)),
        ];
        var requests = new List<string>();
        foreach (var report in catalog.Reports.Select(r => r.Definition))
        {
            var options = Options(report.Columns.Where(c => c.Groupable).Select(c => c.Key)).ToList();
            var blind = false;
            foreach (var parameter in report.Parameters)
            {
                IReadOnlyList<string?> values = parameter.Type switch
                {
                    Erp.Kernel.Reports.ReportParameterType.Reference => parameter.Required
                        ? [.. await OwnIdsAsync(parameter.Lookup)]
                        : Optional(await OwnIdsAsync(parameter.Lookup)),
                    Erp.Kernel.Reports.ReportParameterType.Choice => Optional((parameter.Choices ?? []).Select(c => c.Value)),
                    Erp.Kernel.Reports.ReportParameterType.Boolean => [null, "true", "false"],
                    Erp.Kernel.Reports.ReportParameterType.Date => [null, DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "2000-01-01"],
                    _ => Optional(texts.Take(2)),
                };
                if (values.Count == 0)
                {
                    // A required record tenant B has none of: nothing of B's to print.
                    blind = true;
                    break;
                }
                options.Add((parameter.Key, values));
            }
            if (!blind)
            {
                requests.AddRange(Rows(options).Select(row => $"/api/reports/run/{report.Key}?{row}"));
            }
        }
        foreach (var (list, _) in catalog.PrintableLists)
        {
            var visible = list.Columns.Where(c => !c.Hidden).Select(c => c.Key).ToList();
            var (sort, filter, _) = ReportEndpoints.ViewExampleFor(list);
            var options = Options(list.Columns.Where(c => c.Groupable).Select(c => c.Key)).ToList();
            options.Add(("columns", [null, string.Join(",", list.Columns.Take(30).Select(c => c.Key)), visible.FirstOrDefault()]));
            options.Add(("sort", sort is null ? [null] : [null, sort, sort.StartsWith('-') ? sort[1..] : "-" + sort]));
            options.Add(("search", Optional(texts.Take(2).Select(t => t.Split(' ')[0]))));
            var filters = new List<string?> { null };
            if (filter is not null)
            {
                filters.Add(filter);
            }
            foreach (var column in list.Columns.Where(c => c.Filterable && c.Type == Erp.Kernel.Lists.ListColumnType.Reference))
            {
                filters.AddRange((await OwnIdsAsync(column.ValuesFrom)).Select(id => $"{column.Key} eq '{id}'"));
            }
            options.Add(("filter", filters));
            requests.AddRange(Rows(options).Select(row => $"/api/reports/lists/{list.Key}?{row}"));
        }
        return requests.Distinct(StringComparer.Ordinal).ToList();

        static IEnumerable<string> Rows(IReadOnlyList<(string Name, IReadOnlyList<string?> Values)> options) =>
            Covering(options.Select(o => o.Values.Count).ToList(), CombinedStrength).Select(row => string.Join("&", row
                .Select((value, i) => (options[i].Name, Value: options[i].Values[value]))
                .Where(o => o.Value is not null)
                .Select(o => $"{Uri.EscapeDataString(o.Name)}={Uri.EscapeDataString(o.Value!)}")));
    }

    /// <summary>
    /// Rows of value indexes, one per option, in which every combination of the values of any
    /// <paramref name="strength"/> options occurs at least once (a covering set). Built greedily and
    /// deterministically: each row starts from the first combination not yet covered, and every
    /// other option, in order, takes the value that completes the most combinations not yet covered
    /// with the options already set.
    /// </summary>
    internal static IReadOnlyList<int[]> Covering(IReadOnlyList<int> sizes, int strength)
    {
        var n = sizes.Count;
        var t = Math.Min(strength, n);
        var groups = Subsets(n, t).ToList();
        string Key(int[] columns, IReadOnlyList<int> row) => string.Join(",", columns.Select(c => $"{c}={row[c]}"));
        var uncovered = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var columns in groups)
        {
            var count = columns.Aggregate(1, (product, c) => product * sizes[c]);
            for (var k = 0; k < count; k++)
            {
                var row = new int[n];
                var rest = k;
                foreach (var c in columns.Reverse())
                {
                    row[c] = rest % sizes[c];
                    rest /= sizes[c];
                }
                uncovered.Add(Key(columns, row));
            }
        }
        var rows = new List<int[]>();
        while (uncovered.Count > 0)
        {
            var row = Enumerable.Repeat(-1, n).ToArray();
            foreach (var part in uncovered.Min!.Split(','))
            {
                var pair = part.Split('=');
                row[int.Parse(pair[0], System.Globalization.CultureInfo.InvariantCulture)] = int.Parse(pair[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            for (var k = 0; k < n; k++)
            {
                if (row[k] >= 0)
                {
                    continue;
                }
                var best = 0;
                var gain = -1;
                var touching = groups.Where(g => g.Contains(k) && g.All(c => c == k || row[c] >= 0)).ToList();
                for (var v = 0; v < sizes[k]; v++)
                {
                    row[k] = v;
                    var covers = touching.Count(g => uncovered.Contains(Key(g, row)));
                    if (covers > gain)
                    {
                        (best, gain) = (v, covers);
                    }
                }
                row[k] = best;
            }
            foreach (var columns in groups)
            {
                uncovered.Remove(Key(columns, row));
            }
            rows.Add(row);
        }
        return rows;
    }

    private static IEnumerable<int[]> Subsets(int n, int k)
    {
        if (k == 0)
        {
            yield break;
        }
        var indexes = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])indexes.Clone();
            var i = k - 1;
            while (i >= 0 && indexes[i] == n - k + i) i--;
            if (i < 0) yield break;
            indexes[i]++;
            for (var j = i + 1; j < k; j++) indexes[j] = indexes[j - 1] + 1;
        }
    }
}
