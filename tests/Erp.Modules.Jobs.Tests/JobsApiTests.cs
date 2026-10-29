using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Jobs.Tests;

/// <summary>
/// The Background jobs screens' API on the gate seed (each workspace: twenty-five finished and waiting
/// jobs and the daily clean-up schedule, tenant B's carrying its canary): the list with its search,
/// filters and what the caller may do, starting, cancelling and re-running, the output file, the
/// schedules and every validation rule in both languages.
/// </summary>
public sealed class JobsApiTests(JobsFixture fixture) : IClassFixture<JobsFixture>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.Runner.RunOnceAsync(500);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<HttpClient> AdminAsync() => await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

    private async Task<HttpClient> ViewerAsync() => await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/jobs/jobs", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    [Fact]
    public async Task The_list_holds_the_workspaces_jobs_newest_first_with_search_filters_and_what_the_caller_may_do()
    {
        using var admin = await AdminAsync();
        var rows = await RowsAsync(admin, "/api/jobs/jobs?take=100");
        Assert.True(rows.Count >= 25, $"{rows.Count} jobs");
        var created = rows.Select(r => r.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(created.OrderByDescending(c => c), created);
        // Tenant B's canary never appears in tenant A's list.
        Assert.DoesNotContain(rows, r => r.GetRawText().Contains(Env.TenantB.Canary!, StringComparison.Ordinal));

        var failed = await RowsAsync(admin, "/api/jobs/jobs?take=100&filter=" + Uri.EscapeDataString("status eq 'failed'"));
        Assert.NotEmpty(failed);
        Assert.All(failed, r => Assert.Equal("failed", r.GetProperty("status").GetString()));
        Assert.All(failed, r => Assert.NotEmpty(r.GetProperty("errorEn").GetString()!));
        Assert.All(failed, r => Assert.NotEmpty(r.GetProperty("errorAr").GetString()!));

        // Search in English and in Arabic finds the self-check jobs.
        Assert.NotEmpty(await RowsAsync(admin, "/api/jobs/jobs?search=" + Uri.EscapeDataString("Self-check")));
        Assert.NotEmpty(await RowsAsync(admin, "/api/jobs/jobs?search=" + Uri.EscapeDataString("فحص ذاتي")));
        Assert.Empty(await RowsAsync(admin, "/api/jobs/jobs?search=" + Uri.EscapeDataString("no such job anywhere")));

        // A finished job of a kind with a run permission may be run again by the administrator; a waiting one cancelled.
        var finished = rows.First(r => r.GetProperty("status").GetString() == "succeeded");
        Assert.True(finished.GetProperty("canRerun").GetBoolean());
        Assert.False(finished.GetProperty("canCancel").GetBoolean());
        var waiting = rows.First(r => r.GetProperty("status").GetString() == "queued");
        Assert.True(waiting.GetProperty("canCancel").GetBoolean());
        Assert.False(waiting.GetProperty("canRerun").GetBoolean());

        using var viewer = await ViewerAsync();
        var viewed = await RowsAsync(viewer, "/api/jobs/jobs?take=100");
        Assert.Equal(rows.Count, viewed.Count);
        Assert.All(viewed, r => Assert.False(r.GetProperty("canRerun").GetBoolean() || r.GetProperty("canCancel").GetBoolean()));
    }

    [Fact]
    public async Task The_kinds_endpoint_names_each_kind_in_both_languages_and_says_what_the_caller_may_start()
    {
        using var admin = await AdminAsync();
        var kinds = (await Json(await admin.GetAsync("/api/jobs/types"))).EnumerateArray().ToList();
        var selfCheck = kinds.Single(k => k.GetProperty("key").GetString() == "jobs.selfCheck");
        Assert.Equal(("Self-check of the job queue", "فحص ذاتي لطابور المهام", true), (selfCheck.GetProperty("nameEn").GetString(), selfCheck.GetProperty("nameAr").GetString(), selfCheck.GetProperty("startable").GetBoolean()));
        Assert.True(kinds.Single(k => k.GetProperty("key").GetString() == "jobs.cleanup").GetProperty("schedulable").GetBoolean());
        Assert.False(kinds.Single(k => k.GetProperty("key").GetString() == JobTestModule.Ok).GetProperty("startable").GetBoolean());

        using var viewer = await ViewerAsync();
        var seen = (await Json(await viewer.GetAsync("/api/jobs/types"))).EnumerateArray().ToList();
        Assert.All(seen, k => Assert.False(k.GetProperty("startable").GetBoolean() || k.GetProperty("schedulable").GetBoolean()));
    }

    [Fact]
    public async Task Starting_a_job_queues_it_and_the_worker_runs_it_with_its_output_and_a_second_run_follows_a_rerun()
    {
        using var admin = await AdminAsync();
        var started = await StartAsync(admin, new { type = "jobs.selfCheck", payload = new { steps = 2, pauseMilliseconds = 0 } });
        var id = started.GetProperty("id").GetGuid();
        Assert.Equal(("queued", "user", "Self-check of the job queue", "فحص ذاتي لطابور المهام"),
            (started.GetProperty("status").GetString(), started.GetProperty("source").GetString(), started.GetProperty("titleEn").GetString(), started.GetProperty("titleAr").GetString()));
        Assert.True(started.GetProperty("canCancel").GetBoolean());
        Assert.False(started.GetProperty("hasOutput").GetBoolean());
        Assert.Equal(404, (int)(await admin.GetAsync($"/api/jobs/jobs/{id}/output")).StatusCode);

        await fixture.Runner.RunOnceAsync();
        var done = await Json(await admin.GetAsync($"/api/jobs/jobs/{id}"));
        Assert.Equal(("succeeded", 100, 1), (done.GetProperty("status").GetString(), done.GetProperty("progress").GetInt32(), done.GetProperty("attemptLog").GetArrayLength()));
        Assert.Equal(2, done.GetProperty("result").GetProperty("steps").GetInt32());
        Assert.Equal(2, done.GetProperty("payload").GetProperty("steps").GetInt32());
        Assert.True(done.GetProperty("hasOutput").GetBoolean() && done.GetProperty("canDownload").GetBoolean() && done.GetProperty("canRerun").GetBoolean());

        var output = await admin.GetAsync($"/api/jobs/jobs/{id}/output");
        Assert.Equal(HttpStatusCode.OK, output.StatusCode);
        Assert.Equal("text/plain", output.Content.Headers.ContentType!.MediaType);
        Assert.Equal("self-check.txt", output.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("nosniff", output.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("step 1/2 ok\nstep 2/2 ok\n", await output.Content.ReadAsStringAsync());

        var rerun = await admin.PostAsync($"/api/jobs/jobs/{id}/rerun", null);
        Assert.Equal(HttpStatusCode.Created, rerun.StatusCode);
        var again = await Json(rerun);
        Assert.Equal(("queued", "rerun", id), (again.GetProperty("status").GetString(), again.GetProperty("source").GetString(), again.GetProperty("rerunOf").GetGuid()));
        Assert.Equal(2, again.GetProperty("payload").GetProperty("steps").GetInt32());
        await fixture.Runner.RunOnceAsync();
        Assert.Equal("succeeded", (await fixture.StateAsync(again.GetProperty("id").GetGuid())).Status);
    }

    [Fact]
    public async Task Cancelling_and_re_running_answer_the_state_the_job_is_in_and_unknown_ids_are_not_found()
    {
        using var admin = await AdminAsync();
        var id = (await StartAsync(admin, new { type = "jobs.selfCheck", runAt = DateTimeOffset.UtcNow.AddHours(3) })).GetProperty("id").GetGuid();
        // Not finished: it cannot be run again yet.
        var early = await admin.PostAsync($"/api/jobs/jobs/{id}/rerun", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("jobs.notFinished", (await Json(early)).GetProperty("code").GetString());

        var cancelled = await admin.PostAsync($"/api/jobs/jobs/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var body = await Json(cancelled);
        Assert.Equal(("cancelled", true), (body.GetProperty("status").GetString(), body.GetProperty("cancelRequested").GetBoolean()));
        var twice = await admin.PostAsync($"/api/jobs/jobs/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("jobs.alreadyFinished", (await Json(twice)).GetProperty("code").GetString());

        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/jobs/jobs/{missing}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/jobs/jobs/{missing}/rerun", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/jobs/jobs/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/jobs/jobs/{missing}/output")).StatusCode);
    }

    [Fact]
    public async Task A_reader_may_look_but_not_start_cancel_rerun_or_download()
    {
        using var viewer = await ViewerAsync();
        using var admin = await AdminAsync();
        var id = (await StartAsync(admin, new { type = "jobs.selfCheck", payload = new { steps = 1, pauseMilliseconds = 0 } })).GetProperty("id").GetGuid();
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/jobs/jobs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/jobs/jobs", new { type = "jobs.selfCheck" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/jobs/jobs/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/jobs/jobs/{id}/rerun", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/jobs/jobs/{id}/output")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/jobs/schedules", new { name = "x", type = "jobs.cleanup", cron = "0 0 * * *" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/jobs/schedules/{Guid.NewGuid()}", new { name = "x", cron = "0 0 * * *", isEnabled = true, version = 1 })).StatusCode);
        var detail = await Json(await viewer.GetAsync($"/api/jobs/jobs/{id}"));
        Assert.False(detail.GetProperty("canRerun").GetBoolean() || detail.GetProperty("canCancel").GetBoolean() || detail.GetProperty("canDownload").GetBoolean());
    }

    [Fact]
    public async Task Starting_checks_the_kind_the_parameters_and_the_time_and_says_so_in_the_readers_language()
    {
        using var admin = await AdminAsync();
        async Task<JsonElement> Errors(object body)
        {
            var response = await admin.PostAsJsonAsync("/api/jobs/jobs", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await Json(response)).GetProperty("errors");
        }
        Assert.Equal("required", (await Errors(new { })).GetProperty("type")[0].GetProperty("code").GetString());
        // A kind only code queues (the test kind has no run permission) and an unknown kind are both refused.
        Assert.Equal("jobsTypeNotStartable", (await Errors(new { type = JobTestModule.Ok })).GetProperty("type")[0].GetProperty("code").GetString());
        Assert.Equal("jobsTypeNotStartable", (await Errors(new { type = "nobody.knows" })).GetProperty("type")[0].GetProperty("code").GetString());
        Assert.Equal("jobsPayloadObject", (await Errors(new { type = "jobs.selfCheck", payload = new[] { 1 } })).GetProperty("payload")[0].GetProperty("code").GetString());
        Assert.Equal("jobsPayloadTooLarge", (await Errors(new { type = "jobs.selfCheck", payload = new { text = new string('x', 70_000) } })).GetProperty("payload")[0].GetProperty("code").GetString());
        Assert.Equal("jobsRunAtTooFar", (await Errors(new { type = "jobs.selfCheck", runAt = DateTimeOffset.UtcNow.AddDays(500) })).GetProperty("runAt")[0].GetProperty("code").GetString());
        // A user whose language is Arabic reads the message in Arabic.
        using var arabicAdmin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var refused = await arabicAdmin.PostAsJsonAsync("/api/jobs/jobs", new { type = "nobody.knows" });
        var arabic = (await Json(refused)).GetProperty("errors").GetProperty("type")[0].GetProperty("message").GetString()!;
        Assert.Matches(@"\p{IsArabic}", arabic);
    }

    [Fact]
    public async Task Schedules_are_added_changed_switched_off_and_checked()
    {
        using var admin = await AdminAsync();
        var name = "Weekly clean-up " + Guid.NewGuid().ToString("N")[..6];
        var created = await admin.PostAsJsonAsync("/api/jobs/schedules", new { name, type = "jobs.cleanup", cron = "0 4 * * sun", payload = new { retentionDays = 90 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var schedule = await Json(created);
        var id = schedule.GetProperty("id").GetGuid();
        Assert.Equal(("jobs.cleanup", true, "Asia/Dubai", 90), (schedule.GetProperty("type").GetString(), schedule.GetProperty("isEnabled").GetBoolean(), schedule.GetProperty("timeZone").GetString(), schedule.GetProperty("payload").GetProperty("retentionDays").GetInt32()));
        var next = schedule.GetProperty("nextRunAt").GetDateTimeOffset();
        Assert.Equal((DayOfWeek.Sunday, 4, 0), (next.ToOffset(TimeSpan.FromHours(4)).DayOfWeek, next.ToOffset(TimeSpan.FromHours(4)).Hour, next.Minute));
        Assert.True(next > DateTimeOffset.UtcNow);

        // The list finds it by word and filters by whether it is on.
        Assert.Contains(await RowsAsync(admin, "/api/jobs/schedules?search=" + Uri.EscapeDataString(name)), r => r.GetProperty("id").GetGuid() == id);
        Assert.Contains(await RowsAsync(admin, "/api/jobs/schedules?filter=" + Uri.EscapeDataString("isEnabled eq true")), r => r.GetProperty("id").GetGuid() == id);

        // Change the timing: the next moment follows it.
        var changed = await admin.PutAsJsonAsync($"/api/jobs/schedules/{id}", new { name, cron = "15 5 * * *", payload = new { retentionDays = 60 }, isEnabled = true, version = schedule.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = await Json(changed);
        var moved = updated.GetProperty("nextRunAt").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(4));
        Assert.Equal((5, 15), (moved.Hour, moved.Minute));
        Assert.Equal(60, updated.GetProperty("payload").GetProperty("retentionDays").GetInt32());

        // A stale version is a conflict, not an overwrite.
        var stale = await admin.PutAsJsonAsync($"/api/jobs/schedules/{id}", new { name, cron = "15 5 * * *", isEnabled = true, version = schedule.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Off: no next moment; on again: one from now.
        var off = await Json(await admin.PutAsJsonAsync($"/api/jobs/schedules/{id}", new { name, cron = "15 5 * * *", isEnabled = false, version = updated.GetProperty("version").GetUInt32() }));
        Assert.False(off.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("nextRunAt").ValueKind);
        var on = await Json(await admin.PutAsJsonAsync($"/api/jobs/schedules/{id}", new { name, cron = "15 5 * * *", isEnabled = true, version = off.GetProperty("version").GetUInt32() }));
        Assert.True(on.GetProperty("nextRunAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(id, (await Json(await admin.GetAsync($"/api/jobs/schedules/{id}"))).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_schedule_must_have_a_valid_timing_a_free_name_and_a_kind_that_can_be_scheduled()
    {
        using var admin = await AdminAsync();
        async Task<JsonElement> Errors(object body)
        {
            var response = await admin.PostAsJsonAsync("/api/jobs/schedules", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await Json(response)).GetProperty("errors");
        }
        Assert.Equal(["name", "type", "cron"], (await Errors(new { })).EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("jobsCronFields", (await Errors(new { name = "a", type = "jobs.cleanup", cron = "every day" })).GetProperty("cron")[0].GetProperty("code").GetString());
        Assert.Equal("jobsCronField", (await Errors(new { name = "a", type = "jobs.cleanup", cron = "61 * * * *" })).GetProperty("cron")[0].GetProperty("code").GetString());
        Assert.Equal("jobsCronNever", (await Errors(new { name = "a", type = "jobs.cleanup", cron = "0 0 31 2 *" })).GetProperty("cron")[0].GetProperty("code").GetString());
        Assert.Equal("jobsTypeNotSchedulable", (await Errors(new { name = "a", type = JobTestModule.Ok, cron = "0 0 * * *" })).GetProperty("type")[0].GetProperty("code").GetString());
        Assert.Equal("jobsTypeNotSchedulable", (await Errors(new { name = "a", type = "nobody.knows", cron = "0 0 * * *" })).GetProperty("type")[0].GetProperty("code").GetString());
        Assert.Equal("jobsScheduleNameTaken", (await Errors(new { name = global::Erp.Modules.Jobs.Seeding.JobsSeeder.CleanupScheduleName, type = "jobs.cleanup", cron = "0 0 * * *" })).GetProperty("name")[0].GetProperty("code").GetString());
        Assert.Equal("maxLength", (await Errors(new { name = new string('n', 121), type = "jobs.cleanup", cron = "0 0 * * *" })).GetProperty("name")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tenant_B_cannot_see_or_touch_tenant_As_jobs_and_schedules()
    {
        using var admin = await AdminAsync();
        var mine = (await StartAsync(admin, new { type = "jobs.selfCheck", runAt = DateTimeOffset.UtcNow.AddHours(4) })).GetProperty("id").GetGuid();
        var schedule = (await RowsAsync(admin, "/api/jobs/schedules")).First().GetProperty("id").GetGuid();
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/jobs/jobs/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/jobs/jobs/{mine}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/jobs/jobs/{mine}/rerun", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/jobs/jobs/{mine}/output")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/jobs/schedules/{schedule}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/jobs/schedules/{schedule}", new { name = "taken", cron = "0 0 * * *", isEnabled = false, version = 1 })).StatusCode);
        Assert.DoesNotContain(await RowsAsync(other, "/api/jobs/jobs?take=200"), r => r.GetProperty("id").GetGuid() == mine);
        Assert.Equal("queued", (await fixture.StateAsync(mine)).Status);
    }
}
