using System.Net;
using System.Net.Http.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>Critic p03 round 3 proof for plant L7. Tenant B creates a role; tenant A then creates a
/// role with the same name and with a name nobody uses. Both must answer 201; with L7 the first
/// answers 409, telling tenant A which role names tenant B uses. Copy into
/// tests/Erp.Modules.Identity.Tests/ and run with --filter CriticRoleNameOracleProof.</summary>
public sealed class CriticRoleNameOracleProof(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Tenant_A_cannot_learn_tenant_B_role_names()
    {
        var log = TestContext.Current.TestOutputHelper!;
        var tag = Guid.NewGuid().ToString("N")[..6];
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var created = await b.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"JAFZA payroll {tag}", nameAr = $"رواتب {tag}", permissions = new[] { "identity.users.read" } });
        log.WriteLine($"tenant B creates 'JAFZA payroll {tag}': {(int)created.StatusCode}");
        using var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var same = await a.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"JAFZA payroll {tag}", nameAr = $"رواتب {tag}", permissions = new[] { "identity.users.read" } });
        log.WriteLine($"tenant A creates B's name: {(int)same.StatusCode} {await same.Content.ReadAsStringAsync()}");
        var unused = await a.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Nobody uses {tag}", nameAr = $"لا أحد {tag}", permissions = new[] { "identity.users.read" } });
        log.WriteLine($"tenant A creates an unused name: {(int)unused.StatusCode}");
        Assert.Equal(unused.StatusCode, same.StatusCode);
    }
}
