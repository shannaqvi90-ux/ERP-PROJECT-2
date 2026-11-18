using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.ImportExport;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.ImportExport.Files;
using Erp.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.ImportExport.Imports;

/// <summary>A field of a kind of record a file can fill.</summary>
public sealed record ImportFieldDto(
    string Key,
    [property: Description("The field's name in the caller's language.")] string Label,
    string LabelEn,
    string LabelAr,
    bool Required,
    [property: Description("True for a custom field of the workspace.")] bool Custom,
    string? Example);

/// <summary>The fields of a kind of record and the limits a file keeps.</summary>
public sealed record ImportFieldsDto(
    IReadOnlyList<ImportFieldDto> Items,
    [property: Description("Fields of which at least one must be mapped; empty when none is required that way.")] IReadOnlyList<string> OneOfRequired,
    [property: Description("File name extensions accepted.")] IReadOnlyList<string> Formats,
    long MaxFileBytes,
    int MaxRows,
    int MaxColumns,
    [property: Description("The path of the list of these records.")] string? ListPath);

/// <summary>One column of the uploaded file and the field it fills.</summary>
public sealed record ImportColumnDto(
    int Index,
    string Header,
    [property: Description("The key of the field the column fills, or null when it is left out.")] string? Field,
    [property: Description("How the field was chosen: memory (the same header was mapped to it before), name (the header names it) or you (chosen by hand); null when left out.")] string? Source,
    [property: Description("The first values of the column.")] IReadOnlyList<string> Sample);

/// <summary>A problem found in a row of the file.</summary>
public sealed record ImportIssueDto(int Row, string Field, [property: Description("The field's name in the caller's language, or the key when it is not a field.")] string FieldLabel, string Code, [property: Description("What is wrong, in the caller's language.")] string Message);

/// <summary>An uploaded file and what has been done with it.</summary>
public sealed record ImportSessionDto(
    Guid Id,
    string RecordType,
    string FileName,
    string Format,
    [property: Description("uploaded, running, checked, completed or failed.")] string Status,
    int RowCount,
    IReadOnlyList<ImportColumnDto> Columns,
    [property: Description("The first rows of the file, as text, one value per column.")] IReadOnlyList<IReadOnlyList<string>> Rows,
    [property: Description("reject: write nothing when any row has an error; skip: write the valid rows.")] string OnError,
    [property: Description("True when the last run only checked.")] bool DryRun,
    int RowsChecked,
    int RowsValid,
    int RowsImported,
    int RowsFailed,
    [property: Description("The background job of the last run (see it on the Background jobs screen).")] Guid? JobId,
    IReadOnlyList<ImportIssueDto> Issues,
    [property: Description("Why the run stopped, in the caller's language, when it failed.")] string? Failure,
    [property: Description("A code for the failure.")] string? FailureCode,
    [property: Description("True when an error file can be downloaded.")] bool HasErrorFile,
    [property: Description("Required fields no column fills (names in the caller's language); the file cannot be run until they are mapped.")] IReadOnlyList<string> MissingFields,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? FinishedAt,
    uint Version);

/// <summary>The files uploaded for a kind of record, newest first.</summary>
public sealed record ImportSessionsDto(IReadOnlyList<ImportSessionDto> Items);

/// <summary>A file sent as JSON: its name with the extension (property "file") and its bytes as base64. The body of an upload may instead be the bytes themselves.</summary>
public sealed record UploadImportRequest(
    [property: ApiExample("contacts.csv"), StringLength(ImportLimits.MaxFileNameLength)] string? File,
    [property: ApiExample("TmFtZSxFbWFpbAo="), Description("The file's bytes, base64.")] string? Data);

/// <summary>The field each column fills.</summary>
public sealed record MappingColumn(
    [property: Range(0, ImportLimits.MaxColumns - 1)] int Index,
    [property: ApiExample("email"), StringLength(100), Description("The key of the field the column fills; null or empty leaves the column out.")] string? Field);

