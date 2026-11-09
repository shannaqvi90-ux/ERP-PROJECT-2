using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Erp.Kernel.Attachments;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Attachments.Contracts;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Attachments;

/// <summary>A file attached to a record.</summary>
public sealed record AttachmentDto(
    Guid Id,
    [property: Description("The name to show and to save the file as.")] string Name,
    [property: Description("The type the server decided from the extension and the bytes.")] string ContentType,
    long SizeBytes,
    [property: Description("Lower-case hex SHA-256 of the bytes.")] string Sha256,
    [property: Description("True for images and PDF: the content endpoint can show them in the page (inline=true).")] bool Previewable,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    string? CreatedByName,
    string? CreatedByNameAr);

/// <summary>A file sent as JSON: its name with the extension, and its bytes as base64. The body of an upload may instead be the bytes themselves.</summary>
public sealed record UploadFileRequest(
    [property: ApiExample("licence-copy.txt"), StringLength(AttachmentLimits.MaxNameLength)] string? Name,
    [property: ApiExample("TGljZW5jZSBjb3B5Cg=="), Description("The file's bytes, base64.")] string? Data);

/// <summary>A record's files and the limits an upload must keep.</summary>
public sealed record AttachmentListDto(
    IReadOnlyList<AttachmentDto> Items,
    int Total,
    [property: Description("Most files one record may hold.")] int MaxFiles,
    [property: Description("Largest file, in bytes.")] long MaxFileBytes,
    [property: Description("File name extensions accepted, lower case.")] IReadOnlyList<string> Extensions);

/// <summary>
/// The endpoints of attachments, one set per kind of record registered with
/// <c>ModuleBuilder.AttachmentRecord</c>: <c>/api/attachments/{schema.table}/{id}/files</c>. A set's
/// reads need the record's read permission and its writes the record's write permission, and every
/// call first finds the record in the caller's workspace (row-level security): a record that is not
/// there, or is another workspace's, answers 404 whatever else the call says.
/// </summary>
public static class AttachmentEndpoints
{
    /// <summary>The request header that carries an uploaded file's name (percent-encoded UTF-8).</summary>
    public const string NameHeader = "X-File-Name";

