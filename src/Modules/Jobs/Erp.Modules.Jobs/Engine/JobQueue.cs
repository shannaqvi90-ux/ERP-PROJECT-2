using System.Text.Json;
using Erp.Kernel.Localization;
using Erp.Modules.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Jobs.Engine;

/// <summary>
/// <see cref="IJobQueue"/>: jobs are rows of <c>jobs.jobs</c> written through the caller's own unit
/// of work (the request's or the module's), so they exist only if that unit commits.
/// </summary>
internal sealed class JobQueue(JobsDbContext db, IJobTypes types, StringCatalog strings, TimeProvider clock) : IJobQueue
{
    public async Task<JobTicket> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var type = types.Find(request.Type ?? "") ?? throw new JobRequestException("unknownType", $"'{request.Type}' is not a registered kind of job.");
        var payload = SerializePayload(request.Payload);
        var now = clock.GetUtcNow();
        if (request.RunAt is { } far && far > now + JobsLimits.FarthestRunAt)
        {
            throw new JobRequestException("runAtTooFar", "A job may be queued at most a year ahead.");
        }
        if (request.Priority is < -10 or > 10)
        {
            throw new JobRequestException("priority", "The priority is -10 to 10.");
        }
        var source = request.Source ?? JobSource.Module;
        if (!JobSource.All.Contains(source))
        {
            throw new JobRequestException("source", $"'{source}' is not a source of jobs.");
        }
        var title = Title(type, request.Title);
        var job = new Job
        {
            Type = type.Key,
            TitleEn = title.En,
            TitleAr = title.Ar,
            Status = JobStatus.Queued,
            Source = source,
            Priority = request.Priority,
            Payload = payload,
            RunAt = request.RunAt is { } at && at > now ? at : now,
            MaxAttempts = type.MaxAttempts,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return new JobTicket(job.Id, job.Type, job.Status, job.RunAt);
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null || JobStatus.IsFinished(job.Status))
        {
            return false;
        }
        job.CancelRequested = true;
        if (job.Status == JobStatus.Queued)
        {
            // Nothing runs it: it ends now.
            job.Status = JobStatus.Cancelled;
            job.FinishedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<JobTicket?> RerunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }
        if (!JobStatus.IsFinished(job.Status))
        {
            throw new JobRequestException("notFinished", "Only a finished job can be run again.");
        }
        var type = types.Find(job.Type) ?? throw new JobRequestException("unknownType", $"'{job.Type}' is no longer a registered kind of job.");
        var again = new Job
        {
            Type = job.Type,
            TitleEn = job.TitleEn,
            TitleAr = job.TitleAr,
            Status = JobStatus.Queued,
            Source = JobSource.Rerun,
            Priority = job.Priority,
            Payload = job.Payload,
            RunAt = clock.GetUtcNow(),
            MaxAttempts = type.MaxAttempts,
            ScheduleId = job.ScheduleId,
            RerunOf = job.Id,
        };
        db.Jobs.Add(again);
        await db.SaveChangesAsync(cancellationToken);
        return new JobTicket(again.Id, again.Type, again.Status, again.RunAt);
    }

    /// <summary>The parameters as a JSON object of at most <see cref="JobsLimits.PayloadBytes"/> bytes.</summary>
    internal static string SerializePayload(object? payload)
    {
        if (payload is null)
        {
            return "{}";
        }
        JsonElement element;
        try
        {
            element = payload is JsonElement given ? given : JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            throw new JobRequestException("payloadNotObject", "The parameters could not be written as JSON.");
        }
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return "{}";
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JobRequestException("payloadNotObject", "The parameters must be a JSON object.");
        }
        var text = element.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(text) > JobsLimits.PayloadBytes)
        {
            throw new JobRequestException("payloadTooLarge", $"The parameters may be at most {JobsLimits.PayloadBytes} bytes.");
        }
        return text;
    }

    private JobText Title(JobType type, JobText? given)
    {
        if (given is { IsEmpty: false })
        {
            if (given.En.Length > JobText.MaxLength || given.Ar.Length > JobText.MaxLength)
            {
                throw new JobRequestException("titleTooLong", $"A title may be at most {JobText.MaxLength} characters.");
            }
            return new JobText(string.IsNullOrWhiteSpace(given.En) ? given.Ar.Trim() : given.En.Trim(), string.IsNullOrWhiteSpace(given.Ar) ? given.En.Trim() : given.Ar.Trim());
        }
        return new JobText(strings.Get(type.LabelKey, Languages.English), strings.Get(type.LabelKey, Languages.Arabic));
    }
}
