using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, the file path of printing (critic p06 round 8: a PDF now holds the whole selection, written
/// page by page into a scratch file instead of memory). A scratch file that outlived its request
/// would hold one tenant's document on a disk every request shares, under a name another request
/// could reach. Signed in to tenant A and to tenant B, every printable list and every report the
/// catalogue offers prints as a PDF in both languages, both workspaces at once; afterwards no print
/// file may be left in the scratch folder, and none may be found there while the prints run that is
/// still there a moment later (the product unlinks its file as soon as it opens it).
/// </summary>
public sealed class G1PrintFileTests(GateFixture fixture)
{
    /// <summary>The name every scratch print file starts with (<c>ReportEndpoints.PdfFile</c>).</summary>
    private const string Prefix = "erp-print-";

    [Fact]
    public async Task A_print_leaves_no_file_behind_that_another_request_could_reach()
    {
        var env = fixture.Env;
        var folder = Path.GetTempPath();
        var before = Directory.GetFiles(folder, Prefix + "*").ToHashSet(StringComparer.Ordinal);
        using var a = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var b = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var prints = 0;
        using var stop = new CancellationTokenSource();
        // While the prints run, every print file in the folder is noted.
        var watch = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var file in Directory.GetFiles(folder, Prefix + "*").Where(f => !before.Contains(f)))
                {
                    lock (seen)
                    {
                        seen.Add(file);
                    }
                }
                await Task.Delay(5);
            }
        });
        foreach (var client in new[] { a, b })
        {
            var catalog = await client.GetFromJsonAsync<JsonElement>("/api/reports/catalog", TestContext.Current.CancellationToken);
            var company = (await client.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=1", TestContext.Current.CancellationToken))
                .GetProperty("items")[0].GetProperty("id").GetString();
            var paths = catalog.GetProperty("lists").EnumerateArray().Select(l => l.GetProperty("path").GetString() + "?")
                .Concat(catalog.GetProperty("items").EnumerateArray().Select(r =>
                    r.GetProperty("path").GetString() + "?" + string.Concat(r.GetProperty("parameters").EnumerateArray()
                        .Where(p => p.GetProperty("required").GetBoolean())
                        .Select(p => $"{p.GetProperty("key").GetString()}={company}&"))))
                .ToList();
            Assert.True(paths.Count >= 9, $"{paths.Count} printable paths");
            foreach (var path in paths)
            {
                foreach (var language in new[] { "en", "ar" })
                {
                    using var response = await client.GetAsync($"{path}format=pdf&language={language}", TestContext.Current.CancellationToken);
                    var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
                    prints++;
                    if (response.StatusCode != HttpStatusCode.OK || bytes.Length < 5 || bytes[0] != (byte)'%')
                    {
                        problems.Add($"{path}format=pdf&language={language}: {(int)response.StatusCode}, {bytes.Length} bytes");
                    }
                }
            }
        }
        await stop.CancelAsync();
        await watch;
        // A file seen while a print ran is the print's own for an instant at most: one that is still
        // there now, or one left in the folder after every print, outlived its request.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var left = Directory.GetFiles(folder, Prefix + "*").Where(f => !before.Contains(f)).ToList();
        problems.AddRange(left.Select(f => $"{f}: a print file outlived its request ({new FileInfo(f).Length} bytes)"));
        problems.AddRange(seen.Where(File.Exists).Except(left).Select(f => $"{f}: a print file was still in the folder after its print"));
        TestContext.Current.TestOutputHelper?.WriteLine($"{prints} PDFs printed by two workspaces; {seen.Count} print files glimpsed while they ran; {left.Count} left");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(prints >= Ratchet.Min("g1.printFilesChecked"), $"g1.printFilesChecked: {prints}; ratchet minimum {Ratchet.Min("g1.printFilesChecked")}");
    }
}
