using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Jobs.Engine;

/// <summary>
/// Queues a job for every schedule that is due, inside the workspace's unit of work. A schedule whose
/// previous job has not finished queues nothing this time (jobs never pile up behind a slow one) and
/// moves on to its next moment; a schedule whose kind is no longer registered is switched off.
/// </summary>
internal sealed class ScheduleTimer(JobsDbContext db, JobTypes types, TimeProvider clock)
{
    public const int BatchSize = 20;

    public static TimeZoneInfo ZoneOf(string? id) =>
        id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone
        : TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Dubai", out var dubai) ? dubai : TimeZoneInfo.Utc;

    /// <summary>The next moment a timing fires after <paramref name="after"/> in the workspace's time zone.</summary>
    public static DateTimeOffset? Next(string cron, string? timeZone, DateTimeOffset after) =>
        CronSchedule.Parse(cron).NextAfter(after, ZoneOf(timeZone));

    public async Task<int> QueueDueAsync(string? timeZone, CancellationToken cancellationToken)
    {
        var due = await db.Schedules.FromSqlRaw(
                "SELECT * FROM jobs.schedules WHERE is_enabled AND next_run_at <= now() ORDER BY next_run_at LIMIT 20 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }
        var now = clock.GetUtcNow();
        var lastJobs = due.Where(s => s.LastJobId is not null).Select(s => s.LastJobId!.Value).ToList();
        var unfinished = (await db.Jobs.Where(j => lastJobs.Contains(j.Id) && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running))
            .Select(j => j.Id).ToListAsync(cancellationToken)).ToHashSet();
        var queued = 0;
        foreach (var schedule in due)
        {
            var type = types.Find(schedule.Type);
            if (type is null || !CronSchedule.TryParse(schedule.Cron, out var cron, out _))
            {
                schedule.IsEnabled = false;
                schedule.NextRunAt = null;
                continue;
            }
            if (schedule.LastJobId is not { } last || !unfinished.Contains(last))
            {
                var job = new Job
                {
                    Type = type.Key,
                    TitleEn = schedule.Name,
                    TitleAr = schedule.Name,
                    Status = JobStatus.Queued,
                    Source = JobSource.Schedule,
                    Payload = schedule.Payload,
                    RunAt = now,
                    MaxAttempts = type.MaxAttempts,
                    ScheduleId = schedule.Id,
                };
                db.Jobs.Add(job);
                schedule.LastJobId = job.Id;
                schedule.LastRunAt = now;
                queued++;
            }
            schedule.NextRunAt = cron!.NextAfter(now, ZoneOf(timeZone));
            if (schedule.NextRunAt is null)
            {
                schedule.IsEnabled = false;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }
}
