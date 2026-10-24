using System.ComponentModel;
using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Audit;

/// <summary>One change of a record, as its history shows it.</summary>
/// <param name="Id">The change's own id (pass the last one shown as <c>before</c> for older changes).</param>
/// <param name="ActorId">The person who made it; null for work the system did.</param>
/// <param name="ActorName">Their name (a former user's: "Former user"), or the system's kind.</param>
/// <param name="ActorKind">user, seed, job or system.</param>
/// <param name="Action">insert (created, with every field's first value), update (each changed
/// field's old and new value) or delete (every field's last value).</param>
/// <param name="Changes">The fields, by name, with their old and new values as stored.</param>
public sealed record AuditEvent(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string ActorName,
    string? ActorNameAr,
    string ActorKind,
    string Action,
    IReadOnlyList<AuditFieldChange> Changes);

/// <summary>A record's history, newest change first.</summary>
/// <param name="RecordType">The kind of record asked for (<c>schema.table</c>), or null for every kind.</param>
/// <param name="Items">The changes of this page.</param>
/// <param name="Next">Pass as <c>before</c> to read older changes; null when there are none.</param>
/// <param name="Names">Names of the records the values refer to by id, where the reader may read them.</param>
public sealed record AuditHistory(string? RecordType, IReadOnlyList<AuditEvent> Items, Guid? Next, IReadOnlyDictionary<Guid, AuditRecordName> Names);

internal static class AuditEndpoints
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/log", Log)
            .WithName("audit.log.list")
            .WithSummary("The audit log: every recorded change of the business records the caller may read (each kind of record needs its own read permission as well, in the record's company), newest first, a page at a time: word search on the record's name, filters (actorId, actorKind, action, recordType, record, recordId, fields eq 'email', occurredAt), sort, keyset or offset paging and grouping (the list query contract).")
            .RequirePermission(AuditPermissions.LogRead);

        group.MapGet("/log/{id:guid}", Change)
            .WithName("audit.log.get")
            .WithSummary("One change of the audit log with every field's old and new value as stored, and the names of the records its values refer to by id (where the caller may read them). A change the caller may not read does not exist for them (404).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(AuditPermissions.LogRead);

        var catalog = ((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<ModuleCatalog>();
        // Every kind of audited record is one of the log's record types: a module registered after
        // the audit module would have its changes in the trail but not in the log's choices.
        var listed = catalog.FindList(AuditLists.Log)?.Column("recordType")?.Choices?.Select(c => c.Value).ToHashSet() ?? [];
        var missing = catalog.AuditRecordTypes.Where(t => !listed.Contains(t.Table)).Select(t => t.Table).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Register the audit module after the modules of {string.Join(", ", missing)} (their audited records are not among the audit log's record types).");
        }
        group.MapGet("/records/{id:guid}", History)
            .WithName("audit.history.get")
            .WithSummary("The history of one record, newest change first: who changed it, when, and each field's old and new value (a creation lists every first value, a deletion every last value). Besides the audit log's permission, the caller needs the permission that reads such records, in the record's company (and its branch, where they may work in only some): otherwise, as for an id that names nothing, the history is empty. With type (schema.table, as /api/lists/audit.log/definition names the kinds of records), only that kind's changes, and for rows a form shows by another record's id (a user's company access by the user's id) every row whose owner column held the id.")
            .RequirePermission(AuditPermissions.LogRead);
    }

    private static async Task<Results<Ok<ListPage<AuditLogRow>>, ProblemHttpResult>> Log(
        AuditReader reader, ModuleCatalog catalog, AuditTexts texts, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await AuditLog.PageAsync(reader, catalog, texts, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    private static async Task<Results<Ok<AuditChange>, ProblemHttpResult>> Change(Guid id, AuditReader reader, ModuleCatalog catalog, AuditTexts texts,
        HttpContext http, CancellationToken cancellationToken)
    {
        var entry = await (await reader.VisibleAsync(reader.Db.AuditEntries.AsNoTracking(), cancellationToken))
            .Where(e => e.Uid == id).FirstOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return Problems.NotFound(http);
        }
        var changes = AuditReader.Changes(entry);
        var actors = await reader.ActorsAsync([entry.ActorId], cancellationToken);
        var names = AuditLog.Mentioned(changes, await reader.ReferencedNamesAsync(changes, cancellationToken));
        return TypedResults.Ok(new AuditChange(AuditLog.Row(entry, changes, actors, names, catalog, texts, AuditLog.Language(http)), changes, names));
    }

    private static async Task<Results<Ok<AuditHistory>, ProblemHttpResult>> History(Guid id,
        [FromQuery, Description("The kind of record (schema.table, for example identity.users); without it every change of the record, whatever its kind.")] string? type,
        [FromQuery, Description("The id of the last change shown: older changes follow it.")] Guid? before,
        [FromQuery, Description("Changes per page, 1 to 200 (default 50).")] int? take,
        AuditReader reader, ModuleCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        AuditRecordType? kind = null;
        if (!string.IsNullOrEmpty(type) && (kind = catalog.FindAuditRecordType(type)) is null)
        {
            return new Validator(http).Add("type", "auditRecordType").ToResult();
        }
        var size = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        var visible = await reader.VisibleAsync(reader.Db.AuditEntries.AsNoTracking(), cancellationToken);
        IQueryable<AuditEntry> rows;
        if (kind is null)
        {
            rows = visible.Where(e => e.RecordId == id);
        }
        else if (kind.OwnerField is { } owner)
        {
            // Rows shown by another record's id (a user's company access by the user): every row
            // whose owner column held the id, before or after the change.
            var ownerWas = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { [owner] = new { old = id } });
            var ownerIs = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { [owner] = new { @new = id } });
            var table = kind.Table;
            rows = visible.Where(e => e.RecordType == table && (e.RecordId == id || EF.Functions.JsonContains(e.Changes, ownerWas) || EF.Functions.JsonContains(e.Changes, ownerIs)));
        }
        else
        {
            var table = kind.Table;
            rows = visible.Where(e => e.RecordType == table && e.RecordId == id);
        }
        if (before is { } cursor)
        {
            // Only a change of this very record the caller may read continues its history; any other
            // id (one of another record, another company or another tenant) is refused the same way.
            var at = await rows.Where(e => e.Uid == cursor).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(cancellationToken);
            if (at is not { } time)
            {
                return new Validator(http).Add("before", "auditCursor").ToResult();
            }
            rows = rows.Where(e => e.OccurredAt < time || (e.OccurredAt == time && e.Uid.CompareTo(cursor) < 0));
        }
        var page = await rows.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Uid).Take(size + 1).ToListAsync(cancellationToken);
        Guid? next = null;
        if (page.Count > size)
        {
            page.RemoveAt(page.Count - 1);
            next = page[^1].Uid;
        }
        var texts = http.RequestServices.GetRequiredService<AuditTexts>();
        var actors = await reader.ActorsAsync(page.Select(e => e.ActorId), cancellationToken);
        var changes = page.ToDictionary(e => e.Uid, AuditReader.Changes);
        var names = await reader.ReferencedNamesAsync(changes.Values.SelectMany(c => c), cancellationToken);
        var items = page.Select(e =>
        {
            var (name, nameAr) = AuditLog.ActorName(e.ActorId, e.ActorKind, actors, texts);
            return new AuditEvent(e.Uid, e.OccurredAt, e.ActorId, name, nameAr, e.ActorKind, e.Action, changes[e.Uid]);
        }).ToList();
        return TypedResults.Ok(new AuditHistory(kind?.Table, items, next, names));
    }
}
