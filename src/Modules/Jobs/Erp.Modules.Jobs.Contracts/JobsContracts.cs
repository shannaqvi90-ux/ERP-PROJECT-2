using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Jobs.Contracts;

/// <summary>Permissions of the jobs module.</summary>
public static class JobsPermissions
{
    /// <summary>See the workspace's background jobs: their state, progress, errors and result.</summary>
    public const string JobsRead = "jobs.jobs.read";

    /// <summary>Start a background job of a kind that people may start, and run a finished job again.</summary>
    public const string JobsRun = "jobs.jobs.run";

    /// <summary>Ask a queued or running job to stop.</summary>
    public const string JobsCancel = "jobs.jobs.cancel";

    /// <summary>Download what a job produced (a file it wrote). Separate from reading the job: the
    /// output can hold business data.</summary>
    public const string JobsOutput = "jobs.jobs.output";

    /// <summary>See the schedules that start jobs by themselves.</summary>
    public const string SchedulesRead = "jobs.schedules.read";

    /// <summary>Add a schedule.</summary>
    public const string SchedulesCreate = "jobs.schedules.create";

    /// <summary>Change a schedule: its timing, its parameters, switch it on or off.</summary>
    public const string SchedulesUpdate = "jobs.schedules.update";

    public static readonly IReadOnlyList<string> All =
    [
        JobsRead, JobsRun, JobsCancel, JobsOutput, SchedulesRead, SchedulesCreate, SchedulesUpdate,
    ];
}

/// <summary>Where a job stands.</summary>
public static class JobStatus
{
    /// <summary>Waiting for its time and for a free worker (also: waiting to try again).</summary>
    public const string Queued = "queued";

    /// <summary>A worker is running it.</summary>
    public const string Running = "running";

    /// <summary>Finished without an error.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Stopped by an error and out of attempts.</summary>
    public const string Failed = "failed";

    /// <summary>Stopped on request.</summary>
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Queued, Running, Succeeded, Failed, Cancelled];

    /// <summary>True for the states a job never leaves.</summary>
    public static bool IsFinished(string status) => status is Succeeded or Failed or Cancelled;
}

/// <summary>What started a job.</summary>
public static class JobSource
{
    /// <summary>A person started it on the Background jobs screen (or through the API).</summary>
    public const string User = "user";

    /// <summary>A schedule started it.</summary>
    public const string Schedule = "scheduler";

    /// <summary>Another module queued it (an import, an export, an approval's follow-up).</summary>
    public const string Module = "module";

    /// <summary>A person ran an earlier job again.</summary>
    public const string Rerun = "rerun";

    public static readonly IReadOnlyList<string> All = [User, Schedule, Module, Rerun];
}

/// <summary>A short text in both languages. Jobs run without a person's language, so what they say
/// (a progress line, an error) is kept in English and in Arabic and the screen shows the reader's.</summary>
public sealed record JobText(string En, string Ar)
{
    /// <summary>The longest text a job keeps.</summary>
    public const int MaxLength = 300;

    public bool IsEmpty => string.IsNullOrWhiteSpace(En) && string.IsNullOrWhiteSpace(Ar);

    /// <summary>The text in the language, falling back to the other one when it is empty.</summary>
    public string In(bool arabic) => arabic
        ? (string.IsNullOrWhiteSpace(Ar) ? En : Ar)
        : (string.IsNullOrWhiteSpace(En) ? Ar : En);
}

