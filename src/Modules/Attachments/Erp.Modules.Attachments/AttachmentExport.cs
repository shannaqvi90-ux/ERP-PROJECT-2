using Erp.Kernel.Data;
using Erp.Modules.Attachments.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Attachments;

/// <summary>Reads the workspace's files for a full tenant export (p14). The tenant is the session's own:
/// the query is limited by row-level security and the store is asked for the key's own tenant.</summary>
internal sealed class AttachmentExport(AttachmentsDbContext db, IAttachmentStore store, ITenantContext tenant) : IAttachmentExport
{
    public async Task<IReadOnlyList<ExportedFile>> ListAsync(Guid? after, int take, CancellationToken cancellationToken)
    {
        var query = db.Files.AsNoTracking().AsQueryable();
        if (after is { } last)
        {
            query = query.Where(f => f.Id.CompareTo(last) > 0);
        }
        return await query.OrderBy(f => f.Id).Take(Math.Clamp(take, 1, 1000))
            .Select(f => new ExportedFile(f.Id, f.RecordType, f.RecordId, f.Name, f.ContentType, f.SizeBytes, f.Sha256, f.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Stream?> OpenAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var query = db.Files.AsNoTracking().Where(f => f.Id == fileId);
        var file = await query.SingleOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return null;
        }
        var key = file.StorageKey;
        return await store.OpenAsync(tenant.TenantId, key, cancellationToken);
    }
}
