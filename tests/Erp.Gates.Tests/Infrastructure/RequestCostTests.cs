using System.Diagnostics;
using System.Net.Http.Headers;
using Erp.Testing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// The cost of one request, measured the way the gates pay it: the statements a signed-in request
/// sends to PostgreSQL (counted from the data source's own trace, as the gates count them) and the
/// processor time the process spends per request (client, in-process host and driver together).
/// The statement count has a ceiling: a request may get cheaper, never dearer, so a new module
/// that adds a lookup to every request is caught here and not by the processor-time maximum of a
/// verify two hours later. The numbers are written to <c>ERP_REQUEST_COST_FILE</c> when it is set.
/// </summary>
public sealed class RequestCostTests(GateFixture fixture)
{
    /// <summary>Most statements a signed-in GET of a list may send (session lookup, tenant
    /// settings, the user's grants with the workspace status, the company scope with the
    /// workplace, the list itself, and the audit context): lowered whenever a request gets cheaper.</summary>
    internal const int MaxStatementsPerSignedInGet = 12;

    private static readonly string[] Paths = ["/api/identity/roles", "/api/auth/session", "/api/tenancy/companies"];

    [Fact]
    public async Task A_signed_in_request_sends_no_more_statements_than_its_ceiling_and_its_cost_is_recorded()
    {
        var env = fixture.Env;
        using var client = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var report = new List<string>();
        foreach (var path in Paths)
        {
            // Warm up: JIT, EF model, plans.
            for (var i = 0; i < 30; i++)
            {
                using var warm = await client.GetAsync(path);
            }
            var statements = new List<string>();
            var count = 0;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (StatementCapture.Of(activity) is { } captured)
                    {
                        lock (statements)
                        {
                            count += captured.Commands.Count;
                            statements.AddRange(captured.Commands.Select(c => c.Text));
                        }
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);
            using (var one = await client.GetAsync(path))
            {
                Assert.True(one.IsSuccessStatusCode || path.Contains("companies"), path + " " + (int)one.StatusCode + " " + string.Join(" | ", env.Factory.ErrorLog.Entries));
            }
            int perRequest;
            lock (statements)
            {
                perRequest = count;
            }
            var repeats = int.TryParse(Environment.GetEnvironmentVariable("ERP_REQUEST_COST_REPEATS"), out var r) ? r : 400;
            var process = Process.GetCurrentProcess();
            var before = process.TotalProcessorTime;
            for (var i = 0; i < repeats; i++)
            {
                using var response = await client.GetAsync(path);
            }
            process.Refresh();
            var microseconds = (process.TotalProcessorTime - before).TotalMilliseconds * 1000 / repeats;
            report.Add($"{path}\tstatements={perRequest}\tcpu_us_per_request={microseconds:F0}");
            foreach (var text in statements.Take(perRequest))
            {
                report.Add("\t" + string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Truncate(150));
            }
            Assert.True(perRequest <= MaxStatementsPerSignedInGet, $"{path} sent {perRequest} statements (ceiling {MaxStatementsPerSignedInGet}):\n{string.Join('\n', report)}");
        }
        if (Environment.GetEnvironmentVariable("ERP_REQUEST_COST_FILE") is { Length: > 0 } file)
        {
            File.WriteAllLines(file, report);
        }
    }
}

internal static class RequestCostText
{
    public static string Truncate(this string text, int length) => text.Length <= length ? text : text[..length];
}
