using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Shell;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Roles;
using Erp.Modules.Identity.Seeding;
using Erp.Modules.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Identity;

/// <summary>Users, roles, permissions granted by roles, sign-in and sessions.</summary>
public sealed class IdentityModule : ErpModule
{
    public override string Name => "identity";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. IdentityPermissions.All]);
        module.DbContext<IdentityDbContext>();
        module.Services.AddOptions<AuthOptions>().Bind(module.Configuration.GetSection("Erp:Auth"));
        module.Services.AddScoped<ISessionResolver, SessionResolver>();
        module.Services.AddScoped<SessionGrants>();
        module.Services.AddScoped<ISessionPermissionScope>(sp => sp.GetRequiredService<SessionGrants>());
        module.Services.AddScoped<SignInService>();
        module.Services.AddSingleton<TrustedDevices>();
        module.Services.AddScoped<SessionPayload>();
        module.Services.AddScoped<IUserDirectory, UserDirectory>();
        module.Endpoints("auth", AuthEndpoints.Map);
        module.Endpoints(group =>
        {
            UserEndpoints.Map(group);
            RoleEndpoints.Map(group);
            ProfileEndpoints.Map(group);
        });
        module.Menu(new MenuEntry("identity.users", "identity.menu.users", "/identity/users", IdentityPermissions.UsersRead, Order: 800, Group: "settings"));
        module.Menu(new MenuEntry("identity.roles", "identity.menu.roles", "/identity/roles", IdentityPermissions.RolesRead, Order: 810, Group: "settings"));
        module.Menu(new MenuEntry("identity.me", "identity.menu.me", "/identity/me", IdentityPermissions.ProfileUpdate, Order: 990, Group: "personal"));
        module.List(UsersList.Create());
        module.List(RolesList.Create());
        // Reports print both lists exactly as their screens show them, and run users by role.
        module.ListRows(UsersList.Key, async (services, request, http, cancellationToken) =>
            (await UserEndpoints.PageAsync(services.GetRequiredService<IdentityDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.ListRows(RolesList.Key, async (services, request, http, cancellationToken) =>
            (await RoleEndpoints.PageAsync(services.GetRequiredService<IdentityDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.Report<Reports.UsersByRoleReport>(Reports.UsersByRoleReport.Definition);
        module.Report<Reports.RoleSummaryReport>(Reports.RoleSummaryReport.Definition);
        module.Seeder<IdentitySeeder>();
        module.Seeder<IdentityCompanyRoleSeeder>();
    }
}

public sealed class User : TenantEntity
{
    public string Email { get; set; } = "";

    /// <summary>Lower-case, trimmed e-mail used for sign-in lookups.</summary>
    public string EmailNormalized { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>The name written in Arabic, shown on Arabic screens when given (the display name otherwise).</summary>
    public string? DisplayNameAr { get; set; }

    /// <summary>en or ar.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Digits on Arabic screens: latn (0123) or arab (٠١٢٣). See <see cref="Erp.Kernel.Localization.NumeralSystems"/>.</summary>
    public string Numerals { get; set; } = "latn";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>When an administrator last cleared this account's sign-in pauses. Failed attempts
    /// before it no longer count.</summary>
    public DateTimeOffset? SignInUnblockedAt { get; set; }

    /// <summary>How many roles the user holds in one company (rows of <see cref="UserCompanyRole"/>
    /// in every company, kept by a database trigger). An administrator sees only the rows of the
    /// companies they work in; comparing with this tells whether the user holds roles elsewhere.</summary>
    public int CompanyRoleCount { get; set; }
}

/// <summary>
/// A user's password, kept apart from the user so the application role can write it but never read
/// it back (column privileges: no SELECT on <c>password_hash</c>). Only the reviewed sign-in
/// function reads it. Never load this entity with a query; insert it, update it with
/// <c>ExecuteUpdate</c>, and select only <see cref="MustChange"/>, <see cref="ExpiresAt"/> and
/// <see cref="ChangedAt"/>.
/// </summary>
public sealed class UserCredential : ITenantOwned
{
    /// <summary>The user's id (one credential per user).</summary>
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string PasswordHash { get; set; } = "";

    /// <summary>A one-time set-up code (invitation or reset): the next sign-in must choose a new
    /// password.</summary>
    public bool MustChange { get; set; }

    /// <summary>When a set-up code stops working; null for an ordinary password.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

/// <summary>
/// One sign-in attempt on an account: the sign-in history. Successes are written by the app with
/// their session; failures, paused clients and refused accounts by the reviewed sign-in function.
/// Append-only: the application role may insert and read, never change or delete.
/// </summary>
public sealed class SignInAttempt : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>succeeded, failed, throttled, inactive or expired (<see cref="SignInOutcomes"/>).</summary>
    public string Outcome { get; set; } = "";

    /// <summary>The client key failures are counted under: the IPv4 address or the IPv6 /64.</summary>
    public string Source { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? SessionId { get; set; }
}

public static class SignInOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Throttled = "throttled";
    public const string Inactive = "inactive";
    public const string Expired = "expired";
}

public sealed class Role : TenantEntity
{
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";

    /// <summary>Permission keys the role grants.</summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>System roles (Administrator) are maintained by the platform and cannot be
    /// edited or deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Stable key for system roles (administrator), null for tenant-defined roles.</summary>
    public string? SystemKey { get; set; }
}

public sealed class UserRole : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

/// <summary>
/// A role a user holds in one company only: what it grants counts while the user works in that
/// company and nowhere else ("accountant in the Dubai LLC, read-only in the JAFZA entity"). Rows
/// belong to their company (row-level security shows a caller only the companies they work in;
/// the user's own rows stay readable to their session, which reads them before its scope exists).
/// A role in <see cref="UserRole"/> applies in every company.
/// </summary>
public sealed class UserCompanyRole : TenantEntity, ICompanyOwned
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid CompanyId { get; set; }
}

/// <summary>A signed-in session. Only the SHA-256 hash of the token is stored.</summary>
public sealed class Session : TenantEntity
{
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext? tenant = null)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "identity";

    protected override string Schema => SchemaName;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserCompanyRole> UserCompanyRoles => Set<UserCompanyRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<UserCredential> Credentials => Set<UserCredential>();
    public DbSet<SignInAttempt> SignInAttempts => Set<SignInAttempt>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_language", "language IN ('en', 'ar')");
                t.HasCheckConstraint("ck_users_numerals", "numerals IN ('latn', 'arab')");
                t.HasCheckConstraint("ck_users_email_normalized", "email_normalized = lower(btrim(email))");
                t.HasCheckConstraint("ck_users_company_role_count", "company_role_count >= 0");
            });
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.EmailNormalized).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.DisplayNameAr).HasMaxLength(200);
            e.Property(x => x.Language).HasMaxLength(2);
            // Bulk seeding copies rows without this column; the database fills the default.
            e.Property(x => x.Numerals).HasMaxLength(4).HasDefaultValue("latn");
            e.HasIndex(x => new { x.TenantId, x.EmailNormalized }).IsUnique();
            // Sign-in looks a user up by e-mail before the tenant is known.
            e.HasIndex(x => x.EmailNormalized);
            e.HasIndex(x => new { x.TenantId, x.DisplayName });
            // List framework: word search on trigram indexes, keyset order on (tenant, column, id).
            e.HasIndex(x => new { x.DisplayName, x.EmailNormalized }, "ix_users_search")
                .HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => x.DisplayNameAr, "ix_users_search_ar").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.LastSignInAt, x.Id });
            // Kept by the count_company_roles trigger, never written by the application.
            e.Property(x => x.CompanyRoleCount).HasDefaultValue(0).ValueGeneratedOnAddOrUpdate();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles", t => t.HasCheckConstraint("ck_roles_system_key", "(is_system AND system_key IS NOT NULL) OR (NOT is_system AND system_key IS NULL)"));
            e.Property(x => x.NameEn).HasMaxLength(100);
            e.Property(x => x.NameAr).HasMaxLength(100);
            e.Property(x => x.SystemKey).HasMaxLength(40);
            e.Property(x => x.Permissions).HasColumnType("text[]");
            e.HasIndex(x => new { x.TenantId, x.NameEn }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SystemKey }).IsUnique();
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles");
            e.HasIndex(x => new { x.TenantId, x.UserId, x.RoleId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RoleId });
            e.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(r => new { r.TenantId, r.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserCompanyRole>(e =>
        {
            e.ToTable("user_company_roles");
            e.HasIndex(x => new { x.TenantId, x.UserId, x.RoleId, x.CompanyId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RoleId });
            e.HasIndex(x => new { x.TenantId, x.CompanyId });
            e.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Role>().WithMany().HasForeignKey(x => new { x.TenantId, x.RoleId })
                .HasPrincipalKey(r => new { r.TenantId, r.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserCredential>(e =>
        {
            e.ToTable("user_credentials", t =>
                t.HasCheckConstraint("ck_user_credentials_expiry", "expires_at IS NULL OR must_change"));
            e.HasKey(x => x.Id);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.HasOne<User>().WithOne().HasForeignKey<UserCredential>(x => new { x.TenantId, x.Id })
                .HasPrincipalKey<User>(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SignInAttempt>(e =>
        {
            e.ToTable("sign_in_attempts", t =>
                t.HasCheckConstraint("ck_sign_in_attempts_outcome", "outcome IN ('succeeded', 'failed', 'throttled', 'inactive', 'expired')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.Source).HasMaxLength(64);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(400);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.OccurredAt });
            // The sign-in function counts a client's recent failures on one account.
            e.HasIndex(x => new { x.UserId, x.Source, x.OccurredAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.Property(x => x.TokenHash).HasColumnType("bytea");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(400);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
                .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal sealed class IdentityDbContextDesignFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", IdentityDbContext.SchemaName);
        return new IdentityDbContext(options.Options);
    }
}

internal sealed class UserDirectory(IdentityDbContext db, ModuleCatalog catalog) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }
        return await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.DisplayName, u.Email))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
    }

    public async Task<UserSummaryPage> SearchAsync(string? search, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + ListBinding<User>.EscapeLike(search.Trim().ToLowerInvariant()) + "%";
            query = query.Where(u => EF.Functions.ILike(u.EmailNormalized, pattern, "\\") || EF.Functions.ILike(u.DisplayName, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, UserEndpoints.MaxPageSize))
            .Select(u => new UserSummary(u.Id, u.DisplayName, u.Email))
            .ToListAsync(cancellationToken);
        return new UserSummaryPage(items, total);
    }

    public async Task<ListResult<UserSummary>> QueryListAsync(string listKey, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        // Only lists bound to identity's users resolve here (ListBinding<User> throws otherwise).
        var result = await catalog.ListBinding<User>(listKey).QueryAsync(db.Users.AsNoTracking(), request, http, cancellationToken);
        return result.Map(u => new UserSummary(u.Id, u.DisplayName, u.Email));
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var grants = await GrantQueries.ForUserAsync(db, userId, catalog, ownRows: false, cancellationToken);
        // Roles held in companies outside the current scope grant what nobody here can see: the
        // user counts as holding everything, so only someone who holds everything acts on them.
        return grants.Hidden > 0 ? catalog.PermissionKeys.ToHashSet(StringComparer.Ordinal) : grants.Anywhere();
    }

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await db.Users.AsNoTracking().Where(u => u.EmailNormalized == normalized)
            .Select(u => new UserSummary(u.Id, u.DisplayName, u.Email))
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed class AuthOptions
{
    /// <summary>Absolute session lifetime.</summary>
    public int SessionHours { get; set; } = 12;

    /// <summary>Failed attempts from one client on one account, within
    /// <see cref="LockoutMinutes"/>, after which that client (only) is paused on that account.
    /// The sign-in function clamps it to 3–50.</summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>The window failures are counted in, in minutes (clamped to 1–1440).</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>How long a set-up code (invitation or reset) works, in hours.</summary>
    public int SetupCodeHours { get; set; } = 168;

    /// <summary>Always mark the cookie Secure (set in production behind TLS). When false the
    /// cookie is Secure only on HTTPS requests, so the local demo works over http://localhost.</summary>
    public bool AlwaysSecureCookie { get; set; }

    /// <summary>Key that signs the trusted-device cookie (<see cref="TrustedDevices"/>). Set it
    /// when several app instances serve one deployment, so each accepts the others' cookies;
    /// unset, each process signs with a random key of its own and devices fall back to their
    /// network address after a restart.</summary>
    public string? DeviceKey { get; set; }

    /// <summary>How long a browser stays a trusted device of an account after signing in to it.</summary>
    public int DeviceDays { get; set; } = 180;
}
