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
/// <param name="VolumeSeed">What the generated volume rows (people's names) are drawn from; the
/// tenant's <paramref name="Code"/> when null. Set where the code carries a fresh canary but the
/// rows must be the same every run (the list volume gate counts matches per search letter).</param>
public sealed record SeedTenant(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string EmailDomain,
    string? Canary,
    int Volume,
    SeedAdministrator? Administrator = null,
    string? VolumeSeed = null)
{
    /// <summary>The seed of this tenant's generated volume rows.</summary>
    public string VolumeRowsSeed => VolumeSeed ?? Code;
}

public sealed record SeedPlan(SeedProfile Profile, IReadOnlyList<SeedTenant> Tenants, string DemoPassword)
{
    /// <summary>Rows in the demo's main list (the owner's bar: find one record among 100,000), unless
    /// configured otherwise (<c>Erp:Seed:Volume</c>).</summary>
    public const int DemoVolume = 100_000;

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
    public static SeedPlan Gate(string canary, string password) => GatePlan(canary, password, 25, 25, null);

    /// <summary>What tenant B's generated rows are drawn from in <see cref="GateAtVolume"/>: fixed,
    /// never the canary-bearing code, so B holds the same people (and the same matches per search
    /// letter) on every run.</summary>
    public const string GateVolumeSeedB = "bravo";

    /// <summary>The isolation gate fixture with <paramref name="volumeA"/> and <paramref name="volumeB"/>
    /// rows in each tenant's main lists: the list gates' large-list profile, where code that only runs
    /// for a big list (an estimated total, a deep page, a broad search left unranked) is judged too.
    /// Both tenants' generated rows come from fixed seeds (<see cref="GateVolumeSeedB"/> for tenant B),
    /// so the volume gate's tallies (searches ranked or too big to rank in both tenants, with different
    /// counts) are the same on every run; the canary still marks every text value of tenant B.</summary>
    public static SeedPlan GateAtVolume(string canary, string password, int volumeA, int volumeB) => GatePlan(canary, password, volumeA, volumeB, GateVolumeSeedB);

    private static SeedPlan GatePlan(string canary, string password, int volumeA, int volumeB, string? volumeSeedB) => new(SeedProfile.Gate,
    [
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-00000000000a"), "alpha", "Alpha Trading LLC", "ألفا للتجارة ذ.م.م", "alpha.example", null, volumeA),
        new SeedTenant(Guid.Parse("0190a000-0000-7000-8000-00000000000b"), $"bravo{canary.ToLowerInvariant()}", $"Bravo {canary} LLC", $"برافو {canary} ذ.م.م", $"bravo-{canary.ToLowerInvariant()}.example", canary, volumeB,
            VolumeSeed: volumeSeedB),
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
