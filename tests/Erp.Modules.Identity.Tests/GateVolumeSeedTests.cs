using Erp.Kernel.Seeding;
using Erp.Modules.Identity.Seeding;

namespace Erp.Modules.Identity.Tests;

/// <summary>
/// The list volume gate (G1ListVolumeTests) counts, per search letter, how many of each tenant's
/// rows match, and only a search whose counts differ and lie between 1 and 10,000 in both tenants
/// counts as ranked. Tenant B's code carries a fresh canary every run; when its generated people
/// were drawn from that code, B's matches per letter changed from run to run and the gate's tally
/// of ranked searches with them (integration of p05 round 11: 5 against a minimum of 6). So the
/// volume profile draws B's people from a fixed seed, and the canary still marks B's text.
/// </summary>
public sealed class GateVolumeSeedTests
{
    [Fact]
    public void The_volume_gate_seeds_the_same_people_whatever_the_canary()
    {
        var first = SeedPlan.GateAtVolume("CNRY0123456789", "pw", 100, 120);
        var second = SeedPlan.GateAtVolume("CNRYABCDEF9876", "pw", 100, 120);
        var (b1, b2) = (first.Tenants[1], second.Tenants[1]);

        Assert.Equal(SeedPlan.GateVolumeSeedB, b1.VolumeRowsSeed);
        Assert.Equal(b1.VolumeRowsSeed, b2.VolumeRowsSeed);
        Assert.DoesNotContain("cnry", b1.VolumeRowsSeed, StringComparison.OrdinalIgnoreCase);
        // The canary still marks tenant B (its code, names and e-mail domain), so a leak stays visible.
        Assert.Contains("cnry0123456789", b1.Code, StringComparison.Ordinal);
        Assert.Equal("CNRY0123456789", b1.Canary);
        Assert.Contains("cnry0123456789", b1.EmailDomain, StringComparison.Ordinal);

        Assert.Equal(DemoPeople.Generate(b1.VolumeRowsSeed, 2_000), DemoPeople.Generate(b2.VolumeRowsSeed, 2_000));
        // The two tenants still hold different people.
        Assert.NotEqual(DemoPeople.Generate(first.Tenants[0].VolumeRowsSeed, 2_000), DemoPeople.Generate(b1.VolumeRowsSeed, 2_000));
    }

    [Fact]
    public void A_tenant_without_a_volume_seed_draws_its_people_from_its_code()
    {
        var gate = SeedPlan.Gate("CNRY0123456789", "pw");
        Assert.All(gate.Tenants, t => Assert.Equal(t.Code, t.VolumeRowsSeed));
        Assert.All(SeedPlan.Demo(10, "pw").Tenants, t => Assert.Equal(t.Code, t.VolumeRowsSeed));
    }
}
