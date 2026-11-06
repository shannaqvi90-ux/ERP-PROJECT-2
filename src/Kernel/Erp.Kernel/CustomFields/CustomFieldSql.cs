using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Erp.Kernel.CustomFields;

/// <summary>The id of a record that holds a value matching a question (the answer of the
/// <c>customfields.match_*</c> functions).</summary>
public sealed class CustomFieldMatch
{
    public Guid RecordId { get; set; }
}

/// <summary>
/// The questions a list asks of custom field values, as the SQL functions the custom fields module
/// publishes in its schema (<c>customfields.match_text</c> and the others). A module whose records
/// have custom fields calls <see cref="UseCustomFields"/> in its model: its list queries can then
/// say <c>record.Id IN (customfields.match_text(field, 'eq', value))</c> without mapping or reading
/// the module's tables. Each function runs as the caller, so row-level security keeps the answer
/// inside the caller's workspace; the planner turns the <c>IN</c> into a semi-join over the value
/// indexes (docs/decisions/p09-custom-fields-storage-and-filtering.md).
/// </summary>
public static class CustomFieldSql
{
    public const string Schema = "customfields";

    /// <summary>Text and select values. Operators: eq (ignoring case), contains, startswith, endswith.</summary>
    public static IQueryable<CustomFieldMatch> MatchText(Guid field, string op, string? value) => throw new NotSupportedException();

    /// <summary>Number values. Operators: eq, lt, le, gt, ge.</summary>
    public static IQueryable<CustomFieldMatch> MatchNumber(Guid field, string op, decimal? value) => throw new NotSupportedException();

    /// <summary>Date values. Operators: eq, lt, le, gt, ge.</summary>
    public static IQueryable<CustomFieldMatch> MatchDate(Guid field, string op, DateOnly? value) => throw new NotSupportedException();

    /// <summary>Yes/no values equal to the value.</summary>
    public static IQueryable<CustomFieldMatch> MatchBool(Guid field, bool value) => throw new NotSupportedException();

    /// <summary>Lookup values pointing at the record.</summary>
    public static IQueryable<CustomFieldMatch> MatchRef(Guid field, Guid value) => throw new NotSupportedException();

    /// <summary>Records that hold any value of the field.</summary>
    public static IQueryable<CustomFieldMatch> MatchAny(Guid field) => throw new NotSupportedException();

    /// <summary>Records whose text value in any of the fields matches the LIKE pattern (case-insensitive, backslash escapes).</summary>
    public static IQueryable<CustomFieldMatch> SearchText(Guid[] fields, string pattern) => throw new NotSupportedException();

    public static MethodInfo MethodOf(string name) => typeof(CustomFieldSql).GetMethod(name)!;

    /// <summary>Teach a module's model the functions above (call it from the module's model
    /// configuration). Adds no table and no migration.</summary>
    public static ModelBuilder UseCustomFields(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomFieldMatch>(e =>
        {
            e.HasNoKey();
            e.ToTable((string?)null);
            e.Property(x => x.RecordId).HasColumnName("record_id");
        });
        foreach (var (method, name) in new[]
        {
            (nameof(MatchText), "match_text"), (nameof(MatchNumber), "match_number"), (nameof(MatchDate), "match_date"),
            (nameof(MatchBool), "match_bool"), (nameof(MatchRef), "match_ref"), (nameof(MatchAny), "match_any"), (nameof(SearchText), "search_text"),
        })
        {
            modelBuilder.HasDbFunction(MethodOf(method)).HasSchema(Schema).HasName(name);
        }
        return modelBuilder;
    }
}
