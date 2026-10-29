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
using Erp.Modules.Jobs.Engine;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Jobs.Schedules;

/// <summary>A row of the schedules list.</summary>
public sealed record ScheduleRow(
    Guid Id,
    string Name,
    string Type,
    string TypeNameEn,
    string TypeNameAr,
    string Cron,
    bool IsEnabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    Guid? LastJobId,
    DateTimeOffset UpdatedAt,
    uint Version,
    bool CanUpdate);

/// <summary>A schedule as its form shows it.</summary>
public sealed record ScheduleDto(
    Guid Id,
    string Name,
    string Type,
    string TypeNameEn,
    string TypeNameAr,
    [property: Description("Five fields: minute hour day-of-month month day-of-week, in the workspace's time zone.")] string Cron,
    [property: Description("The parameters every job of this schedule is queued with (a JSON object).")] JsonElement Payload,
    bool IsEnabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    Guid? LastJobId,
    [property: Description("The workspace's time zone (IANA) the timing is read in.")] string TimeZone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    bool CanUpdate);

/// <summary>Add a schedule: queue a job of <c>type</c> whenever <c>cron</c> fires (the workspace's
/// time zone). A schedule whose last job has not finished queues nothing that time. The caller needs
/// the kind's run permission.</summary>
public sealed record CreateScheduleRequest(
    [property: ApiExample("Nightly clean-up"), StringLength(JobsDbContext.NameLength)] string? Name,
    [property: ApiExample("jobs.cleanup"), StringLength(JobsDbContext.TypeLength)] string? Type,
    [property: ApiExample("30 3 * * *"), StringLength(CronSchedule.MaxLength), Description("Minute hour day-of-month month day-of-week; * , - / and the names jan-dec, sun-sat.")] string? Cron,
    [property: Description("The parameters every job of this schedule is queued with, a JSON object of at most 64 KB.")] Dictionary<string, JsonElement>? Payload,
    bool? IsEnabled);

/// <summary>Change a schedule. The kind never changes. The version is the one read.</summary>
public sealed record UpdateScheduleRequest(
    [property: ApiExample("Nightly clean-up"), StringLength(JobsDbContext.NameLength)] string? Name,
    [property: ApiExample("30 3 * * *"), StringLength(CronSchedule.MaxLength)] string? Cron,
    [property: Description("The parameters every job of this schedule is queued with, a JSON object of at most 64 KB.")] Dictionary<string, JsonElement>? Payload,
    bool? IsEnabled,
    uint? Version);