/// <summary>Set the mapping of the columns. A column not named keeps its field.</summary>
public sealed record SetMappingRequest(
    [property: MaxLength(ImportLimits.MaxColumns)] IReadOnlyList<MappingColumn>? Columns,
    [property: Description("The version of the file's record that was read.")] uint? Version);

/// <summary>Check the file or import it.</summary>
public sealed record RunImportRequest(
    [property: Description("True: check the rows and report, write nothing.")] bool? DryRun,
    [property: RegularExpression("^(reject|skip)$"), ApiExample("reject"), Description("reject (default): write nothing when any row has an error; skip: write the valid rows and report the others.")] string? OnError);

/// <summary>
/// The import endpoints, one set per kind of record registered with <c>ModuleBuilder.ImportRecord</c>:
/// <c>/api/importexport/{schema.table}/…</c>, all guarded by the permission that creates that kind of record.
/// A file is uploaded, its columns are matched to fields (by the memory of earlier imports, then by name), the
/// person may change the mapping, check the file (a dry run that writes nothing), and import it; both run as a
/// background job. The error file holds the rows with their problems.
/// </summary>
internal static class ImportEndpoints
{
    public const string NameHeader = "X-File-Name";
    private const int SampleRows = 5;
    private const int KeptSessions = 30;

    public static void Map(RouteGroupBuilder group)
    {
        var catalog = ((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<ModuleCatalog>();
        foreach (var registered in catalog.ImportRecordTypes)
        {
            var type = registered;
            var name = "importexport.imports." + type.Table;
            var root = $"/{type.Table}";

            group.MapGet(root + "/fields", (HttpContext http, CancellationToken ct) => FieldsAsync(type, http, ct))
                .WithName(name + ".fields")
                .WithSummary($"The fields a file of {type.Table} records can fill (the kind's own and the workspace's custom fields), the fields of which one must be mapped, and the limits a file keeps.")
                .RequirePermission(type.Permission);

            group.MapGet(root + "/template", (HttpContext http, CancellationToken ct) => TemplateAsync(type, http, ct))
                .WithName(name + ".template")
                .WithSummary($"A CSV file with the headers of every field of {type.Table} records and one example row, to fill in and upload.")
                .Produces(StatusCodes.Status200OK, contentType: "text/csv")
                .Surface(SurfaceKind.Import)
                .RequirePermission(type.Permission);

            group.MapGet(root + "/imports", ([FromQuery, Description("Most files returned, 1 to 30 (default 10).")] int? take, ImportExportDbContext db, HttpContext http, CancellationToken ct) => ListAsync(type, take, db, http, ct))
                .WithName(name + ".list")
                .WithSummary($"The files uploaded for {type.Table} records, newest first, with their state.")
                .RequirePermission(type.Permission);

            group.MapPost(root + "/imports", (ImportExportDbContext db, HttpContext http, CancellationToken ct) => UploadAsync(type, db, http, ct))
                .WithName(name + ".upload")
                .WithSummary($"Upload a CSV or XLSX file of {type.Table} records. Either the body is the file's bytes (any content type except JSON; the X-File-Name header is its name; what the screen sends) or JSON with the name and the bytes as base64. The server reads it, matches its columns to fields (by past mappings, then by name) and answers the preview: nothing is written to the records yet.")
                .Accepts<UploadImportRequest>("application/json", "application/octet-stream", "*/*")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .Surface(SurfaceKind.Import)
                .RequirePermission(type.Permission);

            group.MapGet(root + "/imports/{id:guid}", (Guid id, ImportExportDbContext db, HttpContext http, CancellationToken ct) => GetAsync(type, id, db, http, ct))
                .WithName(name + ".get")
                .WithSummary("One uploaded file: its columns with their mapping and first values, the first rows, and the result of the last check or import (counts and the first problems).")
                .ProducesProblem(StatusCodes.Status404NotFound)
                .RequirePermission(type.Permission);

            group.MapPut(root + "/imports/{id:guid}/mapping", (Guid id, SetMappingRequest request, ImportExportDbContext db, HttpContext http, CancellationToken ct) => SetMappingAsync(type, id, request, db, http, ct))
                .WithName(name + ".mapping")
                .WithSummary("Choose the field each column fills (null leaves a column out). The choice is remembered: the next file with the same headers is mapped the same way.")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .RequirePermission(type.Permission);

            group.MapPost(root + "/imports/{id:guid}/run", (Guid id, RunImportRequest request, ImportExportDbContext db, IJobQueue jobs, HttpContext http, CancellationToken ct) => RunAsync(type, id, request, db, jobs, http, ct))
                .WithName(name + ".run")
                .WithSummary("Check the file (dryRun true: every row is validated with the record's own rules and nothing is written) or import it. A file of up to 5,000 rows is processed while the caller waits (200 with the file's state: checked, completed or failed); a larger one runs as a background job (202 with the state running; read it again until it is checked, completed or failed).")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .Surface(SurfaceKind.Import)
                .RequirePermission(type.Permission);

            group.MapGet(root + "/imports/{id:guid}/errors", (Guid id, ImportExportDbContext db, HttpContext http, CancellationToken ct) => ErrorsAsync(type, id, db, http, ct))
                .WithName(name + ".errors")
                .WithSummary("The error file of the last run: a CSV of the rows that had problems, as the file had them, with their row number and what is wrong in English and in Arabic. 404 when the last run found none.")
                .Produces(StatusCodes.Status200OK, contentType: "text/csv")
                .ProducesProblem(StatusCodes.Status404NotFound)
                .Surface(SurfaceKind.Import)
                .RequirePermission(type.Permission);

            group.MapDelete(root + "/imports/{id:guid}", (Guid id, ImportExportDbContext db, HttpContext http, CancellationToken ct) => DeleteAsync(type, id, db, http, ct))
                .WithName(name + ".delete")
                .WithSummary("Discard an uploaded file and its error file. What an import wrote to the records stays. A file that is being checked or imported cannot be discarded (409). Deleting a file that is not there answers 204.")
                .ProducesProblem(StatusCodes.Status409Conflict)
                .Surface(SurfaceKind.Import)
                .RequirePermission(type.Permission);
        }
    }

    private static async Task<Ok<ImportFieldsDto>> FieldsAsync(ImportRecordType type, HttpContext http, CancellationToken cancellationToken)
    {
        var language = Languages.ForRequest(http);
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        return TypedResults.Ok(new ImportFieldsDto([.. fields.Select(f => ToDto(f, language))], type.OneOfRequired ?? [], ["csv", "xlsx"],
            ImportLimits.MaxFileBytes, ImportLimits.MaxRows, ImportLimits.MaxColumns, type.ListPath));
    }

    private static ImportFieldDto ToDto(ImportFieldInfo f, string language) =>
        new(f.Key, language == Languages.Arabic ? f.LabelAr : f.LabelEn, f.LabelEn, f.LabelAr, f.Required, f.Custom, f.Example);

    private static async Task<IResult> TemplateAsync(ImportRecordType type, HttpContext http, CancellationToken cancellationToken)
    {
        var language = Languages.ForRequest(http);
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        var header = string.Join(",", fields.Select(f => ImportEngine.Quote(language == Languages.Arabic ? f.LabelAr : f.LabelEn)));
        var example = string.Join(",", fields.Select(f => ImportEngine.Quote(f.Example)));
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(header + "\r\n" + example + "\r\n")).ToArray();
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(bytes, "text/csv; charset=utf-8", type.Table.Replace('.', '-') + "-template.csv");
    }

    private static async Task<Ok<ImportSessionsDto>> ListAsync(ImportRecordType type, int? take, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(take ?? 10, 1, KeptSessions);
        var sessions = await db.ImportSessions.AsNoTracking().Where(s => s.RecordType == type.Table).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(size).ToListAsync(cancellationToken);
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        var withFile = await HasErrorFilesAsync(db, sessions, cancellationToken);
        return TypedResults.Ok(new ImportSessionsDto([.. sessions.Select(s => ToDto(type, s, fields, withFile.Contains(s.Id), Languages.ForRequest(http)))]));
    }

    private static async Task<HashSet<Guid>> HasErrorFilesAsync(ImportExportDbContext db, IReadOnlyCollection<ImportSession> sessions, CancellationToken cancellationToken)
    {
        var ids = sessions.Select(s => s.Id).ToList();
        return (await db.ImportFiles.AsNoTracking().Where(f => ids.Contains(f.SessionId) && f.ErrorReport != null).Select(f => f.SessionId).ToListAsync(cancellationToken)).ToHashSet();
    }

    private static async Task<Results<Ok<ImportSessionDto>, ProblemHttpResult>> GetAsync(ImportRecordType type, Guid id, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.RecordType == type.Table, cancellationToken);
        if (session is null)
        {
            return Problems.NotFound(http);
        }
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        var hasFile = (await HasErrorFilesAsync(db, [session], cancellationToken)).Contains(id);
        return TypedResults.Ok(ToDto(type, session, fields, hasFile, Languages.ForRequest(http)));
    }