/// <summary>
/// A kind of job a module can run. A module registers its kinds with
/// <see cref="JobsRegistration.AddJobType{THandler}"/>; the jobs module runs them in the workspace
/// that queued them, inside that workspace's row-level security, and shows them on the Background
/// jobs screen under <see cref="LabelKey"/>.
/// </summary>
/// <param name="Key">Unique, <c>module.kind</c> in lower camel case (for example <c>currency.importRates</c>).</param>
/// <param name="LabelKey">Server string key of the kind's name, in English and Arabic (the registering
/// module's <c>Resources/en.json</c> and <c>ar.json</c>), for example <c>job.type.currency.importRates</c>.</param>
/// <param name="MaxAttempts">How many times a job is tried before it fails (1 to 10).</param>
/// <param name="Timeout">The longest one attempt may run before it is stopped and counted as failed.</param>
/// <param name="RunPermission">The permission a person needs, besides <see cref="JobsPermissions.JobsRun"/>,
/// to run a job of this kind again and (when <see cref="Manual"/>) to start it. A kind whose run permission
/// is <see cref="JobsPermissions.JobsRun"/> itself needs nothing more. When null only code (a module) queues
/// this kind and people can neither start nor rerun it.</param>
/// <param name="Description">Server string key of one sentence saying what the kind does, or null.</param>
/// <param name="Manual">True when people may start the kind from the Background jobs screen and the API
/// with parameters of their own (needs <paramref name="RunPermission"/>).</param>
/// <param name="Schedulable">True when people who may add and change schedules
/// (<see cref="JobsPermissions.SchedulesCreate"/>, <see cref="JobsPermissions.SchedulesUpdate"/>) may schedule the
/// kind. A module marks a kind schedulable only when running it unattended, with parameters an administrator
/// wrote, is safe: a schedule runs as system work with the permissions of nobody.</param>
public sealed record JobType(
    string Key,
    string LabelKey,
    int MaxAttempts = 3,
    TimeSpan? Timeout = null,
    string? RunPermission = null,
    string? Description = null,
    bool Manual = false,
    bool Schedulable = false)
{
    private static readonly Regex KeyRegex = new("^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int MaxAttemptsLimit = 10;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public static bool IsKey(string? key) => key is not null && key.Length <= 72 && KeyRegex.IsMatch(key);

    public TimeSpan EffectiveTimeout => Timeout ?? DefaultTimeout;

    /// <summary>Problems with the definition itself (checked when the module starts).</summary>
    public IEnumerable<string> Problems()
    {
        if (!IsKey(Key))
        {
            yield return $"job type '{Key}': the key must look like 'module.kind' (lower camel case, at most 72 characters)";
        }
        if (string.IsNullOrWhiteSpace(LabelKey))
        {
            yield return $"job type '{Key}': needs a label key";
        }
        if (MaxAttempts is < 1 or > MaxAttemptsLimit)
        {
            yield return $"job type '{Key}': MaxAttempts must be 1 to {MaxAttemptsLimit}";
        }
        if (Manual && RunPermission is null)
        {
            yield return $"job type '{Key}': a kind people start needs a run permission";
        }
        if (EffectiveTimeout <= TimeSpan.Zero || EffectiveTimeout > TimeSpan.FromHours(12))
        {
            yield return $"job type '{Key}': the timeout must be above zero and at most 12 hours";
        }
    }
}

/// <summary>What a handler sees of the job it runs.</summary>
public interface IJobContext
{
    Guid JobId { get; }

    string Type { get; }

    /// <summary>The parameters the job was queued with (a JSON object; empty when it had none).</summary>
    JsonElement Payload { get; }

    /// <summary>1 for the first try.</summary>
    int Attempt { get; }

    int MaxAttempts { get; }

    /// <summary>The person who queued the job (null: a schedule or another module's own work).</summary>
    Guid? RequestedBy { get; }

    /// <summary>Services of this attempt's scope: its unit of work is bound to the job's workspace as
    /// system work. What the handler writes through it is saved together with the job's own success
    /// and undone with its failure.</summary>
    IServiceProvider Services { get; }

    /// <summary>Raised when the job is cancelled, times out or the application stops. Handlers pass it on.</summary>
    CancellationToken Cancellation { get; }

    /// <summary>Say how far the job has got (0 to 100) and what it is doing. Visible at once on the
    /// Background jobs screen; a cancel request surfaces here as <see cref="Cancellation"/>.</summary>
    Task ReportAsync(int percent, JobText message);

    /// <summary>Keep a short result (a JSON object: counts, the id of what was made). Replaces an earlier one.</summary>
    void SetResult(object result);

    /// <summary>Keep a file the job produced (text: CSV, JSON, a log). People with
    /// <see cref="JobsPermissions.JobsOutput"/> download it. At most <see cref="JobsLimits.OutputBytes"/> bytes.</summary>
    void SetOutput(string fileName, string contentType, string content);
}

/// <summary>Limits the queue keeps.</summary>
public static class JobsLimits
{
    /// <summary>Largest payload, in bytes of JSON.</summary>
    public const int PayloadBytes = 64 * 1024;

    /// <summary>Largest result, in bytes of JSON.</summary>
    public const int ResultBytes = 64 * 1024;

    /// <summary>Largest output file, in bytes.</summary>
    public const int OutputBytes = 1024 * 1024;

    /// <summary>The farthest ahead a job may be queued.</summary>
    public static readonly TimeSpan FarthestRunAt = TimeSpan.FromDays(366);
}

/// <summary>A kind of job's code. Registered as scoped; one instance per attempt.</summary>
public interface IJobHandler
{
    /// <summary>Do the work. Throw <see cref="JobFailure"/> for an error the person should read;
    /// any other exception fails the attempt with a general message. Return normally to succeed.</summary>
    Task RunAsync(IJobContext context);
}

/// <summary>An error a handler wants the person to read, in both languages. It fails the attempt;
/// the job is tried again while attempts remain unless <see cref="Retry"/> is false.</summary>
public sealed class JobFailure(string code, JobText message, bool retry = true) : Exception(message.En)
{
    /// <summary>A stable code (letters, digits, dots), for support and for tests.</summary>
    public string Code { get; } = code;

    public JobText Text { get; } = message;

    /// <summary>False: trying again cannot help (the input is wrong); the job fails now.</summary>
    public bool Retry { get; } = retry;
}

/// <summary>Queue a job.</summary>
/// <param name="Type">A registered <see cref="JobType.Key"/>.</param>
/// <param name="Payload">The parameters (any JSON object serialisable by System.Text.Json), or null.</param>
/// <param name="Title">A short line saying which job this is (for example the file imported), or null.</param>
/// <param name="RunAt">Not before this moment; null: as soon as a worker is free.</param>
/// <param name="Priority">Higher first among jobs that are due (-10 to 10, default 0).</param>
/// <param name="Source">A <see cref="JobSource"/>; <see cref="JobSource.Module"/> when left out.</param>
public sealed record JobRequest(string Type, object? Payload = null, JobText? Title = null, DateTimeOffset? RunAt = null, int Priority = 0, string? Source = null);

/// <summary>A queued job as the caller sees it.</summary>
public sealed record JobTicket(Guid Id, string Type, string Status, DateTimeOffset RunAt);

/// <summary>Why the queue refused a request.</summary>
public sealed class JobRequestException(string code, string message) : Exception(message)
{
    /// <summary><c>unknownType</c>, <c>payloadTooLarge</c>, <c>payloadNotObject</c>, <c>runAtTooFar</c>,
    /// <c>priority</c>, <c>titleTooLong</c>, <c>notFinished</c>, <c>notFound</c>.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// The one background job queue. A module queues work inside its own unit of work, so the job exists
/// only if the module's own change is saved (nothing is queued for a rolled-back request), and the
/// job runs later as system work in the same workspace. Another module never reads the jobs tables.
/// </summary>
public interface IJobQueue
{
    /// <summary>Queue a job in the current workspace. Saved with the caller's unit of work.</summary>
    /// <exception cref="JobRequestException">The request is not valid.</exception>
    Task<JobTicket> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ask a queued job to be cancelled at once, or a running one to stop. False when the job
    /// does not exist in this workspace or has finished already.</summary>
    Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Queue a finished job again with its type, title and parameters. Null when it does not
    /// exist in this workspace.</summary>
    /// <exception cref="JobRequestException"><c>notFinished</c> when the job has not finished.</exception>
    Task<JobTicket?> RerunAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>The registered kinds of job.</summary>
public interface IJobTypes
{
    IReadOnlyList<JobType> All { get; }

    JobType? Find(string key);
}

/// <summary>How a module registers the kinds of job it runs.</summary>
public static class JobsRegistration
{
    /// <summary>Register a kind of job and its handler (scoped).</summary>
    public static IServiceCollection AddJobType<THandler>(this IServiceCollection services, JobType type) where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(type);
        var problems = type.Problems().ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        services.AddSingleton(new JobTypeRegistration(type, typeof(THandler)));
        services.AddScoped<THandler>();
        return services;
    }
}

/// <summary>A registered kind with its handler type (read by the jobs module).</summary>
public sealed record JobTypeRegistration(JobType Type, Type Handler);