internal static class ScheduleEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/schedules", List)
            .WithName("jobs.schedules.list")
            .WithSummary("The workspace's schedules, a page at a time: word search on name and kind, filters, sort, paging and grouping (the list query contract); by name by default.")
            .RequirePermission(JobsPermissions.SchedulesRead);

        group.MapGet("/schedules/{id:guid}", Get)
            .WithName("jobs.schedules.get")
            .WithSummary("One schedule.")
            .RequirePermission(JobsPermissions.SchedulesRead);

        group.MapPost("/schedules", Create)
            .WithName("jobs.schedules.create")
            .WithSummary("Add a schedule for a kind of job the caller may run.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(JobsPermissions.SchedulesCreate);

        group.MapPut("/schedules/{id:guid}", Update)
            .WithName("jobs.schedules.update")
            .WithSummary("Change a schedule's name, timing or parameters, or switch it on or off (switching on works out the next moment from now).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(JobsPermissions.SchedulesUpdate);
    }

    private static async Task<Results<Ok<ListPage<ScheduleRow>>, ProblemHttpResult>> List(
        JobsDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    internal static async Task<ListResult<ScheduleRow>> PageAsync(JobsDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await catalog.ListBinding<JobSchedule>(SchedulesList.Key).QueryAsync(db.Schedules.AsNoTracking(), request, http, cancellationToken);
        if (result.Problem is not null)
        {
            return result.Map(_ => (ScheduleRow)null!);
        }
        var services = http.RequestServices;
        var types = services.GetRequiredService<IJobTypes>();
        var strings = services.GetRequiredService<StringCatalog>();
        var user = services.GetRequiredService<ICurrentUser>();
        return result.Map(s => ToRow(s, types, strings, user));
    }

    private static ScheduleRow ToRow(JobSchedule s, IJobTypes types, StringCatalog strings, ICurrentUser user)
    {
        var (en, ar) = Names(types, strings, s.Type);
        return new ScheduleRow(s.Id, s.Name, s.Type, en, ar, s.Cron, s.IsEnabled, s.NextRunAt, s.LastRunAt, s.LastJobId, s.UpdatedAt, s.Version, CanUpdate(s, types, user));
    }

    private static (string En, string Ar) Names(IJobTypes types, StringCatalog strings, string key) =>
        types.Find(key) is { } type ? (strings.Get(type.LabelKey, Languages.English), strings.Get(type.LabelKey, Languages.Arabic)) : (key, key);

    private static bool CanUpdate(JobSchedule s, IJobTypes types, ICurrentUser user) =>
        user.Has(JobsPermissions.SchedulesUpdate) && types.Find(s.Type)?.RunPermission is { } run && user.Has(run);

    private static async Task<Results<Ok<ScheduleDto>, ProblemHttpResult>> Get(Guid id, JobsDbContext db, ITenantDirectory tenants, HttpContext http, CancellationToken cancellationToken)
    {
        var schedule = await db.Schedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return schedule is null ? Problems.NotFound(http) : TypedResults.Ok(await ToDtoAsync(schedule, tenants, http, cancellationToken));
    }

    private static async Task<ScheduleDto> ToDtoAsync(JobSchedule s, ITenantDirectory tenants, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var types = services.GetRequiredService<IJobTypes>();
        var strings = services.GetRequiredService<StringCatalog>();
        var (en, ar) = Names(types, strings, s.Type);
        var zone = (await tenants.GetCurrentAsync(cancellationToken))?.TimeZone ?? "Asia/Dubai";
        return new ScheduleDto(s.Id, s.Name, s.Type, en, ar, s.Cron, JsonDocument.Parse(s.Payload).RootElement.Clone(), s.IsEnabled, s.NextRunAt, s.LastRunAt, s.LastJobId,
            zone, s.CreatedAt, s.UpdatedAt, s.Version, CanUpdate(s, types, services.GetRequiredService<ICurrentUser>()));
    }

    private static void CheckFields(Validator validator, string? name, string? cron, Dictionary<string, JsonElement>? payload)
    {
        validator.MaxLength("name", name, JobsDbContext.NameLength);
        if (cron is not null)
        {
            validator.MaxLength("cron", cron, CronSchedule.MaxLength);
            if (cron.Length <= CronSchedule.MaxLength && !CronSchedule.TryParse(cron, out _, out var error))
            {
                validator.Add("cron", "jobs" + char.ToUpperInvariant(error![0]) + error[1..]);
            }
        }
        if (payload is not null)
        {
            validator.Must(JsonSerializer.Serialize(payload).Length <= JobsLimits.PayloadBytes, "payload", "jobsPayloadTooLarge", JobsLimits.PayloadBytes / 1024);
        }
    }

    private static async Task<Results<Created<ScheduleDto>, ProblemHttpResult>> Create(
        CreateScheduleRequest request, JobsDbContext db, IJobTypes types, ICurrentUser user, ITenantDirectory tenants, HttpContext http, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var cron = request.Cron?.Trim();
        var validator = new Validator(http).Required("name", name).Required("type", request.Type).Required("cron", cron);
        CheckFields(validator, name, cron, request.Payload);
        var type = request.Type is null ? null : types.Find(request.Type);
        if (request.Type is not null)
        {
            validator.Must(type is { RunPermission: not null }, "type", "jobsTypeNotSchedulable");
        }
        if (validator.IsValid && await db.Schedules.AnyAsync(s => s.Name == name, cancellationToken))
        {
            validator.Add("name", "jobsScheduleNameTaken");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!user.Has(type!.RunPermission!))
        {
            return Problems.Forbidden(http, "jobs.typeNotAllowed");
        }
        var enabled = request.IsEnabled ?? true;
        var zone = (await tenants.GetCurrentAsync(cancellationToken))?.TimeZone;
        var schedule = new JobSchedule
        {
            Name = name!,
            Type = type.Key,
            Cron = cron!,
            Payload = JobQueue.SerializePayload(request.Payload),
            IsEnabled = enabled,
            NextRunAt = enabled ? ScheduleTimer.Next(cron!, zone, DateTimeOffset.UtcNow) : null,
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/jobs/schedules/{schedule.Id}", await ToDtoAsync(schedule, tenants, http, cancellationToken));
    }

    private static async Task<Results<Ok<ScheduleDto>, ProblemHttpResult>> Update(
        Guid id, UpdateScheduleRequest request, JobsDbContext db, IJobTypes types, ICurrentUser user, ITenantDirectory tenants, HttpContext http, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var cron = request.Cron?.Trim();
        var validator = new Validator(http).Required("name", name).Required("cron", cron).Required("isEnabled", request.IsEnabled).Required("version", request.Version);
        CheckFields(validator, name, cron, request.Payload);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var schedule = await db.Schedules.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return Problems.NotFound(http);
        }
        if (types.Find(schedule.Type)?.RunPermission is not { } required || !user.Has(required))
        {
            return Problems.Forbidden(http, "jobs.typeNotAllowed");
        }
        if (name != schedule.Name && await db.Schedules.AnyAsync(s => s.Name == name && s.Id != id, cancellationToken))
        {
            return new Validator(http).Add("name", "jobsScheduleNameTaken").ToResult();
        }
        db.Entry(schedule).Property(s => s.Version).OriginalValue = request.Version!.Value;
        var zone = (await tenants.GetCurrentAsync(cancellationToken))?.TimeZone;
        var timingChanged = cron != schedule.Cron || (request.IsEnabled == true && !schedule.IsEnabled);
        schedule.Name = name!;
        schedule.Cron = cron!;
        if (request.Payload is not null)
        {
            schedule.Payload = JobQueue.SerializePayload(request.Payload);
        }
        schedule.IsEnabled = request.IsEnabled!.Value;
        if (!schedule.IsEnabled)
        {
            schedule.NextRunAt = null;
        }
        else if (timingChanged || schedule.NextRunAt is null)
        {
            schedule.NextRunAt = ScheduleTimer.Next(cron!, zone, DateTimeOffset.UtcNow);
        }
        db.Entry(schedule).Property(s => s.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(schedule, tenants, http, cancellationToken));
    }
}
