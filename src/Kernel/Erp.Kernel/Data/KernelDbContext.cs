using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Kernel.Data;

/// <summary>
/// One row of the audit trail: who changed what (field-level old and new values), when, in which
/// tenant. Written by the <c>audit.capture()</c> trigger in the same transaction as the change,
/// so no write path (EF, raw SQL, bulk insert) can skip it. The application role may insert and
/// read rows of its own tenant but never update or delete them.
/// </summary>
public sealed class AuditEntry : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorKind { get; set; } = "";
    public string TableSchema { get; set; } = "";
    public string TableName { get; set; } = "";
    public Guid? RecordId { get; set; }

    /// <summary>insert, update or delete.</summary>
    public string Action { get; set; } = "";

    /// <summary>{ "column": { "old": …, "new": … } }</summary>
    public JsonDocument Changes { get; set; } = JsonDocument.Parse("{}");
    public long TransactionId { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>A unique id of the row for lists and links (keyset paging breaks ties with it).</summary>
    public Guid Uid { get; set; }

    /// <summary><c>schema.table</c> of the changed record (stored, generated from the two columns).</summary>
    public string RecordType { get; private set; } = "";

    /// <summary>What the record was called when it changed (its name, code or e-mail), so the trail
    /// names it even after it is renamed or deleted.</summary>
    public string? RecordLabel { get; set; }

    /// <summary>The record's name in Arabic script, when it has one.</summary>
    public string? RecordLabelAr { get; set; }

    /// <summary>The record's e-mail or code when it has one (a user's sign-in, a company's code), so
    /// the trail finds a record by it too.</summary>
    public string? RecordCode { get; set; }

    /// <summary>The company the record belongs to (its <c>company_id</c>), or null for a record of the
    /// whole workspace. Row-level security shows the row only inside the reader's company scope.</summary>
    public Guid? RecordCompanyId { get; set; }

    /// <summary>The branch the record belongs to (its <c>branch_id</c>), or null. A reader who may
    /// work in only some branches of the record's company sees it only for those branches.</summary>
    public Guid? RecordBranchId { get; set; }

    /// <summary>The names of the fields the row records (the keys of <see cref="Changes"/>, sorted;
    /// stored, generated from them).</summary>
    public string[]? Fields { get; private set; }
}

/// <summary>Kernel-owned database objects: the <c>erp</c> helper functions row-level security
/// uses, and the <c>audit</c> schema with the audit trail and its trigger.</summary>
public sealed class KernelDbContext(DbContextOptions<KernelDbContext> options, ITenantContext? tenant = null)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "audit";

    protected override string Schema => SchemaName;

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.OccurredAt).HasDefaultValueSql("now()");
            e.Property(x => x.ActorKind).HasMaxLength(20);
            e.Property(x => x.TableSchema).HasMaxLength(63);
            e.Property(x => x.TableName).HasMaxLength(63);
            e.Property(x => x.Action).HasMaxLength(10);
            e.Property(x => x.Changes).HasColumnType("jsonb");
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.Property(x => x.Uid).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.RecordType).HasMaxLength(127).HasComputedColumnSql("((table_schema)::text || '.' || (table_name)::text)", stored: true);
            e.Property(x => x.RecordLabel).HasMaxLength(200);
            e.Property(x => x.RecordLabelAr).HasMaxLength(200);
            e.Property(x => x.RecordCode).HasMaxLength(200);
            e.Property(x => x.Fields).HasComputedColumnSql("audit.field_names(changes)", stored: true);
            e.HasIndex(x => new { x.TenantId, x.TableName, x.RecordId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.OccurredAt, x.Uid });
            e.HasIndex(x => new { x.TenantId, x.ActorId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.RecordId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.Uid }).IsUnique();
            e.HasIndex(x => x.Fields).HasMethod("gin");
            e.HasIndex(x => x.RecordLabel).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.RecordLabelAr).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.RecordCode).HasMethod("gin").HasOperators("gin_trgm_ops");
        });
    }
}

/// <summary>Design-time factory for <c>dotnet ef migrations add</c>. No database is contacted.</summary>
internal sealed class KernelDbContextDesignFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", KernelDbContext.SchemaName);
        return new KernelDbContext(options.Options);
    }
}
