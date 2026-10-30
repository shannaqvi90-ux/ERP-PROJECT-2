using Erp.Kernel.Lists;
using Erp.Modules.Jobs.Contracts;

namespace Erp.Modules.Jobs.Jobs;

/// <summary>
/// The background jobs list over <c>jobs.jobs</c> (row-level security limits it to the workspace's
/// jobs): newest first; search on the title in both languages and the kind (trigram index); filter
/// by status, kind, who started it, progress, tries and any of the moments; group by status, kind or
/// who started it. Every sortable column has a (tenant_id, column, id) index. The demo holds 100,000 jobs.
/// </summary>
internal static class JobsList
{
    public const string Key = "jobs.jobs";

    public static readonly IReadOnlyList<ListChoice> Statuses =
    [
        new(JobStatus.Queued, "jobs.status.queued"),
        new(JobStatus.Running, "jobs.status.running"),
        new(JobStatus.Succeeded, "jobs.status.succeeded"),
        new(JobStatus.Failed, "jobs.status.failed"),
        new(JobStatus.Cancelled, "jobs.status.cancelled"),
    ];

    public static readonly IReadOnlyList<ListChoice> Sources =
    [
        new(JobSource.User, "jobs.source.user"),
        new(JobSource.Schedule, "jobs.source.schedule"),
        new(JobSource.Module, "jobs.source.module"),
        new(JobSource.Rerun, "jobs.source.rerun"),
    ];

    public static ListBinding<Job> Create() =>
        ListBinding<Job>.For(new ListDefinition(
                Key, "jobs.jobs.title", JobsPermissions.JobsRead, "/api/jobs/jobs",
                [
                    new ListColumn("titleEn", "jobs.job.title", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "titleAr", Width: ListColumnWidth.Wide),
                    new ListColumn("titleAr", "jobs.job.titleAr", ListColumnType.Text, Filterable: true, Hidden: true),
                    new ListColumn("jobStatus", "jobs.job.status", ListColumnType.Choice, Sortable: true, Filterable: true, Groupable: true, Choices: Statuses, Width: ListColumnWidth.Narrow),
                    new ListColumn("startedAt", "jobs.job.startedAt", ListColumnType.DateTime, Sortable: true, Filterable: true),
                    new ListColumn("finishedAt", "jobs.job.finishedAt", ListColumnType.DateTime, Sortable: true, Filterable: true),
                    new ListColumn("runAt", "jobs.job.runAt", ListColumnType.DateTime, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("jobType", "jobs.job.type", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true, Hidden: true),
                    new ListColumn("startedBy", "jobs.job.source", ListColumnType.Choice, Filterable: true, Groupable: true, Choices: Sources, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("tryCount", "jobs.job.attempts", ListColumnType.Number, Sortable: true, Filterable: true, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("errorEn", "jobs.job.error", ListColumnType.Text, Filterable: true, ArabicField: "errorAr", Hidden: true, Width: ListColumnWidth.Wide),
                    new ListColumn("createdAt", "jobs.job.createdAt", ListColumnType.DateTime, Sortable: true, Filterable: true, Hidden: true),
                    // Last, never beside a date: an Arabic PDF reads its cells back to back, and a progress of 100 followed by a
                    // date of the 1st ("100" + "01/10/2026") prints a number ("10001") that no cell holds.
                    new ListColumn("progressPercent", "jobs.job.progress", ListColumnType.Number, Sortable: true, Filterable: true, Width: ListColumnWidth.Narrow),
                ],
                SearchFields: ["titleEn", "titleAr", "jobType"],
                DefaultSort: "-createdAt",
                Presets:
                [
                    new ListPreset("waiting", "jobs.view.waiting", Filter: "jobStatus eq 'queued'", Sort: "runAt"),
                    new ListPreset("running", "jobs.view.running", Filter: "jobStatus eq 'running'"),
                    new ListPreset("failed", "jobs.view.failed", Filter: "jobStatus eq 'failed'"),
                    new ListPreset("byStatus", "jobs.view.byStatus", GroupBy: "jobStatus"),
                    new ListPreset("byType", "jobs.view.byType", GroupBy: "jobType"),
                ],
                ArabicSearchFields: ["titleAr"]),
                r => r.Id)
            .Column("titleEn", r => r.TitleEn)
            .Column("titleAr", r => r.TitleAr)
            .Column("jobStatus", r => r.Status)
            .Column("progressPercent", r => r.Progress)
            .Column("startedAt", r => r.StartedAt)
            .Column("finishedAt", r => r.FinishedAt)
            .Column("runAt", r => r.RunAt)
            .Column("jobType", r => r.Type)
            .Column("startedBy", r => r.Source)
            .Column("tryCount", r => r.Attempts)
            .Column("errorEn", r => r.ErrorEn)
            .Column("createdAt", r => r.CreatedAt);
}