    internal static ImportSessionDto ToDto(ImportRecordType type, ImportSession s, IReadOnlyList<ImportFieldInfo> fields, bool hasErrorFile, string language)
    {
        var arabic = language == Languages.Arabic;
        var headers = JsonSerializer.Deserialize<string[]>(s.Headers, JsonSerializerOptions.Web) ?? [];
        var mapping = JsonSerializer.Deserialize<string?[]>(s.Mapping, JsonSerializerOptions.Web) ?? [];
        var sample = JsonSerializer.Deserialize<string[][]>(s.Sample, JsonSerializerOptions.Web) ?? [];
        var issues = JsonSerializer.Deserialize<StoredIssue[]>(s.Issues, JsonSerializerOptions.Web) ?? [];
        var labels = fields.ToDictionary(f => f.Key, f => arabic ? f.LabelAr : f.LabelEn, StringComparer.Ordinal);
        var sources = SourceOf(s, headers, mapping);
        var columns = headers.Select((h, i) => new ImportColumnDto(i, h, i < mapping.Length ? mapping[i] : null, sources.ElementAtOrDefault(i),
            [.. sample.Select(r => i < r.Length ? r[i] : "")])).ToList();
        var missing = ImportEngine.MissingRequired(type, fields, mapping).Select(f => arabic ? f.LabelAr : f.LabelEn).Distinct().ToList();
        return new ImportSessionDto(s.Id, s.RecordType, s.FileName, s.Format, s.Status, s.RowCount, columns, [.. sample.Select(r => (IReadOnlyList<string>)r)],
            s.OnError, s.DryRun, s.RowsChecked, s.RowsValid, s.RowsImported, s.RowsFailed, s.JobId,
            [.. issues.Select(i => new ImportIssueDto(i.Row, i.Field, labels.GetValueOrDefault(i.Field) ?? i.Field, i.Code, arabic ? i.Ar : i.En))],
            arabic ? s.FailureAr : s.FailureEn, s.FailureCode, hasErrorFile, missing, s.CreatedAt, s.CreatedBy, s.FinishedAt, RowVersions.Hide(s.Version));
    }

