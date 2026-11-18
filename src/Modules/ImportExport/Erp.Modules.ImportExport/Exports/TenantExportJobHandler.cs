using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Kernel.CustomFields;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Modules.Attachments.Contracts;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.ImportExport.Imports;
using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.ImportExport.Exports;

/// <summary>
/// Writes the full export of a workspace (p14, owner decision of 2026-11-12, needs-human #44): a zip file with a manifest
/// (<c>manifest.json</c>: what the files hold, their columns, counts and checksums), a readme in English and Arabic, one CSV or JSON
/// file per kind of record every module registered (<c>data/&lt;schema.table&gt;.csv</c>, the record's custom fields as extra columns) and the
/// workspace's attachments (<c>attachments/</c> with their index <c>data/attachments.csv</c>). The job runs in the workspace's
/// own unit of work, so it reads only that workspace's rows; the file is kept on the export's record for a few days.
/// </summary>
internal sealed class TenantExportJobHandler : IJobHandler
{
    public const string TypeKey = "importexport.tenantExport";
    private const int Chunk = 500;

    public static readonly JobType Type = new(TypeKey, "job.type.importexport.tenantExport", MaxAttempts: 1, Timeout: TimeSpan.FromHours(4),
        RunPermission: null, Description: "job.type.importexport.tenantExport.description");

