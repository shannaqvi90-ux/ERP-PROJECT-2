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
internal sealed class JobsSeeder(JobsDbContext db, ErpDbSession session, ITenantDirectory tenants) : ITenantSeeder
{
    public const string CleanupScheduleName = "Clean up finished jobs / تنظيف المهام المنتهية";
    public const int HistoryDays = 60;

    public int Order => 130;

    /// <summary>Kinds of past runs the demo history holds besides the module's own (registered by later modules).</summary>
    private static readonly System.Collections.Immutable.ImmutableArray<(string Key, int MaxAttempts, string En, string Ar)> PastKinds =
    [
        ("currency.importRates", 3, "Load the daily rate file", "تحميل ملف الأسعار اليومي"),
        ("identity.exportUsers", 3, "Export the users list", "تصدير قائمة المستخدمين"),
        ("audit.digest", 3, "Nightly audit digest", "ملخص التدقيق الليلي"),
        ("inventory.revalue", 3, "Recalculate stock valuation", "إعادة احتساب تقييم المخزون"),
        ("sales.reminders", 3, "Send payment reminders", "إرسال تذكيرات الدفع"),
        ("accounting.depreciation", 3, "Post depreciation entries", "ترحيل قيود الإهلاك"),
        ("accounting.closePeriod", 3, "Close the accounting period", "إقفال الفترة المحاسبية"),
        ("banking.importStatement", 3, "Import a bank statement", "استيراد كشف حساب بنكي"),
        ("tax.vatDraft", 3, "Prepare the VAT return draft", "إعداد مسودة الإقرار الضريبي"),
        ("contacts.sync", 3, "Synchronise customer contacts", "مزامنة جهات اتصال العملاء"),
        ("attachments.archive", 3, "Archive old attachments", "أرشفة المرفقات القديمة"),
        ("search.reindex", 3, "Rebuild the search index", "إعادة بناء فهرس البحث"),
    ];

