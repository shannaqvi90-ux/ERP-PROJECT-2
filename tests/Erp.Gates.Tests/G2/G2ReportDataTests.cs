using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2 for reports (critic p06 round 1, plants P1 and P2). A report is guarded by the permission
/// its definition names, and the G2 gate used to check only that the route declares that very
/// permission, so a report that prints companies' licences and tax numbers under the workplace
/// switcher's permission passed. Here the permission is judged by what the report prints: signed in
/// as a user holding exactly the report's permission (and again with each other read permission
/// added), every report runs in English and Arabic for real records, and every value of the
/// workspace's own data the document prints must also be shown by some endpoint those same
/// permissions open (the record's own screen, its list). A value no other endpoint shows them is
/// data the report's permission does not grant. The catalogue must list exactly the reports and
/// lists the caller may run, with only the columns and parameters it may see.
/// </summary>
/// <remarks>Critic p06 round 3 (plants P6 and P8): every format and every printable list too; see
/// <see cref="ReportDataCheck.RunAsync"/>.</remarks>
public sealed class G2ReportDataTests(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    [Fact]
    public async Task Every_report_prints_only_data_its_permissions_show_elsewhere()
    {
        var result = await ReportDataCheck.RunAsync(fixture.Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Reports} reports, {result.PermissionSets} permission sets, {result.Lists} printable lists, " +
                                                        $"{result.ListPermissionSets} list permission sets, {result.Runs} runs, {result.FormatRuns} files, " +
                                                        $"{result.ValuesJudged} printed values judged in JSON, {result.FormatValuesJudged} in files");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems.Take(60)));
        Assert.True(result.Reports >= Ratchet.Min("rules.reportsChecked"), $"{result.Reports} reports judged by what they print");
        Assert.True(result.ValuesJudged >= Ratchet.Min("g2.reportValuesJudged"), $"g2.reportValuesJudged: {result.ValuesJudged}; ratchet minimum {Ratchet.Min("g2.reportValuesJudged")}");
        Assert.True(result.PermissionSets >= Ratchet.Min("g2.reportPermissionSets"), $"g2.reportPermissionSets: {result.PermissionSets}; ratchet minimum {Ratchet.Min("g2.reportPermissionSets")}");
        Assert.True(result.Lists >= Ratchet.Min("rules.printableListsChecked"), $"{result.Lists} printable lists judged by what they print");
        Assert.True(result.ListPermissionSets >= Ratchet.Min("g2.printedListPermissionSets"), $"g2.printedListPermissionSets: {result.ListPermissionSets}; ratchet minimum {Ratchet.Min("g2.printedListPermissionSets")}");
        Assert.True(result.FormatRuns >= Ratchet.Min("g2.reportFilesJudged"), $"g2.reportFilesJudged: {result.FormatRuns}; ratchet minimum {Ratchet.Min("g2.reportFilesJudged")}");
        Assert.True(result.FormatValuesJudged >= Ratchet.Min("g2.reportFileValuesJudged"), $"g2.reportFileValuesJudged: {result.FormatValuesJudged}; ratchet minimum {Ratchet.Min("g2.reportFileValuesJudged")}");
    }

    [Fact]
    public async Task The_report_catalogue_lists_only_what_the_caller_may_run_and_see()
    {
        var problems = await ReportDataCheck.CatalogueProblemsAsync(fixture.Env);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}

/// <summary>The report data check, reusable so the gate's self-tests can prove it catches a report
/// planted under a permission that does not grant what it prints.</summary>
public static class ReportDataCheck
{
    /// <param name="FormatRuns">Answers judged in a format other than JSON (CSV, XLSX, PDF).</param>
    /// <param name="FormatValuesJudged">Values of the workspace's data found in those answers and judged.</param>
    public sealed record Result(IReadOnlyList<string> Problems, int Reports, int PermissionSets, int Runs, int ValuesJudged,
        int Lists = 0, int ListPermissionSets = 0, int FormatRuns = 0, int FormatValuesJudged = 0);

    private const int MaxPages = 40;

    /// <summary>Every format a report or a printed list is served in: the document as JSON first
    /// (the screen's), then the files that leave the system.</summary>
    private static readonly string[] Formats = ["json", "csv", "xlsx", "pdf"];

