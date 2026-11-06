using System.Text.Json;
using Erp.Kernel.Data;
using Erp.Kernel.Localization;
using Erp.Modules.Jobs.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Erp.Modules.Jobs.Engine;

/// <summary>A job a worker has taken and must run.</summary>
public sealed record ClaimedJob(Guid TenantId, Guid Id, string Type, string Payload, int Attempt, int MaxAttempts, Guid? RequestedBy);

/// <summary>How long a failed job waits before its next try.</summary>
public static class JobBackoff
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    /// <summary>The wait after the <paramref name="attemptsMade"/>th failed try: 30 s, 1 min, 2 min, 4 min …
    /// at most an hour, spread by up to 10 percent that depends on the job so jobs that failed together
    /// do not all come back together.</summary>
    public static TimeSpan After(int attemptsMade, Guid job)
    {
        var steps = Math.Clamp(attemptsMade - 1, 0, 12);
        var wait = TimeSpan.FromTicks(Math.Min(First.Ticks << steps, Longest.Ticks));
        // Whole numbers only: up to 10.00 percent more, a different amount for each job.
        var spreadTenthsOfPercent = (job.GetHashCode() & 0x7fffffff) % 101;
        return wait + TimeSpan.FromTicks(wait.Ticks * spreadTenthsOfPercent / 1000);
    }
}