    public static void Map(RouteGroupBuilder group)
    {
        var catalog = ((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<ModuleCatalog>();
        foreach (var type in catalog.AttachmentRecordTypes)
        {
            var record = type;
            var baseName = "attachments." + record.Table;
            var root = $"/{record.Table}";

            group.MapGet(root + "/{id:guid}/files", (Guid id, AttachmentsDbContext db, IUserDirectory users, HttpContext http, CancellationToken ct) => ListAsync(record, id, db, users, http, ct))
                .WithName(baseName + ".list")
                .WithSummary($"The files attached to a record of kind {record.Table} (id is the record), newest first, with the limits an upload must keep.")
                .RequirePermission(record.ReadPermission);

            group.MapPost(root + "/{id:guid}/files", (Guid id, [FromHeader(Name = NameHeader), Description("The file's name with its extension, as the user knows it, percent-encoded as UTF-8 (folders are dropped). Used when the body is the file's bytes; with a JSON body the name is in the body.")] string? name,
                    AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, IUserDirectory users, HttpContext http, CancellationToken ct) => UploadAsync(record, id, name, db, store, tenant, users, http, ct))
                .WithName(baseName + ".upload")
                .WithSummary($"Attach one file to a record of kind {record.Table} (id is the record). Either the request body is the file's bytes (any content type except JSON; the X-File-Name header is its name; what the screen sends) or it is JSON with the name and the bytes as base64 (for API clients). Several files are several calls. Checked on the server: size, type from the extension and the bytes, count per record.")
                .Accepts<UploadFileRequest>("application/json")
                .Accepts<byte[]>("application/octet-stream", "*/*")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .Surface(SurfaceKind.File)
                .RequirePermission(record.WritePermission);

            group.MapGet(root + "/files/{id:guid}/content", (Guid id, [FromQuery, Description("True to show an image or PDF in the page; otherwise the file is offered for saving.")] bool? inline,
                    AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken ct) => ContentAsync(record, id, inline == true, db, store, tenant, http, ct))
                .WithName(baseName + ".content")
                .WithSummary($"The bytes of one file (id is the file) of a record of kind {record.Table}, to save or (images and PDF, inline=true) to show.")
                .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
                .ProducesProblem(StatusCodes.Status404NotFound)
                .Surface(SurfaceKind.File)
                .RequirePermission(record.ReadPermission);

            group.MapDelete(root + "/files/{id:guid}", (Guid id, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken ct) => DeleteAsync(record, id, db, store, tenant, http, ct))
                .WithName(baseName + ".delete")
                .WithSummary($"Remove one file (id is the file) of a record of kind {record.Table}; its bytes are removed with it and the removal stays in the audit trail. Deleting a file that is not there (already removed, or never there) answers 204 as well.")
                .Surface(SurfaceKind.File)
                .RequirePermission(record.WritePermission);
        }
    }

    private static async Task<Results<Ok<AttachmentListDto>, ProblemHttpResult>> ListAsync(
        AttachmentRecordType record, Guid id, AttachmentsDbContext db, IUserDirectory users, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await record.Exists(http.RequestServices, id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var files = await db.Files.AsNoTracking().Where(f => f.RecordType == record.Table && f.RecordId == id)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id).Take(AttachmentLimits.MaxFilesPerRecord).ToListAsync(cancellationToken);
        var names = await users.GetAsync([.. files.Where(f => f.CreatedBy is not null).Select(f => f.CreatedBy!.Value).Distinct()], cancellationToken);
        return TypedResults.Ok(new AttachmentListDto([.. files.Select(f => ToDto(f, names))], files.Count, AttachmentLimits.MaxFilesPerRecord, AttachmentLimits.MaxFileBytes, FileTypes.Extensions));
    }

    private static async Task<Results<Created<AttachmentDto>, ProblemHttpResult>> UploadAsync(
        AttachmentRecordType record, Guid id, string? rawName, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, IUserDirectory users, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await record.Exists(http.RequestServices, id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        // The body is the file's bytes, or JSON holding the name and the bytes as base64.
        var json = http.Request.HasJsonContentType();
        if (http.Request.ContentLength is { } length && length > (json ? JsonBodyLimit : AttachmentLimits.MaxFileBytes))
        {
            return TooLarge(http);
        }
        Stream body = http.Request.Body;
        if (json)
        {
            var parsed = await ReadJsonAsync(http, cancellationToken);
            if (parsed.Problem is not null)
            {
                return parsed.Problem;
            }
            rawName = parsed.Name;
            body = new MemoryStream(parsed.Bytes ?? []);
        }
        var name = FileTypes.CleanName(json ? rawName : Decoded(rawName), AttachmentLimits.MaxNameLength);
        var validator = new Validator(http).Must(name is not null, "name", "attachmentsName");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var kind = FileTypes.ForName(name!);
        if (kind is null)
        {
            return Problems.Result(http, StatusCodes.Status415UnsupportedMediaType, "attachmentsType", new Dictionary<string, FieldError[]>
            {
                ["name"] = [new FieldError("attachmentsType", Problems.Create(http, StatusCodes.Status415UnsupportedMediaType, "attachmentsType").Title ?? "attachmentsType")],
            });
        }
        if (await db.Files.CountAsync(f => f.RecordType == record.Table && f.RecordId == id, cancellationToken) >= AttachmentLimits.MaxFilesPerRecord)
        {
            return Problems.Result(http, StatusCodes.Status409Conflict, "attachmentsTooMany", null, AttachmentLimits.MaxFilesPerRecord);
        }

        // The tenant is read now: the unit of work has ended by the time the response completes.
        var tenantId = tenant.TenantId;
        StoredBlob blob;
        try
        {
            blob = await store.SaveAsync(tenantId, body, AttachmentLimits.MaxFileBytes, cancellationToken);
        }
        catch (BlobTooLargeException)
        {
            return TooLarge(http);
        }
        // Whatever happens next, the bytes of a request that does not end in a saved row go with it
        // (a failed check below, or a failed commit after this method returns).
        http.Response.OnCompleted(async () =>
        {
            if (http.Response.StatusCode >= StatusCodes.Status300MultipleChoices)
            {
                await store.DeleteAsync(tenantId, blob.Key, CancellationToken.None);
            }
        });
        if (blob.Size == 0)
        {
            return Problems.Result(http, StatusCodes.Status400BadRequest, "validation", new Dictionary<string, FieldError[]>
            {
                ["file"] = [new FieldError("attachmentsEmpty", Problems.Create(http, StatusCodes.Status400BadRequest, "attachmentsEmpty").Title ?? "attachmentsEmpty")],
            });
        }
        if (!await FileTypes.MatchesAsync(kind, blob.Head, () => store.OpenAsync(tenantId, blob.Key, cancellationToken)))
        {
            return Problems.Result(http, StatusCodes.Status415UnsupportedMediaType, "attachmentsContent", new Dictionary<string, FieldError[]>
            {
                ["file"] = [new FieldError("attachmentsContent", Problems.Create(http, StatusCodes.Status415UnsupportedMediaType, "attachmentsContent").Title ?? "attachmentsContent")],
            });
        }

        var file = new AttachedFile
        {
            RecordType = record.Table,
            RecordId = id,
            Name = name!,
            ContentType = kind.ContentType,
            SizeBytes = blob.Size,
            Sha256 = blob.Sha256,
            StorageKey = blob.Key,
        };
        db.Files.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        var names = tenant.ActorId is { } actor ? await users.GetAsync([actor], cancellationToken) : new Dictionary<Guid, UserSummary>();
        return TypedResults.Created($"/api/attachments/{record.Table}/files/{file.Id}/content", ToDto(file, names));
    }

    /// <summary>Most a JSON body may hold: a file of the limit as base64 (four characters for three bytes) and its name.</summary>
    private const long JsonBodyLimit = AttachmentLimits.MaxFileBytes / 3 * 4 + 4096;

    /// <summary>The name and the bytes of a JSON upload, or the problem to answer.</summary>
    private static async Task<(string? Name, byte[]? Bytes, ProblemHttpResult? Problem)> ReadJsonAsync(HttpContext http, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > JsonBodyLimit)
            {
                return (null, null, TooLarge(http));
            }
            buffer.Write(chunk, 0, read);
        }
        UploadFileRequest? request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize<UploadFileRequest>(buffer.ToArray(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch (System.Text.Json.JsonException)
        {
            request = null;
        }
        if (request is null)
        {
            return (null, null, new Validator(http).Must(false, "body", "attachmentsBody").ToResult());
        }
        byte[]? bytes = null;
        if (request.Data is { } text)
        {
            var sized = new byte[text.Length / 4 * 3 + 3];
            if (!Convert.TryFromBase64String(text, sized, out var written))
            {
                return (null, null, new Validator(http).Must(false, "data", "attachmentsData").ToResult());
            }
            bytes = sized[..written];
        }
        return (request.Name, bytes ?? [], null);
    }

    /// <summary>The header's name, percent-decoded; null when it is not valid encoded text.</summary>
    private static string? Decoded(string? raw)
    {
        try
        {
            return raw is null ? null : Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static ProblemHttpResult TooLarge(HttpContext http) =>
        Problems.Result(http, StatusCodes.Status413PayloadTooLarge, "attachmentsTooLarge", null, AttachmentLimits.MaxFileBytes / (1024 * 1024));

    /// <summary>The file with this id of this kind of record, and its record visible to the caller; null otherwise (no such file, another
    /// workspace's, another kind's, or a record the caller's company scope hides).</summary>
    private static async Task<AttachedFile?> FindAsync(AttachmentRecordType record, Guid fileId, AttachmentsDbContext db, HttpContext http, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.Files.AsQueryable() : db.Files.AsNoTracking();
        var file = await query.SingleOrDefaultAsync(f => f.Id == fileId && f.RecordType == record.Table, cancellationToken);
        return file is not null && await record.Exists(http.RequestServices, file.RecordId, cancellationToken) ? file : null;
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ContentAsync(
        AttachmentRecordType record, Guid fileId, bool inline, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken cancellationToken)
    {
        var file = await FindAsync(record, fileId, db, http, tracked: false, cancellationToken);
        var stream = file is null ? null : await store.OpenAsync(tenant.TenantId, file.StorageKey, cancellationToken);
        if (file is null || stream is null)
        {
            return Problems.NotFound(http);
        }
        var previewable = FileTypes.ForName(file.Name)?.Inline == true;
        var show = inline && previewable;
        var headers = http.Response.Headers;
        var disposition = new ContentDispositionHeaderValue(show ? "inline" : "attachment");
        disposition.SetHttpFileName(file.Name);
        headers.ContentDisposition = disposition.ToString();
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        if (show && file.ContentType == "application/pdf")
        {
            // A PDF shown in the page may sit in a frame of this origin (the application's own headers forbid framing everything else).
            headers.XFrameOptions = "SAMEORIGIN";
            headers.ContentSecurityPolicy = "frame-ancestors 'self'";
        }
        else
        {
            // A picture is never run as a page, and a download is never rendered.
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        }
        return TypedResults.Stream(stream, file.ContentType, enableRangeProcessing: false);
    }

    private static async Task<NoContent> DeleteAsync(
        AttachmentRecordType record, Guid fileId, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken cancellationToken)
    {
        // Deleting what is not there is a success: the file is gone either way, and a caller cannot tell a
        // file that never existed from another workspace's (row-level security hides it).
        var file = await FindAsync(record, fileId, db, http, tracked: true, cancellationToken);
        if (file is null)
        {
            return TypedResults.NoContent();
        }
        db.Files.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        var key = file.StorageKey;
        var tenantId = tenant.TenantId;
        // The bytes go once the removal is committed (a failed commit answers an error, and they stay).
        http.Response.OnCompleted(async () =>
        {
            if (http.Response.StatusCode < StatusCodes.Status300MultipleChoices)
            {
                await store.DeleteAsync(tenantId, key, CancellationToken.None);
            }
        });
        return TypedResults.NoContent();
    }

    internal static AttachmentDto ToDto(AttachedFile f, IReadOnlyDictionary<Guid, UserSummary> names)
    {
        var by = f.CreatedBy is { } id && names.TryGetValue(id, out var user) ? user : null;
        return new AttachmentDto(f.Id, f.Name, f.ContentType, f.SizeBytes, f.Sha256, FileTypes.ForName(f.Name)?.Inline == true, f.CreatedAt, f.CreatedBy, by?.DisplayName, by?.DisplayNameAr);
    }
}