    public async Task RunAsync(IJobContext context)
    {
        var services = context.Services;
        var db = services.GetRequiredService<ImportExportDbContext>();
        var catalog = services.GetRequiredService<ModuleCatalog>();
        var strings = services.GetRequiredService<StringCatalog>();
        if (context.Payload.ValueKind != JsonValueKind.Object || !context.Payload.TryGetProperty("exportId", out var idElement) || !idElement.TryGetGuid(out var exportId))
        {
            throw new JobFailure("importexport.export.payload", new JobText("The job has no export to write.", "ليس للمهمة تصدير لكتابته."), retry: false);
        }
        var export = await db.TenantExports.SingleOrDefaultAsync(e => e.Id == exportId, context.Cancellation);
        if (export is null)
        {
            throw new JobFailure("importexport.export.gone", new JobText("The export was removed before it was written.", "تمت إزالة التصدير قبل كتابته."), retry: false);
        }
        var json = export.Format == "json";
        var types = catalog.ExportRecordTypes.OrderBy(t => t.Table, StringComparer.Ordinal).ToList();
        var attachments = services.GetService<IAttachmentExport>();
        var customFields = services.GetService<ICustomFields>();
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var manifestFiles = new List<object>();
        using var buffer = new MemoryStream();
        long attachmentCount = 0;
        long attachmentBytes = 0;
        try
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var step = 0;
                var steps = types.Count + 1;
                foreach (var type in types)
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    await context.ReportAsync(step * 90 / steps, new JobText($"Writing {type.Table}.", $"كتابة {type.Table}."));
                    var custom = type.CustomFieldRecord is null || customFields is null ? [] : await customFields.FieldsAsync(type.CustomFieldRecord, context.Cancellation);
                    var columns = type.Columns.Concat(custom.Select(c => ImportCustomValues.Prefix + c.Key)).ToList();
                    var path = $"data/{type.Table}.{export.Format}";
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    long records;
                    string sha;
                    await using (var stream = entry.Open())
                    await using (var hashed = new HashingStream(stream))
                    {
                        records = await WriteRecordsAsync(type, columns, custom, customFields, services, json, hashed, context.Cancellation);
                        sha = hashed.Hex();
                    }
                    counts[type.Table] = records;
                    manifestFiles.Add(new { path, recordType = type.Table, label = new { en = strings.Get(type.LabelKey, "en"), ar = strings.Get(type.LabelKey, "ar") }, records, columns, sha256 = sha });
                    step++;
                    CheckSize(buffer);
                }
                if (attachments is not null)
                {
                    await context.ReportAsync(step * 90 / steps, new JobText("Writing attachments.", "كتابة المرفقات."));
                    var index = new List<ExportedFile>();
                    Guid? after = null;
                    while (true)
                    {
                        context.Cancellation.ThrowIfCancellationRequested();
                        var page = await attachments.ListAsync(after, Chunk, context.Cancellation);
                        if (page.Count == 0)
                        {
                            break;
                        }
                        foreach (var file in page)
                        {
                            await using var source = await attachments.OpenAsync(file.Id, context.Cancellation);
                            if (source is null)
                            {
                                continue;
                            }
                            var entry = zip.CreateEntry($"attachments/{file.Id:N}-{ImportEngine.CleanFileName(file.Name).Replace('/', '_')}", CompressionLevel.NoCompression);
                            await using (var target = entry.Open())
                            {
                                await source.CopyToAsync(target, context.Cancellation);
                            }
                            index.Add(file);
                            attachmentCount++;
                            attachmentBytes += file.SizeBytes;
                            CheckSize(buffer);
                        }
                        after = page[^1].Id;
                    }
                    string[] attachmentColumns = ["id", "recordType", "recordId", "name", "contentType", "sizeBytes", "sha256", "createdAt", "path"];
                    var indexPath = $"data/attachments.{export.Format}";
                    var indexEntry = zip.CreateEntry(indexPath, CompressionLevel.Optimal);
                    string indexSha;
                    await using (var stream = indexEntry.Open())
                    await using (var hashed = new HashingStream(stream))
                    {
                        var rows = index.Select(f => new ExportRecord(f.Id, [f.Id, f.RecordType, f.RecordId, f.Name, f.ContentType, f.SizeBytes, f.Sha256, f.CreatedAt,
                            $"attachments/{f.Id:N}-{ImportEngine.CleanFileName(f.Name).Replace('/', '_')}"]));
                        await WriteAsync(attachmentColumns, rows.ToAsyncEnumerable(), json, hashed, context.Cancellation);
                        indexSha = hashed.Hex();
                    }
                    counts["attachments.files"] = index.Count;
                    manifestFiles.Add(new { path = indexPath, recordType = "attachments.files", label = new { en = "Attachments", ar = "المرفقات" }, records = (long)index.Count, columns = attachmentColumns, sha256 = indexSha });
                }
                var manifest = new
                {
                    format = "erp-tenant-export",
                    version = 1,
                    generatedAt = DateTimeOffset.UtcNow,
                    recordFormat = export.Format,
                    files = manifestFiles,
                    attachments = new { count = attachmentCount, bytes = attachmentBytes, folder = "attachments/" },
                    notes = new
                    {
                        en = "Every file is UTF-8. CSV: comma separated, a header row, quotes around text with commas or line breaks, dates as ISO 8601, amounts as plain decimals with their currency in the neighbouring columns. JSON: an array of objects keyed by the column names.",
                        ar = "كل الملفات بترميز UTF-8. ملفات CSV: مفصولة بفواصل مع صف رؤوس، والنص الذي يحوي فواصل أو أسطرًا جديدة بين علامتي اقتباس، والتواريخ بصيغة ISO 8601، والمبالغ أعداد عشرية عادية وعملتها في الأعمدة المجاورة. ملفات JSON: مصفوفة كائنات مفاتيحها أسماء الأعمدة.",
                    },
                };
                WriteText(zip, "manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                WriteText(zip, "README.txt", Readme(export.Format));
            }
        }
        catch (ExportTooLargeException)
        {
            await FailAsync(db, export, strings, "exportTooLarge", context.Cancellation, ImportLimits.MaxExportBytes / (1024 * 1024));
            return;
        }
        if (buffer.Length > ImportLimits.MaxExportBytes)
        {
            await FailAsync(db, export, strings, "exportTooLarge", context.Cancellation, ImportLimits.MaxExportBytes / (1024 * 1024));
            return;
        }
        var bytes = buffer.ToArray();
        db.ExportFiles.Add(new ExportFile { ExportId = export.Id, SizeBytes = bytes.Length, Content = bytes });
        export.Status = ExportStatus.Succeeded;
        export.FileCount = manifestFiles.Count + 2 + (int)attachmentCount;
        export.ByteSize = bytes.Length;
        export.RecordCounts = JsonSerializer.Serialize(counts, JsonSerializerOptions.Web);
        export.FinishedAt = DateTimeOffset.UtcNow;
        export.ExpiresAt = export.FinishedAt + TenantExports.Lifetime;
        await db.SaveChangesAsync(context.Cancellation);
        context.SetResult(new { files = export.FileCount, bytes = export.ByteSize, records = counts });
        await context.ReportAsync(100, new JobText($"Wrote {export.FileCount} files.", $"تمت كتابة {export.FileCount} ملفًا."));
    }

    private static void CheckSize(MemoryStream buffer)
    {
        if (buffer.Length > ImportLimits.MaxExportBytes)
        {
            throw new ExportTooLargeException();
        }
    }

    private static async Task FailAsync(ImportExportDbContext db, TenantExport export, StringCatalog strings, string code, CancellationToken cancellationToken, params object[] args)
    {
        export.Status = ExportStatus.Failed;
        export.FailureCode = code;
        export.FailureEn = strings.Get("problem." + code, "en", args);
        export.FailureAr = strings.Get("problem." + code, "ar", args);
        export.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void WriteText(ZipArchive zip, string path, string text)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string Readme(string format) =>
        "FULL EXPORT OF THE WORKSPACE\r\n============================\r\n" +
        "manifest.json lists every file with its columns, record count and SHA-256 checksum.\r\n" +
        $"data/ holds one {format.ToUpperInvariant()} file per kind of record (named schema.table); the workspace's custom fields are the columns that start with custom.\r\n" +
        "attachments/ holds the attached files; data/attachments." + format + " says which record each belongs to.\r\n" +
        "All text is UTF-8. Dates are ISO 8601. Amounts are plain decimals; their currency, exchange rate and base-currency amount are in neighbouring columns.\r\n\r\n" +
        "تصدير كامل لمساحة العمل\r\n======================\r\n" +
        "يسرد manifest.json كل ملف مع أعمدته وعدد سجلاته ورمز SHA-256.\r\n" +
        $"يحوي المجلد data ملف {format.ToUpperInvariant()} واحدًا لكل نوع من السجلات (باسم schema.table)، والحقول المخصصة هي الأعمدة التي تبدأ بـ custom.\r\n" +
        "يحوي المجلد attachments الملفات المرفقة، ويبيّن data/attachments." + format + " السجل الذي يخص كل ملف.\r\n" +
        "كل النصوص بترميز UTF-8 والتواريخ بصيغة ISO 8601 والمبالغ أعداد عشرية عادية وعملتها وسعر الصرف والمبلغ بالعملة الأساسية في الأعمدة المجاورة.\r\n";

    private static async Task<long> WriteRecordsAsync(ExportRecordType type, IReadOnlyList<string> columns, IReadOnlyList<CustomFieldInfo> custom, ICustomFields? customFields,
        IServiceProvider services, bool json, Stream output, CancellationToken cancellationToken)
    {
        long count = 0;
        async IAsyncEnumerable<ExportRecord> Rows([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            var chunk = new List<ExportRecord>(Chunk);
            async IAsyncEnumerable<ExportRecord> Flush()
            {
                var values = custom.Count > 0 && customFields is not null && type.CustomFieldRecord is not null
                    ? await customFields.ValuesAsync(type.CustomFieldRecord, chunk.Select(r => r.Id).ToList(), token)
                    : null;
                foreach (var record in chunk)
                {
                    IDictionary<string, object?>? own = null;
                    values?.TryGetValue(record.Id, out own);
                    yield return custom.Count == 0 ? record : new ExportRecord(record.Id, [.. record.Cells, .. custom.Select(c => own is not null && own.TryGetValue(c.Key, out var v) ? v : null)]);
                }
                chunk.Clear();
            }
            await foreach (var record in type.Rows(services, token))
            {
                chunk.Add(record);
                if (chunk.Count >= Chunk)
                {
                    await foreach (var r in Flush())
                    {
                        yield return r;
                    }
                }
            }
            await foreach (var r in Flush())
            {
                yield return r;
            }
        }
        async IAsyncEnumerable<ExportRecord> Counted(IAsyncEnumerable<ExportRecord> source)
        {
            await foreach (var record in source)
            {
                count++;
                yield return record;
            }
        }
        await WriteAsync(columns, Counted(Rows(cancellationToken)), json, output, cancellationToken);
        return count;
    }

    internal static async Task WriteAsync(IReadOnlyList<string> columns, IAsyncEnumerable<ExportRecord> rows, bool json, Stream output, CancellationToken cancellationToken)
    {
        if (json)
        {
            await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });
            writer.WriteStartArray();
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                writer.WriteStartObject();
                for (var i = 0; i < columns.Count && i < row.Cells.Count; i++)
                {
                    writer.WritePropertyName(columns[i]);
                    WriteJsonValue(writer, row.Cells[i]);
                }
                writer.WriteEndObject();
                if (writer.BytesPending > 64 * 1024)
                {
                    await writer.FlushAsync(cancellationToken);
                }
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);
            return;
        }
        var text = new StreamWriter(output, new UTF8Encoding(true), 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await text.WriteLineAsync(string.Join(",", columns.Select(c => ImportEngine.Quote(c))));
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await text.WriteLineAsync(string.Join(",", row.Cells.Select(c => ImportEngine.Quote(CsvText(c)))));
        }
        await text.FlushAsync(cancellationToken);
    }

    private static string? CsvText(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        JsonElement e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(),
        _ => value.ToString(),
    };

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case decimal d: writer.WriteNumberValue(d); break;
            case int i: writer.WriteNumberValue(i); break;
            case long l: writer.WriteNumberValue(l); break;
            case DateTimeOffset d: writer.WriteStringValue(d.ToUniversalTime()); break;
            case Guid g: writer.WriteStringValue(g); break;
            case JsonElement e: e.WriteTo(writer); break;
            default: writer.WriteStringValue(CsvText(value)); break;
        }
    }
}

/// <summary>The export grew past what a workspace's export file can hold.</summary>
internal sealed class ExportTooLargeException : Exception;

/// <summary>Passes bytes through and keeps their SHA-256 (for the manifest).</summary>
internal sealed class HashingStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _hex;

    public string Hex() => _hex ??= Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _hash.AppendData(buffer, offset, count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _hash.AppendData(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }
        base.Dispose(disposing);
    }
}