    /// <summary>Critic p06 round 3 (plants P6 and P8): the check used to judge only the JSON
    /// document of each report, and no printed list at all, so a CSV or XLSX export that put back
    /// the columns a caller's roles withhold, and a list printout naming another area's records
    /// without checking the caller may read them, passed. Now every report and every printable list
    /// runs in every format (JSON, CSV, XLSX, PDF read for its text) and both languages, as users
    /// holding exactly its permission and again with each other read permission added; and:
    /// <list type="bullet">
    /// <item>an export's column titles are exactly the JSON document's (no withheld column comes back
    /// in a file), every value of the workspace's data a file prints is in the JSON document too, and
    /// a file answers as the JSON does (a refused parameter is refused in every format);</item>
    /// <item>a report's document leaves out every column whose permission the caller lacks;</item>
    /// <item>a printed list prints only what the list itself shows that caller (its endpoint's rows),
    /// plus the names of records of another list the caller may read (ListColumn.ValuesFrom); a caller
    /// who cannot read that list gets a count, never names;</item>
    /// <item>every value a report prints must still be shown by an endpoint those permissions open.</item>
    /// </list></summary>
    public static async Task<Result> RunAsync(ErpTestEnvironment env, string? onlyReport = null, string? onlyList = null)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = EndpointInventory.From(env.Factory.Services);
        var tenant = env.TenantA;
        using var admin = await env.SignInAsync(env.Email(tenant, "admin"));
        var reads = catalog.PermissionKeys.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var reports = onlyList is null ? catalog.Reports.Select(r => r.Definition).Where(d => onlyReport is null || d.Key == onlyReport).ToList() : [];
        var lists = onlyReport is null ? catalog.PrintableLists.Select(p => p.List).Where(l => onlyList is null || l.Key == onlyList).ToList() : [];

