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
            e.HasIndex(x => new { x.TenantId, x.TableName, x.RecordId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.ActorId, x.OccurredAt });
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
