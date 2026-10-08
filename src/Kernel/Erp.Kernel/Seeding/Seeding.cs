namespace Erp.Kernel.Seeding;

/// <summary>How much data to seed.</summary>
public enum SeedProfile
{
    /// <summary>Only what is needed to sign in.</summary>
    Minimal,

    /// <summary>The demo: realistic data and Odoo-comparable volume.</summary>
    Demo,

    /// <summary>The gate fixture: two tenants, tenant B full of canary data.</summary>
    Gate,

    /// <summary>A new customer workspace provisioned by a platform operator: its first company and
    /// branch, the Administrator role and one administrator, no demo data.</summary>
    Provision,
}

/// <summary>The first administrator of a provisioned workspace.</summary>
public sealed record SeedAdministrator(string Email, string DisplayName, string Language, string Password);

/// <summary>A tenant to seed.</summary>
/// <param name="Canary">Unique marker woven into every text field of this tenant's seed data so a
/// leak is detectable anywhere (null for ordinary tenants).</param>
/// <param name="Volume">Rows per main list when the profile carries volume.</param>
public sealed record SeedTenant(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string EmailDomain,
    string? Canary,
    int Volume,
    SeedAdministrator? Administrator = null);

public sealed record SeedPlan(SeedProfile Profile, IReadOnlyList<SeedTenant> Tenants, string DemoPassword)
{
    /// <summary>The demo: one large trading company and one smaller second tenant.</summary>
    public static SeedPlan Demo(int volume, string password) => new(SeedProfile.Demo,
    [
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-000000000001"), "alnoor", "Al Noor Trading LLC", "شركة النور للتجارة ذ.م.م", "alnoor.example", null, volume),
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-000000000002"), "gulfsteel", "Gulf Steel Fabrication LLC", "الخليج لتصنيع الصلب ذ.م.م", "gulfsteel.example", null, Math.Min(volume, 1000)),
    ], password);

    /// <summary>One new workspace for a platform operator (no demo data, no demo password).</summary>
    public static SeedPlan Provision(SeedTenant tenant) => new(SeedProfile.Provision, [tenant],
        tenant.Administrator?.Password ?? throw new ArgumentException("A provisioned workspace needs its first administrator.", nameof(tenant)));

    public static SeedPlan Minimal(string password) => new(SeedProfile.Minimal,
    [
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-000000000001"), "alnoor", "Al Noor Trading LLC", "شركة النور للتجارة ذ.م.م", "alnoor.example", null, 0),
    ], password);

    /// <summary>The isolation gate fixture: tenant A attacks, tenant B carries canaries.</summary>
    public static SeedPlan Gate(string canary, string password) => new(SeedProfile.Gate,
    [
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-00000000000a"), "alpha", "Alpha Trading LLC", "ألفا للتجارة ذ.م.م", "alpha.example", null, 25),
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-00000000000b"), $"bravo{canary.ToLowerInvariant()}", $"Bravo {canary} LLC", $"برافو {canary} ذ.م.م", $"bravo-{canary.ToLowerInvariant()}.example", canary, 25),
    ], password);
}

/// <summary>Context for one tenant's seeding. The database session is already bound to the tenant.</summary>
public sealed record TenantSeedContext(SeedPlan Plan, SeedTenant Tenant, IServiceProvider Services)
{
    /// <summary>Decorate a text value with the tenant's canary (if any).</summary>
    public string Mark(string value) => Tenant.Canary is null ? value : $"{value} {Tenant.Canary}";
}

/// <summary>Seeds one module's data for one tenant. Must be idempotent: running the seed twice
/// leaves the same data.</summary>
public interface ITenantSeeder
{
    /// <summary>Lower runs first. Tenancy 0, identity 10, others 100+.</summary>
    int Order { get; }

    Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken);
}
