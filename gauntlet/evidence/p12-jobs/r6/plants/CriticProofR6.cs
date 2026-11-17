using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Critic p12 r6: proof that plant P7 is live (FAILS on the planted code, passes unplanted). A user holding
/// jobs.schedules.update and read, not jobs.jobs.run, changes only a number in a self-check schedule's parameters.</summary>
public sealed class CriticProofR6(JobsWriteSetFixture fixture) : IClassFixture<JobsWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task P7_changing_only_a_number_in_the_parameters_needs_the_run_permission()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Role crit6", nameAr = "دور crit6", permissions = new[] { "jobs.schedules.update", "jobs.schedules.read" } });
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new { email = $"crit6.changer@{Env.TenantA.EmailDomain}", displayName = "crit6", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}");
        var given = access.GetProperty("options").EnumerateArray().Select(o => new { companyId = o.GetProperty("id").GetGuid(), allBranches = true, branchIds = Array.Empty<Guid>() }).ToList();
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access.GetProperty("version").GetUInt32() });
        using var changer = await Env.SignInAsync($"crit6.changer@{Env.TenantA.EmailDomain}");
        using var made = await admin.PostAsJsonAsync("/api/jobs/schedules", new { scheduleName = $"crit6 {Guid.NewGuid():N}", type = "jobs.selfCheck", cron = "30 3 * * *", payload = new { steps = 3 }, isEnabled = true });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var s = await made.Content.ReadFromJsonAsync<JsonElement>();
        using var change = await changer.PutAsJsonAsync($"/api/jobs/schedules/{s.GetProperty("id").GetGuid()}",
            new { scheduleName = s.GetProperty("scheduleName").GetString(), cron = "30 3 * * *", payload = new { steps = 12 }, isEnabled = true, version = s.GetProperty("version").GetUInt32() });
        var body = await change.Content.ReadAsStringAsync();
        Assert.True(change.StatusCode == HttpStatusCode.Forbidden, $"steps 3 -> 12 by a user without jobs.jobs.run: got {(int)change.StatusCode} {body}");
    }
}
