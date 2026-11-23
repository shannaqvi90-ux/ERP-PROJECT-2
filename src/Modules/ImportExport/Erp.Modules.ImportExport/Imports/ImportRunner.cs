using System.Text.Json;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.ImportExport.Files;
using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.ImportExport.Imports;

/// <summary>
/// Checks or imports an uploaded file (p14). Every row is first checked with the record's own rules (a dry run); a check only reports.
/// An import writes the valid rows, all of them in the caller's unit of work (so a failure leaves the records as they were), unless
/// some rows have errors and the run is set to refuse them, in which case nothing is written. The error file and the counts are kept
/// on the file's record. Small files run while the person waits (the request's own unit of work); larger ones run as a background job
/// (<see cref="ImportJobHandler"/>), the same code in both.
/// </summary>
internal static class ImportRunner
{
    /// <summary>Files up to this many rows are processed while the person waits (a few seconds); larger ones go to the background.</summary>
    public const int InlineRows = 5000;

    public static async Task RunAsync(IServiceProvider services, ImportExportDbContext db, ImportSession session, ImportFile file,
        Func<int, JobText, Task>? report, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<ModuleCatalog>();
        var strings = services.GetRequiredService<StringCatalog>();
        Task Report(int percent, JobText text) => report is null ? Task.CompletedTask : report(percent, text);
        var type = catalog.ImportRecordTypes.FirstOrDefault(t => t.Table == session.RecordType);
        if (type is null)
        {
            await FailAsync(db, session, strings, "importUnknownType", cancellationToken);
            return;
        }

        Table table;
        try
        {
            table = TabularFile.Read(file.Content, session.FileName);
        }
        catch (TableFormatException problem)
        {
            await FailAsync(db, session, strings, CodeOf(problem.Code), cancellationToken, problem.Args);
            return;
        }
        var mapping = JsonSerializer.Deserialize<string?[]>(session.Mapping, JsonSerializerOptions.Web) ?? [];
        var rows = ImportEngine.Rows(table, mapping).ToList();
        var state = new Dictionary<string, object?>(StringComparer.Ordinal);
        var issuesByRow = new SortedDictionary<int, List<ImportIssue>>();
        var total = Math.Max(1, rows.Count);

        // Check every row first: this is the whole of a dry run and the first half of an import.
        var checkShare = session.DryRun ? 100 : 40;
        for (var from = 0; from < rows.Count; from += ImportEngine.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = rows.Skip(from).Take(ImportEngine.BatchSize).ToList();
            foreach (var issue in await type.Process(services, new ImportBatch(batch, DryRun: true, state, session.OnDuplicate), cancellationToken))
            {
                AddIssue(issuesByRow, issue);
            }
            await Report((int)((long)Math.Min(from + batch.Count, rows.Count) * checkShare / total),
                new JobText($"Checked {Math.Min(from + batch.Count, rows.Count)} of {rows.Count} rows.", $"تم فحص {Math.Min(from + batch.Count, rows.Count)} من {rows.Count} صفًا."));
        }

        session.RowsChecked = rows.Count;
        var duplicateRows = state.TryGetValue(ImportStateKeys.DuplicateRows, out var seen) && seen is HashSet<int> set ? set : [];
        session.RowsDuplicate = duplicateRows.Count;
        session.RowsValid = rows.Count - issuesByRow.Count;
        var imported = 0;
        var failedLater = 0;
        var refused = !session.DryRun && issuesByRow.Count > 0 && session.OnError == ImportOnError.Reject;
        if (!session.DryRun && !refused)
        {
            // Write the valid rows, with a fresh state: the check's references (a name used twice in the file) are met again in file order.
            // No progress is reported from here: the job's progress is written by a connection of its own, and the audit trail's lock
            // taken by the first row written would make it wait for this unit of work to end.
            await Report(40, new JobText($"Importing {rows.Count - issuesByRow.Count} rows.", $"استيراد {rows.Count - issuesByRow.Count} صفًا."));
            state = new Dictionary<string, object?>(StringComparer.Ordinal);
            // A row that matches a record already there is left out when the run says skip (it is not written, so it is not "imported").
            var valid = rows.Where(r => !issuesByRow.ContainsKey(r.Number) && !(session.OnDuplicate == ImportOnDuplicate.Skip && duplicateRows.Contains(r.Number))).ToList();
            for (var from = 0; from < valid.Count; from += ImportEngine.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = valid.Skip(from).Take(ImportEngine.BatchSize).ToList();
                var late = await type.Process(services, new ImportBatch(batch, DryRun: false, state, session.OnDuplicate), cancellationToken);
                var lateRows = late.Select(i => i.Row).Distinct().ToHashSet();
                foreach (var issue in late)
                {
                    AddIssue(issuesByRow, issue);
                }
                failedLater += lateRows.Count;
                imported += batch.Count - lateRows.Count;
            }
        }

        var updated = state.TryGetValue(ImportStateKeys.Updated, out var u) && u is int updatedCount ? updatedCount : 0;
        session.RowsUpdated = updated;
        session.RowsImported = imported - updated;
        session.RowsFailed = issuesByRow.Count;
        session.RowsValid = rows.Count - issuesByRow.Count;
        session.Issues = JsonSerializer.Serialize(issuesByRow.Values.SelectMany(i => i).Take(ImportLimits.MaxShownIssues)
            .Select(i => new StoredIssue(i.Row, i.Field, i.Code, i.MessageEn, i.MessageAr)), JsonSerializerOptions.Web);
        var allFields = await ImportEngine.FieldsAsync(type, services, cancellationToken);
        file.ErrorReport = issuesByRow.Count > 0 ? ImportEngine.ErrorFile(table, issuesByRow, strings, mapping, allFields) : null;
        file.SizeBytes = file.Content.Length;
        if (refused)
        {
            session.Status = ImportStatus.Failed;
            session.FailureCode = "importRowsRejected";
            session.FailureEn = strings.Get("import.failure.importRowsRejected", "en", issuesByRow.Count);
            session.FailureAr = strings.Get("import.failure.importRowsRejected", "ar", issuesByRow.Count);
        }
        else
        {
            session.Status = session.DryRun ? ImportStatus.Checked : ImportStatus.Completed;
        }
        session.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (session.DryRun || refused)
        {
            await Report(100, new JobText(
                session.DryRun ? $"Checked {rows.Count} rows: {session.RowsValid} valid, {session.RowsFailed} with problems." : "Nothing imported: some rows have problems.",
                session.DryRun ? $"تم فحص {rows.Count} صفًا: {session.RowsValid} صالحًا و{session.RowsFailed} به مشكلات." : "لم يتم استيراد شيء: بعض الصفوف بها مشكلات."));
        }
    }


