using Erp.Kernel.Data;
using Erp.Modules.Jobs.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Jobs.Handlers;

/// <summary>
/// Removes finished jobs older than <c>retentionDays</c> (default 180, 7 to 3650) with their tries, a
/// batch at a time. Jobs that have not finished are never touched. The audit trail keeps what was
/// removed. Scheduled daily in every workspace; people with the schedule permission may run it by hand.
/// </summary>
internal sealed class CleanupJobHandler : IJobHandler
{
    public const string TypeKey = "jobs.cleanup";
    public const int DefaultRetentionDays = 180;
    public const int BatchSize = 2000;
    public const int MaxBatches = 100;

    public static readonly JobType Type = new(TypeKey, "job.type.jobs.cleanup", MaxAttempts: 2, Timeout: TimeSpan.FromMinutes(20),
        RunPermission: JobsPermissions.SchedulesUpdate, Description: "job.type.jobs.cleanup.description", Manual: true);

    public async Task RunAsync(IJobContext context)
    {
        var days = DefaultRetentionDays;
        if (context.Payload.ValueKind == System.Text.Json.JsonValueKind.Object && context.Payload.TryGetProperty("retentionDays", out var given))
        {
            if (given.ValueKind != System.Text.Json.JsonValueKind.Number || !given.TryGetInt32(out days) || days is < 7 or > 3650)
            {
                throw new JobFailure("jobs.cleanup.retention", new JobText("retentionDays must be a whole number from 7 to 3650.", "يجب أن تكون retentionDays عددًا صحيحًا من 7 إلى 3650."), retry: false);
            }
        }
        var session = context.Services.GetRequiredService<ErpDbSession>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        long deleted = 0;
        for (var batch = 0; batch < MaxBatches; batch++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            await using var command = new NpgsqlCommand("""
                DELETE FROM jobs.jobs
                 WHERE id IN (SELECT id FROM jobs.jobs
                               WHERE status IN ('succeeded', 'failed', 'cancelled') AND finished_at < @cutoff
                               ORDER BY finished_at LIMIT @batch)
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("cutoff", cutoff);
            command.Parameters.AddWithValue("batch", BatchSize);
            var count = await command.ExecuteNonQueryAsync(context.Cancellation);
            deleted += count;
            if (count < BatchSize)
            {
                break;
            }
            await context.ReportAsync(Math.Min(95, (batch + 1) * 100 / MaxBatches),
                new JobText($"Removed {deleted} finished jobs so far.", $"تمت إزالة {deleted} من المهام المنتهية حتى الآن."));
        }
        context.SetResult(new { deleted, retentionDays = days });
    }
}
