using Erp.Kernel.Data;
using Erp.Kernel.Localization;
using Erp.Kernel.Seeding;
using Erp.Modules.Jobs.Contracts;
using Erp.Modules.Jobs.Engine;
using Erp.Modules.Jobs.Handlers;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Erp.Modules.Jobs.Seeding;

/// <summary>
/// Seeds each workspace's schedules and job history. Every workspace gets the daily clean-up
/// schedule (03:30 in its time zone). The demo and gate profiles also get a history of finished jobs
/// (as many as the tenant's volume: 100,000 in the main demo workspace, spread over the last sixty days,
/// mostly succeeded, some failed with an error, some cancelled) and a few jobs waiting for tomorrow;
/// tenant B of the gate carries its canary in titles, errors, results and output files. Idempotent:
/// history goes only into a workspace that has no jobs, and a schedule is added only when absent.
/// </summary>
internal sealed class JobsSeeder(JobsDbContext db, ErpDbSession session, StringCatalog strings, ITenantDirectory tenants) : ITenantSeeder
{
    public const string CleanupScheduleName = "Clean up finished jobs / تنظيف المهام المنتهية";
    public const int HistoryDays = 60;

    public int Order => 130;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.Schedules.AnyAsync(s => s.Name == CleanupScheduleName, cancellationToken))
        {
            var zone = (await tenants.GetCurrentAsync(cancellationToken))?.TimeZone;
            db.Schedules.Add(new JobSchedule
            {
                Name = CleanupScheduleName,
                Type = CleanupJobHandler.TypeKey,
                Cron = "30 3 * * *",
                Payload = "{}",
                IsEnabled = true,
                NextRunAt = ScheduleTimer.Next("30 3 * * *", zone, DateTimeOffset.UtcNow),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        if (context.Plan.Profile is not (SeedProfile.Demo or SeedProfile.Gate) || await db.Jobs.AnyAsync(cancellationToken))
        {
            return;
        }
        await InsertHistoryAsync(context, cancellationToken);
    }

    private async Task InsertHistoryAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var count = Math.Max(context.Tenant.Volume, context.Plan.Profile == SeedProfile.Gate ? 12 : 0);
        var kinds = new[] { CleanupJobHandler.Type, SelfCheckJobHandler.Type };
        var names = kinds.Select(k => (Type: k, En: strings.Get(k.LabelKey, Languages.English), Ar: strings.Get(k.LabelKey, Languages.Arabic))).ToArray();
        var now = DateTimeOffset.UtcNow;
        var span = TimeSpan.FromDays(HistoryDays);
        var step = count <= 1 ? TimeSpan.Zero : span / count;
        var columns = new BulkColumn[]
        {
            new("id", NpgsqlDbType.Uuid), new("tenant_id", NpgsqlDbType.Uuid), new("type", NpgsqlDbType.Varchar),
            new("title_en", NpgsqlDbType.Varchar), new("title_ar", NpgsqlDbType.Varchar), new("status", NpgsqlDbType.Varchar),
            new("source", NpgsqlDbType.Varchar), new("priority", NpgsqlDbType.Integer), new("payload", NpgsqlDbType.Jsonb),
            new("run_at", NpgsqlDbType.TimestampTz), new("attempts", NpgsqlDbType.Integer), new("max_attempts", NpgsqlDbType.Integer),
            new("started_at", NpgsqlDbType.TimestampTz), new("finished_at", NpgsqlDbType.TimestampTz), new("progress", NpgsqlDbType.Integer),
            new("progress_en", NpgsqlDbType.Varchar), new("progress_ar", NpgsqlDbType.Varchar), new("cancel_requested", NpgsqlDbType.Boolean),
            new("error_code", NpgsqlDbType.Varchar), new("error_en", NpgsqlDbType.Varchar), new("error_ar", NpgsqlDbType.Varchar),
            new("result", NpgsqlDbType.Jsonb), new("output_name", NpgsqlDbType.Varchar), new("output_content_type", NpgsqlDbType.Varchar),
            new("output_text", NpgsqlDbType.Text), new("created_at", NpgsqlDbType.TimestampTz), new("updated_at", NpgsqlDbType.TimestampTz),
        };
        IEnumerable<object?[]> Rows()
        {
            var seed = context.Tenant.VolumeRowsSeed.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff;
            for (var i = 0; i < count; i++)
            {
                // Oldest first, so the newest row is the last one made. A fixed mix, not random.
                var created = now - span + step * (i + 1);
                var mix = (int)((uint)(i * 2654435761u + (uint)seed) % 100);
                var (kind, en, ar) = names[i % names.Length];
                var (status, source) = mix switch
                {
                    < 88 => (JobStatus.Succeeded, i % 4 == 0 ? JobSource.Schedule : JobSource.User),
                    < 96 => (JobStatus.Failed, JobSource.User),
                    _ => (JobStatus.Cancelled, JobSource.User),
                };
                var waiting = i >= count - 3 && count > 20;
                if (waiting)
                {
                    status = JobStatus.Queued;
                    source = JobSource.User;
                }
                var started = created.AddSeconds(1 + mix % 7);
                var finished = status is JobStatus.Queued ? (DateTimeOffset?)null : started.AddSeconds(2 + mix % 40);
                var failed = status == JobStatus.Failed;
                var title = context.Mark(en);
                yield return
                [
                    Guid.CreateVersion7(), context.Tenant.Id, kind.Key, title, context.Mark(ar), status, source, 0, kind.Key == SelfCheckJobHandler.TypeKey ? "{\"steps\":1,\"pauseMilliseconds\":0}" : "{}",
                    waiting ? now.AddDays(1) : created, status == JobStatus.Queued ? 0 : failed ? kind.MaxAttempts : 1, kind.MaxAttempts,
                    waiting ? null : started, finished, status == JobStatus.Succeeded ? 100 : status == JobStatus.Queued ? 0 : mix % 90,
                    "", "", false,
                    failed ? "jobs.seed" : null, failed ? context.Mark("The job stopped with an error.") : "", failed ? context.Mark("توقفت المهمة بسبب خطأ.") : "",
                    status == JobStatus.Succeeded ? $"{{\"steps\":{1 + mix % 20}}}" : null,
                    status == JobStatus.Succeeded && i % 10 == 0 ? "self-check.txt" : null,
                    status == JobStatus.Succeeded && i % 10 == 0 ? "text/plain" : null,
                    status == JobStatus.Succeeded && i % 10 == 0 ? context.Mark("step 1/1 ok\n") : null,
                    created, finished ?? created,
                ];
            }
        }
        await BulkInsert.InsertAsync(session, JobsDbContext.SchemaName, "jobs", columns, Rows(), cancellationToken);
    }
}
