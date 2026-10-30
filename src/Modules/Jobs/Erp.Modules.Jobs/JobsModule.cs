using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.Jobs.Contracts;
using Erp.Modules.Jobs.Engine;
using Erp.Modules.Jobs.Handlers;
using Erp.Modules.Jobs.Jobs;
using Erp.Modules.Jobs.Schedules;
using Erp.Modules.Jobs.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Modules.Jobs;

/// <summary>
/// Background jobs: the one durable job queue every module uses (<see cref="IJobQueue"/>), kept in
/// PostgreSQL. A job is queued inside its module's own unit of work, runs later as system work
/// inside its workspace's row-level security, is tried again with a growing wait when it fails,
/// reports its progress, can be cancelled, and keeps its errors, a short result and an output file.
/// Schedules (cron timing in the workspace's time zone) queue jobs by themselves. The Background
/// jobs screen lists, filters and opens jobs, and runs a finished one again.
/// </summary>
public sealed class JobsModule : ErpModule
{
    public override string Name => "jobs";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. JobsPermissions.All]);
        module.DbContext<JobsDbContext>();
        module.Services.AddScoped<IJobQueue, JobQueue>();
        module.Services.AddSingleton<JobTypes>();
        module.Services.AddSingleton<IJobTypes>(services => services.GetRequiredService<JobTypes>());
        module.Services.TryAddSingleton(TimeProvider.System);
        module.Services.AddSingleton<JobRunner>();
        module.Command(JobWorkerCommand.Definition);
        // The kinds of job the module itself runs.
        module.Services.AddJobType<CleanupJobHandler>(CleanupJobHandler.Type);
        module.Services.AddJobType<SelfCheckJobHandler>(SelfCheckJobHandler.Type);
        module.Endpoints(group =>
        {
            JobEndpoints.Map(group);
            ScheduleEndpoints.Map(group);
        });
        module.Menu(new MenuEntry("jobs.jobs", "jobs.menu.jobs", "/jobs/jobs", JobsPermissions.JobsRead, Order: 870, Group: "settings"));
        module.Menu(new MenuEntry("jobs.schedules", "jobs.menu.schedules", "/jobs/schedules", JobsPermissions.SchedulesRead, Order: 871, Group: "settings"));
        module.List(JobsList.Create());
        module.List(SchedulesList.Create());
        // Reports print both lists exactly as their screens show them.
        module.ListRows(JobsList.Key, async (services, request, http, cancellationToken) =>
            (await JobEndpoints.PageAsync(services.GetRequiredService<JobsDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.ListRows(SchedulesList.Key, async (services, request, http, cancellationToken) =>
            (await ScheduleEndpoints.PageAsync(services.GetRequiredService<JobsDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        module.IsolationProbe<JobsIsolationProbe>();
        module.Seeder<JobsSeeder>();
    }
}

/// <summary>
/// One background job. Row-level security keeps it inside its workspace. Workers update a running
/// job's progress and lease often; those columns are left out of the audit trail
/// (the migration), everything that decides what happened to the job is audited.
/// </summary>
public sealed class Job : TenantEntity
{
    /// <summary>The registered kind (<see cref="JobType.Key"/>).</summary>
    public string Type { get; set; } = "";

    /// <summary>Which job this is, in English and Arabic: the kind's name unless the queuer gave a title.</summary>
    public string TitleEn { get; set; } = "";

    public string TitleAr { get; set; } = "";

    /// <summary><see cref="JobStatus"/>.</summary>
    public string Status { get; set; } = JobStatus.Queued;

    /// <summary><see cref="JobSource"/>.</summary>
    public string Source { get; set; } = JobSource.Module;

    public int Priority { get; set; }

    /// <summary>The parameters, a JSON object.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Not before this moment (a retry moves it forward by the back-off).</summary>
    public DateTimeOffset RunAt { get; set; }

    /// <summary>Tries started so far.</summary>
    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>A running job's lease: a worker that stops renewing it loses the job after this moment.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public string? LockedBy { get; set; }

    /// <summary>0 to 100.</summary>
    public int Progress { get; set; }

    public string ProgressEn { get; set; } = "";

    public string ProgressAr { get; set; } = "";

    /// <summary>Someone asked for the job to stop.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>The last attempt's error code, or null.</summary>
    public string? ErrorCode { get; set; }

    public string ErrorEn { get; set; } = "";

    public string ErrorAr { get; set; } = "";

    /// <summary>A short JSON object the handler kept, or null.</summary>
    public string? Result { get; set; }

    public string? OutputName { get; set; }

    public string? OutputContentType { get; set; }

    public string? OutputText { get; set; }

    /// <summary>The schedule that queued it, or null.</summary>
    public Guid? ScheduleId { get; set; }

    /// <summary>The earlier job it runs again, or null.</summary>
    public Guid? RerunOf { get; set; }
}

/// <summary>One try of a job: when it ran, how it ended and what went wrong.</summary>
public sealed class JobAttempt : TenantEntity
{
    public Guid JobId { get; set; }

    /// <summary>1 for the first try.</summary>
    public int Number { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary><see cref="AttemptOutcome"/>.</summary>
    public string Outcome { get; set; } = AttemptOutcome.Running;

    public string? ErrorCode { get; set; }

    public string ErrorEn { get; set; } = "";

    public string ErrorAr { get; set; } = "";

    public string? WorkerId { get; set; }
}

/// <summary>How a try ended.</summary>
public static class AttemptOutcome
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timedOut";

    /// <summary>The worker stopped renewing its lease (it crashed or lost the database).</summary>
    public const string Abandoned = "abandoned";

    public static readonly IReadOnlyList<string> All = [Running, Succeeded, Failed, Cancelled, TimedOut, Abandoned];
}

/// <summary>A timing that queues a job by itself.</summary>
public sealed class JobSchedule : TenantEntity
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    /// <summary>Five fields: minute hour day-of-month month day-of-week (<see cref="CronSchedule"/>).</summary>
    public string Cron { get; set; } = "";

    public string Payload { get; set; } = "{}";

    public bool IsEnabled { get; set; } = true;

    /// <summary>The next moment the schedule queues a job (null while it is switched off).</summary>
    public DateTimeOffset? NextRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public Guid? LastJobId { get; set; }
}

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "jobs";

    public const int TypeLength = 72;
    public const int TitleLength = JobText.MaxLength;
    public const int CronLength = 100;
    public const int NameLength = 120;
    public const int CodeLength = 80;

    protected override string Schema => SchemaName;

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobAttempt> Attempts => Set<JobAttempt>();
    public DbSet<JobSchedule> Schedules => Set<JobSchedule>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(e =>
        {
            e.ToTable("jobs", t =>
            {
                t.HasCheckConstraint("ck_jobs_status", "status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')");
                t.HasCheckConstraint("ck_jobs_source", "source IN ('user', 'scheduler', 'module', 'rerun')");
                t.HasCheckConstraint("ck_jobs_progress", "progress BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_jobs_priority", "priority BETWEEN -10 AND 10");
                t.HasCheckConstraint("ck_jobs_attempts", "attempts >= 0 AND max_attempts BETWEEN 1 AND 10 AND attempts <= max_attempts");
                t.HasCheckConstraint("ck_jobs_type", "type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
            });
            e.Property(x => x.Type).HasMaxLength(TypeLength);
            e.Property(x => x.TitleEn).HasMaxLength(TitleLength);
            e.Property(x => x.TitleAr).HasMaxLength(TitleLength);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Source).HasMaxLength(12);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.Result).HasColumnType("jsonb");
            e.Property(x => x.LockedBy).HasMaxLength(CodeLength);
            e.Property(x => x.ProgressEn).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.ProgressAr).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.ErrorCode).HasMaxLength(CodeLength);
            e.Property(x => x.ErrorEn).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.ErrorAr).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.OutputName).HasMaxLength(NameLength);
            e.Property(x => x.OutputContentType).HasMaxLength(NameLength);
            e.Property(x => x.OutputText).HasColumnType("text");
            // The worker's claim: due queued jobs of a workspace, best first.
            e.HasIndex(x => new { x.TenantId, x.Status, x.RunAt });
            // The list's sortable columns, each ending in the id (keyset paging seeks, never sorts).
            e.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Type, x.Id });
            e.HasIndex(x => new { x.TenantId, x.TitleEn, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Status, x.Id });
            e.HasIndex(x => new { x.TenantId, x.RunAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.StartedAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.FinishedAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Progress, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Attempts, x.Id });
            e.HasIndex(x => new { x.TitleEn, x.TitleAr, x.Type }, "ix_jobs_search").HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops", "gin_trgm_ops");
            e.HasIndex(x => new { x.TenantId, x.ScheduleId });
        });

        modelBuilder.Entity<JobAttempt>(e =>
        {
            e.ToTable("job_attempts", t =>
            {
                t.HasCheckConstraint("ck_job_attempts_outcome", "outcome IN ('running', 'succeeded', 'failed', 'cancelled', 'timedOut', 'abandoned')");
                t.HasCheckConstraint("ck_job_attempts_number", "number BETWEEN 1 AND 10");
            });
            e.Property(x => x.Outcome).HasMaxLength(12);
            e.Property(x => x.ErrorCode).HasMaxLength(CodeLength);
            e.Property(x => x.ErrorEn).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.ErrorAr).HasMaxLength(JobText.MaxLength);
            e.Property(x => x.WorkerId).HasMaxLength(CodeLength);
            e.HasIndex(x => new { x.TenantId, x.JobId, x.Number }).IsUnique();
            e.HasOne<Job>().WithMany().HasForeignKey(x => new { x.TenantId, x.JobId })
                .HasPrincipalKey(j => new { j.TenantId, j.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobSchedule>(e =>
        {
            e.ToTable("schedules", t =>
            {
                t.HasCheckConstraint("ck_schedules_type", "type ~ '^[a-z][a-zA-Z0-9]+\\.[a-z][a-zA-Z0-9]+$'");
            });
            e.Property(x => x.Name).HasMaxLength(NameLength);
            e.Property(x => x.Type).HasMaxLength(TypeLength);
            e.Property(x => x.Cron).HasMaxLength(CronLength);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsEnabled, x.NextRunAt });
            e.HasIndex(x => new { x.TenantId, x.Name, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Type, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Cron, x.Id });
            e.HasIndex(x => new { x.TenantId, x.IsEnabled, x.Id });
            e.HasIndex(x => new { x.TenantId, x.NextRunAt, x.Id });
            e.HasIndex(x => new { x.TenantId, x.LastRunAt, x.Id });
            e.HasIndex(x => new { x.Name, x.Type }, "ix_schedules_search").HasMethod("gin").HasOperators("gin_trgm_ops", "gin_trgm_ops");
        });
    }
}

internal sealed class JobsDbContextDesignFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobsDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", JobsDbContext.SchemaName);
        return new JobsDbContext(options.Options);
    }
}
