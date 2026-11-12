using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Modules.Attachments.Contracts;
using Erp.Modules.Attachments.Seeding;
using Erp.Modules.Audit.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Modules.Attachments;

/// <summary>
/// Attachments (p11): files on any record whose module registers its kind of record
/// (<c>ModuleBuilder.AttachmentRecord</c>). Upload (several files at once), list, download, preview
/// of images and PDF, and delete. The bytes live behind <see cref="IAttachmentStore"/> (a local
/// volume by default); the database keeps one row per file with the record it belongs to. The
/// endpoints are the record's own: one set per registered kind, under the record's read permission
/// (list, download, preview) and write permission (add, delete), so a file is as private as its record
/// (docs/decisions/p11-attachments-record-permissions.md).
/// </summary>
public sealed class AttachmentsModule : ErpModule
{
    public override string Name => "attachments";

    public override void Register(ModuleBuilder module)
    {
        module.DbContext<AttachmentsDbContext>();
        module.Services.TryAddSingleton<IAttachmentStore, LocalAttachmentStore>();
        module.Endpoints(AttachmentEndpoints.Map);
        // The audit trail shows every upload and delete (p07) to whoever reads the whole log.
        module.AuditedRecord(new AuditRecordType("attachments.files", "attachments.record.file", AuditPermissions.LogRead, null));
        module.IsolationProbe<AttachmentsIsolationProbe>();
        module.Seeder<AttachmentsSeeder>();
    }
}

/// <summary>One file attached to a record. The bytes are in the store under <see cref="StorageKey"/>,
/// a name the store made.</summary>
public sealed class AttachedFile : TenantEntity
{
    /// <summary>The kind of record (<c>schema.table</c> of a registered attachment record type).</summary>
    public string RecordType { get; set; } = "";

    public Guid RecordId { get; set; }

    /// <summary>The name shown and offered when downloading: cleaned of folders and control characters,
    /// never used to find or make a file.</summary>
    public string Name { get; set; } = "";

    /// <summary>The type the server decided from the extension and the file's bytes.</summary>
    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>Lower-case hex SHA-256 of the bytes.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>The store's name for the bytes: the tenant and a random identifier, 32 hex digits each.</summary>
    public string StorageKey { get; set; } = "";
}

public sealed class AttachmentsDbContext(DbContextOptions<AttachmentsDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "attachments";

    protected override string Schema => SchemaName;

    public DbSet<AttachedFile> Files => Set<AttachedFile>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttachedFile>(e =>
        {
            e.ToTable("files", t =>
            {
                t.HasCheckConstraint("ck_files_size", "size_bytes BETWEEN 1 AND 1073741824");
                t.HasCheckConstraint("ck_files_name", "btrim(name) <> ''");
                // The store key is the file's tenant and a random identifier: a row never points at another
                // tenant's bytes.
                t.HasCheckConstraint("ck_files_storage_key", "storage_key ~ '^[0-9a-f]{32}/[0-9a-f]{32}$' AND left(storage_key, 32) = replace(tenant_id::text, '-', '')");
                t.HasCheckConstraint("ck_files_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
            });
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(AttachmentLimits.MaxNameLength);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.StorageKey).HasMaxLength(65);
            // A record's files, newest first.
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.RecordId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.StorageKey }).IsUnique();
        });
    }
}

internal sealed class AttachmentsDbContextDesignFactory : IDesignTimeDbContextFactory<AttachmentsDbContext>
{
    public AttachmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AttachmentsDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", AttachmentsDbContext.SchemaName);
        return new AttachmentsDbContext(options.Options);
    }
}
