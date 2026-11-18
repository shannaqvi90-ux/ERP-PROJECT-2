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
/// Checks or imports an uploaded file, in the background (p14). Every row is first checked with the record's own rules (a
/// dry run); a check only reports. An import writes the valid rows, all of them in one unit of work with the job (so a
/// failure leaves the records as they were), unless some rows have errors and the run is set to refuse them, in which
/// case nothing is written. The error file and the counts are kept on the file's record. One attempt only: a retry would
/// repeat a half-reported run.
/// </summary>
internal sealed class ImportJobHandler : IJobHandler
{
    public const string TypeKey = "importexport.import";

    public static readonly JobType Type = new(TypeKey, "job.type.importexport.import", MaxAttempts: 1, Timeout: TimeSpan.FromHours(2),
        RunPermission: null, Description: "job.type.importexport.import.description");

    public async Task RunAsync(IJobContext context)
    {
        var services = context.Services;
        var db = services.GetRequiredService<ImportExportDbContext>();
        var catalog = services.GetRequiredService<ModuleCatalog>();
        var strings = services.GetRequiredService<StringCatalog>();
        if (context.Payload.ValueKind != JsonValueKind.Object || !context.Payload.TryGetProperty("sessionId", out var idElement) || !idElement.TryGetGuid(out var sessionId))
        {
            throw new JobFailure("importexport.import.payload", new JobText("The job has no file to import.", "ليس للمهمة ملف لاستيراده."), retry: false);
        }
        var session = await db.ImportSessions.SingleOrDefaultAsync(s => s.Id == sessionId, context.Cancellation);
        var file = session is null ? null : await db.ImportFiles.SingleOrDefaultAsync(f => f.SessionId == sessionId, context.Cancellation);
        if (session is null || file is null)
        {
            throw new JobFailure("importexport.import.gone", new JobText("The file was discarded before it was imported.", "تم تجاهل الملف قبل استيراده."), retry: false);
        }
        var type = catalog.ImportRecordTypes.FirstOrDefault(t => t.Table == session.RecordType);
        if (type is null)
        {
            await FailAsync(db, session, strings, "importUnknownType", context.Cancellation);
            return;
        }

        Table table;
        try
        {
            table = TabularFile.Read(file.Content, session.FileName);
        }
        catch (TableFormatException problem)
        {
            await FailAsync(db, session, strings, "import" + char.ToUpperInvariant(problem.Code[0]) + problem.Code[1..], context.Cancellation, problem.Args);
            return;
        }
        var mapping = JsonSerializer.Deserialize<string?[]>(session.Mapping, ImportEngine.Json) ?? [];
        var rows = ImportEngine.Rows(table, mapping).ToList();
        var state = new Dictionary<string, object?>(StringComparer.Ordinal);
        var issuesByRow = new SortedDictionary<int, List<ImportIssue>>();
        var total = Math.Max(1, rows.Count);

        // Check every row first: this is the whole of a dry run and the first half of an import.
        var checkShare = session.DryRun ? 100 : 40;
        for (var from = 0; from < rows.Count; from += ImportEngine.BatchSize)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var batch = rows.Skip(from).Take(ImportEngine.BatchSize).ToList();
            foreach (var issue in await type.Process(services, new ImportBatch(batch, DryRun: true, state), context.Cancellation))
            {
                AddIssue(issuesByRow, issue);
            }
            await context.ReportAsync((int)((long)Math.Min(from + batch.Count, rows.Count) * checkShare / total),
                new JobText($"Checked {Math.Min(from + batch.Count, rows.Count)} of {rows.Count} rows.", $"تم فحص {Math.Min(from + batch.Count, rows.Count)} من {rows.Count} صفًا."));
        }

        session.RowsChecked = rows.Count;
        session.RowsValid = rows.Count - issuesByRow.Count;
        var imported = 0;
        var failedLater = 0;
        var refused = !session.DryRun && issuesByRow.Count > 0 && session.OnError == ImportOnError.Reject;
        if (!session.DryRun && !refused)
        {
            // Write the valid rows, with a fresh state: the check's references (a name used twice in the file) are met again in file order.
            state = new Dictionary<string, object?>(StringComparer.Ordinal);
            var valid = rows.Where(r => !issuesByRow.ContainsKey(r.Number)).ToList();
            for (var from = 0; from < valid.Count; from += ImportEngine.BatchSize)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var batch = valid.Skip(from).Take(ImportEngine.BatchSize).ToList();
                var late = await type.Process(services, new ImportBatch(batch, DryRun: false, state), context.Cancellation);
                var lateRows = late.Select(i => i.Row).Distinct().ToHashSet();
                foreach (var issue in late)
                {
                    AddIssue(issuesByRow, issue);
                }
                failedLater += lateRows.Count;
                imported += batch.Count - lateRows.Count;
                await context.ReportAsync(40 + (int)((long)Math.Min(from + batch.Count, valid.Count) * 59 / Math.Max(1, valid.Count)),
                    new JobText($"Imported {imported} of {valid.Count} rows.", $"تم استيراد {imported} من {valid.Count} صفًا."));
            }
        }

        session.RowsImported = imported;
        session.RowsFailed = issuesByRow.Count;
        session.RowsValid = rows.Count - issuesByRow.Count;
        session.Issues = JsonSerializer.Serialize(issuesByRow.Values.SelectMany(i => i).Take(ImportLimits.MaxShownIssues)
            .Select(i => new StoredIssue(i.Row, i.Field, i.Code, i.MessageEn, i.MessageAr)), ImportEngine.Json);
        file.ErrorReport = issuesByRow.Count > 0 ? ImportEngine.ErrorFile(table, issuesByRow, strings) : null;
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
        await db.SaveChangesAsync(context.Cancellation);
        context.SetResult(new { session.RecordType, rows = rows.Count, valid = session.RowsValid, imported, failed = session.RowsFailed, dryRun = session.DryRun });
        await context.ReportAsync(100, new JobText(
            session.DryRun ? $"Checked {rows.Count} rows: {session.RowsValid} valid, {session.RowsFailed} with problems." : refused ? "Nothing imported: some rows have problems." : $"Imported {imported} rows.",
            session.DryRun ? $"تم فحص {rows.Count} صفًا: {session.RowsValid} صالحًا و{session.RowsFailed} به مشكلات." : refused ? "لم يتم استيراد شيء: بعض الصفوف بها مشكلات." : $"تم استيراد {imported} صفًا."));
    }

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

    /// <summary>The file cannot be processed: say so on its record (the job itself ends as done: it did what it could).</summary>
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
