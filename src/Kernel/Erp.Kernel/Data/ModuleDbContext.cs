using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Erp.Kernel.Data;

/// <summary>A row owned by a tenant. Row-level security keys on <see cref="TenantId"/>.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Base for business records: tenant-owned, UUIDv7 key, timestamps, optimistic
/// concurrency on PostgreSQL's <c>xmin</c> (seen by clients through <see cref="RowVersions"/>). Every insert, update and delete is captured in the
/// audit trail by a database trigger (see <see cref="TenantSql"/>).</summary>
public abstract class TenantEntity : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Concurrency token mapped to <c>xmin</c>; its value is <see cref="RowVersions.Hide"/> of it.</summary>
    public uint Version { get; set; }
}

/// <summary>
/// Base DbContext for a module. It owns one schema, applies the tenant query filter to every
/// <see cref="ITenantOwned"/> entity (the second layer behind row-level security), stamps tenant
/// and timestamps on save, and refuses to move a row to another tenant.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    protected ModuleDbContext(DbContextOptions options, ITenantContext? tenant) : base(options)
    {
        _tenant = tenant;
        // The shared unit-of-work transaction is already open on the connection.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }

    /// <summary>The module's schema.</summary>
    protected abstract string Schema { get; }

    /// <summary>Tenant used by the query filter. Throws outside a tenant-bound unit of work.</summary>
    internal bool HasTenant => _tenant?.HasTenant == true;

    public Guid CurrentTenantId => _tenant?.TenantId ?? throw new TenantContextMissingException();

    private ICompanyContext? Companies => _tenant as ICompanyContext;

    /// <summary>Company query filter: true for system work that sees every company.</summary>
    public bool CompanyFilterAll => Companies?.AllCompanies == true;

    /// <summary>Company query filter: the companies a user may see (fail closed: none).</summary>
    public Guid[] CompanyFilterIds => Companies?.CompanyIds.ToArray() ?? [];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property(nameof(ITenantOwned.TenantId)).IsRequired();

            // e => e.TenantId == this.CurrentTenantId
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            entity.HasQueryFilter(TenantFilterName, Expression.Lambda(body, parameter));

            if (typeof(ICompanyOwned).IsAssignableFrom(entityType.ClrType))
            {
                // e => this.CompanyFilterAll || this.CompanyFilterIds.Contains(e.CompanyId): the
                // second layer behind the company_scope row-level security policy.
                entity.Property(nameof(ICompanyOwned.CompanyId)).IsRequired();
                var companyParameter = Expression.Parameter(entityType.ClrType, "e");
                var contains = Expression.Call(
                    typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
                    Expression.Property(Expression.Constant(this), nameof(CompanyFilterIds)),
                    Expression.Property(companyParameter, nameof(ICompanyOwned.CompanyId)));
                var companyBody = Expression.OrElse(Expression.Property(Expression.Constant(this), nameof(CompanyFilterAll)), contains);
                entity.HasQueryFilter(CompanyFilterName, Expression.Lambda(companyBody, companyParameter));
            }

            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                entity.HasKey(nameof(TenantEntity.Id));
                // The client sees a keyed permutation of xmin, never the database-wide transaction id (RowVersions).
                entity.Property(nameof(TenantEntity.Version)).IsRowVersion()
                    .HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<uint, uint>(v => RowVersions.Reveal(v), x => RowVersions.Hide(x)));
                entity.Property(nameof(TenantEntity.CreatedAt)).HasDefaultValueSql("now()");
                entity.Property(nameof(TenantEntity.UpdatedAt)).HasDefaultValueSql("now()");
                // Composite principal key so children can reference (tenant_id, id): the database
                // itself then refuses a reference to another tenant's row.
                entity.HasAlternateKey(nameof(ITenantOwned.TenantId), nameof(TenantEntity.Id));
            }
        }
    }

    public const string TenantFilterName = "tenant";

    public const string CompanyFilterName = "company";

    /// <summary>Module model configuration.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not ITenantOwned owned)
            {
                continue;
            }
            switch (entry.State)
            {
                case EntityState.Added:
                    if (owned.TenantId == Guid.Empty)
                    {
                        owned.TenantId = CurrentTenantId;
                    }
                    else if (owned.TenantId != CurrentTenantId)
                    {
                        throw new CrossTenantWriteException(entry.Metadata.ClrType.Name);
                    }
                    if (entry.Entity is ICompanyOwned newRow)
                    {
                        if (newRow.CompanyId == Guid.Empty && Companies?.ActiveCompanyId is { } working)
                        {
                            newRow.CompanyId = working;
                        }
                        if (newRow.CompanyId == Guid.Empty || Companies?.AllowsCompany(newRow.CompanyId) != true)
                        {
                            throw new CrossCompanyWriteException(entry.Metadata.ClrType.Name);
                        }
                    }
                    if (entry.Entity is ICompanyWide newShared && Companies?.HoldsEveryBranch(newShared.CompanyId) != true)
                    {
                        throw new CrossBranchWriteException(entry.Metadata.ClrType.Name);
                    }
                    if (entry.Entity is IWorkspaceWide && _tenant is not IWorkspaceScope { HoldsWholeWorkspace: true })
                    {
                        throw new CrossBranchWriteException(entry.Metadata.ClrType.Name, CrossBranchWriteException.WorkspaceCode);
                    }
                    if (entry.Entity is TenantEntity added)
                    {
                        added.CreatedAt = now;
                        added.UpdatedAt = now;
                        added.CreatedBy = _tenant?.ActorId;
                        added.UpdatedBy = _tenant?.ActorId;
                    }
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    var tenantProperty = entry.Property(nameof(ITenantOwned.TenantId));
                    if ((Guid)tenantProperty.OriginalValue! != CurrentTenantId || owned.TenantId != CurrentTenantId)
                    {
                        throw new CrossTenantWriteException(entry.Metadata.ClrType.Name);
                    }
                    if (entry.Entity is ICompanyOwned companyRow)
                    {
                        var originalCompany = (Guid)entry.Property(nameof(ICompanyOwned.CompanyId)).OriginalValue!;
                        if (Companies?.AllowsCompany(originalCompany) != true || Companies.AllowsCompany(companyRow.CompanyId) != true)
                        {
                            throw new CrossCompanyWriteException(entry.Metadata.ClrType.Name);
                        }
                    }
                    if (entry.Entity is ICompanyWide sharedRow &&
                        (Companies?.HoldsEveryBranch((Guid)entry.Property(nameof(ICompanyOwned.CompanyId)).OriginalValue!) != true ||
                         Companies.HoldsEveryBranch(sharedRow.CompanyId) != true))
                    {
                        throw new CrossBranchWriteException(entry.Metadata.ClrType.Name);
                    }
                    if (entry.Entity is IWorkspaceWide && _tenant is not IWorkspaceScope { HoldsWholeWorkspace: true })
                    {
                        throw new CrossBranchWriteException(entry.Metadata.ClrType.Name, CrossBranchWriteException.WorkspaceCode);
                    }
                    if (entry.State == EntityState.Modified && entry.Entity is TenantEntity modified)
                    {
                        modified.UpdatedAt = now;
                        modified.UpdatedBy = _tenant?.ActorId;
                    }
                    break;
            }
        }
    }
}

