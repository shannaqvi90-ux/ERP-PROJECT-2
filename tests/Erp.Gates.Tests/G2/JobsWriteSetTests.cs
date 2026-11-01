using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what jobs.jobs.run and jobs.jobs.cancel let a request write once a job has finished. The generic
/// write-set gate (<see cref="WriteSets"/>) reaches every job endpoint at a job it has just queued, but
/// a job can be run again only after it has finished and the gate's workspace runs no worker, so POST
/// /api/jobs/jobs/{id}/rerun is listed in tests/Gates/write-set-unexercised.txt. This gate sends it
/// itself: an administrator queues a job and cancels it (a cancelled job has finished), then a user
/// holding exactly jobs.jobs.run runs it again. The audit trail must show one new job row and nothing
/// else (the write set of jobs.jobs.run), the original job untouched, and a user holding only
/// jobs.jobs.read or only jobs.jobs.cancel must be refused with nothing written.
/// </summary>
public sealed class JobsWriteSetTests(JobsWriteSetFixture fixture) : IClassFixture<JobsWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Running_a_finished_job_again_writes_one_new_job_and_changes_nothing_else()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var runner = await UserHoldingAsync(admin, "jws.runner", ["jobs.jobs.run"]);
        var started = await admin.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.selfCheck", runAt = DateTimeOffset.UtcNow.AddHours(2) });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var original = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var cancelled = await admin.PostAsync($"/api/jobs/jobs/{original}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var before = await LastAuditIdAsync();
        using var again = await runner.PostAsync($"/api/jobs/jobs/{original}/rerun", null);
        var text = await again.Content.ReadAsStringAsync();
        Assert.True(again.StatusCode == HttpStatusCode.Created, text);
        var made = JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
        var writes = await WritesSinceAsync(before);
        Assert.NotEmpty(writes);
        var problems = writes.Where(w => !(w.Table == "jobs.jobs" && w.Action == "insert" && w.Record == made))
            .Select(w => $"wrote {w.Action} {w.Table} {w.Record} ({string.Join(", ", w.Columns)}) outside the write set of jobs.jobs.run (one new job)").ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Contains(writes, w => w.Table == "jobs.jobs" && w.Action == "insert" && w.Record == made);
    }

    [Fact]
    public async Task A_reader_and_a_user_who_may_only_cancel_cannot_run_a_finished_job_again()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var reader = await UserHoldingAsync(admin, "jws.reader", ["jobs.jobs.read"]);
        using var canceller = await UserHoldingAsync(admin, "jws.canceller", ["jobs.jobs.cancel", "jobs.jobs.read"]);
        var started = await admin.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.selfCheck", runAt = DateTimeOffset.UtcNow.AddHours(2) });
        var original = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var cancelled = await admin.PostAsync($"/api/jobs/jobs/{original}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var before = await LastAuditIdAsync();
        foreach (var client in new[] { reader, canceller })
        {
            using var refused = await client.PostAsync($"/api/jobs/jobs/{original}/rerun", null);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            using var start = await client.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.selfCheck" });
            Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        }
        Assert.DoesNotContain(await WritesSinceAsync(before), w => w.Table.StartsWith("jobs.", StringComparison.Ordinal));
    }

    /// <summary>The run permission of each schedulable kind (JobType.RunPermission). A kind not listed fails the gate below, so a new schedulable kind must be added here.</summary>
    private static readonly Dictionary<string, string> RunPermissionOf = new()
    {
        ["jobs.selfCheck"] = "jobs.jobs.run",
        ["jobs.cleanup"] = "jobs.schedules.update",
    };

    [Fact]
    public async Task A_user_who_may_only_add_schedules_cannot_schedule_a_kind_whose_run_permission_they_lack()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var kinds = (await admin.GetFromJsonAsync<JsonElement>("/api/jobs/types")).EnumerateArray()
            .Where(t => t.GetProperty("schedulable").GetBoolean()).Select(t => t.GetProperty("key").GetString()!).ToList();
        Assert.NotEmpty(kinds);
        Assert.All(kinds, k => Assert.True(RunPermissionOf.ContainsKey(k), $"schedulable kind {k} has no run permission listed in this gate"));
        using var adder = await UserHoldingAsync(admin, "jws.adder", ["jobs.schedules.create", "jobs.schedules.read"]);
        using var changer = await UserHoldingAsync(admin, "jws.changer", ["jobs.schedules.update", "jobs.schedules.read"]);
        foreach (var kind in kinds)
        {
            var needed = RunPermissionOf[kind];
            using var made = await adder.PostAsJsonAsync("/api/jobs/schedules", new { scheduleName = $"jws {kind} {Guid.NewGuid():N}", type = kind, cron = "30 3 * * *", payload = new { }, isEnabled = true });
            Assert.True(made.StatusCode == HttpStatusCode.Forbidden, $"{kind}: a user with only jobs.schedules.create (not {needed}) got {(int)made.StatusCode}");
            Assert.Contains("jobs.typeNotAllowed", await made.Content.ReadAsStringAsync());
            // The same schedule, added by an administrator, switched off: the changer may switch on, retime or reparameterise it only with the run permission.
            using var seeded = await admin.PostAsJsonAsync("/api/jobs/schedules", new { scheduleName = $"jws seeded {kind} {Guid.NewGuid():N}", type = kind, cron = "30 3 * * *", payload = new { }, isEnabled = false });
            Assert.Equal(HttpStatusCode.Created, seeded.StatusCode);
            var schedule = await seeded.Content.ReadFromJsonAsync<JsonElement>();
            var id = schedule.GetProperty("id").GetGuid();
            var version = schedule.GetProperty("version").GetUInt32();
            var name = schedule.GetProperty("scheduleName").GetString();
            var holds = needed == "jobs.schedules.update";
            foreach (var step in new[] { 0, 1, 2 })
            {
                object change = step switch
                {
                    0 => new { scheduleName = name, cron = "30 3 * * *", payload = (object?)null, isEnabled = true, version },
                    1 => new { scheduleName = name, cron = "45 4 * * *", payload = (object?)null, isEnabled = false, version },
                    _ => new { scheduleName = name, cron = "45 4 * * *", payload = (object?)new { note = "changed" }, isEnabled = false, version },
                };
                using var put = await changer.PutAsJsonAsync($"/api/jobs/schedules/{id}", change);
                var body = await put.Content.ReadAsStringAsync();
                if (holds)
                {
                    Assert.True(put.IsSuccessStatusCode, $"{kind}: the changer holds its run permission but got {(int)put.StatusCode} {body}");
                    schedule = JsonDocument.Parse(body).RootElement.Clone();
                    version = schedule.GetProperty("version").GetUInt32();
                }
                else
                {
                    Assert.True(put.StatusCode == HttpStatusCode.Forbidden, $"{kind}: a user with only jobs.schedules.update (not {needed}) got {(int)put.StatusCode} {body}");
                }
            }
        }
    }

    [Fact]
    public async Task Starting_and_running_again_each_need_the_run_permission_of_the_kind()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        // jobs.cleanup needs jobs.schedules.update besides: holding only jobs.jobs.run is refused on Start and Rerun.
        using var runner = await UserHoldingAsync(admin, "jws.onlyrun", ["jobs.jobs.run", "jobs.jobs.read"]);
        var started = await admin.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.cleanup", runAt = DateTimeOffset.UtcNow.AddHours(2) });
        Assert.True(started.StatusCode == HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
        var original = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var cancelled = await admin.PostAsync($"/api/jobs/jobs/{original}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var start = await runner.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.cleanup" });
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Contains("jobs.typeNotAllowed", await start.Content.ReadAsStringAsync());
        using var again = await runner.PostAsync($"/api/jobs/jobs/{original}/rerun", null);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Contains("jobs.typeNotAllowed", await again.Content.ReadAsStringAsync());
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

    private async Task<long> LastAuditIdAsync()
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<long>(owner, "SELECT COALESCE(max(id), 0) FROM audit.entries");
    }

    private async Task<List<(string Table, string Action, Guid? Record, string[] Columns)>> WritesSinceAsync(long after)
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ReadAsync(owner,
            "SELECT table_schema || '.' || table_name, action, record_id, ARRAY(SELECT jsonb_object_keys(changes) ORDER BY 1) FROM audit.entries WHERE id > @after ORDER BY id",
            r => (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2), r.GetFieldValue<string[]>(3)), ("after", after));
    }
}

/// <summary>Its own environment: the gate queues and cancels jobs of tenant A.</summary>
public sealed class JobsWriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
