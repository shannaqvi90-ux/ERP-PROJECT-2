using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Jobs.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Jobs.Jobs;

/// <summary>A row of the background jobs list. <c>canRerun</c> and <c>canCancel</c> say what the caller may do with it.</summary>
public sealed record JobRow(
    Guid Id,
    string Type,
    string TitleEn,
    string TitleAr,
    string Status,
    string Source,
    int Progress,
    string ProgressEn,
    string ProgressAr,
    DateTimeOffset RunAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Attempts,
    int MaxAttempts,
    string? ErrorCode,
    string ErrorEn,
    string ErrorAr,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    bool CanRerun,
    bool CanCancel);

/// <summary>One try of a job.</summary>
public sealed record JobAttemptDto(int Number, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Outcome, string? ErrorCode, string ErrorEn, string ErrorAr);

/// <summary>A job as its detail panel shows it.</summary>
public sealed record JobDto(
    Guid Id,
    string Type,
    string TypeNameEn,
    string TypeNameAr,
    string TitleEn,
    string TitleAr,
    string Status,
    string Source,
    int Priority,
    int Progress,
    string ProgressEn,
    string ProgressAr,
    DateTimeOffset RunAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int Attempts,
    int MaxAttempts,
    bool CancelRequested,
    string? ErrorCode,
    string ErrorEn,
    string ErrorAr,
    [property: Description("The parameters the job was queued with (a JSON object).")] JsonElement Payload,
    [property: Description("What the job kept as its result (a JSON object), or null.")] JsonElement? Result,
    [property: Description("True when the job produced a file; download it with the output endpoint.")] bool HasOutput,
    string? OutputName,
    string? OutputContentType,
    long OutputBytes,
    Guid? ScheduleId,
    Guid? RerunOf,
    IReadOnlyList<JobAttemptDto> AttemptLog,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    [property: Description("True when the caller may run this job again (it has finished, and the caller holds the run permission of its kind).")] bool CanRerun,
    [property: Description("True when the caller may cancel this job (it has not finished and the caller holds the cancel permission).")] bool CanCancel,
    [property: Description("True when the caller may download the job's output.")] bool CanDownload);

/// <summary>Start a job of a kind people may start. <c>payload</c> is a JSON object of at most 64 KB; the
/// kind says which parameters it reads. <c>runAt</c> queues it for later (at most a year ahead).</summary>
public sealed record StartJobRequest(
    [property: ApiExample("jobs.selfCheck"), StringLength(JobsDbContext.TypeLength)] string? Type,
    [property: Description("The parameters, a JSON object of at most 64 KB.")] Dictionary<string, JsonElement>? Payload,
    [property: ApiExample("2026-12-01T03:00:00Z")] DateTimeOffset? RunAt);

/// <summary>A kind of job the workspace can run.</summary>
public sealed record JobTypeDto(
    string Key,
    string NameEn,
    string NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    int MaxAttempts,
    int TimeoutSeconds,
    [property: Description("True when people may start this kind by hand and the caller may.")] bool Startable,
    [property: Description("True when the caller may schedule this kind (a module marked it schedulable and the caller may add schedules).")] bool Schedulable);

