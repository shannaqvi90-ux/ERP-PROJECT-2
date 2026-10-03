using Erp.Kernel.Data;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Seeding;
using Erp.Modules.Lists.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Modules.Lists;

/// <summary>
/// The list framework's per-user state: saved views (columns, sort, filter, search, grouping) a
/// user keeps for themselves or shares with everyone who can read the list, and the definition of
/// every registered list for the screens. Every list another module registers gets its endpoints
/// here, under <c>/api/lists/{list key}/…</c>, guarded by that list's own read permission; sharing
/// needs <see cref="ListsPermissions.ViewsShare"/> as well.
/// </summary>
public sealed class ListsModule : ErpModule
{
    public override string Name => "lists";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. ListsPermissions.All]);
        module.DbContext<ListsDbContext>();
        module.Endpoints(ViewEndpoints.Map);
        module.Seeder<ListsSeeder>();
    }
}

/// <summary>A saved view of a list: personal (one owner) or shared (every user who can read the list).</summary>
public sealed class SavedView : TenantEntity
{
    public string ListKey { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The user a personal view belongs to; null for a shared view.</summary>
    public Guid? OwnerUserId { get; set; }
    public bool IsShared { get; set; }

    /// <summary>Opens when the owner (personal) or anyone without a personal default (shared)
    /// opens the list.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Visible column keys in display order.</summary>
    public List<string> Columns { get; set; } = [];
    public string? Sort { get; set; }
    public string? Filter { get; set; }
    public string? Search { get; set; }
    public string? GroupBy { get; set; }
}

public sealed class ListsDbContext(DbContextOptions<ListsDbContext> options, ITenantContext? tenant = null)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "lists";

    public const int NameMaxLength = 100;

    protected override string Schema => SchemaName;

    public DbSet<SavedView> SavedViews => Set<SavedView>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedView>(e =>
        {
            e.ToTable("saved_views", t =>
            {
                t.HasCheckConstraint("ck_saved_views_owner", "(is_shared AND owner_user_id IS NULL) OR (NOT is_shared AND owner_user_id IS NOT NULL)");
                t.HasCheckConstraint("ck_saved_views_name", "length(btrim(name)) > 0");
            });
            e.Property(x => x.ListKey).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(NameMaxLength);
            e.Property(x => x.Columns).HasColumnType("text[]");
            e.Property(x => x.Sort).HasMaxLength(200);
            e.Property(x => x.Filter).HasMaxLength(ListFilter.MaxLength);
            e.Property(x => x.Search).HasMaxLength(ListRequest.MaxSearchLength);
            e.Property(x => x.GroupBy).HasMaxLength(64);
            // One name per owner (or among shared views) per list; one default per owner and one
            // shared default per list.
            e.HasIndex(x => new { x.TenantId, x.ListKey, x.IsShared, x.OwnerUserId, x.Name }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.TenantId, x.ListKey, x.OwnerUserId }).IsUnique().AreNullsDistinct(false)
                .HasFilter("is_default").HasDatabaseName("ux_saved_views_default");
        });
    }
}

internal sealed class ListsDbContextDesignFactory : IDesignTimeDbContextFactory<ListsDbContext>
{
    public ListsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ListsDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", ListsDbContext.SchemaName);
        return new ListsDbContext(options.Options);
    }
}

/// <summary>
/// One shared view per registered list in every seeded workspace (the list's visible columns,
/// default sort, its first built-in filter and first groupable column), so a team starts with a
/// view to adapt and the gate fixture holds saved views of both tenants. The same sort, filter
/// and grouping are the API document's examples for view bodies, so views written from those
/// examples hold no value the seeded workspaces do not already share. Idempotent.
/// </summary>
internal sealed class ListsSeeder(ListsDbContext db, ModuleCatalog catalog) : ITenantSeeder
{
    public int Order => 100;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var list in catalog.Lists)
        {
            var name = context.Mark("Team view");
            if (await db.SavedViews.AnyAsync(v => v.ListKey == list.Key && v.IsShared && v.Name == name, cancellationToken))
            {
                continue;
            }
            var (sort, filter, groupBy) = ViewExample.For(list);
            db.SavedViews.Add(new SavedView
            {
                ListKey = list.Key,
                Name = name,
                IsShared = true,
                Columns = list.Columns.Where(c => !c.Hidden).Select(c => c.Key).ToList(),
                Sort = sort,
                Filter = filter,
                GroupBy = groupBy,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
