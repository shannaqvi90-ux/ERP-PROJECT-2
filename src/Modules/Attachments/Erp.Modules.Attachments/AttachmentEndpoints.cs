using System.ComponentModel;
using Erp.Kernel.Attachments;
using Erp.Kernel.Data;
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
            var root = $"/{record.Table}/{{id:guid}}/files";

            group.MapGet(root, (Guid id, AttachmentsDbContext db, IUserDirectory users, HttpContext http, CancellationToken ct) => ListAsync(record, id, db, users, http, ct))
                .WithName(baseName + ".list")
                .WithSummary($"The files attached to a record of kind {record.Table}, newest first, with the limits an upload must keep.")
                .RequirePermission(record.ReadPermission);

            group.MapPost(root, (Guid id, [FromHeader(Name = NameHeader), Description("The file's name with its extension, as the user knows it, percent-encoded as UTF-8 (folders are dropped).")] string? name, AttachmentsDbContext db, IAttachmentStore store,
                    ITenantContext tenant, IUserDirectory users, HttpContext http, CancellationToken ct) => UploadAsync(record, id, name, db, store, tenant, users, http, ct))
                .WithName(baseName + ".upload")
                .WithSummary($"Attach one file to a record of kind {record.Table}: the request body is the file's bytes (any content type), the X-File-Name header is its name. Several files are several calls. Checked on the server: size, type from the extension and the bytes, count per record.")
                .Accepts<byte[]>("application/octet-stream", "*/*")
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .Surface(SurfaceKind.File)
                .RequirePermission(record.WritePermission);

            group.MapGet(root + "/{fileId:guid}/content", (Guid id, Guid fileId, [FromQuery, Description("True to show an image or PDF in the page; otherwise the file is offered for saving.")] bool? inline,
                    AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken ct) => ContentAsync(record, id, fileId, inline == true, db, store, tenant, http, ct))
                .WithName(baseName + ".content")
                .WithSummary($"The bytes of one file of a record of kind {record.Table}, to save or (images and PDF, inline=true) to show.")
                .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
                .ProducesProblem(StatusCodes.Status404NotFound)
                .Surface(SurfaceKind.File)
                .RequirePermission(record.ReadPermission);

            group.MapDelete(root + "/{fileId:guid}", (Guid id, Guid fileId, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken ct) => DeleteAsync(record, id, fileId, db, store, tenant, http, ct))
                .WithName(baseName + ".delete")
                .WithSummary($"Remove one file of a record of kind {record.Table}; its bytes are removed with it. The removal stays in the audit trail.")
                .ProducesProblem(StatusCodes.Status404NotFound)
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
        var name = FileTypes.CleanName(Decoded(rawName), AttachmentLimits.MaxNameLength);
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
        if (http.Request.ContentLength is { } length && length > AttachmentLimits.MaxFileBytes)
        {
            return TooLarge(http);
        }
        if (await db.Files.CountAsync(f => f.RecordType == record.Table && f.RecordId == id, cancellationToken) >= AttachmentLimits.MaxFilesPerRecord)
        {
            return Problems.Result(http, StatusCodes.Status409Conflict, "attachmentsTooMany", null, AttachmentLimits.MaxFilesPerRecord);
        }

        StoredBlob blob;
        try
        {
            blob = await store.SaveAsync(tenant.TenantId, http.Request.Body, AttachmentLimits.MaxFileBytes, cancellationToken);
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
                await store.DeleteAsync(tenant.TenantId, blob.Key, CancellationToken.None);
            }
        });
        if (blob.Size == 0)
        {
            return Problems.Result(http, StatusCodes.Status400BadRequest, "validation", new Dictionary<string, FieldError[]>
            {
                ["file"] = [new FieldError("attachmentsEmpty", Problems.Create(http, StatusCodes.Status400BadRequest, "attachmentsEmpty").Title ?? "attachmentsEmpty")],
            });
        }
        if (!await FileTypes.MatchesAsync(kind, blob.Head, () => store.OpenAsync(tenant.TenantId, blob.Key, cancellationToken)))
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
        return TypedResults.Created($"/api/attachments/{record.Table}/{id}/files/{file.Id}/content", ToDto(file, names));
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

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ContentAsync(
        AttachmentRecordType record, Guid id, Guid fileId, bool inline, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await record.Exists(http.RequestServices, id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var file = await db.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId && f.RecordType == record.Table && f.RecordId == id, cancellationToken);
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
        if (show)
        {
            // Shown in the page: a PDF may sit in a frame of this origin; an image is never run as a page.
            if (file.ContentType == "application/pdf")
            {
                headers.XFrameOptions = "SAMEORIGIN";
                headers.ContentSecurityPolicy = "frame-ancestors 'self'";
            }
            else
            {
                headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            }
        }
        else
        {
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        }
        return TypedResults.Stream(stream, file.ContentType, enableRangeProcessing: false);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        AttachmentRecordType record, Guid id, Guid fileId, AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await record.Exists(http.RequestServices, id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var file = await db.Files.SingleOrDefaultAsync(f => f.Id == fileId && f.RecordType == record.Table && f.RecordId == id, cancellationToken);
        if (file is null)
        {
            return Problems.NotFound(http);
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
