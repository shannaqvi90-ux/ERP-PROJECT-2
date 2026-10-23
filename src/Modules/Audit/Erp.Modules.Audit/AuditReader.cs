using System.Linq.Expressions;
using System.Text.Json;
using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Audit;

/// <summary>Values the capture trigger writes in place of a secret.</summary>
public static class AuditValues
{
    public const string Redacted = "[redacted]";
}

/// <summary>One field of one change: its stored name and its value before and after (raw JSON:
/// text, number, flag, list or nothing).</summary>
public sealed record AuditFieldChange(string Field, JsonElement? Old, JsonElement? New);

/// <summary>A record's name in English and, when it has one, in Arabic script.</summary>
public sealed record AuditRecordName(string Name, string? NameAr)
{
    public string For(string language) => language == "ar" && !string.IsNullOrWhiteSpace(NameAr) ? NameAr : Name;
}

/// <summary>Who made a change, as the trail shows them.</summary>
public sealed record AuditActor(Guid? Id, string Kind, string? Name, string? NameAr);

/// <summary>
/// Reads the audit trail for one request. Row-level security already limits it to the tenant and to
/// the companies of the request's scope (the <c>audit_company_scope</c> policy); on top of that a
/// reader sees the changes of a kind of record only when they hold its read permission, and of a
/// record of a company only in a company where they hold it (<see cref="VisibleAsync"/>): the
/// history of a record tells everything it ever held.
/// </summary>
public sealed class AuditReader(KernelDbContext db, ModuleCatalog catalog, ICurrentUser user, ICompanyPermissions places, IUserDirectory users)
{
    public KernelDbContext Db => db;

    /// <summary>The kinds of records the caller may read, each with the companies where they may
    /// (null: everywhere).</summary>
    public async Task<IReadOnlyList<(AuditRecordType Type, IReadOnlySet<Guid>? Companies)>> ReadableTypesAsync(CancellationToken cancellationToken)
    {
        var readable = new List<(AuditRecordType, IReadOnlySet<Guid>?)>();
        foreach (var type in catalog.AuditRecordTypes)
        {
            if (!user.Has(type.ReadPermission))
            {
                continue;
            }
            readable.Add((type, await places.CompaniesAsync(type.ReadPermission, cancellationToken)));
        }
        return readable;
    }

    /// <summary>The audit rows among <paramref name="source"/> the caller may read (see the class).</summary>
    public async Task<IQueryable<AuditEntry>> VisibleAsync(IQueryable<AuditEntry> source, CancellationToken cancellationToken)
    {
        var readable = await ReadableTypesAsync(cancellationToken);
        var everywhere = readable.Where(r => r.Companies is null).Select(r => r.Type.Table).ToArray();
        var row = Expression.Parameter(typeof(AuditEntry), "e");
        var recordType = Expression.Property(row, nameof(AuditEntry.RecordType));
        var company = Expression.Property(row, nameof(AuditEntry.RecordCompanyId));
        Expression condition = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)],
            Expression.Constant(everywhere), recordType);
        foreach (var (type, companies) in readable.Where(r => r.Companies is not null))
        {
            // This kind of record, of no company or of a company where the permission is held.
            var ids = companies!.Select(c => (Guid?)c).ToArray();
            var inCompany = Expression.OrElse(
                Expression.Equal(company, Expression.Constant(null, typeof(Guid?))),
                Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid?)], Expression.Constant(ids), company));
            condition = Expression.OrElse(condition, Expression.AndAlso(Expression.Equal(recordType, Expression.Constant(type.Table)), inCompany));
        }
        return source.Where(Expression.Lambda<Func<AuditEntry, bool>>(condition, row));
    }

    /// <summary>The fields one audit row records, by field name.</summary>
    public static IReadOnlyList<AuditFieldChange> Changes(AuditEntry entry)
    {
        var changes = new List<AuditFieldChange>();
        foreach (var field in entry.Changes.RootElement.EnumerateObject())
        {
            JsonElement? old = field.Value.TryGetProperty("old", out var o) ? o.Clone() : null;
            JsonElement? @new = field.Value.TryGetProperty("new", out var n) ? n.Clone() : null;
            changes.Add(new AuditFieldChange(field.Name, old, @new));
        }
        return changes.OrderBy(c => c.Field, StringComparer.Ordinal).ToList();
    }

    /// <summary>Who made each change: a person by their name now (from identity), a person the
    /// workspace no longer has as a former user, work the system did by its kind.</summary>
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> ActorsAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0 ? new Dictionary<Guid, UserSummary>() : await users.GetAsync(wanted, cancellationToken);
    }

    /// <summary>
    /// The names of the records that values in <paramref name="changes"/> refer to by id (a role id
    /// in a user's roles, the company of a branch), as the audit trail last named them, among the
    /// kinds of records the caller may read; ids of anything else stay ids.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, AuditRecordName>> ReferencedNamesAsync(IEnumerable<AuditFieldChange> changes, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        void Collect(JsonElement? value)
        {
            if (value is not { } v)
            {
                return;
            }
            if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id))
            {
                ids.Add(id);
            }
            else if (v.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in v.EnumerateArray())
                {
                    Collect(item);
                }
            }
        }
        foreach (var change in changes.Where(c => c.Field != "id"))
        {
            Collect(change.Old);
            Collect(change.New);
        }
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AuditRecordName>();
        }
        var wanted = ids.Take(500).Select(i => (Guid?)i).ToArray();
        var named = await (await VisibleAsync(db.AuditEntries.AsNoTracking(), cancellationToken))
            .Where(e => wanted.Contains(e.RecordId) && e.RecordLabel != null)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.RecordId, e.RecordLabel, e.RecordLabelAr })
            .Take(2000)
            .ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, AuditRecordName>();
        foreach (var n in named)
        {
            names.TryAdd(n.RecordId!.Value, new AuditRecordName(n.RecordLabel!, n.RecordLabelAr));
        }
        return names;
    }
}
