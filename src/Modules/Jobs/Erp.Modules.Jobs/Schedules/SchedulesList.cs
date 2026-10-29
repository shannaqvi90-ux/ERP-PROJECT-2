using Erp.Kernel.Lists;
using Erp.Modules.Jobs.Contracts;

namespace Erp.Modules.Jobs.Schedules;

/// <summary>The schedules list over <c>jobs.schedules</c>: by name; search on name and kind (trigram
/// index); filter by kind, whether it is on, and its moments. A workspace holds a handful.</summary>
internal static class SchedulesList
{
    public const string Key = "jobs.schedules";

    public static ListBinding<JobSchedule> Create() =>
        ListBinding<JobSchedule>.For(new ListDefinition(
                Key, "jobs.schedules.title", JobsPermissions.SchedulesRead, "/api/jobs/schedules",
                [
                    new ListColumn("name", "jobs.schedule.name", ListColumnType.Text, Sortable: true, Filterable: true, Width: ListColumnWidth.Wide),
                    new ListColumn("type", "jobs.schedule.type", ListColumnType.Text, Sortable: true, Filterable: true, Groupable: true),
                    new ListColumn("cron", "jobs.schedule.cron", ListColumnType.Text, Sortable: true, Filterable: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("isEnabled", "jobs.schedule.enabled", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "jobs.schedule.on", FalseLabelKey: "jobs.schedule.off", Width: ListColumnWidth.Narrow),
                    new ListColumn("nextRunAt", "jobs.schedule.nextRunAt", ListColumnType.DateTime, Sortable: true, Filterable: true),
                    new ListColumn("lastRunAt", "jobs.schedule.lastRunAt", ListColumnType.DateTime, Sortable: true, Filterable: true),
                ],
                SearchFields: ["name", "type"],
                DefaultSort: "name",
                Presets:
                [
                    new ListPreset("on", "jobs.view.on", Filter: "isEnabled eq true"),
                    new ListPreset("off", "jobs.view.off", Filter: "isEnabled eq false"),
                ]),
                r => r.Id)
            .Column("name", r => r.Name)
            .Column("type", r => r.Type)
            .Column("cron", r => r.Cron)
            .Column("isEnabled", r => r.IsEnabled)
            .Column("nextRunAt", r => r.NextRunAt)
            .Column("lastRunAt", r => r.LastRunAt);
}
