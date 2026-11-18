using System.IO.Compression;
using System.Text;
using Erp.Kernel.Seeding;
using Erp.Modules.ImportExport.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.ImportExport.Seeding;

/// <summary>
/// Gives the demo and the gate workspaces something to show and to try to reach: one finished import of contacts, with an error
/// file, and one finished full export holding a small file. Their names and texts carry the workspace's mark, so tenant B's
/// canary in an answer to tenant A is recognised. A minimal or newly provisioned workspace has none.
/// </summary>
internal sealed class ImportExportSeeder(ImportExportDbContext db) : ITenantSeeder
{
    public int Order => 200;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (context.Plan.Profile is SeedProfile.Minimal or SeedProfile.Provision || await db.ImportSessions.AnyAsync(cancellationToken))
        {
            return;
        }
        var at = new DateTimeOffset(2024, 2, 5, 9, 0, 0, TimeSpan.Zero);
        var fileName = context.Mark("customers-import") + ".csv";
        var csv = $"Name,Email,Phone\r\n{context.Mark("Gulf Spare Parts Trading")},orders@{context.Tenant.EmailDomain},+971 4 555 0100\r\n,,\r\n";
        var report = $"Name,Email,Phone,Row number,Problems (English),Problems (Arabic)\r\n,,,3,nameEn: Enter the name in English or in Arabic.,nameEn: أدخل الاسم بالإنجليزية أو بالعربية.\r\n";
        var session = new ImportSession
        {
            RecordType = "contacts.contacts",
            FileName = fileName,
            Format = "csv",
            Status = ImportStatus.Completed,
            RowCount = 2,
            Headers = "[\"Name\",\"Email\",\"Phone\"]",
            Mapping = "[\"name\",\"email\",\"phone\"]",
            Sources = "nnn",
            Sample = System.Text.Json.JsonSerializer.Serialize(new[] { new[] { context.Mark("Gulf Spare Parts Trading"), $"orders@{context.Tenant.EmailDomain}", "+971 4 555 0100" } }),
            OnError = ImportOnError.Skip,
            RowsChecked = 2,
            RowsValid = 1,
            RowsImported = 1,
            RowsFailed = 1,
            Issues = System.Text.Json.JsonSerializer.Serialize(new[] { new { row = 3, field = "nameEn", code = "required", en = "Enter the name in English or in Arabic.", ar = "أدخل الاسم بالإنجليزية أو بالعربية." } }),
            FinishedAt = at,
        };
        foreach (var (header, field) in new[] { ("customername", "name"), ("customeremailaddress", "email"), ("customertelephone", "phone") })
        {
            db.ImportMappings.Add(new ImportMapping { RecordType = "contacts.contacts", HeaderKey = header, FieldKey = field, TimesUsed = 3, LastUsedAt = at });
        }
        db.ImportSessions.Add(session);
        db.ImportFiles.Add(new ImportFile { SessionId = session.Id, SizeBytes = csv.Length, Content = new UTF8Encoding(false).GetBytes(csv), ErrorReport = new UTF8Encoding(true).GetBytes(report) });

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("README.txt").Open();
            entry.Write(Encoding.UTF8.GetBytes(context.Mark("Export of the demo workspace") + "\r\n"));
        }
        var export = new TenantExport
        {
            Status = ExportStatus.Succeeded,
            Format = "csv",
            FileCount = 1,
            ByteSize = buffer.Length,
            RecordCounts = "{\"contacts.contacts\":0}",
            FinishedAt = at,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3650),
        };
        db.TenantExports.Add(export);
        db.ExportFiles.Add(new ExportFile { ExportId = export.Id, SizeBytes = buffer.Length, Content = buffer.ToArray() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