        // Every permission set judged: the report's (or list's) own alone, then with each other read permission.
        var sets = new List<(ReportDefinition Report, IReadOnlyList<string> Permissions)>();
        foreach (var report in reports)
        {
            sets.Add((report, [report.Permission]));
            foreach (var other in reads.Where(r => r != report.Permission))
            {
                sets.Add((report, [report.Permission, other]));
            }
        }
        var listSets = new List<(ListDefinition List, IReadOnlyList<string> Permissions)>();
        foreach (var list in lists)
        {
            listSets.Add((list, [list.Permission]));
            foreach (var other in reads.Where(r => r != list.Permission))
            {
                listSets.Add((list, [list.Permission, other]));
            }
        }
        // Users first (their rows are workspace data too), then the data, then what each shows.
        var users = new Dictionary<string, HttpClient>(StringComparer.Ordinal);
        try
        {
            foreach (var permissions in sets.Select(s => s.Permissions).Concat(listSets.Select(s => s.Permissions))
                         .Concat(reads.Select(r => (IReadOnlyList<string>)[r])).DistinctBy(Key))
            {
                users[Key(permissions)] = await UserWithAsync(env, admin, permissions, "g2rd");
            }

            var data = await WorkspaceTextAsync(env, tenant.Id);
            var labels = ResourceStrings();
            // The product's own words that contain a value of the data ("Inactive" holds "active"):
            // an occurrence of the value inside one of them is that word, not the data printed.
            var embedding = data.Select(v => (Value: v, Words: labels.Where(l => l.Length > v.Length && l.Contains(v, StringComparison.Ordinal)).ToList()))
                .Where(x => x.Words.Count > 0).ToDictionary(x => x.Value, x => x.Words, StringComparer.Ordinal);
            bool Prints(string text, string value)
            {
                var words = embedding.GetValueOrDefault(value);
                for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
                {
                    if (words is null || !words.Any(w => Inside(text, i, value, w)))
                    {
                        return true;
                    }
                }
                return false;
            }
            var shown = new Dictionary<string, string>(StringComparer.Ordinal);
            async Task<string> ShownByAsync(string permission)
            {
                if (!shown.TryGetValue(permission, out var text))
                {
                    text = await CorpusAsync(users[Key([permission])], endpoints.Where(e => e.Method == "GET" && !e.IsAnonymous && e.Permission == permission));
                    shown[permission] = text;
                }
                return text;
            }
            var anonymous = await CorpusAsync(users[Key([reads[0]])], endpoints.Where(e => e.Method == "GET" && e.IsAnonymous));

            var problems = new List<string>();
            var printedBy = new HashSet<string>(StringComparer.Ordinal);
            var runs = 0;
            var formatRuns = 0;
            var judged = 0;
            var formatJudged = 0;

            // Judges one printed answer's values: each must be shown by the corpus, and (a file) be in
            // the JSON document of the same request.
            void JudgeValues(string key, string what, string format, string printed, string? json, Func<string, bool> isShown, string because)
            {
                foreach (var value in data)
                {
                    if (!printed.Contains(value, StringComparison.Ordinal) || labels.Contains(value) || !Prints(printed, value))
                    {
                        continue;
                    }
                    if (format == "json")
                    {
                        judged++;
                    }
                    else
                    {
                        formatJudged++;
                    }
                    printedBy.Add(key);
                    if (!isShown(value))
                    {
                        problems.Add($"{what}: the {format.ToUpperInvariant()} prints '{value}', {because}");
                    }
                    if (json is not null && !json.Contains(value, StringComparison.Ordinal))
                    {
                        problems.Add($"{what}: the {format.ToUpperInvariant()} prints '{value}', which the JSON document of the same request does not (a file prints what the document shows, no more)");
                    }
                }
            }

            const string elsewhere = "which no other endpoint those permissions open shows";
            foreach (var (report, permissions) in sets)
            {
                var client = users[Key(permissions)];
                var corpus = string.Join("\n", await Task.WhenAll(permissions.Select(ShownByAsync))) + "\n" + anonymous;
                var withheld = report.Columns.Where(c => c.Permission is { } extra && !permissions.Contains(extra)).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var query in await QueriesAsync(admin, catalog, report))
                {
                    // A parameter that names another area's records must be refused to a caller
                    // who cannot read that area, in every format.
                    var refusedParameter = report.Parameters.FirstOrDefault(p => p.Permission is { } extra && !permissions.Contains(extra) &&
                                                                                 query.Contains(p.Key + "=", StringComparison.Ordinal));
                    foreach (var language in new[] { "en", "ar" })
                    {
                        var path = $"/api/reports/run/{report.Key}?{query}{(query.Length > 0 ? "&" : "")}language={language}";
                        var who = $"{report.Key} (permission {report.Permission}) as a user holding exactly [{string.Join(", ", permissions)}]";
                        var answers = await PrintAsync(client, path);
                        runs++;
                        formatRuns += answers.Files.Count;
                        if (refusedParameter is not null)
                        {
                            foreach (var (format, status) in answers.Statuses.Where(a => a.Value != HttpStatusCode.BadRequest))
                            {
                                problems.Add($"{report.Key} as [{string.Join(", ", permissions)}]: parameter '{refusedParameter.Key}' needs {refusedParameter.Permission} but {path}&format={format} answered {(int)status}");
                            }
                            continue;
                        }
                        if (answers.Document is not { } document)
                        {
                            problems.Add($"{who}: {path}&format=json answered {(int)answers.Statuses["json"]}, so what it prints could not be judged");
                            continue;
                        }
                        foreach (var key in document.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).Where(withheld.Contains))
                        {
                            problems.Add($"{who}: {path} prints column '{key}', which needs {report.Columns.Single(c => c.Key == key).Permission}");
                        }
                        JudgeValues(report.Key, $"{who} {path}", "json", PrintedText(document), null, corpus.Contains, elsewhere);
                        problems.AddRange(FileProblems(who, path, answers));
                        foreach (var (format, file) in answers.Files)
                        {
                            JudgeValues(report.Key, $"{who} {path}&format={format}", format, file.Text, answers.WholeJson(format), corpus.Contains, elsewhere);
                        }
                    }
                }
            }

            // Printed lists: what the list itself shows the caller, and names of records of the lists
            // the caller may read.
            var listCorpora = new Dictionary<string, string>(StringComparer.Ordinal);
            async Task<string> ListCorpusAsync(HttpClient client, IReadOnlyList<string> permissions, ListDefinition list)
            {
                var cacheKey = list.Key + "|" + Key(permissions);
                if (!listCorpora.TryGetValue(cacheKey, out var text))
                {
                    var parts = new List<string> { await RowsShownAsync(client, list.Endpoint) };
                    foreach (var column in list.Columns.Where(c => c.ValuesFrom is not null))
                    {
                        var source = catalog.FindList(column.ValuesFrom!);
                        if (source is not null && permissions.Contains(source.Permission))
                        {
                            parts.Add(await RowsShownAsync(client, source.Endpoint));
                        }
                    }
                    text = string.Join("\n", parts);
                    listCorpora[cacheKey] = text;
                }
                return text;
            }
            const string unlisted = "which the list does not show that caller (its endpoint's rows, and the lists it may read)";
            foreach (var (list, permissions) in listSets)
            {
                var client = users[Key(permissions)];
                var corpus = string.Join("\n", await Task.WhenAll(permissions.Select(ShownByAsync))) + "\n" + anonymous;
                var rowsShown = await ListCorpusAsync(client, permissions, list);
                var unnamed = list.Columns.Where(c => c.ValuesFrom is { } from && catalog.FindList(from) is { } source && !permissions.Contains(source.Permission))
                    .Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
                // The list's visible columns, then every column it has (hidden ones too).
                foreach (var query in new[] { "", "columns=" + string.Join(",", list.Columns.Take(30).Select(c => c.Key)) })
                {
                    foreach (var language in new[] { "en", "ar" })
                    {
                        var path = $"/api/reports/lists/{list.Key}?{query}{(query.Length > 0 ? "&" : "")}language={language}";
                        var who = $"{list.Key} (permission {list.Permission}) as a user holding exactly [{string.Join(", ", permissions)}]";
                        var answers = await PrintAsync(client, path);
                        runs++;
                        formatRuns += answers.Files.Count;
                        if (answers.Document is not { } document)
                        {
                            problems.Add($"{who}: {path}&format=json answered {(int)answers.Statuses["json"]}, so what it prints could not be judged");
                            continue;
                        }
                        // The letterhead is the working company's or the workspace's name, judged by
                        // the endpoints the caller's permissions open; everything else is the list's.
                        var issuer = document.GetProperty("issuer").GetString() ?? "";
                        JudgeValues(list.Key, $"{who} {path}", "json", issuer, null, corpus.Contains, elsewhere);
                        var columns = document.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToList();
                        foreach (var (key, index) in columns.Select((k, i) => (k, i)).Where(c => unnamed.Contains(c.k)))
                        {
                            foreach (var cell in document.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("rows").EnumerateArray())
                                         .Select(r => r.GetProperty("cells")[index].GetProperty("text").GetString() ?? "")
                                         .Where(t => t.Length > 0 && !t.All(c => char.IsDigit(c) || c is ',' or '٬')).Distinct().Take(3))
                            {
                                problems.Add($"{who}: {path} prints '{cell}' in column '{key}', whose values are records of {list.Column(key)!.ValuesFrom}, which the caller may not read (a count is printed instead)");
                            }
                        }
                        JudgeValues(list.Key, $"{who} {path}", "json", Without(PrintedText(document, withIssuer: false), issuer), null, rowsShown.Contains, unlisted);
                        problems.AddRange(FileProblems(who, path, answers));
                        foreach (var (format, file) in answers.Files)
                        {
                            JudgeValues(list.Key, $"{who} {path}&format={format}", format, Without(file.Text, issuer), answers.WholeJson(format), rowsShown.Contains, unlisted);
                        }
                    }
                }
            }
            foreach (var report in reports.Where(r => !printedBy.Contains(r.Key)))
            {
                problems.Add($"{report.Key}: no run printed anything of the workspace's data, so the check was blind to it");
            }
            foreach (var list in lists.Where(l => !printedBy.Contains(l.Key)))
            {
                problems.Add($"{list.Key}: no print printed anything of the workspace's data, so the check was blind to it");
            }
            return new Result(problems.Distinct().ToList(), reports.Count, sets.Count, runs, judged, lists.Count, listSets.Count, formatRuns, formatJudged);
        }
        finally
        {
            foreach (var client in users.Values)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>A file's text as a reader sees it (CSV and XLSX cells, PDF page text), with its
    /// column titles (CSV and XLSX).</summary>
    private sealed record PrintedFile(string Text, IReadOnlyList<string>? Header);

    /// <summary>One request in every format: each status, the JSON document and every string it
    /// holds, and each file.</summary>
    private sealed record Answers(IReadOnlyDictionary<string, HttpStatusCode> Statuses, JsonElement? Document, string JsonStrings,
        IReadOnlyDictionary<string, PrintedFile> Files)
    {
        /// <summary>Every string of the JSON document when it holds every row the file can, else
        /// null: an export then holds rows the document stopped before, judged by the corpus only.</summary>
        public string? WholeJson(string format) =>
            Document is { } d && (format == "pdf" || !d.GetProperty("truncated").GetBoolean()) ? JsonStrings : null;
    }

    private static async Task<Answers> PrintAsync(HttpClient client, string path)
    {
        var statuses = new Dictionary<string, HttpStatusCode>(StringComparer.Ordinal);
        JsonElement? document = null;
        var strings = "";
        var files = new Dictionary<string, PrintedFile>(StringComparer.Ordinal);
        string? printedBy = null;
        foreach (var format in Formats)
        {
            using var response = await client.GetAsync($"{path}&format={format}");
            statuses[format] = response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                continue;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (format == "json")
            {
                document = JsonDocument.Parse(bytes).RootElement.Clone();
                var all = new HashSet<string>(StringComparer.Ordinal);
                Walk(document.Value, all);
                strings = string.Join("\n", all);
                printedBy = document.Value.GetProperty("printedBy").GetString();
                continue;
            }
            var (text, header) = format switch
            {
                "csv" => CsvText(bytes),
                "xlsx" => XlsxText(bytes),
                _ => (ResponseText.PdfReadable(bytes), (IReadOnlyList<string>?)null),
            };
            foreach (var stamp in ResponseText.DeclaredStamps(response))
            {
                text = text.Replace(stamp, " ", StringComparison.Ordinal);
            }
            // Who printed it is the caller's own name, not the data printed.
            files[format] = new PrintedFile(printedBy is { Length: > 0 } ? Without(text, printedBy) : text, header);
        }
        return new Answers(statuses, document, strings, files);
    }

    /// <summary>Files that answer differently from the JSON document of the same request: another
    /// status, or (CSV and XLSX) other column titles than the document's.</summary>
    private static IEnumerable<string> FileProblems(string who, string path, Answers answers)
    {
        foreach (var (format, status) in answers.Statuses.Where(s => s.Key != "json" && s.Value != answers.Statuses["json"]))
        {
            yield return $"{who}: {path}&format={format} answered {(int)status}, the JSON document {(int)answers.Statuses["json"]}";
        }
        if (answers.Document is not { } document)
        {
            yield break;
        }
        var columns = document.GetProperty("columns").EnumerateArray().ToList();
        var groupBy = document.GetProperty("groupBy").ValueKind == JsonValueKind.String ? document.GetProperty("groupBy").GetString() : null;
        var expected = new List<string>();
        if (document.GetProperty("groupLabel").ValueKind == JsonValueKind.String && columns.All(c => c.GetProperty("key").GetString() != groupBy))
        {
            expected.Add(document.GetProperty("groupLabel").GetString()!);
        }
        expected.AddRange(columns.Select(c => c.GetProperty("label").GetString()!));
        foreach (var (format, file) in answers.Files.Where(f => f.Value.Header is not null))
        {
            if (!file.Header!.SequenceEqual(expected))
            {
                yield return $"{who}: {path}&format={format} has the columns [{string.Join(", ", file.Header!)}], the JSON document [{string.Join(", ", expected)}] " +
                             "(a file carries exactly the document's columns: none the caller's roles withhold, none twice)";
            }
        }
    }

    /// <summary>True when the occurrence of <paramref name="value"/> at <paramref name="at"/> in the
    /// text is part of an occurrence of <paramref name="word"/> there.</summary>
    private static bool Inside(string text, int at, string value, string word)
    {
        for (var k = word.IndexOf(value, StringComparison.Ordinal); k >= 0; k = word.IndexOf(value, k + 1, StringComparison.Ordinal))
        {
            var start = at - k;
            if (start >= 0 && start + word.Length <= text.Length && string.CompareOrdinal(text, start, word, 0, word.Length) == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The text with every occurrence of <paramref name="part"/> blanked.</summary>
    private static string Without(string text, string part) => part.Length == 0 ? text : text.Replace(part, " ", StringComparison.Ordinal);

    /// <summary>A CSV's cells, one per line, and its first record (the column titles).</summary>
    private static (string Text, IReadOnlyList<string>? Header) CsvText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\n')
            {
                record.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                records.Add(record);
                record = [];
            }
            else
            {
                cell.Append(c);
            }
        }
        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }
        // A cell that starts with a formula character carries an apostrophe (the CSV injection guard).
        var cells = records.SelectMany(r => r).Select(v => v.StartsWith('\'') ? v[1..] : v);
        // A document without columns writes an empty first line: no column titles.
        var header = records.Count == 0 || records[0] is [""] ? [] : records[0];
        return (string.Join("\n", cells), header);
    }

    /// <summary>An XLSX workbook's cells (text, values, formulas), one per line, and the first row's
    /// text cells (the column titles).</summary>
    private static (string Text, IReadOnlyList<string>? Header) XlsxText(byte[] bytes)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var text = new StringBuilder();
        List<string>? header = null;
        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(xml, "<(t|v|f)(?: [^>]*)?>([^<]*)</\\1>"))
            {
                text.Append(System.Net.WebUtility.HtmlDecode(match.Groups[2].Value)).Append('\n');
            }
            // The sheet's name is the document's title (the product's words), not a cell.
            if (entry.FullName == "xl/worksheets/sheet1.xml")
            {
                var first = System.Text.RegularExpressions.Regex.Match(xml, "<row r=\"1\"[^>]*>(.*?)</row>");
                header = System.Text.RegularExpressions.Regex.Matches(first.Groups[1].Value, "<t(?: [^>]*)?>([^<]*)</t>")
                    .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
            }
        }
        return (text.ToString(), header);
    }

    /// <summary>Every string and number a list endpoint answers the caller, every page.</summary>
    private static async Task<string> RowsShownAsync(HttpClient client, string endpoint)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        for (var page = 0; page < MaxPages; page++)
        {
            using var response = await client.GetAsync(endpoint + "?take=200" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after)));
            if (!response.IsSuccessStatusCode)
            {
                break;
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Walk(json, shown);
            after = json.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            if (after is null)
            {
                break;
            }
        }
        return string.Join("\n", shown);
    }

    /// <summary>The catalogue as users holding the catalogue's permission and exactly one other:
    /// it lists exactly the reports and printable lists whose permission they hold, without the
    /// columns and parameters that need a permission they lack.</summary>
    public static async Task<List<string>> CatalogueProblemsAsync(ErpTestEnvironment env)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var problems = new List<string>();
        var permissions = catalog.Reports.Select(r => r.Definition.Permission)
            .Concat(catalog.PrintableLists.Select(p => p.List.Permission))
            .Concat(catalog.PermissionKeys.Where(p => p.EndsWith(".read", StringComparison.Ordinal)))
            .Where(p => p != "reports.catalog.read").Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var permission in permissions)
        {
            var held = new[] { "reports.catalog.read", permission };
            using var client = await UserWithAsync(env, admin, held, "g2rc");
            var answer = await client.GetFromJsonAsync<JsonElement>("/api/reports/catalog?language=en");
            var items = answer.GetProperty("items").EnumerateArray().ToList();
            var listed = items.Select(i => i.GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToList();
            var expected = catalog.Reports.Where(r => held.Contains(r.Definition.Permission)).Select(r => r.Definition.Key).Order(StringComparer.Ordinal).ToList();
            if (!listed.SequenceEqual(expected))
            {
                problems.Add($"catalogue for [{string.Join(", ", held)}] lists reports [{string.Join(", ", listed)}], expected [{string.Join(", ", expected)}]");
            }
            var lists = answer.GetProperty("lists").EnumerateArray().Select(l => l.GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToList();
            var expectedLists = catalog.PrintableLists.Where(p => held.Contains(p.List.Permission)).Select(p => p.List.Key).Order(StringComparer.Ordinal).ToList();
            if (!lists.SequenceEqual(expectedLists))
            {
                problems.Add($"catalogue for [{string.Join(", ", held)}] lists printable lists [{string.Join(", ", lists)}], expected [{string.Join(", ", expectedLists)}]");
            }
            foreach (var item in items)
            {
                var definition = catalog.FindReport(item.GetProperty("key").GetString()!)!.Definition;
                var columns = item.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToList();
                foreach (var hidden in definition.Columns.Where(c => c.Permission is { } extra && !held.Contains(extra)).Where(c => columns.Contains(c.Key)))
                {
                    problems.Add($"catalogue for [{string.Join(", ", held)}] shows column '{hidden.Key}' of {definition.Key}, which needs {hidden.Permission}");
                }
                var parameters = item.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("key").GetString()!).ToList();
                foreach (var hidden in definition.Parameters.Where(p => p.Permission is { } extra && !held.Contains(extra)).Where(p => parameters.Contains(p.Key)))
                {
                    problems.Add($"catalogue for [{string.Join(", ", held)}] offers parameter '{hidden.Key}' of {definition.Key}, which needs {hidden.Permission}");
                }
            }
        }
        return problems;
    }

    private static string Key(IReadOnlyList<string> permissions) => string.Join("+", permissions.Order(StringComparer.Ordinal));

    private static int _users;

    /// <summary>A role granting exactly these permissions and a user holding only that role, signed in.</summary>
    private static async Task<HttpClient> UserWithAsync(ErpTestEnvironment env, HttpClient admin, IReadOnlyList<string> permissions, string prefix)
    {
        var n = Interlocked.Increment(ref _users);
        var name = $"{prefix} {n}";
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Only {name}", nameAr = $"فقط {name}", permissions });
        Assert.True(role.StatusCode == HttpStatusCode.Created, $"role for [{string.Join(", ", permissions)}]: {(int)role.StatusCode} {await role.Content.ReadAsStringAsync()}");
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"{prefix}-{n}@{env.TenantA.EmailDomain}";
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = $"G2 {name}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } });
        Assert.True(user.StatusCode == HttpStatusCode.Created, $"user for [{string.Join(", ", permissions)}]: {(int)user.StatusCode}");
        // The user works in every company of the workspace (all branches), so what a report
        // prints is decided by its permissions alone, not by the company scope.
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200")).GetProperty("items").EnumerateArray()
            .Select(c => new { companyId = c.GetProperty("id").GetGuid(), allBranches = true }).ToList();
        // Access saves carry the version read (p02): read it first, as the access screen does.
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}")).GetProperty("version").GetUInt32();
        using var access = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies, version });
        Assert.True(access.IsSuccessStatusCode, $"company access for [{string.Join(", ", permissions)}]: {(int)access.StatusCode} {await access.Content.ReadAsStringAsync()}");
        return await env.SignInAsync(email);
    }

    /// <summary>Every text value of the workspace's own rows (read with the superuser), at least four
    /// characters long: what a document might print. The audit trail is left out (its rows repeat
    /// every other table's values).</summary>
    private static async Task<IReadOnlyList<string>> WorkspaceTextAsync(ErpTestEnvironment env, Guid tenantId)
    {
        await using var connection = await env.OpenAdminAsync();
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in await DbCatalog.TenantTablesAsync(connection))
        {
            if (table.Schema == "audit")
            {
                continue;
            }
            foreach (var column in await DbCatalog.ColumnsAsync(connection, table))
            {
                if (column.Name == "tenant_id" || column.Type.EndsWith("[]", StringComparison.Ordinal) ||
                    !(column.Type.StartsWith("text", StringComparison.Ordinal) || column.Type.StartsWith("character varying", StringComparison.Ordinal) || column.Type == "citext"))
                {
                    continue;
                }
                values.UnionWith(await DbCatalog.ReadAsync(connection,
                    $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE tenant_id = @t AND length(\"{column.Name}\"::text) >= 4 LIMIT 5000",
                    r => r.GetString(0), ("t", tenantId)));
            }
        }
        return values.Select(v => v.Trim()).Where(v => v.Length >= 4).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every string of every resource file (labels, choices, messages): a value equal to
    /// one is a word the product prints itself, not the workspace's data.</summary>
    private static HashSet<string> ResourceStrings()
    {
        var strings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.json", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(Repo.Root, "web", "src"), "*.json", SearchOption.AllDirectories))
                     .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                                 f.Contains($"{Path.DirectorySeparatorChar}i18n{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Walk(document.RootElement, strings);
        }
        return strings;
    }

    /// <summary>The queries each report runs with: without parameters (when none is required) and
    /// with each of up to three of the workspace's records in each reference parameter.</summary>
    private static async Task<IReadOnlyList<string>> QueriesAsync(HttpClient admin, ModuleCatalog catalog, ReportDefinition report)
    {
        var queries = new List<string>();
        var required = report.Parameters.Where(p => p.Required).ToList();
        if (required.Count == 0)
        {
            queries.Add("");
        }
        foreach (var parameter in report.Parameters.Where(p => p.Type == ReportParameterType.Reference))
        {
            var list = catalog.FindList(parameter.Lookup!)!;
            var page = await admin.GetFromJsonAsync<JsonElement>($"{list.Endpoint}?take=3");
            foreach (var row in page.GetProperty("items").EnumerateArray())
            {
                var others = required.Where(r => r.Key != parameter.Key).ToList();
                if (others.Count > 0)
                {
                    continue;
                }
                queries.Add($"{parameter.Key}={row.GetProperty("id").GetString()}");
            }
        }
        return queries;
    }

    /// <summary>Everything the endpoints show (every string and number of every answer, lists paged
    /// to the end, every record's own GET for the ids the lists gave).</summary>
    private static async Task<string> CorpusAsync(HttpClient client, IEnumerable<ApiEndpoint> endpoints)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = endpoints.Where(e => !e.Pattern.StartsWith("/api/reports/", StringComparison.Ordinal) && !e.Pattern.Contains("{*", StringComparison.Ordinal)).ToList();
        foreach (var endpoint in all.Where(e => e.RouteParameters.Count == 0))
        {
            string? after = null;
            for (var page = 0; page < MaxPages; page++)
            {
                var path = endpoint.Path(_ => "") + "?take=200" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after));
                using var response = await client.GetAsync(path);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
                {
                    break;
                }
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Walk(json, shown, ids);
                after = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                if (after is null)
                {
                    break;
                }
            }
        }
        foreach (var endpoint in all.Where(e => e.RouteParameters.Count == 1))
        {
            foreach (var id in ids.Take(400).ToList())
            {
                using var response = await client.GetAsync(endpoint.Path(_ => id));
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    Walk(await response.Content.ReadFromJsonAsync<JsonElement>(), shown, ids);
                }
            }
        }
        return string.Join("\n", shown);
    }

    private static void Walk(JsonElement element, HashSet<string> into, HashSet<string>? ids = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ids is not null && property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(property.Value.GetString(), out _))
                    {
                        ids.Add(property.Value.GetString()!);
                    }
                    Walk(property.Value, into, ids);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, into, ids);
                break;
            case JsonValueKind.String:
                into.Add(element.GetString()!);
                break;
            case JsonValueKind.Number:
                into.Add(element.GetRawText());
                break;
        }
    }

    /// <summary>What a document prints of data: its letterhead (the issuing company), subject, the
    /// parameters' and facts' values, group labels, cells and totals. Not its title, labels or fixed
    /// texts (the product's own words), nor who printed it (the caller's own name).</summary>
    private static string PrintedText(JsonElement document, bool withIssuer = true)
    {
        var text = new StringBuilder();
        void Add(JsonElement e, string name)
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) text.Append(v.GetString()).Append('\n');
        }
        if (withIssuer)
        {
            Add(document, "issuer");
        }
        Add(document, "subject");
        foreach (var section in new[] { "parameters", "facts" })
        {
            foreach (var fact in document.GetProperty(section).EnumerateArray()) Add(fact, "text");
        }
        foreach (var group in document.GetProperty("groups").EnumerateArray())
        {
            Add(group, "label");
            foreach (var row in group.GetProperty("rows").EnumerateArray())
            {
                foreach (var cell in row.GetProperty("cells").EnumerateArray()) Add(cell, "text");
            }
        }
        foreach (var total in document.GetProperty("totals").EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object)) Add(total, "text");
        return text.ToString();
    }
}
