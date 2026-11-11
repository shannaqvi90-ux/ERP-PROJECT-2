using Microsoft.Extensions.DependencyInjection;
using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Localization;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Modules.Audit.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Audit;

/// <summary>One change of the audit log as its screen shows it (a row is kept small: the change's
/// fields with their stored values are read with <c>GET /api/audit/log/{id}</c>).</summary>
/// <param name="Id">The change's own id (a history and the log link to it).</param>
/// <param name="ActorId">The person who made it; null for work the system did (seeding, jobs).</param>
/// <param name="ActorName">Their name (a former user's: "Former user"); the system's kind otherwise.</param>
/// <param name="ActorKind">user, seed, job or system.</param>
/// <param name="Action">insert, update or delete.</param>
/// <param name="RecordType">The kind of record (<c>schema.table</c>, as registered).</param>
/// <param name="Record">What the record was called when it changed.</param>
/// <param name="RecordCode">The record's e-mail or code, when it has one.</param>
/// <param name="RecordId">The record's id.</param>
/// <param name="RecordRoute">The record's web address, when it has a screen.</param>
/// <param name="Fields">The fields the change recorded.</param>
/// <param name="Changes">Every field's old and new value as one line, in the request's language (the
/// document's, when the reports framework prints the log).</param>
public sealed record AuditLogRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string ActorName,
    string? ActorNameAr,
    string ActorKind,
    string Action,
    string RecordType,
    string? Record,
    string? RecordAr,
    string? RecordCode,
    Guid? RecordId,
    string? RecordRoute,
    IReadOnlyList<string> Fields,
    string Changes);

/// <summary>One change with every field's stored old and new value (the log's change panel).</summary>
/// <param name="Change">The change as the log lists it.</param>
/// <param name="Details">Every field's old and new value, as stored, by field name.</param>
/// <param name="Names">Names of the records the values refer to by id, where the reader may read them.</param>
public sealed record AuditChange(AuditLogRow Change, IReadOnlyList<AuditFieldChange> Details, IReadOnlyDictionary<Guid, AuditRecordName> Names,
    IReadOnlyDictionary<string, AuditFieldLabel>? Fields = null);

/// <summary>A tenant-defined field as the trail names it: its labels in both languages and, for a number, its decimals.</summary>
public sealed record AuditFieldLabel(string LabelEn, string LabelAr, string Type, int Decimals)
{
    public string For(string language) => language == "ar" && !string.IsNullOrWhiteSpace(LabelAr) ? LabelAr : LabelEn;
}

/// <summary>
/// The audit log list: every recorded change of the business records the caller may read, newest
/// first, through the list framework (search by the record's name, filters by person, kind of
/// record, record, field, change and time, saved views, grouping, printing and export). Sorted by
/// time on the (tenant_id, occurred_at, uid) index; the record's name in English and Arabic on
/// trigram indexes; the fields on a GIN index.
/// </summary>
internal static class AuditLog
{
    public const string Key = AuditLists.Log;

    /// <summary>Who may make a change: a person, the demo seed, a background job, the system.</summary>
    public static readonly System.Collections.Immutable.ImmutableArray<string> ActorKinds = ["user", "seed", "job", "system", "unknown"];

