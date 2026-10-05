using Erp.Kernel.Data;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Shell;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Access;
using Erp.Modules.Tenancy.Branches;
using Erp.Modules.Tenancy.Companies;
using Erp.Modules.Tenancy.Contracts;
using Erp.Modules.Tenancy.Provisioning;
using Erp.Modules.Tenancy.Seeding;
using Erp.Modules.Tenancy.Workplace;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Tenancy;

/// <summary>
/// Tenants (customer workspaces) and their settings, companies and branches within a tenant, which
/// companies and branches each user may work in, and the user's working company and branch. A
/// user's companies become the company scope of every request (row-level security on every table
/// that carries <c>company_id</c>, see <see cref="CompanyScopeBinder"/>).
/// </summary>
public sealed class TenancyModule : ErpModule
{
    public override string Name => "tenancy";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. TenancyPermissions.All]);
        module.DbContext<TenancyDbContext>();
        module.Services.AddScoped<ITenantDirectory, TenantDirectory>();
        module.Services.AddScoped<ICompanyDirectory, CompanyDirectory>();
        module.Services.AddScoped<ISessionScopeBinder, CompanyScopeBinder>();
        module.Services.AddScoped<TenancyBranchScope>();
        module.Endpoints(group =>
        {
            TenancyEndpoints.Map(group);
            CompanyEndpoints.Map(group);
            BranchEndpoints.Map(group);
            AccessEndpoints.Map(group);
            WorkplaceEndpoints.Map(group);
        });
        module.Menu(new MenuEntry("tenancy.companies", "tenancy.menu.companies", "/tenancy/companies", TenancyPermissions.CompaniesRead, Order: 830, Group: "settings"));
        module.Menu(new MenuEntry("tenancy.branches", "tenancy.menu.branches", "/tenancy/branches", TenancyPermissions.BranchesRead, Order: 840, Group: "settings"));
        module.Menu(new MenuEntry("tenancy.access", "tenancy.menu.access", "/tenancy/access", TenancyPermissions.AccessRead, Order: 820, Group: "settings"));
        module.Menu(new MenuEntry("tenancy.tenant", "tenancy.menu.tenant", "/tenancy/tenant", TenancyPermissions.TenantRead, Order: 900, Group: "settings"));
        module.List(CompaniesList.Create());
        module.List(BranchesList.Create());
        // The access list's rows are identity's users: identity's users list serves its query.
        module.List(AccessList.Definition, servedBy: IdentityLists.Users);
        // Reports print the three lists exactly as their screens show them.
        module.ListRows(CompaniesList.Key, async (services, request, http, cancellationToken) =>
            (await CompanyEndpoints.PageAsync(services.GetRequiredService<TenancyDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.ListRows(BranchesList.Key, async (services, request, http, cancellationToken) =>
            (await BranchEndpoints.PageAsync(services.GetRequiredService<TenancyDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.ListRows(AccessList.Key, async (services, request, http, cancellationToken) =>
            (await AccessEndpoints.PageAsync(services.GetRequiredService<TenancyDbContext>(), services.GetRequiredService<IUserDirectory>(), request, http, cancellationToken)).Map(r => (object)r));
        module.Report<Reports.CompanyProfileReport>(Reports.CompanyProfileReport.Definition);
        module.Report<Reports.BranchDirectoryReport>(Reports.BranchDirectoryReport.Definition);
        module.Seeder<TenancySeeder>();
        module.Seeder<TenancyAccessSeeder>();
        module.IsolationProbe<CompanyLogoProbe>();
        module.Command(TenantCommand.Definition);
    }
}

/// <summary>A customer workspace. Its <c>tenant_id</c> always equals its own id (check
/// constraint), so the same row-level security policy that guards every other table guards this
/// one.</summary>
public sealed class Tenant : TenantEntity
{
    public string Code { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string Status { get; set; } = TenantStatus.Active;

    /// <summary>Language new users and printed documents start in (en or ar).</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>IANA time zone the workspace's dates are shown in.</summary>
    public string TimeZone { get; set; } = TenantSettings.DefaultTimeZone;

    /// <summary>First day of the working week (monday, sunday or saturday).</summary>
    public string WeekStart { get; set; } = "monday";

    /// <summary>How many companies the workspace has, kept by the database (a trigger on company
    /// inserts; companies are never deleted). Never shown: it tells whether a caller works in
    /// every company (see <see cref="CompanyAccessRules"/>) without reading companies outside the
    /// caller's scope.</summary>
    public int CompanyCount { get; set; }
}

public static class TenantStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
}

/// <summary>
/// A legal entity within a tenant: its own trade licence, tax registration, base currency and
/// fiscal year. Its <c>company_id</c> always equals its own id, so the company-scope policy that
/// guards company data guards the company record itself.
/// </summary>
public sealed class Company : TenantEntity, ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    /// <summary>Legal name in English; empty when only the Arabic name is known (at least one is set).</summary>
    public string LegalNameEn { get; set; } = "";

    /// <summary>Legal name in Arabic; empty when only the English name is known.</summary>
    public string LegalNameAr { get; set; } = "";
    public string? TradeLicenceNumber { get; set; }
    public string? TradeLicenceAuthority { get; set; }
    public string? TaxRegistrationNumber { get; set; }

    /// <summary>ISO 4217 code of the currency the company keeps its books in.</summary>
    public string BaseCurrency { get; set; } = "AED";
    public int FiscalYearStartMonth { get; set; } = 1;
    public int FiscalYearStartDay { get; set; } = 1;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Emirate { get; set; }
    public string? PoBox { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; set; } = "AE";

    /// <summary>The address in Arabic, as printed on Arabic documents.</summary>
    public string? AddressAr { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }

    /// <summary>SHA-256 of the logo (hex): the audit trail records logo changes by hash.</summary>
    public string? LogoHash { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A place a company works from (office, warehouse, shop, factory).</summary>
public sealed class Branch : TenantEntity, ICompanyOwned
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Emirate { get; set; }
    public string? PoBox { get; set; }
    public string Country { get; set; } = "AE";
    public string? AddressAr { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A user may work in a company: in all its branches, or only those listed in
/// <see cref="UserBranchAccess"/>.</summary>
public sealed class UserCompanyAccess : TenantEntity, ICompanyOwned
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public bool AllBranches { get; set; } = true;
}

/// <summary>One branch a user may work in, when their company access is limited to branches.</summary>
public sealed class UserBranchAccess : TenantEntity, ICompanyOwned
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
}

/// <summary>How many companies a user may work in, kept by the database (a trigger on
/// <see cref="UserCompanyAccess"/> inserts and deletes). It is not scoped to companies, so a caller
/// can tell whether a user holds access outside the caller's own companies without seeing which.</summary>
public sealed class UserCompanyTotal : TenantEntity
{
    public Guid UserId { get; set; }
    public int CompanyCount { get; set; }
}

/// <summary>The company and branch a user is working in now (one row per user).</summary>
public sealed class UserWorkplace : TenantEntity, ICompanyOwned
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
}

public sealed class TenancyDbContext : ModuleDbContext
{
    private readonly TenancyBranchScope? branches;

    /// <summary>For migrations and design-time tools (no request, so no branch limits).</summary>
    public TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext? tenant = null)
        : base(options, tenant)
    {
    }

    /// <summary>For a request: the branch limits the company scope binder set apply.</summary>
    [ActivatorUtilitiesConstructor]
    public TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext? tenant, TenancyBranchScope branches)
        : base(options, tenant)
    {
        this.branches = branches;
    }

    public const string SchemaName = "tenancy";

    /// <summary>Name of the branch filter: a user limited to some branches of a company sees only
    /// those branches of it (every branch of the companies where they hold every branch).</summary>
    public const string BranchFilterName = "branch";

    /// <summary>True when no company of the scope is limited to branches.</summary>
    public bool BranchFilterOff => branches is null || branches.LimitedCompanyIds.Count == 0;

    /// <summary>Companies where the user may work only in some branches.</summary>
    public Guid[] BranchLimitedCompanyIds => branches?.LimitedCompanyIds.ToArray() ?? [];

    /// <summary>The branches the user may work in, in those companies.</summary>
    public Guid[] BranchAllowedIds => branches?.AllowedBranchIds.ToArray() ?? [];

    protected override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserCompanyAccess> CompanyAccess => Set<UserCompanyAccess>();
    public DbSet<UserBranchAccess> BranchAccess => Set<UserBranchAccess>();
    public DbSet<UserWorkplace> Workplaces => Set<UserWorkplace>();
    public DbSet<UserCompanyTotal> CompanyTotals => Set<UserCompanyTotal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants", t =>
            {
                t.HasCheckConstraint("ck_tenants_status", "status IN ('active', 'suspended')");
                t.HasCheckConstraint("ck_tenants_tenant_is_self", "tenant_id = id");
                t.HasCheckConstraint("ck_tenants_code", "code ~ '^[a-z0-9][a-z0-9-]{1,39}$'");
                t.HasCheckConstraint("ck_tenants_default_language", "default_language IN ('en', 'ar')");
                t.HasCheckConstraint("ck_tenants_week_start", "week_start IN ('monday', 'sunday', 'saturday')");
            });
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.DefaultLanguage).HasMaxLength(2).HasDefaultValue("en").HasSentinel("");
            e.Property(x => x.TimeZone).HasMaxLength(64).HasDefaultValue(TenantSettings.DefaultTimeZone).HasSentinel("");
            e.Property(x => x.WeekStart).HasMaxLength(10).HasDefaultValue("monday").HasSentinel("");
            e.Property(x => x.CompanyCount).HasDefaultValue(0);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies", t =>
            {
                t.HasCheckConstraint("ck_companies_company_is_self", "company_id = id");
                t.HasCheckConstraint("ck_companies_code", "code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'");
                t.HasCheckConstraint("ck_companies_base_currency", "base_currency ~ '^[A-Z]{3}$'");
                t.HasCheckConstraint("ck_companies_country", "country ~ '^[A-Z]{2}$'");
                t.HasCheckConstraint("ck_companies_fiscal_year_start", "fiscal_year_start_month BETWEEN 1 AND 12 AND fiscal_year_start_day BETWEEN 1 AND 31");
                t.HasCheckConstraint("ck_companies_logo", "(logo IS NULL) = (logo_content_type IS NULL) AND (logo IS NULL) = (logo_hash IS NULL)");
                t.HasCheckConstraint("ck_companies_legal_name", "legal_name_en <> '' OR legal_name_ar <> ''");
            });
            ConfigureAddress(e);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.LegalNameEn).HasMaxLength(200);
            e.Property(x => x.LegalNameAr).HasMaxLength(200);
            e.Property(x => x.TradeLicenceNumber).HasMaxLength(50);
            e.Property(x => x.TradeLicenceAuthority).HasMaxLength(100);
            e.Property(x => x.TaxRegistrationNumber).HasMaxLength(20);
            e.Property(x => x.BaseCurrency).HasMaxLength(3);
            e.Property(x => x.Website).HasMaxLength(200);
            e.Property(x => x.Logo).HasColumnType("bytea");
            e.Property(x => x.LogoContentType).HasMaxLength(20);
            e.Property(x => x.LogoHash).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.LegalNameEn });
            // List framework: word search on trigram indexes, keyset order on (tenant, column, id).
            e.HasIndex(x => new { x.Code, x.LegalNameEn, x.LegalNameAr }, "ix_companies_search")
                .HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.LegalNameAr, x.Id });
            e.HasIndex(x => new { x.TenantId, x.City, x.Id });
            // Children reference (tenant_id, company_id): the database refuses a child in another
            // tenant's company.
            e.HasAlternateKey(x => new { x.TenantId, x.CompanyId });
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("branches", t =>
            {
                t.HasCheckConstraint("ck_branches_code", "code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'");
                t.HasCheckConstraint("ck_branches_country", "country ~ '^[A-Z]{2}$'");
                t.HasCheckConstraint("ck_branches_name", "name_en <> '' OR name_ar <> ''");
            });
            ConfigureAddress(e);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.NameEn });
            // List framework: word search on trigram indexes, keyset order on (tenant, column, id).
            e.HasIndex(x => new { x.Code, x.NameEn, x.NameAr }, "ix_branches_search")
                .HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.Code, x.Id });
            e.HasIndex(x => new { x.TenantId, x.NameAr, x.Id });
            e.HasIndex(x => new { x.TenantId, x.City, x.Id });
            // (tenant, company, branch): a branch reference also proves the branch's company.
            e.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId })
                .HasPrincipalKey(c => new { c.TenantId, c.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(BranchFilterName, b => BranchFilterOff || !BranchLimitedCompanyIds.Contains(b.CompanyId) || BranchAllowedIds.Contains(b.Id));
        });

        modelBuilder.Entity<UserCompanyAccess>(e =>
        {
            e.ToTable("user_company_access");
            e.HasIndex(x => new { x.TenantId, x.CompanyId });
            // Unique (tenant, user, company); branch access and workplaces hang under it.
            e.HasAlternateKey(x => new { x.TenantId, x.UserId, x.CompanyId });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId })
                .HasPrincipalKey(c => new { c.TenantId, c.CompanyId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserBranchAccess>(e =>
        {
            e.ToTable("user_branch_access");
            e.HasIndex(x => new { x.TenantId, x.UserId, x.BranchId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.BranchId });
            // Branch access exists only under the user's access to the branch's company.
            e.HasOne<UserCompanyAccess>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId, x.CompanyId })
                .HasPrincipalKey(a => new { a.TenantId, a.UserId, a.CompanyId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.BranchId })
                .HasPrincipalKey(b => new { b.TenantId, b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserCompanyTotal>(e =>
        {
            e.ToTable("user_company_totals", t => t.HasCheckConstraint("ck_user_company_totals_count", "company_count >= 0"));
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<UserWorkplace>(e =>
        {
            e.ToTable("user_workplaces");
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CompanyId });
            e.HasOne<UserCompanyAccess>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId, x.CompanyId })
                .HasPrincipalKey(a => new { a.TenantId, a.UserId, a.CompanyId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.BranchId })
                .HasPrincipalKey(b => new { b.TenantId, b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAddress<T>(EntityTypeBuilder<T> e) where T : class
    {
        e.Property<string?>("AddressLine1").HasMaxLength(200);
        e.Property<string?>("AddressLine2").HasMaxLength(200);
        e.Property<string?>("City").HasMaxLength(100);
        e.Property<string?>("Emirate").HasMaxLength(20);
        e.Property<string?>("PoBox").HasMaxLength(20);
        e.Property<string>("Country").HasMaxLength(2);
        e.Property<string?>("AddressAr").HasMaxLength(400);
        e.Property<string?>("Phone").HasMaxLength(30);
        e.Property<string?>("Email").HasMaxLength(254);
    }
}

internal sealed class TenancyDbContextDesignFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", TenancyDbContext.SchemaName);
        return new TenancyDbContext(options.Options);
    }
}

/// <summary>The branch limits of the signed-in user (set once per request by the company scope
/// binder): in a company where they may work only in some branches, only those branches exist for
/// them. Empty for system work and for users who hold every branch of their companies.</summary>
public sealed class TenancyBranchScope
{
    private bool _set;

    public IReadOnlySet<Guid> LimitedCompanyIds { get; private set; } = new HashSet<Guid>();

    public IReadOnlySet<Guid> AllowedBranchIds { get; private set; } = new HashSet<Guid>();

    /// <summary>Set the limits once; a second call throws, so code later in the request cannot widen them.</summary>
    internal void Set(IEnumerable<Guid> limitedCompanies, IEnumerable<Guid> allowedBranches)
    {
        if (_set)
        {
            throw new InvalidOperationException("The branch scope of this request is already set.");
        }
        LimitedCompanyIds = limitedCompanies.ToHashSet();
        AllowedBranchIds = allowedBranches.ToHashSet();
        _set = true;
    }

    /// <summary>True when the user may work in this branch of this company (a company of their scope).</summary>
    public bool Allows(Guid companyId, Guid branchId) => !LimitedCompanyIds.Contains(companyId) || AllowedBranchIds.Contains(branchId);

    /// <summary>True when the user holds every branch of the company.</summary>
    public bool HoldsEveryBranch(Guid companyId) => !LimitedCompanyIds.Contains(companyId);
}

internal sealed class TenantDirectory(TenancyDbContext db, ITenantContext tenant) : ITenantDirectory
{
    public async Task<TenantInfo?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (!tenant.HasTenant)
        {
            return null;
        }
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenant.TenantId && t.Status == TenantStatus.Active)
            .Select(t => new TenantInfo(t.Id, t.Code, t.NameEn, t.NameAr, t.Status) { TimeZone = t.TimeZone })
            .SingleOrDefaultAsync(cancellationToken);
    }
}

internal sealed class CompanyDirectory(TenancyDbContext db, ICompanyContext scope) : ICompanyDirectory
{
    public async Task<CompanyInfo?> GetAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Companies.AsNoTracking().Where(c => c.Id == companyId)
            .Select(c => new CompanyInfo(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency, c.FiscalYearStartMonth, c.FiscalYearStartDay, c.Country, c.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CompanyInfo>> ListAsync(CancellationToken cancellationToken) =>
        await db.Companies.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new CompanyInfo(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency, c.FiscalYearStartMonth, c.FiscalYearStartDay, c.Country, c.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<CompanyInfo?> GetWorkingAsync(CancellationToken cancellationToken) =>
        scope.ActiveCompanyId is { } id ? await GetAsync(id, cancellationToken) : null;
}