    /// <summary>The source of each column's field. Kept in the mapping's own text after the fields: a second JSON array in the same column
    /// would need another column, so the source is derived: a field chosen by the header's own name is "name", any other is "you" or "memory".</summary>
    private static IReadOnlyList<string?> SourceOf(ImportSession s, string[] headers, string?[] mapping) =>
        [.. headers.Select((h, i) => i < mapping.Length && mapping[i] is not null ? SourceFor(s, i) : null)];

    private static string SourceFor(ImportSession s, int index) => s.Sources.Length > index ? s.Sources[index] switch { 'm' => "memory", 'n' => "name", _ => "you" } : "you";

    private static async Task<Results<Created<ImportSessionDto>, ProblemHttpResult>> UploadAsync(
        ImportRecordType type, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var json = http.Request.HasJsonContentType();
        var limit = json ? ImportLimits.MaxFileBytes / 3 * 4 + 4096L : ImportLimits.MaxFileBytes;
        if (http.Request.ContentLength is { } length && length > limit)
        {
            return TooLarge(http);
        }
        string? rawName;
        byte[] bytes;
        var validator = new Validator(http);
        if (json)
        {
            var (name, data, problem) = await ReadJsonAsync(http, limit, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }
            rawName = name;
            bytes = data!;
        }
        else
        {
            rawName = Decoded(http.Request.Headers[NameHeader].ToString());
            var read = await ReadBytesAsync(http.Request.Body, limit, cancellationToken);
            if (read is null)
            {
                return TooLarge(http);
            }
            bytes = read;
        }
        var fileName = ImportEngine.CleanFileName(rawName);
        validator.Must(fileName.Length > 0, "file", "importName");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        Table table;
        try
        {
            table = TabularFile.Read(bytes, fileName);
        }
        catch (TableFormatException problem)
        {
            return validator.Add("file", ImportRunner.CodeOf(problem.Code), problem.Args).ToResult();
        }

        var services = http.RequestServices;
        var fields = await ImportEngine.FieldsAsync(type, services, cancellationToken);
        var remembered = await db.ImportMappings.AsNoTracking().Where(m => m.RecordType == type.Table).ToDictionaryAsync(m => m.HeaderKey, m => m.FieldKey, cancellationToken);
        var (mapping, source) = ImportEngine.Suggest(table.Headers, fields, remembered);
        var session = new ImportSession
        {
            RecordType = type.Table,
            FileName = fileName,
            Format = table.Format,
            Status = ImportStatus.Uploaded,
            RowCount = table.Rows.Count,
            Headers = JsonSerializer.Serialize(table.Headers, JsonSerializerOptions.Web),
            Mapping = JsonSerializer.Serialize(mapping, JsonSerializerOptions.Web),
            Sources = new string(source.Select(x => x switch { "memory" => 'm', "name" => 'n', _ => 'y' }).ToArray()),
            Sample = JsonSerializer.Serialize(table.Rows.Take(SampleRows), JsonSerializerOptions.Web),
        };
        db.ImportSessions.Add(session);
        db.ImportFiles.Add(new ImportFile { SessionId = session.Id, SizeBytes = bytes.Length, Content = bytes });
        await ForgetOldAsync(type, db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/importexport/{type.Table}/imports/{session.Id}", ToDto(type, session, fields, false, Languages.ForRequest(http)));
    }

    /// <summary>Only the latest files are kept: older finished ones (and their bytes) go when a new one arrives.</summary>
    private static async Task ForgetOldAsync(ImportRecordType type, ImportExportDbContext db, CancellationToken cancellationToken)
    {
        var old = await db.ImportSessions.Where(s => s.RecordType == type.Table && s.Status != ImportStatus.Running)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Skip(KeptSessions - 1).ToListAsync(cancellationToken);
        db.ImportSessions.RemoveRange(old);
    }

    private static async Task<byte[]?> ReadBytesAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async Task<(string? Name, byte[]? Bytes, ProblemHttpResult? Problem)> ReadJsonAsync(HttpContext http, long limit, CancellationToken cancellationToken)
    {
        var body = await ReadBytesAsync(http.Request.Body, limit, cancellationToken);
        if (body is null)
        {
            return (null, null, TooLarge(http));
        }
        UploadImportRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<UploadImportRequest>(body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null)
        {
            return (null, null, new Validator(http).Must(false, "body", "importBody").ToResult());
        }
        byte[] bytes = [];
        if (request.Data is { } text)
        {
            var sized = new byte[text.Length / 4 * 3 + 3];
            if (!Convert.TryFromBase64String(text, sized, out var written))
            {
                return (null, null, new Validator(http).Must(false, "data", "importData").ToResult());
            }
            bytes = sized[..written];
        }
        return (request.File, bytes, null);
    }

    private static string? Decoded(string? raw)
    {
        try
        {
            return string.IsNullOrEmpty(raw) ? null : Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static ProblemHttpResult TooLarge(HttpContext http) =>
        Problems.Result(http, StatusCodes.Status413PayloadTooLarge, "importTooLarge", null, ImportLimits.MaxFileBytes / (1024 * 1024));

    private static async Task<Results<Ok<ImportSessionDto>, ProblemHttpResult>> SetMappingAsync(
        ImportRecordType type, Guid id, SetMappingRequest request, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions.SingleOrDefaultAsync(s => s.Id == id && s.RecordType == type.Table, cancellationToken);
        if (session is null)
        {
            return Problems.NotFound(http);
        }
        var validator = new Validator(http).Required("columns", request.Columns?.Count is > 0 ? "x" : null);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (session.Status == ImportStatus.Running)
        {
            return Problems.Conflict(http, "importRunning");
        }
        if (request.Version is { } version && RowVersions.Reveal(version) != session.Version)
        {
            return Problems.Conflict(http, "importVersion");
        }
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        var known = fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var headers = JsonSerializer.Deserialize<string[]>(session.Headers, JsonSerializerOptions.Web) ?? [];
        var mapping = JsonSerializer.Deserialize<string?[]>(session.Mapping, JsonSerializerOptions.Web) ?? new string?[headers.Length];
        var sources = session.Sources.PadRight(headers.Length, 'y').ToCharArray();
        foreach (var column in request.Columns!)
        {
            validator.Must(column.Index >= 0 && column.Index < headers.Length, "columns", "importMappingColumn");
            var field = string.IsNullOrWhiteSpace(column.Field) ? null : column.Field.Trim();
            validator.Must(field is null || known.Contains(field), "columns", "importMappingField");
            if (validator.IsValid)
            {
                mapping[column.Index] = field;
                sources[column.Index] = 'y';
            }
        }
        var chosen = mapping.Where(m => m is not null).Select(m => m!).ToList();
        validator.Must(chosen.Count == chosen.Distinct(StringComparer.Ordinal).Count(), "columns", "importMappingDuplicate");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        session.Mapping = JsonSerializer.Serialize(mapping, JsonSerializerOptions.Web);
        session.Sources = new string(sources);
        // The next file with these headers is mapped by itself.
        var remembered = await db.ImportMappings.Where(m => m.RecordType == type.Table).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < headers.Length; i++)
        {
            var key = ImportEngine.Normalize(headers[i]);
            if (key.Length == 0 || key.Length > 200)
            {
                continue;
            }
            var existing = remembered.FirstOrDefault(m => m.HeaderKey == key);
            if (mapping[i] is null)
            {
                if (existing is not null)
                {
                    db.ImportMappings.Remove(existing);
                }
            }
            else if (existing is null)
            {
                db.ImportMappings.Add(new ImportMapping { RecordType = type.Table, HeaderKey = key, FieldKey = mapping[i]!, LastUsedAt = now });
            }
            else if (existing.FieldKey != mapping[i])
            {
                existing.FieldKey = mapping[i]!;
                existing.LastUsedAt = now;
            }
        }
        if (session.Status is ImportStatus.Checked or ImportStatus.Failed)
        {
            // A new mapping makes an earlier check out of date.
            session.Status = ImportStatus.Uploaded;
        }
        await db.SaveChangesAsync(cancellationToken);
        var hasFile = (await HasErrorFilesAsync(db, [session], cancellationToken)).Contains(id);
        return TypedResults.Ok(ToDto(type, session, fields, hasFile, Languages.ForRequest(http)));
    }

    private static async Task<Results<Ok<ImportSessionDto>, Accepted<ImportSessionDto>, ProblemHttpResult>> RunAsync(
        ImportRecordType type, Guid id, RunImportRequest request, ImportExportDbContext db, IJobQueue jobs, HttpContext http, CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions.SingleOrDefaultAsync(s => s.Id == id && s.RecordType == type.Table, cancellationToken);
        if (session is null)
        {
            return Problems.NotFound(http);
        }
        var validator = new Validator(http);
        var onError = request.OnError ?? ImportOnError.Reject;
        validator.OneOf("onError", onError, ImportOnError.All.ToList());
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (session.Status == ImportStatus.Running)
        {
            return Problems.Conflict(http, "importRunning");
        }
        if (session.Status == ImportStatus.Completed)
        {
            return Problems.Conflict(http, "importDone");
        }
        var fields = await ImportEngine.FieldsAsync(type, http.RequestServices, cancellationToken);
        var mapping = JsonSerializer.Deserialize<string?[]>(session.Mapping, JsonSerializerOptions.Web) ?? [];
        var language = Languages.ForRequest(http);
        var missing = ImportEngine.MissingRequired(type, fields, mapping);
        if (missing.Count > 0)
        {
            return validator.Add("columns", "importMappingIncomplete", string.Join(", ", missing.Select(f => language == Languages.Arabic ? f.LabelAr : f.LabelEn).Distinct())).ToResult();
        }
        session.DryRun = request.DryRun == true;
        session.OnError = onError;
        session.Issues = "[]";
        session.RowsChecked = session.RowsValid = session.RowsImported = session.RowsFailed = 0;
        session.FailureCode = session.FailureEn = session.FailureAr = null;
        session.FinishedAt = null;
        session.Status = ImportStatus.Running;
        if (session.RowCount <= ImportRunner.InlineRows)
        {
            // A small file is processed while the person waits: no background job is needed for a few seconds' work.
            var file = await db.ImportFiles.SingleAsync(f => f.SessionId == id, cancellationToken);
            await ImportRunner.RunAsync(http.RequestServices, db, session, file, null, cancellationToken);
            return TypedResults.Ok(ToDto(type, session, fields, file.ErrorReport is not null, language));
        }
        var label = new JobText(session.FileName, session.FileName);
        var ticket = await jobs.EnqueueAsync(new JobRequest(ImportJobHandler.TypeKey, new { sessionId = session.Id }, label, Priority: 0, Source: JobSource.Module), cancellationToken);
        session.JobId = ticket.Id;
        await db.SaveChangesAsync(cancellationToken);
        var hasFile = (await HasErrorFilesAsync(db, [session], cancellationToken)).Contains(id);
        return TypedResults.Accepted($"/api/importexport/{type.Table}/imports/{session.Id}", ToDto(type, session, fields, hasFile, language));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ErrorsAsync(ImportRecordType type, Guid id, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions.AsNoTracking().Where(s => s.Id == id && s.RecordType == type.Table).Select(s => new { s.Id, s.FileName }).SingleOrDefaultAsync(cancellationToken);
        var report = session is null ? null : await db.ImportFiles.AsNoTracking().Where(f => f.SessionId == id).Select(f => f.ErrorReport).SingleOrDefaultAsync(cancellationToken);
        if (session is null || report is null)
        {
            return Problems.NotFound(http);
        }
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        var name = Path.GetFileNameWithoutExtension(session.FileName);
        return TypedResults.File(report, "text/csv; charset=utf-8", (name.Length == 0 ? "import" : name) + "-errors.csv");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(ImportRecordType type, Guid id, ImportExportDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions.SingleOrDefaultAsync(s => s.Id == id && s.RecordType == type.Table, cancellationToken);
        if (session is null)
        {
            return TypedResults.NoContent();
        }
        if (session.Status == ImportStatus.Running)
        {
            return Problems.Conflict(http, "importRunning");
        }
        db.ImportSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
