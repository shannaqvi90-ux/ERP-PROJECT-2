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
        module.Services.AddScoped<SignInService>();
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
        module.List(new ListDefinition(
            "identity.users", "identity.users.title", IdentityPermissions.UsersRead, "/api/identity/users",
            [
                new ListColumn("displayName", "identity.users.name", ListColumnType.Text, Sortable: true),
                new ListColumn("email", "identity.users.email", ListColumnType.Text, Sortable: true),
                new ListColumn("language", "identity.users.language", ListColumnType.Choice, Filterable: true),
                new ListColumn("isActive", "identity.users.status", ListColumnType.Boolean, Filterable: true),
                new ListColumn("lastSignInAt", "identity.users.lastSignIn", ListColumnType.DateTime, Sortable: true),
                new ListColumn("createdAt", "identity.users.created", ListColumnType.DateTime, Sortable: true),
            ],
            SearchFields: ["displayName", "email"],
            DefaultSort: "-createdAt"));
        module.List(new ListDefinition(
            "identity.roles", "identity.roles.title", IdentityPermissions.RolesRead, "/api/identity/roles",
            [
                new ListColumn("nameEn", "identity.roles.name", ListColumnType.Text, Sortable: true),
                new ListColumn("isSystem", "identity.roles.kind", ListColumnType.Boolean, Filterable: true),
                new ListColumn("userCount", "identity.roles.users", ListColumnType.Number, Sortable: true),
                new ListColumn("permissions", "identity.roles.permissions", ListColumnType.Choice),
            ],
            SearchFields: [],
            DefaultSort: "nameEn"));
        module.Seeder<IdentitySeeder>();
    }
}

public sealed class User : TenantEntity
{
    public string Email { get; set; } = "";

    /// <summary>Lower-case, trimmed e-mail used for sign-in lookups.</summary>
    public string EmailNormalized { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>en or ar.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Digits on Arabic screens: latn (0123) or arab (٠١٢٣). See <see cref="Erp.Kernel.Localization.NumeralSystems"/>.</summary>
    public string Numerals { get; set; } = "latn";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int FailedSignInCount { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
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
    public DbSet<Session> Sessions => Set<Session>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_language", "language IN ('en', 'ar')");
                t.HasCheckConstraint("ck_users_numerals", "numerals IN ('latn', 'arab')");
                t.HasCheckConstraint("ck_users_email_normalized", "email_normalized = lower(btrim(email))");
                t.HasCheckConstraint("ck_users_failed_sign_in_count", "failed_sign_in_count >= 0");
            });
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.EmailNormalized).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Language).HasMaxLength(2);
            // Bulk seeding copies rows without this column; the database fills the default.
            e.Property(x => x.Numerals).HasMaxLength(4).HasDefaultValue("latn");
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.EmailNormalized }).IsUnique();
            // Sign-in looks a user up by e-mail before the tenant is known.
            e.HasIndex(x => x.EmailNormalized);
            e.HasIndex(x => new { x.TenantId, x.DisplayName });
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

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
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
}

public sealed class AuthOptions
{
    /// <summary>Absolute session lifetime.</summary>
    public int SessionHours { get; set; } = 12;

    /// <summary>Failed attempts before the account is paused.</summary>
    public int LockoutThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Always mark the cookie Secure (set in production behind TLS). When false the
    /// cookie is Secure only on HTTPS requests, so the local demo works over http://localhost.</summary>
    public bool AlwaysSecureCookie { get; set; }
}
