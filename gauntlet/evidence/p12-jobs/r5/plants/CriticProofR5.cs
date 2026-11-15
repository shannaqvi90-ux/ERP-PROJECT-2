using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>Critic p12 r5: proofs that plants L4 and P6 are live (they must FAIL on the planted code and pass unplanted).</summary>
public sealed class CriticProofR5(JobsWriteSetFixture fixture) : IClassFixture<JobsWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task P6_a_schedule_added_without_isEnabled_needs_the_run_permission()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Role crit", nameAr = "دور crit", permissions = new[] { "jobs.schedules.create", "jobs.schedules.read" } });
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new { email = $"crit.adder@{Env.TenantA.EmailDomain}", displayName = "crit", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}");
        var given = access.GetProperty("options").EnumerateArray().Select(o => new { companyId = o.GetProperty("id").GetGuid(), allBranches = true, branchIds = Array.Empty<Guid>() }).ToList();
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access.GetProperty("version").GetUInt32() });
        using var adder = await Env.SignInAsync($"crit.adder@{Env.TenantA.EmailDomain}");
        using var made = await adder.PostAsJsonAsync("/api/jobs/schedules", new { scheduleName = $"crit omit {Guid.NewGuid():N}", type = "jobs.cleanup", cron = "* * * * *", payload = new { retentionDays = 1 } });
        var body = await made.Content.ReadAsStringAsync();
        Assert.True(made.StatusCode == HttpStatusCode.Forbidden, $"isEnabled omitted: got {(int)made.StatusCode} {body}");
    }

    [Fact]
    public async Task L4_the_worker_lookup_role_reads_no_other_column_of_any_workspace_job()
    {
        var cs = Env.Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Worker")!;
        await using var c = new NpgsqlConnection(cs);
        await c.OpenAsync();
        var readable = new List<string>();
        await using (var cmd = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema='jobs' AND table_name='jobs' AND has_column_privilege(current_user, 'jobs.jobs', column_name, 'SELECT')", c))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) readable.Add(r.GetString(0));
        }
        Assert.True(readable.OrderBy(x => x).SequenceEqual(new[] { "locked_until", "run_at", "status", "tenant_id" }), "erp_worker may read: " + string.Join(", ", readable));
    }
}