/// <summary>
/// Code tried to write a row the user's branch limits forbid: a record every branch of a company
/// shares (<see cref="ICompanyWide"/>), by a user who may work in only some branches of it, or a
/// module's own branch rule. Answered 403 with <see cref="Code"/> (a problem code with an English
/// and an Arabic message), and logged as an error: the endpoint should have refused first.
/// </summary>
public sealed class CrossBranchWriteException(string entity, string code = CrossBranchWriteException.DefaultCode)
    : InvalidOperationException($"Refused to write a {entity} row that every branch of its company shares, for a user limited to some branches.")
{
    public const string DefaultCode = "companyNeedsEveryBranch";

    /// <summary>A record every company of the workspace shares (<see cref="IWorkspaceWide"/>), by a
    /// user who may work in only some companies or branches.</summary>
    public const string WorkspaceCode = "workspaceNeedsEveryCompany";

    public string Code { get; } = code;
}

/// <summary>Code tried to write a row of a company outside the unit of work's company scope.</summary>
public sealed class CrossCompanyWriteException(string entity)
    : InvalidOperationException($"Refused to write a {entity} row of a company outside the company scope.");

public sealed class CrossTenantWriteException(string entity)
    : InvalidOperationException($"Refused to write a {entity} row that belongs to another tenant.");
