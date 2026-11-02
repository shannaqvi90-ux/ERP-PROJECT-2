namespace Erp.Gates.Tests.G1;

/// <summary>Critic p07 r2 only (never committed): the HTTP isolation attack on the audit module's surface alone.</summary>
public sealed class CriticAuditScopeTests(G1AttackFixture fixture) : IClassFixture<G1AttackFixture>
{
    [Fact]
    public async Task Critic_audit_surface_isolation()
    {
        var report = await IsolationAttack.RunAsync(fixture.Env, sortedLists: l => l.Key.StartsWith("audit.", StringComparison.Ordinal),
            only: e => e.Pattern.StartsWith("/api/audit/", StringComparison.Ordinal) || e.Pattern.Contains("/audit.log", StringComparison.Ordinal));
        foreach (var leak in report.Leaks) TestContext.Current.TestOutputHelper?.WriteLine($"LEAK: {leak}");
        foreach (var o in report.Oracles) TestContext.Current.TestOutputHelper?.WriteLine($"ORACLE: {o}");
        TestContext.Current.TestOutputHelper?.WriteLine($"attacked {report.EndpointsAttacked} endpoints");
        foreach (var v in report.BindViolations) TestContext.Current.TestOutputHelper?.WriteLine($"BIND: {v}");
        foreach (var v in report.SettingViolations) TestContext.Current.TestOutputHelper?.WriteLine($"SETTING: {v}");
        foreach (var v in report.TenantValueViolations) TestContext.Current.TestOutputHelper?.WriteLine($"TENANTVALUE: {v}");
        foreach (var v in report.ServerErrors) TestContext.Current.TestOutputHelper?.WriteLine($"SERVERERROR: {v}");
        foreach (var v in report.InputEnumerations) TestContext.Current.TestOutputHelper?.WriteLine($"INPUTENUM: {v}");
        TestContext.Current.TestOutputHelper?.WriteLine($"COUNTS leaks={report.Leaks.Count} oracles={report.Oracles.Count} bind={report.BindViolations.Count} setting={report.SettingViolations.Count} tenantvalue={report.TenantValueViolations.Count} servererrors={report.ServerErrors.Count} inputenum={report.InputEnumerations.Count}");
        Assert.Empty(report.Leaks);
        Assert.Empty(report.Oracles);
        Assert.Empty(report.BindViolations);
        Assert.Empty(report.SettingViolations);
        Assert.Empty(report.TenantValueViolations);
    }
}

public sealed class CriticT2ConfirmTests(G1AttackFixture fixture) : IClassFixture<G1AttackFixture>
{
    [Fact]
    public async Task Critic_T2_really_leaks()
    {
        var env = fixture.Env;
        var b = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        var bLog = await b.GetStringAsync("/api/audit/log?take=50&filter=recordType%20eq%20'identity.users'");
        var doc = System.Text.Json.JsonDocument.Parse(bLog);
        var rec = doc.RootElement.GetProperty("items")[0].GetProperty("recordId").GetString();
        var a = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var plain = await a.GetStringAsync($"/api/audit/records/{rec}");
        var switched = await a.GetStringAsync($"/api/audit/records/{rec}?tenant={env.TenantB.Id}");
        TestContext.Current.TestOutputHelper?.WriteLine($"B record {rec}\nplain: {plain[..Math.Min(300, plain.Length)]}\nswitched: {switched[..Math.Min(600, switched.Length)]}");
        Assert.Contains("\"items\":[]", plain);
        Assert.DoesNotContain("\"items\":[]", switched);
    }
}