    public static ListBinding<AuditEntry> Create(IEnumerable<AuditRecordType> types, AuditTexts texts) =>
        ListBinding<AuditEntry>.For(new ListDefinition(
                Key, "audit.log.title", AuditPermissions.LogRead, "/api/audit/log",
                [
                    new ListColumn("occurredAt", "audit.log.when", ListColumnType.DateTime, Sortable: true, Filterable: true),
                    new ListColumn("actorId", "audit.log.who", ListColumnType.Reference, Filterable: true, LabelField: "actorName", ArabicField: "actorNameAr"),
                    new ListColumn("actorKind", "audit.log.source", ListColumnType.Choice, Filterable: true, Groupable: true, Hidden: true,
                        Choices: [.. ActorKinds.Select(k => new ListChoice(k, $"audit.source.{k}"))], Width: ListColumnWidth.Narrow),
                    new ListColumn("action", "audit.log.action", ListColumnType.Choice, Filterable: true, Groupable: true,
                        Choices: [new ListChoice("insert", "audit.action.insert"), new ListChoice("update", "audit.action.update"), new ListChoice("delete", "audit.action.delete")],
                        Width: ListColumnWidth.Narrow),
                    new ListColumn("recordType", "audit.log.recordType", ListColumnType.Choice, Filterable: true, Groupable: true,
                        Choices: [.. types.Select(t => new ListChoice(t.Table, t.LabelKey))]),
                    new ListColumn("record", "audit.log.record", ListColumnType.Text, Filterable: true, ArabicField: "recordAr"),
                    // Searched (an Arabic word, an e-mail or a code), not filtered on their own.
                    new ListColumn("recordAr", "audit.log.recordAr", ListColumnType.Text, Hidden: true),
                    new ListColumn("recordCode", "audit.log.recordCode", ListColumnType.Text, Hidden: true),
                    new ListColumn("recordId", "audit.log.recordId", ListColumnType.Reference, Filterable: true, Hidden: true, LabelField: "record"),
                    new ListColumn("fields", "audit.log.fields", ListColumnType.Choice, Filterable: true,
                        Choices: [.. texts.Fields.Select(f => new ListChoice(f, AuditTexts.FieldPrefix + f))]),
                    new ListColumn("changes", "audit.log.changes", ListColumnType.Text, Width: ListColumnWidth.Wide),
                ],
                // The record's name, or its e-mail or code (a user's sign-in, a company's code).
                SearchFields: ["record", "recordCode"],
                ArabicSearchFields: ["record", "recordAr"],
                DefaultSort: "-occurredAt",
                Presets:
                [
                    new ListPreset("changes", "audit.log.view.changes", Filter: "action eq 'update'"),
                    new ListPreset("created", "audit.log.view.created", Filter: "action eq 'insert'"),
                    new ListPreset("deleted", "audit.log.view.deleted", Filter: "action eq 'delete'"),
                    new ListPreset("byType", "audit.log.view.byType", GroupBy: "recordType"),
                    new ListPreset("bySource", "audit.log.view.bySource", GroupBy: "actorKind"),
                ]),
                e => e.Uid)
            .Column("occurredAt", e => e.OccurredAt)
            .Column("actorId", e => e.ActorId)
            .Column("actorKind", e => e.ActorKind)
            .Column("action", e => e.Action)
            .Column("recordType", e => e.RecordType)
            .Column("record", e => e.RecordLabel)
            .Column("recordAr", e => e.RecordLabelAr)
            .Column("recordCode", e => e.RecordCode)
            .Column("recordId", e => e.RecordId)
            .Column("fields", e => e.Fields);

    /// <summary>One page of the audit log exactly as its endpoint serves it (reports print it too).</summary>
    public static async Task<ListResult<AuditLogRow>> PageAsync(AuditReader reader, ModuleCatalog catalog, AuditTexts texts, ListRequest request,
        HttpContext http, CancellationToken cancellationToken)
    {
        var source = await reader.VisibleAsync(reader.Db.AuditEntries.AsNoTracking(), cancellationToken);
        var result = await catalog.ListBinding<AuditEntry>(Key).QueryAsync(source, request, http, cancellationToken);
        if (result.Problem is not null)
        {
            return result.Map(_ => (AuditLogRow)null!);
        }
        var actors = await reader.ActorsAsync(result.Rows.Select(r => r.ActorId), cancellationToken);
        var details = result.Rows.ToDictionary(r => r.Uid, AuditReader.Changes);
        var names = await reader.ReferencedNamesAsync(details.Values.SelectMany(d => d), cancellationToken);
        var language = Language(http);
        var labels = await CustomFieldLabelsAsync(http.RequestServices, details.Values.SelectMany(d => d), null, cancellationToken);
        return result.Map(e => Row(e, details[e.Uid], actors, names, catalog, texts, language, labels));
    }

    /// <summary>The prefix of a custom field's name in a record's history (the contract of audit.capture()).</summary>
    public const string CustomFieldPrefix = "custom_fields.";

