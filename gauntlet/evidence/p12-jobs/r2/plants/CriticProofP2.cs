using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Critic r2 proof for plant P2: a user holding jobs.jobs.run and jobs.jobs.read, but not the clean-up kind's
/// own run permission (jobs.schedules.update), runs a finished clean-up job again.</summary>
public sealed class CriticProofP2(JobsWriteSetFixture fixture) : IClassFixture<JobsWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Rerun_of_a_cleanup_job_by_a_user_without_the_kinds_permission()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var runner = await UserHoldingAsync(admin, "cp2.runner", ["jobs.jobs.run", "jobs.jobs.read"]);
        var started = await admin.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.cleanup", payload = new { retentionDays = 30 }, runAt = DateTimeOffset.UtcNow.AddHours(2) });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var original = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var cancelled = await admin.PostAsync($"/api/jobs/jobs/{original}/cancel", null);
        using var again = await runner.PostAsync($"/api/jobs/jobs/{original}/rerun", null);
        using var start = await runner.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.cleanup", payload = new { retentionDays = 30 } });
        Assert.Fail($"rerun by runner: {(int)again.StatusCode}; start by runner: {(int)start.StatusCode}");
    }


    [Fact]
    public async Task A_user_who_may_only_add_schedules_schedules_a_kind_he_may_not_run()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var planner = await UserHoldingAsync(admin, "cp2.planner", ["jobs.schedules.create", "jobs.schedules.read"]);
        using var start = await planner.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.cleanup", payload = new { retentionDays = 30 } });
        using var kinds = await planner.GetAsync("/api/jobs/types");
        using var schedule = await planner.PostAsJsonAsync("/api/jobs/schedules", new { scheduleName = "every minute clean-up", type = "jobs.cleanup", cron = "* * * * *", payload = new { retentionDays = 30 } });
        Assert.Fail($"start cleanup by planner: {(int)start.StatusCode}; kinds: {(int)kinds.StatusCode}; schedule cleanup by planner: {(int)schedule.StatusCode} {await schedule.Content.ReadAsStringAsync()}");
    }

    private async Task<HttpClient> UserHoldingAsync(HttpClient admin, string local, string[] permissions)
    {
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Role {local}", nameAr = $"دور {local}", permissions });
        Assert.True(role.StatusCode == HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"{local}@{Env.TenantA.EmailDomain}", displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { roleId },
        });
        Assert.True(user.StatusCode == HttpStatusCode.Created, await user.Content.ReadAsStringAsync());
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}");
        var given = access.GetProperty("options").EnumerateArray()
            .Select(o => new { companyId = o.GetProperty("id").GetGuid(), allBranches = true, branchIds = Array.Empty<Guid>() }).ToList();
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access.GetProperty("version").GetUInt32() });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return await Env.SignInAsync($"{local}@{Env.TenantA.EmailDomain}");
    }

}
