using Erp.Kernel.ImportExport;
using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.ImportExport.Contracts;
using Erp.Modules.ImportExport.Exports;
using Erp.Modules.ImportExport.Imports;
using Erp.Modules.ImportExport.Seeding;
using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.ImportExport;

/// <summary>
/// Import and export (p14). Imports: CSV and XLSX files into every kind of record whose module registers it
/// (<c>ModuleBuilder.ImportRecord</c>): upload, column mapping with the memory of earlier mappings, a check
/// (dry run) with a report, the import as a background job, and a downloadable error file. Full tenant export:
/// the administrator's export of everything the workspace holds, as a background job, with a manifest, one
/// CSV or JSON file per kind of record and the attachments, downloaded within a few days and audited.
/// Exporting a filtered list is the reports module's (CSV and XLSX of the list as the screen shows it);
/// the list screens call it from their toolbar.
/// </summary>
public sealed class ImportExportModule : ErpModule
{
    public override string Name => "importexport";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. ImportExportPermissions.All]);
        module.DbContext<ImportExportDbContext>();
        module.Services.AddScoped<ITenantExports, TenantExports>();
        module.Endpoints(ImportEndpoints.Map);
        module.Endpoints(TenantExportEndpoints.Map);
        module.Services.AddJobType<ImportJobHandler>(ImportJobHandler.Type);
        module.Services.AddJobType<TenantExportJobHandler>(TenantExportJobHandler.Type);
        module.Menu(new MenuEntry("importexport.exports", "importexport.menu.exports", "/importexport/exports", ImportExportPermissions.ExportsRead, Order: 905, Group: "settings"));
        module.AuditedRecord(new AuditRecordType("importexport.import_sessions", "importexport.record.import", Erp.Modules.Audit.Contracts.AuditPermissions.LogRead, null));
        module.AuditedRecord(new AuditRecordType("importexport.import_files", "importexport.record.importFile", Erp.Modules.Audit.Contracts.AuditPermissions.LogRead, null));
        module.AuditedRecord(new AuditRecordType("importexport.export_files", "importexport.record.exportFile", Erp.Modules.Audit.Contracts.AuditPermissions.LogRead, null));
        module.AuditedRecord(new AuditRecordType("importexport.import_mappings", "importexport.record.mapping", Erp.Modules.Audit.Contracts.AuditPermissions.LogRead, null));
        module.AuditedRecord(new AuditRecordType("importexport.tenant_exports", "importexport.record.export", ImportExportPermissions.ExportsRead, null));
        module.IsolationProbe<ImportExportIsolationProbe>();
        module.Seeder<ImportExportSeeder>();
    }
}

/// <summary>One uploaded file and what has been done with it: its columns, the mapping to fields, the last check or import.</summary>
public sealed class ImportSession : TenantEntity
{
    /// <summary><c>schema.table</c> of the kind of record the file is imported into.</summary>
    public string RecordType { get; set; } = "";

    public string FileName { get; set; } = "";

    /// <summary><c>csv</c> or <c>xlsx</c>.</summary>
    public string Format { get; set; } = "csv";

    public string Status { get; set; } = ImportStatus.Uploaded;

    public int RowCount { get; set; }

    /// <summary>The file's header row (JSON array of text).</summary>
    public string Headers { get; set; } = "[]";

    /// <summary>The field each column fills, by column (JSON array of field keys, null for a column left out).</summary>
    public string Mapping { get; set; } = "[]";

    /// <summary>How each column's field was chosen, one letter per column: m (memory), n (name), y (by hand).</summary>
    public string Sources { get; set; } = "";

    /// <summary>The first rows of the file, for the preview (JSON array of arrays).</summary>
    public string Sample { get; set; } = "[]";

    /// <summary>What a run does with rows that have errors (<see cref="ImportOnError"/>).</summary>
    public string OnError { get; set; } = ImportOnError.Reject;

    /// <summary>True when the last run only checked.</summary>
    public bool DryRun { get; set; }