internal static class JobEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/jobs", List)
            .WithName("jobs.jobs.list")
            .WithSummary("The workspace's background jobs, a page at a time: word search on the title and kind, filters (status eq 'failed', finishedAt ge '2026-01-01'), sort, keyset or offset paging and grouping (the list query contract); newest first by default.")
            .RequirePermission(JobsPermissions.JobsRead);

        group.MapGet("/jobs/{id:guid}", Get)
            .WithName("jobs.jobs.get")
            .WithSummary("One job: its state and progress, parameters, result, every try with its error, and what the caller may do with it.")
            .RequirePermission(JobsPermissions.JobsRead);

        group.MapPost("/jobs", Start)
            .WithName("jobs.jobs.start")
            .WithSummary("Start a job of a kind people may start (see the kinds endpoint). It runs as soon as a worker is free, or at runAt.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(JobsPermissions.JobsRun);

        group.MapPost("/jobs/{id:guid}/cancel", Cancel)
            .WithName("jobs.jobs.cancel")
            .WithSummary("Cancel a job: a queued one ends at once, a running one is asked to stop and ends when it does. 409 once it has finished.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(JobsPermissions.JobsCancel);

        group.MapPost("/jobs/{id:guid}/rerun", Rerun)
            .WithName("jobs.jobs.rerun")
            .WithSummary("Run a finished job again with the same kind and parameters: a new job is queued and answered; the old one is kept as it was. Needs the run permission of the job's kind.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(JobsPermissions.JobsRun);

        group.MapGet("/jobs/{id:guid}/output", Output)
            .WithName("jobs.jobs.output")
            .WithSummary("Download the file a job produced (404 when it produced none).")
            .Surface(SurfaceKind.Job)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(JobsPermissions.JobsOutput);

        group.MapGet("/types", Types)
            .WithName("jobs.types.list")
            .WithSummary("The kinds of job the product can run, with what the caller may do with each.")
            .RequirePermission(JobsPermissions.JobsRead);
    }

    private static async Task<Results<Ok<ListPage<JobRow>>, ProblemHttpResult>> List(
        JobsDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the jobs list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<JobRow>> PageAsync(JobsDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await catalog.ListBinding<Job>(JobsList.Key).QueryAsync(db.Jobs.AsNoTracking(), request, http, cancellationToken);
        if (result.Problem is not null)
        {
            return result.Map(_ => (JobRow)null!);
        }
        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        var types = http.RequestServices.GetRequiredService<IJobTypes>();
        return result.Map(j => ToRow(j, user, types));
    }

    private static JobRow ToRow(Job j, ICurrentUser user, IJobTypes types) => new(
        j.Id, j.Type, j.TitleEn, j.TitleAr, j.Status, j.Source, j.Progress, j.ProgressEn, j.ProgressAr, j.RunAt, j.StartedAt, j.FinishedAt,
        j.Attempts, j.MaxAttempts, j.ErrorCode, j.ErrorEn, j.ErrorAr, j.CreatedAt, j.UpdatedAt, j.Version,
        CanRerun(j, user, types), CanCancel(j, user));

    internal static bool CanRerun(Job j, ICurrentUser user, IJobTypes types) =>
        JobStatus.IsFinished(j.Status) && user.Has(JobsPermissions.JobsRun)
        && types.Find(j.Type)?.RunPermission is { } permission && user.Has(permission);

    internal static bool CanCancel(Job j, ICurrentUser user) => !JobStatus.IsFinished(j.Status) && user.Has(JobsPermissions.JobsCancel);

    private static async Task<Results<Ok<JobDto>, ProblemHttpResult>> Get(Guid id, JobsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        return job is null ? Problems.NotFound(http) : TypedResults.Ok(await ToDtoAsync(job, db, http, cancellationToken));
    }

    private static async Task<JobDto> ToDtoAsync(Job j, JobsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var user = services.GetRequiredService<ICurrentUser>();
        var types = services.GetRequiredService<IJobTypes>();
        var strings = services.GetRequiredService<StringCatalog>();
        var type = types.Find(j.Type);
        var attempts = await db.Attempts.AsNoTracking().Where(a => a.JobId == j.Id).OrderBy(a => a.Number)
            .Select(a => new JobAttemptDto(a.Number, a.StartedAt, a.FinishedAt, a.Outcome, a.ErrorCode, a.ErrorEn, a.ErrorAr)).ToListAsync(cancellationToken);
        var nameEn = type is null ? j.Type : strings.Get(type.LabelKey, Languages.English);
        var nameAr = type is null ? j.Type : strings.Get(type.LabelKey, Languages.Arabic);
        var outputBytes = j.OutputText is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(j.OutputText);
        return new JobDto(
            j.Id, j.Type, nameEn, nameAr, j.TitleEn, j.TitleAr, j.Status, j.Source, j.Priority, j.Progress, j.ProgressEn, j.ProgressAr,
            j.RunAt, j.StartedAt, j.FinishedAt, j.Attempts, j.MaxAttempts, j.CancelRequested, j.ErrorCode, j.ErrorEn, j.ErrorAr,
            JsonDocument.Parse(j.Payload).RootElement.Clone(),
            j.Result is null ? null : JsonDocument.Parse(j.Result).RootElement.Clone(),
            j.OutputText is not null, j.OutputName, j.OutputContentType, outputBytes, j.ScheduleId, j.RerunOf, attempts,
            j.CreatedAt, j.UpdatedAt, j.Version, CanRerun(j, user, types), CanCancel(j, user),
            j.OutputText is not null && user.Has(JobsPermissions.JobsOutput));
    }

    private static async Task<Results<Created<JobDto>, ProblemHttpResult>> Start(
        StartJobRequest request, IJobQueue queue, JobsDbContext db, IJobTypes types, ICurrentUser user, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("type", request.Type);
        var type = request.Type is null ? null : types.Find(request.Type);
        if (request.Type is not null)
        {
            validator.Must(type is { Manual: true }, "type", "jobsTypeNotStartable");
        }
        if (request.Payload is { } payload)
        {
            validator.Must(JsonSerializer.Serialize(payload).Length <= JobsLimits.PayloadBytes, "payload", "jobsPayloadTooLarge", JobsLimits.PayloadBytes / 1024);
        }
        if (request.RunAt is { } at)
        {
            validator.Must(at <= DateTimeOffset.UtcNow + JobsLimits.FarthestRunAt, "runAt", "jobsRunAtTooFar");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (type!.RunPermission is not { } required || !user.Has(required))
        {
            return Problems.Forbidden(http, "jobs.typeNotAllowed");
        }
        try
        {
            var ticket = await queue.EnqueueAsync(new JobRequest(type.Key, request.Payload, null, request.RunAt, 0, JobSource.User), cancellationToken);
            var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == ticket.Id, cancellationToken);
            return TypedResults.Created($"/api/jobs/jobs/{job.Id}", await ToDtoAsync(job, db, http, cancellationToken));
        }
        catch (JobRequestException ex)
        {
            return ex.Code switch
            {
                "payloadTooLarge" => new Validator(http).Add("payload", "jobsPayloadTooLarge", JobsLimits.PayloadBytes / 1024).ToResult(),
                "runAtTooFar" => new Validator(http).Add("runAt", "jobsRunAtTooFar").ToResult(),
                _ => new Validator(http).Add("payload", "jobsPayloadObject").ToResult(),
            };
        }
    }

    private static async Task<Results<Ok<JobDto>, ProblemHttpResult>> Cancel(Guid id, IJobQueue queue, JobsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var exists = await db.Jobs.AsNoTracking().AnyAsync(j => j.Id == id, cancellationToken);
        if (!exists)
        {
            return Problems.NotFound(http);
        }
        if (!await queue.CancelAsync(id, cancellationToken))
        {
            return Problems.Conflict(http, "jobs.alreadyFinished");
        }
        db.ChangeTracker.Clear();
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id, cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(job, db, http, cancellationToken));
    }

    private static async Task<Results<Created<JobDto>, ProblemHttpResult>> Rerun(
        Guid id, IJobQueue queue, JobsDbContext db, IJobTypes types, ICurrentUser user, HttpContext http, CancellationToken cancellationToken)
    {
        var original = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (original is null)
        {
            return Problems.NotFound(http);
        }
        if (types.Find(original.Type)?.RunPermission is not { } required || !user.Has(required))
        {
            return Problems.Forbidden(http, "jobs.typeNotAllowed");
        }
        try
        {
            var ticket = await queue.RerunAsync(id, cancellationToken);
            if (ticket is null)
            {
                return Problems.NotFound(http);
            }
            var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == ticket.Id, cancellationToken);
            return TypedResults.Created($"/api/jobs/jobs/{job.Id}", await ToDtoAsync(job, db, http, cancellationToken));
        }
        catch (JobRequestException ex) when (ex.Code == "notFinished")
        {
            return Problems.Conflict(http, "jobs.notFinished");
        }
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Output(Guid id, JobsDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().Where(j => j.Id == id)
            .Select(j => new { j.OutputName, j.OutputContentType, j.OutputText }).SingleOrDefaultAsync(cancellationToken);
        if (job?.OutputText is null)
        {
            return Problems.NotFound(http);
        }
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.File(System.Text.Encoding.UTF8.GetBytes(job.OutputText), job.OutputContentType ?? "text/plain", job.OutputName ?? "output.txt");
    }

    private static Ok<IReadOnlyList<JobTypeDto>> Types(IJobTypes types, StringCatalog strings, ICurrentUser user)
    {
        IReadOnlyList<JobTypeDto> list =
        [
            .. types.All.Select(t => new JobTypeDto(
                t.Key,
                strings.Get(t.LabelKey, Languages.English),
                strings.Get(t.LabelKey, Languages.Arabic),
                t.Description is null ? null : strings.Get(t.Description, Languages.English),
                t.Description is null ? null : strings.Get(t.Description, Languages.Arabic),
                t.MaxAttempts,
                (int)t.EffectiveTimeout.TotalSeconds,
                t.Manual && t.RunPermission is { } run && user.Has(run) && user.Has(JobsPermissions.JobsRun),
                t.Schedulable && user.Has(JobsPermissions.SchedulesCreate))),
        ];
        return TypedResults.Ok(list);
    }
}