    /// <summary>The validation code (its text is in the module's strings) of a file that cannot be read.</summary>
    public static string CodeOf(string reason) => reason switch
    {
        "unknownFormat" => "importUnknownFormat",
        "empty" => "importEmpty",
        "noHeader" => "importNoHeader",
        "tooManyRows" => "importTooManyRows",
        "tooManyColumns" => "importTooManyColumns",
        "cellTooLong" => "importCellTooLong",
        "tooLarge" => "importTooLarge",
        _ => "importDamaged",
    };

    private static void AddIssue(SortedDictionary<int, List<ImportIssue>> byRow, ImportIssue issue)
    {
        if (!byRow.TryGetValue(issue.Row, out var list))
        {
            byRow[issue.Row] = list = [];
        }
        if (list.Count < 10)
        {
            list.Add(issue);
        }
    }

    /// <summary>The file cannot be processed: say so on its record (the run itself ends as done: it did what it could).</summary>
    private static async Task FailAsync(ImportExportDbContext db, ImportSession session, StringCatalog strings, string code, CancellationToken cancellationToken, params object[] args)
    {
        session.Status = ImportStatus.Failed;
        session.FailureCode = code;
        session.FailureEn = strings.Get("validation." + code, "en", args);
        session.FailureAr = strings.Get("validation." + code, "ar", args);
        session.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