    public int RowsChecked { get; set; }
    public int RowsValid { get; set; }
    public int RowsImported { get; set; }
    public int RowsFailed { get; set; }

    /// <summary>Rows that match a record already there (found by the last run).</summary>
    public int RowsDuplicate { get; set; }

    /// <summary>Rows that changed a record already there (<see cref="ImportOnDuplicate.Update"/>).</summary>
    public int RowsUpdated { get; set; }

    /// <summary>What a run does with rows that match a record already there (<see cref="ImportOnDuplicate"/>).</summary>
    public string OnDuplicate { get; set; } = ImportOnDuplicate.Create;

    public Guid? JobId { get; set; }

    /// <summary>The first problems found, for the screen (JSON array; the error file holds all).</summary>
    public string Issues { get; set; } = "[]";

    public string? FailureCode { get; set; }
    public string? FailureEn { get; set; }
    public string? FailureAr { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>The bytes of an import: the file as uploaded and the error file of the last run. Kept apart from the
/// session so that listing sessions never reads them; the audit trail records only their size.</summary>
public sealed class ImportFile : TenantEntity
{
    public Guid SessionId { get; set; }

    public long SizeBytes { get; set; }

    public byte[] Content { get; set; } = [];

    /// <summary>The error file of the last run (CSV), or null.</summary>
    public byte[]? ErrorReport { get; set; }
}

/// <summary>A column header seen before, and the field it was mapped to: the next file with that header is mapped by itself.</summary>
public sealed class ImportMapping : TenantEntity
{
    public string RecordType { get; set; } = "";

    /// <summary>The header with everything but letters and digits dropped, lower case.</summary>
    public string HeaderKey { get; set; } = "";

    public string FieldKey { get; set; } = "";

    public int TimesUsed { get; set; } = 1;

    public DateTimeOffset LastUsedAt { get; set; }
}

/// <summary>A full export of the workspace's data.</summary>
public sealed class TenantExport : TenantEntity
{
    public string Status { get; set; } = ExportStatus.Queued;

    /// <summary><c>csv</c> or <c>json</c>: the format of the file kept for each kind of record.</summary>
    public string Format { get; set; } = "csv";

    public Guid? JobId { get; set; }

    public int FileCount { get; set; }

    public long ByteSize { get; set; }

    /// <summary>The records written, by kind (JSON object).</summary>
    public string RecordCounts { get; set; } = "{}";

    public string? FailureCode { get; set; }
    public string? FailureEn { get; set; }
    public string? FailureAr { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>After this moment the file is removed and cannot be downloaded.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public int DownloadCount { get; set; }

    public DateTimeOffset? DownloadedAt { get; set; }

    public Guid? DownloadedBy { get; set; }
}

/// <summary>The zip file of a full export (manifest, one file per kind of record, the attachments), apart from the
/// export row so that listing exports never reads it; the audit trail records only its size.</summary>
public sealed class ExportFile : TenantEntity
{
    public Guid ExportId { get; set; }

    public long SizeBytes { get; set; }

    public byte[] Content { get; set; } = [];
}

public sealed class ImportExportDbContext(DbContextOptions<ImportExportDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "importexport";

    protected override string Schema => SchemaName;

    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();

    public DbSet<ImportFile> ImportFiles => Set<ImportFile>();

    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();

    public DbSet<TenantExport> TenantExports => Set<TenantExport>();

    public DbSet<ExportFile> ExportFiles => Set<ExportFile>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportSession>(e =>
        {
            e.ToTable("import_sessions", t =>
            {
                t.HasCheckConstraint("ck_import_sessions_status", "status IN ('uploaded', 'running', 'checked', 'completed', 'failed')");
                t.HasCheckConstraint("ck_import_sessions_format", "format IN ('csv', 'xlsx')");
                t.HasCheckConstraint("ck_import_sessions_on_error", "on_error IN ('reject', 'skip')");
                t.HasCheckConstraint("ck_import_sessions_on_duplicate", "on_duplicate IN ('create', 'skip', 'update') AND rows_duplicate >= 0 AND rows_updated >= 0");
                t.HasCheckConstraint("ck_import_sessions_counts", "row_count >= 0 AND rows_checked >= 0 AND rows_valid >= 0 AND rows_imported >= 0 AND rows_failed >= 0");
                t.HasCheckConstraint("ck_import_sessions_file_name", "btrim(file_name) <> ''");
            });
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.FileName).HasMaxLength(ImportLimits.MaxFileNameLength);
            e.Property(x => x.Format).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.OnError).HasMaxLength(10);
            e.Property(x => x.OnDuplicate).HasMaxLength(10).HasDefaultValue(ImportOnDuplicate.Create);
            e.Property(x => x.Headers).HasColumnType("jsonb");
            e.Property(x => x.Mapping).HasColumnType("jsonb");
            e.Property(x => x.Sources).HasMaxLength(ImportLimits.MaxColumns);
            e.Property(x => x.Sample).HasColumnType("jsonb");
            e.Property(x => x.Issues).HasColumnType("jsonb");
            e.Property(x => x.FailureCode).HasMaxLength(100);
            e.Property(x => x.FailureEn).HasMaxLength(JobText.MaxLength * 2);
            e.Property(x => x.FailureAr).HasMaxLength(JobText.MaxLength * 2);
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.CreatedAt });
        });
        modelBuilder.Entity<ImportFile>(e =>
        {
            e.ToTable("import_files", t => t.HasCheckConstraint("ck_import_files_size", "size_bytes BETWEEN 1 AND 104857600"));
            e.Property(x => x.Content).HasColumnType("bytea");
            e.Property(x => x.ErrorReport).HasColumnType("bytea");
            e.HasOne<ImportSession>().WithMany().HasForeignKey(x => new { x.TenantId, x.SessionId }).HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.SessionId }).IsUnique();
        });
        modelBuilder.Entity<ImportMapping>(e =>
        {
            e.ToTable("import_mappings", t =>
            {
                t.HasCheckConstraint("ck_import_mappings_header", "btrim(header_key) <> ''");
                t.HasCheckConstraint("ck_import_mappings_times", "times_used >= 1");
            });
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.HeaderKey).HasMaxLength(200);
            e.Property(x => x.FieldKey).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.HeaderKey }).IsUnique();
        });
        modelBuilder.Entity<TenantExport>(e =>
        {
            e.ToTable("tenant_exports", t =>
            {
                t.HasCheckConstraint("ck_tenant_exports_status", "status IN ('queued', 'running', 'succeeded', 'failed', 'expired')");
                t.HasCheckConstraint("ck_tenant_exports_format", "format IN ('csv', 'json')");
                t.HasCheckConstraint("ck_tenant_exports_counts", "file_count >= 0 AND byte_size >= 0 AND download_count >= 0");
            });
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Format).HasMaxLength(10);
            e.Property(x => x.RecordCounts).HasColumnType("jsonb");
            e.Property(x => x.FailureCode).HasMaxLength(100);
            e.Property(x => x.FailureEn).HasMaxLength(JobText.MaxLength * 2);
            e.Property(x => x.FailureAr).HasMaxLength(JobText.MaxLength * 2);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });
        modelBuilder.Entity<ExportFile>(e =>
        {
            e.ToTable("export_files", t => t.HasCheckConstraint("ck_export_files_size", "size_bytes BETWEEN 1 AND 536870912"));
            e.Property(x => x.Content).HasColumnType("bytea");
            e.HasOne<TenantExport>().WithMany().HasForeignKey(x => new { x.TenantId, x.ExportId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.ExportId }).IsUnique();
        });
    }
}

internal sealed class ImportExportDbContextDesignFactory : IDesignTimeDbContextFactory<ImportExportDbContext>
{
    public ImportExportDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ImportExportDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", ImportExportDbContext.SchemaName);
        return new ImportExportDbContext(options.Options);
    }
}