/// <summary>
/// Takes due jobs and runs them. A pass finds the workspaces with work (the one reviewed
/// cross-tenant lookup, <see cref="DueTenantsAsync"/>), and then, workspace by workspace and inside that
/// workspace's row-level security as system work: queues what its schedules are due for, takes back
/// jobs whose worker stopped renewing its lease, and takes the due jobs. Each attempt runs in a
/// service scope of its own whose unit of work is bound to the job's workspace; its writes and its
/// success commit together, a failure rolls them back and the job is queued again after a growing
/// wait while attempts remain.
/// </summary>
public sealed class JobRunner : IDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly JobTypes types;
    private readonly StringCatalog strings;
    private readonly TimeProvider clock;
    private readonly ILogger<JobRunner> log;
    private readonly string _appConnection;

    public JobRunner(IServiceScopeFactory scopes, JobTypes types, StringCatalog strings, TimeProvider clock, IConfiguration configuration, ILogger<JobRunner> log)
    {
        this.scopes = scopes;
        this.types = types;
        this.strings = strings;
        this.clock = clock;
        this.log = log;
        _appConnection = configuration.GetConnectionString(ConnectionNames.App) ?? "";
    }

    /// <summary>How long a worker's claim on a running job lasts without being renewed.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    /// <summary>How often a running attempt renews its lease and looks for a cancel request.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _skipUntil = new();

    /// <summary>Names this worker in the jobs it holds.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>One pass: take what is due and run it to the end. For tests and operator tools; the
    /// worker uses <see cref="ClaimAsync"/> and <see cref="ExecuteAsync"/> so long jobs do not hold up the next pass.</summary>
    public async Task<int> RunOnceAsync(int capacity = 100, CancellationToken cancellationToken = default)
    {
        var claimed = await ClaimAsync(capacity, cancellationToken);
        await Task.WhenAll(claimed.Select(c => ExecuteAsync(c, cancellationToken)));
        return claimed.Count;
    }

    /// <summary>Take up to <paramref name="capacity"/> due jobs across the workspaces that have work.</summary>
    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(int capacity, CancellationToken cancellationToken)
    {
        var taken = new List<ClaimedJob>();
        var now = clock.GetUtcNow();
        foreach (var tenant in await DueTenantsAsync(cancellationToken))
        {
            if (_skipUntil.TryGetValue(tenant, out var until) && until > now)
            {
                continue;
            }
            try
            {
                var claimed = await ClaimForAsync(tenant, Math.Max(0, capacity - taken.Count), cancellationToken);
                if (claimed is null)
                {
                    // Suspended or closed: its jobs wait until it is active again; look again in a minute.
                    _skipUntil[tenant] = now.AddMinutes(1);
                    continue;
                }
                taken.AddRange(claimed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One workspace's fault never stops the others.
                log.LogError(ex, "Taking jobs of workspace {Tenant} failed.", tenant);
                _skipUntil[tenant] = now.AddSeconds(10);
            }
            if (taken.Count >= capacity)
            {
                break;
            }
        }
        return taken;
    }

    /// <summary>The workspaces that have a job due, a running job whose lease ran out or a schedule due,
    /// the longest waiting first (tenant ids only, see <see cref="JobsSql"/>). Uses the owner role's
    /// application-role connection of the worker process, through the reviewed function jobs.due_tenants()
    /// (owned by the narrow lookup role); the worker holds no owner credentials.</summary>
    public async Task<IReadOnlyList<Guid>> DueTenantsAsync(CancellationToken cancellationToken)
    {
        if (_appConnection.Length == 0)
        {
            throw new InvalidOperationException("The job worker needs the application role's connection string (ConnectionStrings:App).");
        }
        await using var connection = new NpgsqlConnection(_appConnection);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var tenants = new List<Guid>();
        await using (var command = new NpgsqlCommand(JobsSql.DueTenants, connection, transaction))
        {
            command.Parameters.AddWithValue("limit", 50);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tenants.Add(reader.GetGuid(0));
            }
        }
        await transaction.RollbackAsync(cancellationToken);
        return tenants;
    }

    public void Dispose()
    {
    }

    /// <summary>Queue due schedules, take back abandoned jobs and take due jobs of one workspace.
    /// Null when the workspace is not active.</summary>
    private async Task<IReadOnlyList<ClaimedJob>?> ClaimForAsync(Guid tenantId, int capacity, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(tenantId, null, "job", cancellationToken);
        try
        {
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().GetCurrentAsync(cancellationToken);
            if (tenant is null)
            {
                await session.RollbackAsync(cancellationToken);
                return null;
            }
            var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            await new ScheduleTimer(db, types, clock).QueueDueAsync(tenant.TimeZone, cancellationToken);
            await TakeBackAbandonedAsync(db, cancellationToken);
            var claimed = capacity > 0 ? await TakeAsync(session, db, tenantId, capacity, cancellationToken) : [];
            await session.CommitAsync(cancellationToken);
            return claimed;
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task TakeBackAbandonedAsync(JobsDbContext db, CancellationToken cancellationToken)
    {
        var lost = await db.Jobs.FromSqlRaw(
                "SELECT *, xmin FROM jobs.jobs WHERE status = 'running' AND locked_until < now() ORDER BY locked_until LIMIT 20 FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (lost.Count == 0)
        {
            return;
        }
        var now = clock.GetUtcNow();
        var ids = lost.Select(j => j.Id).ToList();
        var attempts = await db.Attempts.Where(a => ids.Contains(a.JobId) && a.Outcome == AttemptOutcome.Running).ToListAsync(cancellationToken);
        foreach (var job in lost)
        {
            var text = Text("job.error.abandoned");
            foreach (var attempt in attempts.Where(a => a.JobId == job.Id))
            {
                Close(attempt, now, AttemptOutcome.Abandoned, "jobs.abandoned", text);
            }
            Fail(job, now, "jobs.abandoned", text, retry: true);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<ClaimedJob>> TakeAsync(ErpDbSession session, JobsDbContext db, Guid tenantId, int capacity, CancellationToken cancellationToken)
    {
        var taken = new List<ClaimedJob>();
        await using (var command = new NpgsqlCommand("""
            WITH picked AS (
                SELECT id FROM jobs.jobs
                 WHERE status = 'queued' AND run_at <= now()
                 ORDER BY priority DESC, run_at, id
                 LIMIT @capacity
                   FOR UPDATE SKIP LOCKED)
            UPDATE jobs.jobs j
               SET status = 'running', attempts = j.attempts + 1, started_at = now(), finished_at = NULL,
                   locked_until = now() + make_interval(secs => @lease), locked_by = @worker,
                   progress = 0, progress_en = '', progress_ar = '', updated_at = now()
              FROM picked
             WHERE j.id = picked.id
            RETURNING j.id, j.type, j.payload::text, j.attempts, j.max_attempts, j.created_by
            """, session.Connection, session.Transaction))
        {
            command.Parameters.AddWithValue("capacity", capacity);
            command.Parameters.AddWithValue("lease", Lease.TotalSeconds);
            command.Parameters.AddWithValue("worker", WorkerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                taken.Add(new ClaimedJob(tenantId, reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5)));
            }
        }
        var now = clock.GetUtcNow();
        foreach (var job in taken)
        {
            db.Attempts.Add(new JobAttempt { JobId = job.Id, Number = job.Attempt, StartedAt = now, WorkerId = WorkerId });
        }
        await db.SaveChangesAsync(cancellationToken);
        return taken;
    }

    /// <summary>Run one taken job to the end of this attempt.</summary>
    public async Task ExecuteAsync(ClaimedJob job, CancellationToken stopping)
    {
        var type = types.Find(job.Type);
        var handler = types.HandlerOf(job.Type);
        if (type is null || handler is null)
        {
            await FailAsync(job, AttemptOutcome.Failed, "jobs.unknownType", Text("job.error.unknownType"), retry: false, CancellationToken.None);
            return;
        }
        using var cancelled = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(type.EffectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, cancelled.Token, timeout.Token);
        var lost = false;
        await using var scope = scopes.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        session.CorrelationId = job.Id.ToString();
        var context = new JobContext(this, job, scope.ServiceProvider, linked.Token, cancelled);
        using var heartbeat = new CancellationTokenSource();
        Task? beat = null;
        try
        {
            await session.BeginAsync(job.TenantId, job.RequestedBy, "job", CancellationToken.None);
            beat = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(Heartbeat);
                try
                {
                    while (await timer.WaitForNextTickAsync(heartbeat.Token))
                    {
                        var state = await RenewAsync(job, heartbeat.Token);
                        if (state is null)
                        {
                            lost = true;
                            await cancelled.CancelAsync();
                            return;
                        }
                        if (state.Value)
                        {
                            await cancelled.CancelAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Renewing the lease of job {Job} failed.", job.Id);
                }
            }, CancellationToken.None);

            await ((IJobHandler)scope.ServiceProvider.GetRequiredService(handler)).RunAsync(context);

            await heartbeat.CancelAsync();
            await beat;
            if (lost)
            {
                throw new LostLeaseException();
            }
            await FinishSucceededAsync(scope.ServiceProvider, session, job, context, CancellationToken.None);
            await session.CommitAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await heartbeat.CancelAsync();
            if (beat is not null)
            {
                await beat;
            }
            await session.RollbackAsync(CancellationToken.None);
            await RecordFailureAsync(job, ex, lost, cancelled.IsCancellationRequested, timeout.IsCancellationRequested, stopping.IsCancellationRequested);
        }
    }

    private async Task RecordFailureAsync(ClaimedJob job, Exception ex, bool lost, bool cancelRequested, bool timedOut, bool stopping)
    {
        try
        {
            switch (ex)
            {
                case LostLeaseException:
                    // Another worker holds the job now; it records what happens.
                    log.LogWarning("Job {Job} lost its lease; its attempt was rolled back.", job.Id);
                    return;
                case CancelledAtEndException:
                    await FinishCancelledAsync(job);
                    return;
                case OperationCanceledException when lost:
                    return;
                case OperationCanceledException when stopping:
                    await ReleaseAsync(job);
                    return;
                case OperationCanceledException when cancelRequested:
                    await FinishCancelledAsync(job);
                    return;
                case OperationCanceledException when timedOut:
                    await FailAsync(job, AttemptOutcome.TimedOut, "jobs.timedOut", Text("job.error.timedOut"), retry: true, CancellationToken.None);
                    return;
                case JobFailure failure:
                    await FailAsync(job, AttemptOutcome.Failed, Clip(failure.Code, JobsDbContext.CodeLength), Clip(failure.Text), failure.Retry, CancellationToken.None);
                    return;
                default:
                    log.LogError(ex, "Job {Job} ({Type}) of workspace {Tenant} failed on attempt {Attempt}.", job.Id, job.Type, job.TenantId, job.Attempt);
                    await FailAsync(job, AttemptOutcome.Failed, "jobs.unexpected", Text("job.error.unexpected"), retry: true, CancellationToken.None);
                    return;
            }
        }
        catch (Exception recording) when (recording is not OperationCanceledException)
        {
            // The lease runs out and the job is taken back as abandoned.
            log.LogError(recording, "Recording the end of job {Job} failed.", job.Id);
        }
    }

    private async Task FinishSucceededAsync(IServiceProvider services, ErpDbSession session, ClaimedJob claimed, JobContext context, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<JobsDbContext>();
        var now = clock.GetUtcNow();
        var job = await HeldAsync(db, claimed, cancellationToken) ?? throw new LostLeaseException();
        if (job.CancelRequested)
        {
            // A cancel accepted before the final write wins: the job's own writes roll back with the scope.
            throw new CancelledAtEndException();
        }
        job.Status = JobStatus.Succeeded;
        job.FinishedAt = now;
        job.LockedUntil = null;
        job.LockedBy = null;
        job.Progress = 100;
        job.ErrorCode = null;
        job.ErrorEn = "";
        job.ErrorAr = "";
        job.Result = context.ResultJson;
        job.OutputName = context.OutputName;
        job.OutputContentType = context.OutputContentType;
        job.OutputText = context.OutputText;
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.JobId == claimed.Id && a.Number == claimed.Attempt, cancellationToken);
        if (attempt is not null)
        {
            Close(attempt, now, AttemptOutcome.Succeeded, null, new JobText("", ""));
        }
        await db.SaveChangesAsync(cancellationToken);
        _ = session;
    }

    private async Task FailAsync(ClaimedJob claimed, string outcome, string code, JobText text, bool retry, CancellationToken cancellationToken)
    {
        await UnitAsync(claimed.TenantId, async db =>
        {
            var job = await HeldAsync(db, claimed, cancellationToken);
            if (job is null)
            {
                return;
            }
            var now = clock.GetUtcNow();
            var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.JobId == claimed.Id && a.Number == claimed.Attempt, cancellationToken);
            if (attempt is not null)
            {
                Close(attempt, now, outcome, code, text);
            }
            Fail(job, now, code, text, retry);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task FinishCancelledAsync(ClaimedJob claimed)
    {
        await UnitAsync(claimed.TenantId, async db =>
        {
            var job = await HeldAsync(db, claimed, CancellationToken.None);
            if (job is null)
            {
                return;
            }
            var now = clock.GetUtcNow();
            job.Status = JobStatus.Cancelled;
            job.FinishedAt = now;
            job.LockedUntil = null;
            job.LockedBy = null;
            var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.JobId == claimed.Id && a.Number == claimed.Attempt, CancellationToken.None);
            if (attempt is not null)
            {
                Close(attempt, now, AttemptOutcome.Cancelled, null, new JobText("", ""));
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }, CancellationToken.None);
    }

    /// <summary>The application is stopping: give the job back without counting the try.</summary>
    private async Task ReleaseAsync(ClaimedJob claimed)
    {
        await UnitAsync(claimed.TenantId, async db =>
        {
            var job = await HeldAsync(db, claimed, CancellationToken.None);
            if (job is null)
            {
                return;
            }
            job.Status = JobStatus.Queued;
            job.Attempts = Math.Max(0, job.Attempts - 1);
            job.RunAt = clock.GetUtcNow();
            job.LockedUntil = null;
            job.LockedBy = null;
            var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.JobId == claimed.Id && a.Number == claimed.Attempt, CancellationToken.None);
            if (attempt is not null)
            {
                db.Attempts.Remove(attempt);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }, CancellationToken.None);
    }

    /// <summary>Renew the lease; false: keep going; true: cancel was requested; null: the job is no longer ours.</summary>
    private async Task<bool?> RenewAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        bool? state = null;
        await RawUnitAsync(job.TenantId, async session =>
        {
            await using var command = new NpgsqlCommand("""
                UPDATE jobs.jobs SET locked_until = now() + make_interval(secs => @lease)
                 WHERE id = @id AND status = 'running' AND locked_by = @worker AND attempts = @attempt
                RETURNING cancel_requested
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("lease", Lease.TotalSeconds);
            command.Parameters.AddWithValue("id", job.Id);
            command.Parameters.AddWithValue("worker", WorkerId);
            command.Parameters.AddWithValue("attempt", job.Attempt);
            state = await command.ExecuteScalarAsync(cancellationToken) is bool requested ? requested : null;
        }, cancellationToken);
        return state;
    }

    /// <summary>Write a running job's progress; true when cancel was requested.</summary>
    internal async Task<bool> ReportAsync(ClaimedJob job, int percent, JobText message, CancellationToken cancellationToken)
    {
        var requested = false;
        var text = Clip(message);
        await RawUnitAsync(job.TenantId, async session =>
        {
            await using var command = new NpgsqlCommand("""
                UPDATE jobs.jobs SET progress = @percent, progress_en = @en, progress_ar = @ar, locked_until = now() + make_interval(secs => @lease)
                 WHERE id = @id AND status = 'running' AND locked_by = @worker AND attempts = @attempt
                RETURNING cancel_requested
                """, session.Connection, session.Transaction);
            command.Parameters.AddWithValue("percent", Math.Clamp(percent, 0, 100));
            command.Parameters.AddWithValue("en", text.En);
            command.Parameters.AddWithValue("ar", text.Ar);
            command.Parameters.AddWithValue("lease", Lease.TotalSeconds);
            command.Parameters.AddWithValue("id", job.Id);
            command.Parameters.AddWithValue("worker", WorkerId);
            command.Parameters.AddWithValue("attempt", job.Attempt);
            requested = await command.ExecuteScalarAsync(cancellationToken) is true;
        }, cancellationToken);
        return requested;
    }

    /// <summary>The job row, locked, if this worker still holds this attempt of it.</summary>
    private async Task<Job?> HeldAsync(JobsDbContext db, ClaimedJob claimed, CancellationToken cancellationToken) =>
        (await db.Jobs.FromSqlInterpolated(
                $"SELECT *, xmin FROM jobs.jobs WHERE id = {claimed.Id} AND status = 'running' AND locked_by = {WorkerId} AND attempts = {claimed.Attempt} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    private async Task UnitAsync(Guid tenantId, Func<JobsDbContext, Task> work, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(tenantId, null, "job", cancellationToken);
        try
        {
            await work(scope.ServiceProvider.GetRequiredService<JobsDbContext>());
            await session.CommitAsync(cancellationToken);
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RawUnitAsync(Guid tenantId, Func<ErpDbSession, Task> work, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(tenantId, null, "job", cancellationToken);
        try
        {
            await work(session);
            await session.CommitAsync(cancellationToken);
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private void Fail(Job job, DateTimeOffset now, string code, JobText text, bool retry)
    {
        job.ErrorCode = code;
        job.ErrorEn = text.En;
        job.ErrorAr = text.Ar;
        job.LockedUntil = null;
        job.LockedBy = null;
        if (retry && job.Attempts < job.MaxAttempts && !job.CancelRequested)
        {
            job.Status = JobStatus.Queued;
            job.RunAt = now + JobBackoff.After(job.Attempts, job.Id);
        }
        else
        {
            job.Status = job.CancelRequested ? JobStatus.Cancelled : JobStatus.Failed;
            job.FinishedAt = now;
        }
    }

    private static void Close(JobAttempt attempt, DateTimeOffset now, string outcome, string? code, JobText text)
    {
        attempt.FinishedAt = now;
        attempt.Outcome = outcome;
        attempt.ErrorCode = code;
        attempt.ErrorEn = text.En;
        attempt.ErrorAr = text.Ar;
    }

    internal JobText Text(string key) => new(strings.Get(key, Languages.English), strings.Get(key, Languages.Arabic));

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    private static JobText Clip(JobText text) => new(Clip(text.En ?? "", JobText.MaxLength), Clip(text.Ar ?? "", JobText.MaxLength));
}

/// <summary>A cancel was accepted after the handler's last check: the attempt ends as cancelled, not succeeded.</summary>
internal sealed class CancelledAtEndException() : InvalidOperationException("The job was cancelled before it could finish.");

internal sealed class LostLeaseException() : InvalidOperationException("The worker no longer holds the job.");

/// <summary>What one attempt's handler sees; keeps its result and output until the attempt succeeds.</summary>
internal sealed class JobContext(JobRunner runner, ClaimedJob job, IServiceProvider services, CancellationToken cancellation, CancellationTokenSource cancelled) : IJobContext
{
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;

    public Guid JobId => job.Id;
    public string Type => job.Type;
    public JsonElement Payload { get; } = JsonDocument.Parse(job.Payload).RootElement.Clone();
    public int Attempt => job.Attempt;
    public int MaxAttempts => job.MaxAttempts;
    public Guid? RequestedBy => job.RequestedBy;
    public IServiceProvider Services => services;
    public CancellationToken Cancellation => cancellation;

    public string? ResultJson { get; private set; }
    public string? OutputName { get; private set; }
    public string? OutputContentType { get; private set; }
    public string? OutputText { get; private set; }

    public async Task ReportAsync(int percent, JobText message)
    {
        ArgumentNullException.ThrowIfNull(message);
        // At most a few writes a second; the last word (100) always goes.
        var now = DateTimeOffset.UtcNow;
        if (percent < 100 && now - _lastReport < TimeSpan.FromMilliseconds(250))
        {
            return;
        }
        _lastReport = now;
        if (await runner.ReportAsync(job, percent, message, CancellationToken.None))
        {
            await cancelled.CancelAsync();
        }
    }

    public void SetResult(object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web).GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(text) > JobsLimits.ResultBytes)
        {
            throw new JobFailure("jobs.resultTooLarge", new JobText("The job's result is too large to keep.", "نتيجة المهمة أكبر من أن تُحفظ."), retry: false);
        }
        ResultJson = text;
    }

    public void SetOutput(string fileName, string contentType, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);
        if (System.Text.Encoding.UTF8.GetByteCount(content) > JobsLimits.OutputBytes)
        {
            throw new JobFailure("jobs.outputTooLarge", new JobText("The file the job produced is too large to keep.", "الملف الذي أنتجته المهمة أكبر من أن يُحفظ."), retry: false);
        }
        OutputName = fileName.Length <= JobsDbContext.NameLength ? fileName : fileName[..JobsDbContext.NameLength];
        OutputContentType = contentType.Length <= JobsDbContext.NameLength ? contentType : contentType[..JobsDbContext.NameLength];
        OutputText = content;
    }
}