    /// <summary>A well-mixed 32-bit number for row <paramref name="i"/> of a workspace (a multiplicative hash with
    /// avalanche steps): rows come out in no pattern a sampling stride could line up with.</summary>
    private static uint Mix(int i, int seed)
    {
        unchecked
        {
            var h = (uint)i * 2246822519u + (uint)seed * 3266489917u;
            h ^= h >> 15;
            h *= 2654435761u;
            h ^= h >> 13;
            h *= 2246822519u;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Which kind row <paramref name="i"/> belongs to: about a quarter are the self-check, one in twelve the
    /// clean-up, and the past kinds share the rest evenly. A small workspace (the gates') takes every fourth row as a
    /// self-check so it always holds some.</summary>
    private static int Pick(int i, uint h, bool small)
    {
        var bucket = (int)(h % 100);
        if (small ? i % 4 == 0 : bucket < 25)
        {
            return 0;
        }
        if (small ? (h >> 16) % 100 < 11 : bucket < 33)
        {
            return 1;
        }
        return 2 + (int)((h >> 8) % (uint)PastKinds.Length);
    }

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
                // The demo and gate workspaces have run it before (their history holds its clean-ups).
                LastRunAt = context.Plan.Profile is SeedProfile.Demo or SeedProfile.Gate ? DateTimeOffset.UtcNow.AddDays(-1) : null,
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
        // The two kinds this module runs, and a history of past runs of kinds that later modules will register
        // (so the list has the variety of a real workspace: a search by a word finds some of the jobs, not half).
        // The module's own texts, read from its own resources only: the application-wide catalogue checks every loaded
        // module's texts at once, and a workspace is seeded even when another module's texts are unfinished (a test module).
        var own = new StringCatalog([typeof(JobsModule).Assembly]);
        var names = new List<(string Key, int MaxAttempts, string En, string Ar)>
        {
            (SelfCheckJobHandler.TypeKey, SelfCheckJobHandler.Type.MaxAttempts, own.Get(SelfCheckJobHandler.Type.LabelKey, Languages.English), own.Get(SelfCheckJobHandler.Type.LabelKey, Languages.Arabic)),
            (CleanupJobHandler.TypeKey, CleanupJobHandler.Type.MaxAttempts, own.Get(CleanupJobHandler.Type.LabelKey, Languages.English), own.Get(CleanupJobHandler.Type.LabelKey, Languages.Arabic)),
        };
        names.AddRange(PastKinds);
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
        // Workspaces small enough to open every job get each job's tries as well (the demo's big one keeps
        // only the jobs: 100,000 rows are audited as they are, and their tries add nothing to the volume).
        var withTries = count <= 5000;
        var tries = new List<object?[]>();
        IEnumerable<object?[]> Rows()
        {
            var seed = context.Tenant.VolumeRowsSeed.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff;
            for (var i = 0; i < count; i++)
            {
                // Oldest first, so the newest row is the last one made. A fixed mix, not random.
                var created = now - span + step * (i + 1);
                // A small workspace (the gates') always holds failed and cancelled jobs among its first rows.
                var mix = count <= 1000 && i % 6 == 1 ? 90 : count <= 1000 && i % 10 == 7 ? 98 : (int)(Mix(i, seed ^ 0x5bd1e995) % 100);
                var (key, maxAttempts, en, ar) = names[Pick(i, Mix(i, seed), count <= 1000)];
                var (status, source) = mix switch
                {
                    < 88 => (JobStatus.Succeeded, i % 4 == 0 ? JobSource.Schedule : JobSource.User),
                    < 96 => (JobStatus.Failed, JobSource.User),
                    _ => (JobStatus.Cancelled, JobSource.User),
                };
                // Just before the last three rows, so the newest job of a kind is always one that has finished (the screen's
                // best match for a name opens it, and a finished job can be run again).
                var waiting = i >= count - 6 && i < count - 3 && count > 20;
                if (waiting)
                {
                    status = JobStatus.Queued;
                    source = JobSource.User;
                    // Waiting for tomorrow: a kind this module runs, so it really runs then.
                    (key, maxAttempts, en, ar) = names[0];
                }
                var started = created.AddSeconds(1 + mix % 7);
                var finished = status is JobStatus.Queued ? (DateTimeOffset?)null : started.AddSeconds(2 + mix % 40);
                var failed = status == JobStatus.Failed;
                var title = context.Mark(en);
                var id = Guid.CreateVersion7();
                if (withTries && status != JobStatus.Queued)
                {
                    var made = failed ? maxAttempts : 1;
                    for (var number = 1; number <= made; number++)
                    {
                        var last = number == made;
                        var ok = last && status == JobStatus.Succeeded;
                        var outcome = ok ? AttemptOutcome.Succeeded : last && status == JobStatus.Cancelled ? AttemptOutcome.Cancelled : AttemptOutcome.Failed;
                        tries.Add([Guid.CreateVersion7(), context.Tenant.Id, id, number, started, started.AddSeconds(1), outcome,
                            outcome == AttemptOutcome.Failed ? "jobs.seed" : null,
                            outcome == AttemptOutcome.Failed ? context.Mark("The job stopped with an error.") : "", outcome == AttemptOutcome.Failed ? context.Mark("توقفت المهمة بسبب خطأ.") : "",
                            "seed-worker", started, started.AddSeconds(1)]);
                    }
                }
                yield return
                [
                    id, context.Tenant.Id, key, title, context.Mark(ar), status, source, 0, key == SelfCheckJobHandler.TypeKey ? "{\"steps\":1,\"pauseMilliseconds\":0}" : "{}",
                    waiting ? now.AddDays(1) : created, status == JobStatus.Queued ? 0 : failed ? maxAttempts : 1, maxAttempts,
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
        if (tries.Count > 0)
        {
            var tryColumns = new BulkColumn[]
            {
                new("id", NpgsqlDbType.Uuid), new("tenant_id", NpgsqlDbType.Uuid), new("job_id", NpgsqlDbType.Uuid), new("number", NpgsqlDbType.Integer),
                new("started_at", NpgsqlDbType.TimestampTz), new("finished_at", NpgsqlDbType.TimestampTz), new("outcome", NpgsqlDbType.Varchar),
                new("error_code", NpgsqlDbType.Varchar), new("error_en", NpgsqlDbType.Varchar), new("error_ar", NpgsqlDbType.Varchar), new("worker_id", NpgsqlDbType.Varchar),
                new("created_at", NpgsqlDbType.TimestampTz), new("updated_at", NpgsqlDbType.TimestampTz),
            };
            await BulkInsert.InsertAsync(session, JobsDbContext.SchemaName, "job_attempts", tryColumns, tries, cancellationToken);
        }
    }
}
