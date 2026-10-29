using System.Text.Json;
using Erp.Modules.Jobs.Contracts;
using Erp.Modules.Jobs.Engine;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Jobs.Tests;

/// <summary>
/// The queue and the worker on the gate seed: a job runs inside its workspace as system work, commits
/// with its own success and rolls back with its failure, is tried again with a growing wait, times out,
/// can be cancelled while it runs, is taken back when its worker disappears, never runs twice, and one
/// workspace's job never sees another's rows.
/// </summary>
public sealed class JobRunnerTests(JobsFixture fixture) : IClassFixture<JobsFixture>, IAsyncLifetime
{
    // Whatever an earlier test left due runs first, so each test starts with an empty queue.
    public async ValueTask InitializeAsync() => await fixture.Runner.RunOnceAsync(500);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ErpTestEnvironment Env => fixture.Env;

    private SeedTenant A => Env.TenantA;

    private SeedTenant B => Env.TenantB;

    [Fact]
    public async Task A_job_runs_to_success_and_keeps_its_progress_result_output_and_try()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Ok);
        Assert.Equal("queued", (await fixture.StateAsync(id)).Status);

        await fixture.Runner.RunOnceAsync();

        var job = await fixture.StateAsync(id);
        Assert.Equal(("succeeded", 1, 100), (job.Status, job.Attempts, job.Progress));
        Assert.Null(job.ErrorCode);
        Assert.Equal("ok.csv", job.OutputName);
        Assert.Equal(42, JsonDocument.Parse(job.Result!).RootElement.GetProperty("answer").GetInt32());
        Assert.NotNull(job.FinishedAt);
        Assert.Null(job.LockedBy);
        Assert.Equal([(1, "succeeded", (string?)null)], await fixture.AttemptsAsync(id));
        // The audit trail says the job changed the row as system work.
        var audited = await fixture.ScalarAsync<long>("SELECT count(*) FROM audit.entries WHERE record_id = @id AND table_name = 'jobs' AND actor_kind = 'job'", ("id", id));
        Assert.True(audited >= 2, $"expected audit rows written by the job, found {audited}");
    }

    [Fact]
    public async Task A_failing_job_is_tried_again_with_a_growing_wait_and_succeeds_when_the_cause_goes()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Flaky, new { failTimes = 2 });
        await fixture.Runner.RunOnceAsync();

        var first = await fixture.StateAsync(id);
        Assert.Equal(("queued", 1, "jobs.unexpected"), (first.Status, first.Attempts, first.ErrorCode));
        // The wait before try 2 is the first back-off step, not "now" and not a long wait.
        var wait = first.RunAt - DateTimeOffset.UtcNow;
        Assert.InRange(wait.TotalSeconds, 20, 40);
        // Neither language shows the exception's own text.
        Assert.DoesNotContain("secret", first.ErrorEn + first.ErrorAr);
        Assert.NotEmpty(first.ErrorEn);
        Assert.NotEmpty(first.ErrorAr);

        // Not due yet: nothing runs it.
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(1, (await fixture.StateAsync(id)).Attempts);

        await fixture.MakeDueAsync(id);
        await fixture.Runner.RunOnceAsync();
        var second = await fixture.StateAsync(id);
        Assert.Equal(("queued", 2), (second.Status, second.Attempts));
        Assert.InRange((second.RunAt - DateTimeOffset.UtcNow).TotalSeconds, 50, 75);

        await fixture.MakeDueAsync(id);
        await fixture.Runner.RunOnceAsync();
        var third = await fixture.StateAsync(id);
        Assert.Equal(("succeeded", 3), (third.Status, third.Attempts));
        Assert.Null(third.ErrorCode);
        Assert.Equal(["failed", "failed", "succeeded"], (await fixture.AttemptsAsync(id)).Select(a => a.Outcome).ToArray());
    }

    [Fact]
    public async Task A_job_that_keeps_failing_ends_failed_after_its_last_try_with_the_error_in_both_languages()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Broken);
        await fixture.Runner.RunOnceAsync();
        await fixture.MakeDueAsync(id);
        await fixture.Runner.RunOnceAsync();

        var job = await fixture.StateAsync(id);
        Assert.Equal(("failed", 2, "jobs.unexpected"), (job.Status, job.Attempts, job.ErrorCode));
        Assert.NotNull(job.FinishedAt);
        Assert.Contains("unexpected", job.ErrorEn);
        Assert.Contains("غير متوقع", job.ErrorAr);
        // It is not tried a third time.
        await fixture.MakeDueAsync(id);
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(2, (await fixture.StateAsync(id)).Attempts);
    }

    [Fact]
    public async Task A_failure_the_handler_says_cannot_be_retried_fails_at_once_with_its_own_message()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Rejects);
        await fixture.Runner.RunOnceAsync();
        var job = await fixture.StateAsync(id);
        Assert.Equal(("failed", 1, "jobstest.rejected"), (job.Status, job.Attempts, job.ErrorCode));
        Assert.Equal(("The input is wrong.", "المدخلات خاطئة."), (job.ErrorEn, job.ErrorAr));
    }

    [Fact]
    public async Task What_a_job_writes_is_saved_with_its_success_and_undone_with_its_failure()
    {
        var failed = await fixture.EnqueueAsync(A, JobTestModule.WriteThenFail);
        var kept = await fixture.EnqueueAsync(A, JobTestModule.WriteThenSucceed);
        await fixture.Runner.RunOnceAsync();
        Assert.Equal("failed", (await fixture.StateAsync(failed)).Status);
        Assert.Equal("succeeded", (await fixture.StateAsync(kept)).Status);
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.schedules WHERE name = 'written by a job'"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.schedules WHERE name = 'kept from a job' AND tenant_id = @t", ("t", A.Id)));
    }

    [Fact]
    public async Task A_job_that_runs_longer_than_its_kind_allows_is_stopped_and_tried_again()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Sleepy);
        await fixture.Runner.RunOnceAsync();
        var job = await fixture.StateAsync(id);
        Assert.Equal(("queued", "jobs.timedOut"), (job.Status, job.ErrorCode));
        Assert.Equal("timedOut", (await fixture.AttemptsAsync(id)).Single().Outcome);
    }

    [Fact]
    public async Task A_queued_job_cancels_at_once_and_a_finished_one_cannot_be_cancelled()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Ok, runAt: DateTimeOffset.UtcNow.AddHours(1));
        Assert.True(await fixture.WithQueueAsync(A, q => q.CancelAsync(id)));
        var job = await fixture.StateAsync(id);
        Assert.Equal(("cancelled", true), (job.Status, job.CancelRequested));
        Assert.NotNull(job.FinishedAt);
        Assert.False(await fixture.WithQueueAsync(A, q => q.CancelAsync(id)));
        // Another workspace cannot see it at all.
        Assert.False(await fixture.WithQueueAsync(B, q => q.CancelAsync(id)));
        Assert.Equal("cancelled", (await fixture.StateAsync(id)).Status);
    }

    [Fact]
    public async Task A_running_job_shows_its_progress_and_stops_when_cancelled()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Slow);
        var claimed = (await fixture.Runner.ClaimAsync(100, CancellationToken.None)).Single(c => c.Id == id);
        var running = Task.Run(() => fixture.Runner.ExecuteAsync(claimed, CancellationToken.None));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        JobState state;
        do
        {
            await Task.Delay(100);
            state = await fixture.StateAsync(id);
        }
        while (state.Progress < 3 && DateTime.UtcNow < deadline);
        Assert.Equal("running", state.Status);
        Assert.True(state.Progress >= 3, "progress was not visible while the job ran");
        Assert.StartsWith("Working", state.ProgressEn);
        Assert.NotNull(state.LockedBy);

        Assert.True(await fixture.WithQueueAsync(A, q => q.CancelAsync(id)));
        Assert.Equal(("running", true), ((await fixture.StateAsync(id)).Status, (await fixture.StateAsync(id)).CancelRequested));
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        var job = await fixture.StateAsync(id);
        Assert.Equal("cancelled", job.Status);
        Assert.Equal("cancelled", (await fixture.AttemptsAsync(id)).Single().Outcome);
        Assert.Null(job.LockedBy);
    }

    [Fact]
    public async Task A_job_whose_worker_disappeared_is_taken_back_and_the_lost_try_is_recorded()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Ok);
        // The worker took it and died: running, lease over, an open try.
        await fixture.SqlAsync("UPDATE jobs.jobs SET status = 'running', attempts = 1, started_at = now() - interval '10 minutes', locked_by = 'dead-worker', locked_until = now() - interval '5 minutes' WHERE id = @id", ("id", id));
        await fixture.SqlAsync("INSERT INTO jobs.job_attempts (id, tenant_id, job_id, number, started_at, outcome, error_en, error_ar, worker_id) VALUES (gen_random_uuid(), @t, @id, 1, now() - interval '10 minutes', 'running', '', '', 'dead-worker')", ("t", A.Id), ("id", id));

        await fixture.Runner.RunOnceAsync();
        var back = await fixture.StateAsync(id);
        Assert.Equal(("queued", "jobs.abandoned"), (back.Status, back.ErrorCode));
        Assert.Equal([(1, "abandoned", (string?)"jobs.abandoned")], await fixture.AttemptsAsync(id));

        await fixture.MakeDueAsync(id);
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(("succeeded", 2), ((await fixture.StateAsync(id)).Status, (await fixture.StateAsync(id)).Attempts));
    }

    [Fact]
    public async Task A_job_that_lost_its_lease_to_another_worker_changes_nothing_when_it_finishes()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.WriteThenSucceed);
        var claimed = (await fixture.Runner.ClaimAsync(100, CancellationToken.None)).Single(c => c.Id == id);
        // Another worker takes the job over before this one's attempt finishes.
        await fixture.SqlAsync("UPDATE jobs.jobs SET locked_by = 'other-worker' WHERE id = @id", ("id", id));
        await fixture.SqlAsync("DELETE FROM jobs.schedules WHERE name = 'kept from a job' AND tenant_id = @t", ("t", A.Id));
        await fixture.Runner.ExecuteAsync(claimed, CancellationToken.None);
        var job = await fixture.StateAsync(id);
        Assert.Equal(("running", "other-worker"), (job.Status, job.LockedBy));
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.schedules WHERE name = 'kept from a job' AND tenant_id = @t", ("t", A.Id)));
    }

    [Fact]
    public async Task Each_job_runs_inside_its_own_workspace_and_sees_only_its_rows()
    {
        var inA = await fixture.EnqueueAsync(A, JobTestModule.WhoAmI);
        var inB = await fixture.EnqueueAsync(B, JobTestModule.WhoAmI);
        await fixture.Runner.RunOnceAsync();
        foreach (var (id, tenant) in new[] { (inA, A), (inB, B) })
        {
            var result = JsonDocument.Parse((await fixture.StateAsync(id)).Result!).RootElement;
            var own = await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE tenant_id = @t", ("t", tenant.Id));
            Assert.Equal(1, result.GetProperty("tenants").GetInt64());
            Assert.Equal(tenant.Id, result.GetProperty("bound").GetGuid());
            Assert.Equal(tenant.Id.ToString(), result.GetProperty("tenant").GetString());
            Assert.Equal("job", result.GetProperty("actor").GetString());
            // Every job of the workspace and none of the other's (the job itself was running when it counted).
            Assert.InRange(result.GetProperty("visible").GetInt64(), 1, own);
        }
    }

    [Fact]
    public async Task Jobs_never_run_twice_when_workers_overlap()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 24; i++)
        {
            ids.Add(await fixture.EnqueueAsync(i % 2 == 0 ? A : B, JobTestModule.Counting));
        }
        await Task.WhenAll(fixture.Runner.RunOnceAsync(100), fixture.Runner.RunOnceAsync(100), fixture.Runner.RunOnceAsync(100));
        await fixture.Runner.RunOnceAsync(100);
        foreach (var id in ids)
        {
            Assert.Equal(1, JobTestModule.Runs.GetValueOrDefault(id));
            Assert.Equal(("succeeded", 1), ((await fixture.StateAsync(id)).Status, (await fixture.StateAsync(id)).Attempts));
        }
    }

    [Fact]
    public async Task Higher_priority_and_earlier_jobs_are_taken_first_and_future_jobs_wait()
    {
        // Drain whatever other tests left due.
        await fixture.Runner.RunOnceAsync(500);
        var low = await fixture.EnqueueAsync(A, JobTestModule.Ok, priority: -5);
        var high = await fixture.EnqueueAsync(A, JobTestModule.Ok, priority: 9);
        var later = await fixture.EnqueueAsync(A, JobTestModule.Ok, runAt: DateTimeOffset.UtcNow.AddHours(2));
        var first = await fixture.Runner.ClaimAsync(1, CancellationToken.None);
        Assert.Equal(high, first.Single().Id);
        await fixture.Runner.ExecuteAsync(first.Single(), CancellationToken.None);
        await fixture.Runner.RunOnceAsync();
        Assert.Equal("succeeded", (await fixture.StateAsync(low)).Status);
        Assert.Equal("queued", (await fixture.StateAsync(later)).Status);
    }

    [Fact]
    public async Task The_due_workspaces_lookup_names_workspaces_with_work_and_answers_nothing_inside_a_workspace()
    {
        await fixture.Runner.RunOnceAsync(500);
        var id = await fixture.EnqueueAsync(B, JobTestModule.Ok);
        var due = await fixture.Runner.DueTenantsAsync(CancellationToken.None);
        Assert.Equal([B.Id], due);
        await fixture.Runner.RunOnceAsync();
        Assert.DoesNotContain(B.Id, await fixture.Runner.DueTenantsAsync(CancellationToken.None));
        Assert.Equal("succeeded", (await fixture.StateAsync(id)).Status);

        // Bound to a workspace, the function answers nothing, even with work due.
        await fixture.EnqueueAsync(A, JobTestModule.Ok);
        await using var app = await Env.OpenAppAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using (var bind = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)", app, tx))
        {
            bind.Parameters.AddWithValue("t", A.Id.ToString());
            await bind.ExecuteNonQueryAsync();
        }
        await using (var lookup = new NpgsqlCommand("SELECT count(*) FROM jobs.due_tenants(50)", app, tx))
        {
            Assert.Equal(0L, await lookup.ExecuteScalarAsync());
        }
        await tx.RollbackAsync();
        await fixture.Runner.RunOnceAsync();
    }

    [Fact]
    public async Task A_due_schedule_queues_one_job_moves_on_and_waits_while_its_last_job_is_unfinished()
    {
        await fixture.Runner.RunOnceAsync(500);
        var schedule = Guid.CreateVersion7();
        await fixture.SqlAsync("""
            INSERT INTO jobs.schedules (id, tenant_id, name, type, cron, payload, is_enabled, next_run_at)
            VALUES (@id, @t, 'nightly ok', 'jobstest.ok', '0 2 * * *', '{"x":1}', true, now() - interval '1 minute')
            """, ("id", schedule), ("t", A.Id));

        await fixture.Runner.ClaimAsync(0, CancellationToken.None);
        var queued = await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE schedule_id = @id AND status = 'queued' AND source = 'schedule'", ("id", schedule));
        Assert.Equal(1, queued);
        var next = new DateTimeOffset(DateTime.SpecifyKind(await fixture.ScalarAsync<DateTime>("SELECT next_run_at FROM jobs.schedules WHERE id = @id", ("id", schedule)), DateTimeKind.Utc));
        Assert.True(next > DateTimeOffset.UtcNow);
        // "0 2 * * *" in the workspace's time zone (Dubai, UTC+4): 02:00 there.
        Assert.Equal((2, 0), (next.ToOffset(TimeSpan.FromHours(4)).Hour, next.UtcDateTime.Minute));

        // Due again, but the last job has not finished: nothing is queued; the schedule moves on.
        await fixture.SqlAsync("UPDATE jobs.schedules SET next_run_at = now() - interval '1 minute' WHERE id = @id", ("id", schedule));
        await fixture.Runner.ClaimAsync(0, CancellationToken.None);
        Assert.Equal(1L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE schedule_id = @id", ("id", schedule)));
        Assert.True(await fixture.ScalarAsync<DateTime>("SELECT next_run_at FROM jobs.schedules WHERE id = @id", ("id", schedule)) > DateTime.UtcNow);

        // The job runs with the schedule's parameters; the next due moment queues another.
        await fixture.Runner.RunOnceAsync();
        await fixture.SqlAsync("UPDATE jobs.schedules SET next_run_at = now() - interval '1 minute' WHERE id = @id", ("id", schedule));
        await fixture.Runner.ClaimAsync(0, CancellationToken.None);
        Assert.Equal(2L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE schedule_id = @id", ("id", schedule)));
        await fixture.SqlAsync("UPDATE jobs.schedules SET is_enabled = false WHERE id = @id", ("id", schedule));
        await fixture.Runner.RunOnceAsync();
    }

    [Fact]
    public async Task A_schedule_of_a_kind_that_is_gone_switches_itself_off()
    {
        await fixture.Runner.RunOnceAsync(500);
        var schedule = Guid.CreateVersion7();
        await fixture.SqlAsync("""
            INSERT INTO jobs.schedules (id, tenant_id, name, type, cron, payload, is_enabled, next_run_at)
            VALUES (@id, @t, 'orphan', 'removed.kind', '0 2 * * *', '{}', true, now() - interval '1 minute')
            """, ("id", schedule), ("t", B.Id));
        await fixture.Runner.ClaimAsync(0, CancellationToken.None);
        Assert.False(await fixture.ScalarAsync<bool>("SELECT is_enabled FROM jobs.schedules WHERE id = @id", ("id", schedule)));
    }

    [Fact]
    public async Task Queuing_checks_the_request_and_a_rolled_back_unit_leaves_no_job()
    {
        async Task<string> Code(JobRequest request)
        {
            var ex = await Assert.ThrowsAsync<JobRequestException>(() => fixture.WithQueueAsync(A, q => q.EnqueueAsync(request)));
            return ex.Code;
        }
        Assert.Equal("unknownType", await Code(new JobRequest("nobody.knows")));
        Assert.Equal("payloadNotObject", await Code(new JobRequest(JobTestModule.Ok, new[] { 1, 2 })));
        Assert.Equal("payloadTooLarge", await Code(new JobRequest(JobTestModule.Ok, new { text = new string('x', JobsLimits.PayloadBytes) })));
        Assert.Equal("runAtTooFar", await Code(new JobRequest(JobTestModule.Ok, null, null, DateTimeOffset.UtcNow.AddDays(400))));
        Assert.Equal("priority", await Code(new JobRequest(JobTestModule.Ok, null, null, null, 11)));
        Assert.Equal("titleTooLong", await Code(new JobRequest(JobTestModule.Ok, null, new JobText(new string('t', 301), "x"))));

        var before = await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE tenant_id = @t", ("t", A.Id));
        await fixture.WithQueueAsync(A, q => q.EnqueueAsync(new JobRequest(JobTestModule.Ok)), commit: false);
        Assert.Equal(before, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE tenant_id = @t", ("t", A.Id)));
    }

    [Fact]
    public async Task A_finished_job_is_run_again_as_a_new_job_with_the_same_kind_and_parameters()
    {
        var id = await fixture.EnqueueAsync(A, JobTestModule.Rejects, new { note = "again" });
        await Assert.ThrowsAsync<JobRequestException>(() => fixture.WithQueueAsync(A, q => q.RerunAsync(id)));
        await fixture.Runner.RunOnceAsync();
        Assert.Equal("failed", (await fixture.StateAsync(id)).Status);

        var ticket = await fixture.WithQueueAsync(A, q => q.RerunAsync(id));
        Assert.NotNull(ticket);
        var again = await fixture.StateAsync(ticket!.Id);
        Assert.Equal(("queued", "rerun", id, 0), (again.Status, again.Source, again.RerunOf, again.Attempts));
        Assert.Equal("failed", (await fixture.StateAsync(id)).Status);
        Assert.Equal("again", JsonDocument.Parse(await fixture.ScalarAsync<string>("SELECT payload::text FROM jobs.jobs WHERE id = @id", ("id", ticket.Id))).RootElement.GetProperty("note").GetString());
        Assert.Null(await fixture.WithQueueAsync(B, q => q.RerunAsync(id)));
        await fixture.Runner.RunOnceAsync();
    }

    [Fact]
    public async Task The_clean_up_job_removes_old_finished_jobs_and_nothing_else()
    {
        var old = await fixture.EnqueueAsync(B, JobTestModule.Ok);
        var oldQueued = await fixture.EnqueueAsync(B, JobTestModule.Ok, runAt: DateTimeOffset.UtcNow.AddHours(5));
        var recent = await fixture.EnqueueAsync(B, JobTestModule.Ok);
        await fixture.Runner.RunOnceAsync(500);
        await fixture.SqlAsync("UPDATE jobs.jobs SET finished_at = now() - interval '400 days' WHERE id = @id", ("id", old));
        await fixture.SqlAsync("UPDATE jobs.jobs SET created_at = now() - interval '400 days' WHERE id = @id", ("id", oldQueued));
        var cleanup = await fixture.EnqueueAsync(B, "jobs.cleanup", new { retentionDays = 30 });
        await fixture.Runner.RunOnceAsync();
        var done = await fixture.StateAsync(cleanup);
        Assert.Equal("succeeded", done.Status);
        Assert.True(JsonDocument.Parse(done.Result!).RootElement.GetProperty("deleted").GetInt64() >= 1);
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.jobs WHERE id = @id", ("id", old)));
        Assert.Equal(0L, await fixture.ScalarAsync<long>("SELECT count(*) FROM jobs.job_attempts WHERE job_id = @id", ("id", old)));
        Assert.Equal("queued", (await fixture.StateAsync(oldQueued)).Status);
        Assert.Equal("succeeded", (await fixture.StateAsync(recent)).Status);

        var bad = await fixture.EnqueueAsync(B, "jobs.cleanup", new { retentionDays = 1 });
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(("failed", "jobs.cleanup.retention"), ((await fixture.StateAsync(bad)).Status, (await fixture.StateAsync(bad)).ErrorCode));
    }

    [Fact]
    public async Task The_self_check_job_reports_each_step_in_both_languages_and_keeps_an_output_file()
    {
        var id = await fixture.EnqueueAsync(A, "jobs.selfCheck", new { steps = 3, pauseMilliseconds = 0 });
        await fixture.Runner.RunOnceAsync();
        var job = await fixture.StateAsync(id);
        Assert.Equal(("succeeded", 100, "self-check.txt"), (job.Status, job.Progress, job.OutputName));
        Assert.Equal("Step 3 of 3", job.ProgressEn);
        var progressAr = await fixture.ScalarAsync<string>("SELECT progress_ar FROM jobs.jobs WHERE id = @id", ("id", id));
        Assert.Equal("الخطوة 3 من 3", progressAr);

        var bad = await fixture.EnqueueAsync(A, "jobs.selfCheck", new { steps = 99 });
        await fixture.Runner.RunOnceAsync();
        Assert.Equal(("failed", "jobs.selfCheck.parameter"), ((await fixture.StateAsync(bad)).Status, (await fixture.StateAsync(bad)).ErrorCode));
    }
}