    /// <summary>The tenant's own labels of the custom fields the changes mention (none when the host has no
    /// custom fields), by stored field name.</summary>
    internal static async Task<IReadOnlyDictionary<string, AuditFieldLabel>> CustomFieldLabelsAsync(IServiceProvider services, IEnumerable<AuditFieldChange> changes,
        string? recordType, CancellationToken cancellationToken)
    {
        var keys = changes.Select(c => c.Field).Where(f => f.StartsWith(CustomFieldPrefix, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)
            .Select(f => f[CustomFieldPrefix.Length..]).ToList();
        if (keys.Count == 0 || services.GetService<Erp.Kernel.CustomFields.ICustomFields>() is not { } customFields)
        {
            return new Dictionary<string, AuditFieldLabel>();
        }
        var found = await customFields.DescribeAsync(keys, recordType, cancellationToken);
        return found.ToDictionary(p => CustomFieldPrefix + p.Key, p => new AuditFieldLabel(p.Value.LabelEn, p.Value.LabelAr, p.Value.Type, p.Value.Decimals), StringComparer.Ordinal);
    }

    /// <summary>The language of the change texts: the document's (the reports framework's
    /// <c>language</c> parameter), else the reader's.</summary>
    internal static string Language(HttpContext http) =>
        http.Request.Query["language"].ToString() is { } asked && Languages.IsSupported(asked) ? asked : Languages.ForRequest(http);

    /// <summary>The names among <paramref name="names"/> that the change's values mention: each value
    /// is read once (a row's values against the page's names, never every name against every value).</summary>
    internal static IReadOnlyDictionary<Guid, AuditRecordName> Mentioned(IReadOnlyList<AuditFieldChange> changes, IReadOnlyDictionary<Guid, AuditRecordName> names)
    {
        var used = new Dictionary<Guid, AuditRecordName>();
        if (names.Count == 0)
        {
            return used;
        }
        void Visit(System.Text.Json.JsonElement? value)
        {
            if (value is not { } v)
            {
                return;
            }
            if (v.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (Guid.TryParse(v.GetString(), out var id) && names.TryGetValue(id, out var name))
                {
                    used[id] = name;
                }
            }
            else if (v.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in v.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
        foreach (var change in changes.Where(c => c.Field != "id"))
        {
            Visit(change.Old);
            Visit(change.New);
        }
        return used;
    }

    internal static AuditLogRow Row(AuditEntry e, IReadOnlyList<AuditFieldChange> changes, IReadOnlyDictionary<Guid, Identity.Contracts.UserSummary> actors,
        IReadOnlyDictionary<Guid, AuditRecordName> names, ModuleCatalog catalog, AuditTexts texts, string language,
        IReadOnlyDictionary<string, AuditFieldLabel>? fieldLabels = null)
    {
        var (name, nameAr) = ActorName(e.ActorId, e.ActorKind, actors, texts);
        var shown = changes.Where(c => c.Field != "id").ToList();
        var route = catalog.FindAuditRecordType(e.RecordType)?.Route is { } template && e.RecordId is { } recordId && e.Action != "delete"
            ? template.Replace("{id}", recordId.ToString(), StringComparison.Ordinal)
            : null;
        return new AuditLogRow(e.Uid, e.OccurredAt, e.ActorId, name, nameAr, e.ActorKind, e.Action, e.RecordType, e.RecordLabel, e.RecordLabelAr, e.RecordCode, e.RecordId, route,
            e.Fields ?? [], texts.Summary(shown, e.Action, language, Mentioned(changes, names), fieldLabels: fieldLabels));
    }

    /// <summary>Who made a change, in English and Arabic: the person's name now; a person no longer
    /// in the workspace as a former user; the system's work by its kind.</summary>
    internal static (string Name, string? NameAr) ActorName(Guid? actorId, string kind, IReadOnlyDictionary<Guid, Identity.Contracts.UserSummary> actors, AuditTexts texts)
    {
        if (actorId is { } id)
        {
            return actors.TryGetValue(id, out var person)
                ? (person.DisplayName, person.DisplayNameAr)
                : (texts.Get("audit.actor.former", Languages.English), texts.Get("audit.actor.former", Languages.Arabic));
        }
        var key = ActorKinds.Contains(kind) && kind != "user" ? $"audit.source.{kind}" : "audit.actor.none";
        return (texts.Get(key, Languages.English), texts.Get(key, Languages.Arabic));
    }
}
