using System.Reflection;
using System.Runtime.CompilerServices;
using Erp.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.ImportExport;

/// <summary>
/// Lets a module put one of its tables in the full tenant export (p14) without writing a reader: every plain column of the entity
/// (text, numbers, amounts, dates, flags, ids, JSON text) is written, named in lower camel case, in id order, read through the
/// module's own database context (so the workspace's row-level security and filters apply). Binary columns, the row version and the
/// workspace id are never written; the columns named in the <c>exclude</c> argument (secrets, tokens) are left out too.
/// </summary>
public static class ExportEntities
{
    public static ExportRecordType For<TContext, TEntity>(string table, string labelKey, params string[] exclude)
        where TContext : DbContext
        where TEntity : TenantEntity
    {
        var skip = new HashSet<string>(exclude.Append(nameof(TenantEntity.TenantId)).Append(nameof(TenantEntity.Version)), StringComparer.Ordinal);
        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !skip.Contains(p.Name) && (IsPlain(p.PropertyType) || IsList(p.PropertyType)) && p.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>() is null)
            .OrderBy(p => p.Name == nameof(TenantEntity.Id) ? 0 : 1).ThenBy(p => p.DeclaringType == typeof(TenantEntity) ? 1 : 0).ThenBy(p => p.MetadataToken)
            .ToList();
        var columns = properties.Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]).ToList();

        async IAsyncEnumerable<ExportRecord> Rows(IServiceProvider services, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var db = services.GetRequiredService<TContext>();
            var last = Guid.Empty;
            while (true)
            {
                var page = await db.Set<TEntity>().AsNoTracking().Where(e => e.Id.CompareTo(last) > 0).OrderBy(e => e.Id).Take(1000).ToListAsync(cancellationToken);
                if (page.Count == 0)
                {
                    yield break;
                }
                foreach (var entity in page)
                {
                    yield return new ExportRecord(entity.Id, [.. properties.Select(p => Cell(p.GetValue(entity)))]);
                }
                last = page[^1].Id;
            }
        }

        return new ExportRecordType(table, labelKey, columns, Rows);
    }

    private static object? Cell(object? value) => value switch
    {
        null => null,
        Enum e => e.ToString(),
        System.Collections.IEnumerable list when value is not string => System.Text.Json.JsonSerializer.SerializeToElement(list, value.GetType(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
        _ => value,
    };

    private static bool IsList(Type type) => type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static bool IsPlain(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }
}
