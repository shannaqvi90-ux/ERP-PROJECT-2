using System.Collections.Concurrent;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Modules.Jobs.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Jobs.Tests;

/// <summary>Kinds of job only the tests run (added to the host in the Testing environment).</summary>
public sealed class JobTestModule : ErpModule
{
    public override string Name => "jobstest";

    public const string Ok = "jobstest.ok";
    public const string Flaky = "jobstest.flaky";
    public const string Broken = "jobstest.broken";
    public const string Rejects = "jobstest.rejects";
    public const string Slow = "jobstest.slow";
    public const string Sleepy = "jobstest.sleepy";
    public const string WriteThenFail = "jobstest.writeThenFail";
    public const string WriteThenSucceed = "jobstest.writeThenSucceed";
    public const string WhoAmI = "jobstest.whoAmI";
    public const string Counting = "jobstest.counting";
    public const string Restricted = "jobstest.restricted";

    /// <summary>How many times each kind's handler ran, by job id.</summary>
    public static readonly ConcurrentDictionary<Guid, int> Runs = new();

    public override void Register(ModuleBuilder module)
    {
        module.Services.AddJobType<OkHandler>(new JobType(Ok, "job.type.jobstest.ok", MaxAttempts: 3));
        module.Services.AddJobType<FlakyHandler>(new JobType(Flaky, "job.type.jobstest.flaky", MaxAttempts: 4));
        module.Services.AddJobType<BrokenHandler>(new JobType(Broken, "job.type.jobstest.broken", MaxAttempts: 2));
        module.Services.AddJobType<RejectsHandler>(new JobType(Rejects, "job.type.jobstest.rejects", MaxAttempts: 5));
        module.Services.AddJobType<SlowHandler>(new JobType(Slow, "job.type.jobstest.slow", MaxAttempts: 1, Timeout: TimeSpan.FromMinutes(1)));
        module.Services.AddJobType<SlowHandler>(new JobType(Sleepy, "job.type.jobstest.sleepy", MaxAttempts: 2, Timeout: TimeSpan.FromSeconds(1)));
        module.Services.AddJobType<WriteHandler>(new JobType(WriteThenFail, "job.type.jobstest.writeThenFail", MaxAttempts: 1));
        module.Services.AddJobType<WriteSucceedHandler>(new JobType(WriteThenSucceed, "job.type.jobstest.writeThenSucceed", MaxAttempts: 1));
        module.Services.AddJobType<WhoAmIHandler>(new JobType(WhoAmI, "job.type.jobstest.whoAmI", MaxAttempts: 1));
        module.Services.AddJobType<CountingHandler>(new JobType(Counting, "job.type.jobstest.counting", MaxAttempts: 1));
        module.Services.AddJobType<OkHandler>(new JobType(Restricted, "job.type.jobstest.restricted", MaxAttempts: 1, RunPermission: JobsPermissions.SchedulesRead, Manual: true));
    }

    private sealed class OkHandler : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            await context.ReportAsync(50, new JobText("Half way", "في المنتصف"));
            context.SetResult(new { answer = 42, attempt = context.Attempt });
            context.SetOutput("ok.csv", "text/csv", "a,b\n1,2\n");
        }
    }

    private sealed class FlakyHandler : IJobHandler
    {
        public Task RunAsync(IJobContext context)
        {
            var failTimes = context.Payload.TryGetProperty("failTimes", out var n) ? n.GetInt32() : 1;
            if (context.Attempt <= failTimes)
            {
                throw new InvalidOperationException($"secret internal detail of attempt {context.Attempt}");
            }
            context.SetResult(new { attempt = context.Attempt });
            return Task.CompletedTask;
        }
    }

    private sealed class BrokenHandler : IJobHandler
    {
        public Task RunAsync(IJobContext context) => throw new InvalidOperationException("secret internal detail");
    }

    private sealed class RejectsHandler : IJobHandler
    {
        public Task RunAsync(IJobContext context) =>
            throw new JobFailure("jobstest.rejected", new JobText("The input is wrong.", "المدخلات خاطئة."), retry: false);
    }

    private sealed class SlowHandler : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            for (var i = 1; i <= 100; i++)
            {
                await context.ReportAsync(i, new JobText($"Working {i}", $"يعمل {i}"));
                await Task.Delay(TimeSpan.FromMilliseconds(300), context.Cancellation);
            }
        }
    }

    private sealed class WriteHandler(ErpDbSession session) : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO jobs.schedules (id, tenant_id, name, type, cron, payload, is_enabled) VALUES (gen_random_uuid(), erp.current_tenant_id(), 'written by a job', 'jobs.cleanup', '0 0 * * *', '{}', false)",
                session.Connection, session.Transaction);
            await command.ExecuteNonQueryAsync(context.Cancellation);
            throw new InvalidOperationException("after the write");
        }
    }

    private sealed class WriteSucceedHandler(ErpDbSession session) : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO jobs.schedules (id, tenant_id, name, type, cron, payload, is_enabled) VALUES (gen_random_uuid(), erp.current_tenant_id(), 'kept from a job', 'jobs.cleanup', '0 0 * * *', '{}', false)",
                session.Connection, session.Transaction);
            await command.ExecuteNonQueryAsync(context.Cancellation);
        }
    }

    private sealed class WhoAmIHandler(ErpDbSession session) : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            await using var command = new NpgsqlCommand("SELECT count(*), count(DISTINCT tenant_id), min(tenant_id::text) FROM jobs.jobs", session.Connection, session.Transaction);
            await using var reader = await command.ExecuteReaderAsync(context.Cancellation);
            await reader.ReadAsync(context.Cancellation);
            context.SetResult(new { visible = reader.GetInt64(0), tenants = reader.GetInt64(1), tenant = reader.GetString(2), bound = session.TenantId, actor = session.ActorKind });
        }
    }

    private sealed class CountingHandler : IJobHandler
    {
        public async Task RunAsync(IJobContext context)
        {
            Runs.AddOrUpdate(context.JobId, 1, (_, n) => n + 1);
            await Task.Delay(20, context.Cancellation);
        }
    }
}
