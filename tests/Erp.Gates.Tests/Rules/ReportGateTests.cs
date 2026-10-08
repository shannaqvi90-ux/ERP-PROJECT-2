using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Modules.Reports.Pdf;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// The report framework holds for every registration, current and future: every registered list
/// can be printed (a module registers a row reader with it, or the reason it cannot is reviewed in
/// tests/Gates/unprintable-lists.txt); every report names a permission of the catalogue and looks
/// its references up in registered lists; and every report and printable list actually renders,
/// in every format and both languages, for an administrator (a PDF that is a PDF, a workbook that
/// is a ZIP, a document whose direction follows its language), and every character a document
/// prints has a glyph in the embedded fonts (critic p06 round 2: sort arrows printed as boxes).
/// </summary>
public sealed class ReportGateTests(GateFixture fixture)
{
    private const string UnprintablePath = "tests/Gates/unprintable-lists.txt";

    [Fact]
    public void Every_list_is_printable_and_every_report_is_well_formed()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var reviewed = Repo.ReadReviewedList(UnprintablePath);
        var problems = new List<string>();
        problems.AddRange(reviewed.Where(e => e.Reason.Length == 0).Select(e => $"{e.Entry}: needs a reason after '#'"));
        var printable = catalog.PrintableLists.Select(p => p.List.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var list in catalog.Lists.Where(l => !printable.Contains(l.Key) && reviewed.All(r => r.Entry != l.Key)))
        {
            problems.Add($"list {list.Key} cannot be printed: register a row reader (ModuleBuilder.ListRows) or review why not in {UnprintablePath}");
        }
        problems.AddRange(reviewed.Where(r => printable.Contains(r.Entry) || catalog.FindList(r.Entry) is null)
            .Select(r => $"{UnprintablePath}: {r.Entry} is printable or no longer a list; remove it"));
        foreach (var report in catalog.Reports.Select(r => r.Definition))
        {
            if (!catalog.IsPermission(report.Permission))
            {
                problems.Add($"report {report.Key}: permission {report.Permission} is not in the catalogue");
            }
            foreach (var parameter in report.Parameters.Where(p => p.Lookup is not null))
            {
                if (catalog.FindList(parameter.Lookup!) is null)
                {
                    problems.Add($"report {report.Key}: parameter {parameter.Key} looks up {parameter.Lookup}, which is not a registered list");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(catalog.Reports.Count() >= Ratchet.Min("rules.reportsChecked"), $"rules.reportsChecked: {catalog.Reports.Count()}");
        Assert.True(printable.Count >= Ratchet.Min("rules.printableListsChecked"), $"rules.printableListsChecked: {printable.Count}");
    }

    [Fact]
    public async Task Every_report_and_printable_list_renders_in_every_format_and_both_languages()
    {
        var env = fixture.Env;
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var problems = new List<string>();
        var paths = new List<string>();
        foreach (var report in catalog.Reports.Select(r => r.Definition))
        {
            var query = new List<string>();
            foreach (var parameter in report.Parameters.Where(p => p.Required))
            {
                query.Add(parameter.Type switch
                {
                    ReportParameterType.Reference => $"{parameter.Key}={await FirstIdAsync(admin, catalog.FindList(parameter.Lookup!)!.Endpoint)}",
                    ReportParameterType.Choice => $"{parameter.Key}={parameter.Choices![0].Value}",
                    ReportParameterType.Date => $"{parameter.Key}=2026-01-01",
                    ReportParameterType.Boolean => $"{parameter.Key}=true",
                    _ => $"{parameter.Key}=a",
                });
            }
            paths.Add($"/api/reports/run/{report.Key}?{string.Join("&", query)}");
        }
        paths.AddRange(catalog.PrintableLists.Select(p => $"/api/reports/lists/{p.List.Key}?"));
        // Sorted, so the document prints its order too (one column descending, one ascending).
        paths.AddRange(catalog.PrintableLists
            .Select(p => (p.List.Key, Sortable: p.List.Columns.Where(c => c.Sortable).Select(c => c.Key).Take(2).ToList()))
            .Where(p => p.Sortable.Count > 0)
            .Select(p => $"/api/reports/lists/{p.Key}?sort={string.Join(",", p.Sortable.Select((k, i) => i == 0 ? "-" + k : k))}"));
        var rendered = 0;
        var charactersChecked = 0;
        using var fonts = new PdfFonts();
        foreach (var path in paths)
        {
            foreach (var language in new[] { "en", "ar" })
            {
                foreach (var format in new[] { "json", "pdf", "csv", "xlsx" })
                {
                    using var response = await admin.GetAsync($"{path}&format={format}&language={language}");
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var where = $"{path} {format} {language}";
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        problems.Add($"{where}: {(int)response.StatusCode} {System.Text.Encoding.UTF8.GetString(bytes)[..Math.Min(200, bytes.Length)]}");
                        continue;
                    }
                    rendered++;
                    switch (format)
                    {
                        case "pdf" when !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8):
                        case "xlsx" when bytes[0] != 'P' || bytes[1] != 'K':
                            problems.Add($"{where}: not a {format} file");
                            break;
                        case "json":
                            var document = JsonDocument.Parse(bytes).RootElement;
                            if (document.GetProperty("direction").GetString() != (language == "ar" ? "rtl" : "ltr") || document.GetProperty("language").GetString() != language)
                            {
                                problems.Add($"{where}: the document's language or direction does not follow the request");
                            }
                            foreach (var missing in Unprintable(document, fonts, ref charactersChecked))
                            {
                                problems.Add($"{where}: '{missing.Text}' prints U+{missing.Codepoint:X4} '{char.ConvertFromUtf32(missing.Codepoint)}', which no embedded font has (a box on paper)");
                            }
                            break;
                    }
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(rendered >= Ratchet.Min("rules.reportRenders"), $"rules.reportRenders: {rendered}");
        Assert.True(charactersChecked >= Ratchet.Min("rules.reportGlyphsChecked"), $"rules.reportGlyphsChecked: {charactersChecked}");
        TestContext.Current.TestOutputHelper?.WriteLine($"rules.reportRenders: {rendered}, rules.reportGlyphsChecked: {charactersChecked}");
    }

    /// <summary>Every character of every text the document carries (titles, parameters, column
    /// titles, cells, totals, fixed texts) that the PDF would draw, and no face of the embedded
    /// fonts has. Whitespace and invisible format characters (direction marks) are not drawn.</summary>
    private static IEnumerable<(string Text, int Codepoint)> Unprintable(JsonElement element, PdfFonts fonts, ref int checkedCount)
    {
        var missing = new List<(string, int)>();
        var count = 0;
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in e.EnumerateObject())
                    {
                        Walk(property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray())
                    {
                        Walk(item);
                    }
                    break;
                case JsonValueKind.String:
                    var text = e.GetString()!;
                    for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
                    {
                        var codepoint = char.ConvertToUtf32(text, i);
                        var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codepoint);
                        if (category is System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Control || char.IsWhiteSpace(text, i))
                        {
                            continue;
                        }
                        count++;
                        if (fonts.For(codepoint, bold: false, null) is null || fonts.For(codepoint, bold: true, null) is null)
                        {
                            missing.Add((text, codepoint));
                        }
                    }
                    break;
            }
        }
        Walk(element);
        checkedCount += count;
        return missing.DistinctBy(m => m.Item2);
    }

    private static async Task<string> FirstIdAsync(HttpClient client, string endpoint)
    {
        var page = await client.GetFromJsonAsync<JsonElement>($"{endpoint}?take=1");
        return page.GetProperty("items")[0].GetProperty("id").GetString()!;
    }
}
