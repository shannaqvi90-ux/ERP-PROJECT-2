using Erp.Kernel.Data;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.ImportExport.Exports;

/// <summary>Starts a full export of the current workspace (see <see cref="ITenantExports"/>).</summary>
internal sealed class TenantExports(ImportExportDbContext db, IJobQueue jobs) : ITenantExports
{
    /// <summary>How long a finished export can be downloaded.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>How many exports are kept, newest first (older ones go with their files).</summary>
    public const int Kept = 20;

    public async Task<TenantExportTicket> StartAsync(string format, CancellationToken cancellationToken)
    {
        if (format is not ("csv" or "json"))
        {
            throw new ArgumentException("The format is csv or json.", nameof(format));
        }
        await ForgetAsync(db, cancellationToken);
        if (await db.TenantExports.AnyAsync(e => e.Status == ExportStatus.Queued || e.Status == ExportStatus.Running, cancellationToken))
        {
            throw new TenantExportRunningException();
        }
        var export = new TenantExport { Format = format };
        db.TenantExports.Add(export);
        var ticket = await jobs.EnqueueAsync(new JobRequest(TenantExportJobHandler.TypeKey, new { exportId = export.Id },
            new JobText("Full export of the workspace", "تصدير كامل لبيانات مساحة العمل"), Source: JobSource.Module), cancellationToken);
        export.JobId = ticket.Id;
        await db.SaveChangesAsync(cancellationToken);
        return new TenantExportTicket(export.Id, export.Status);
    }

    /// <summary>Removes the files of exports past their lifetime (the record stays, as expired) and the oldest records beyond those kept.</summary>
    internal static async Task ForgetAsync(ImportExportDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.TenantExports.Where(e => e.Status == ExportStatus.Succeeded && e.ExpiresAt != null && e.ExpiresAt < now).ToListAsync(cancellationToken);
        foreach (var export in expired)
        {
            export.Status = ExportStatus.Expired;
        }
        var expiredIds = expired.Select(e => e.Id).ToList();
        var files = await db.ExportFiles.Where(f => expiredIds.Contains(f.ExportId)).ToListAsync(cancellationToken);
        db.ExportFiles.RemoveRange(files);
        var old = await db.TenantExports.Where(e => e.Status != ExportStatus.Queued && e.Status != ExportStatus.Running)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip(Kept - 1).ToListAsync(cancellationToken);
        db.TenantExports.RemoveRange(old);
    }
}

/// <summary>An export is already queued or running in the workspace.</summary>
public sealed class TenantExportRunningException() : Exception("A full export is already running.");
