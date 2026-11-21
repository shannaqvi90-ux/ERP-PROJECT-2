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
        await ImportRunner.RunAsync(services, db, session, file, context.ReportAsync, context.Cancellation);
        if (session.Status != ImportStatus.Failed || session.FailureCode == "importRowsRejected")
        {
            context.SetResult(new { session.RecordType, rows = session.RowCount, valid = session.RowsValid, imported = session.RowsImported, updated = session.RowsUpdated, duplicates = session.RowsDuplicate, failed = session.RowsFailed, dryRun = session.DryRun });
        }
    }
}
