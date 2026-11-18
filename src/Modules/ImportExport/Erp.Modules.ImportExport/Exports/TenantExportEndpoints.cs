using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Localization;
using Erp.Kernel.Security;
using Erp.Modules.ImportExport.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.ImportExport.Exports;

/// <summary>A full export of the workspace.</summary>
public sealed record TenantExportDto(
    Guid Id,
    [property: Description("queued, running, succeeded, failed or expired (the file is removed after a few days).")] string Status,
    [property: Description("csv or json: the format of the file kept for each kind of record.")] string Format,
    [property: Description("The background job writing it (see its progress on the Background jobs screen).")] Guid? JobId,
    int FileCount,
    long ByteSize,
    [property: Description("The records written, by kind of record.")] IReadOnlyDictionary<string, long> RecordCounts,
    string? Failure,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset? FinishedAt,
    [property: Description("After this moment the file cannot be downloaded.")] DateTimeOffset? ExpiresAt,
    int DownloadCount,
    DateTimeOffset? DownloadedAt,
    [property: Description("True when the caller may download it now.")] bool Downloadable);

public sealed record TenantExportsDto(IReadOnlyList<TenantExportDto> Items);

/// <summary>Start a full export.</summary>
public sealed record StartTenantExportRequest(
    [property: RegularExpression("^(csv|json)$"), ApiExample("csv"), Description("The format of the file kept for each kind of record: csv (default) or json.")] string? Format);

/// <summary>
/// The full tenant export endpoints (p14, needs-human #44): the workspace's administrator exports everything the workspace
/// holds, as a background job, then downloads it within a few days. Starting, reading and downloading are separate permissions;
/// a download is a write (it counts the download on the export's record and so stays in the audit trail).
/// </summary>
internal static class TenantExportEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/tenant-exports", List)
            .WithName("importexport.exports.list")
            .WithSummary("The workspace's full exports, newest first: state, size, what each holds and when its file expires.")
            .RequirePermission(ImportExportPermissions.ExportsRead);

        group.MapGet("/tenant-exports/{id:guid}", Get)
            .WithName("importexport.exports.get")
            .WithSummary("One full export: its state, counts per kind of record and expiry.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ImportExportPermissions.ExportsRead);

        group.MapPost("/tenant-exports", Start)
            .WithName("importexport.exports.start")
            .WithSummary("Start a full export of everything the workspace holds (every kind of record, with custom fields, and the attachments). It runs in the background; read the export until it has succeeded, then download it. One export runs at a time (409).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Surface(SurfaceKind.Export)
            .RequirePermission(ImportExportPermissions.ExportsCreate);

        group.MapPost("/tenant-exports/{id:guid}/download", Download)
            .WithName("importexport.exports.download")
            .WithSummary("Download a finished full export: a zip file with a manifest, one CSV or JSON file per kind of record and the attachments. Not available before it has succeeded or after it expires (410). Each download is counted on the export's record and stays in the audit trail.")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .Surface(SurfaceKind.Export)
            .RequirePermission(ImportExportPermissions.ExportsDownload);
    }

    private static async Task<Ok<TenantExportsDto>> List(ImportExportDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var items = await db.TenantExports.AsNoTracking().OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(TenantExports.Kept).ToListAsync(cancellationToken);
        return TypedResults.Ok(new TenantExportsDto([.. items.Select(e => ToDto(e, caller, Languages.ForRequest(http)))]));
    }

    private static async Task<Results<Ok<TenantExportDto>, ProblemHttpResult>> Get(Guid id, ImportExportDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var export = await db.TenantExports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return export is null ? Problems.NotFound(http) : TypedResults.Ok(ToDto(export, caller, Languages.ForRequest(http)));
    }

    internal static TenantExportDto ToDto(TenantExport e, ICurrentUser caller, string language)
    {
        var expired = e.Status == ExportStatus.Expired || (e.Status == ExportStatus.Succeeded && e.ExpiresAt < DateTimeOffset.UtcNow);
        var status = expired ? ExportStatus.Expired : e.Status;
        var counts = JsonSerializer.Deserialize<Dictionary<string, long>>(e.RecordCounts, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        return new TenantExportDto(e.Id, status, e.Format, e.JobId, e.FileCount, e.ByteSize, counts,
            language == Languages.Arabic ? e.FailureAr : e.FailureEn, e.FailureCode, e.CreatedAt, e.CreatedBy, e.FinishedAt, e.ExpiresAt, e.DownloadCount, e.DownloadedAt,
            status == ExportStatus.Succeeded && caller.Has(ImportExportPermissions.ExportsDownload));
    }

    private static async Task<Results<Accepted<TenantExportDto>, ProblemHttpResult>> Start(StartTenantExportRequest request, ImportExportDbContext db, ITenantExports exports, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var format = request.Format ?? "csv";
        var validator = new Validator(http).OneOf("format", format, ["csv", "json"]);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        TenantExportTicket ticket;
        try
        {
            ticket = await exports.StartAsync(format, cancellationToken);
        }
        catch (TenantExportRunningException)
        {
            return Problems.Conflict(http, "exportRunning");
        }
        var export = await db.TenantExports.AsNoTracking().SingleAsync(e => e.Id == ticket.Id, cancellationToken);
        return TypedResults.Accepted($"/api/importexport/tenant-exports/{export.Id}", ToDto(export, caller, Languages.ForRequest(http)));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Download(Guid id, ImportExportDbContext db, ITenantContext tenant, HttpContext http, CancellationToken cancellationToken)
    {
        var export = await db.TenantExports.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (export is null)
        {
            return Problems.NotFound(http);
        }
        if (export.Status == ExportStatus.Expired || (export.Status == ExportStatus.Succeeded && export.ExpiresAt < DateTimeOffset.UtcNow))
        {
            return Problems.Result(http, StatusCodes.Status410Gone, "exportExpired");
        }
        if (export.Status != ExportStatus.Succeeded)
        {
            return Problems.Conflict(http, "exportNotReady");
        }
        var content = await db.ExportFiles.AsNoTracking().Where(f => f.ExportId == id).Select(f => f.Content).SingleOrDefaultAsync(cancellationToken);
        if (content is null)
        {
            return Problems.Result(http, StatusCodes.Status410Gone, "exportExpired");
        }
        export.DownloadCount++;
        export.DownloadedAt = DateTimeOffset.UtcNow;
        export.DownloadedBy = tenant.ActorId;
        await db.SaveChangesAsync(cancellationToken);
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(content, "application/zip", $"workspace-export-{export.CreatedAt:yyyyMMdd-HHmm}.zip");
    }
}
