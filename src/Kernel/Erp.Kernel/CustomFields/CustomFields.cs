using System.Text.Json;
using Erp.Kernel.Http;

namespace Erp.Kernel.CustomFields;

/// <summary>The kinds of value a tenant-defined field holds (p09).</summary>
public static class CustomFieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";
    public const string Select = "select";
    public const string Lookup = "lookup";

    public static readonly IReadOnlyList<string> All = [Text, Number, Date, Boolean, Select, Lookup];
}

/// <summary>One allowed value of a single-select field, with its label in both languages.</summary>
public sealed record CustomFieldOption(string Value, string LabelEn, string LabelAr);

/// <summary>A tenant-defined field as the platform reads it: what the forms, the list columns and
/// the filters are built from.</summary>
/// <param name="Key">The field's machine name (lower camel case, starts with <c>cf</c>): the column
/// key in lists and filters and the property name in the API.</param>
/// <param name="Default">The default as canonical text (a number with a dot, a date as yyyy-mm-dd,
/// true or false, an option value, a record id), or null.</param>
/// <param name="Decimals">For a number: how many decimals it keeps (0 to 8).</param>
/// <param name="LookupTarget">For a lookup: the record type it points at (<c>schema.table</c>).</param>
public sealed record CustomFieldInfo(
    Guid Id,
    string RecordType,
    string Key,
    string LabelEn,
    string LabelAr,
    string Type,
    bool Required,
    string? Default,
    int Decimals,
    IReadOnlyList<CustomFieldOption> Options,
    string? LookupTarget,
    int SortOrder,
    bool Searchable)
{
    /// <summary>Web string key of the field's label (the label is the tenant's own text, handed to
    /// the screen with the list definition; see <c>registerDynamicStrings</c> on the web).</summary>
    public string LabelKey => LabelKeyOf(Id);

    public string OptionKey(string value) => $"customField.{Id:N}.option.{value}";

    public static string LabelKeyOf(Guid id) => $"customField.{id:N}.label";
}

/// <summary>A field of a record as its form shows it: the field, the record's value (or the default
/// for a new record) and, for a lookup, the name of the record it points at.</summary>
public sealed record CustomFieldEntry(
    string Key,
    string Type,
    string LabelEn,
    string LabelAr,
    bool Required,
    int Decimals,
    IReadOnlyList<CustomFieldOption> Options,
    string? LookupTarget,
    string? LookupEndpoint,
    string? LookupLabelProperty,
    string? LookupLabelPropertyAr,
    JsonElement? Value,
    string? ValueLabel,
    string? ValueLabelAr,
    bool Searchable);

/// <summary>
/// A kind of record custom fields can be added to (a module registers its own with
/// <c>ModuleBuilder.CustomFieldRecord</c>): the table, what one record is called, the list that
/// shows the records and who may read them. Values follow the record's own permissions: they travel
/// with the record's endpoints, which already declare them.
/// </summary>
/// <param name="Table"><c>schema.table</c> of the records (the registering module's schema).</param>
/// <param name="LabelKey">Web string key of what the records are called ("Contacts"), in the module's own strings.</param>
/// <param name="ReadPermission">The permission that reads such records.</param>
/// <param name="ListKey">The registered list that shows the records: its definition gets the fields as columns.</param>
/// <param name="Exists">Whether a record of the caller's workspace has this id (row-level security
/// limits it): a value is never saved for another workspace's record or for none.</param>
/// <param name="Names">The names (English, Arabic) of records by id, for a lookup that points at
/// this kind of record; null when the kind cannot be pointed at.</param>
/// <param name="LookupEndpoint">The list endpoint a lookup picks records from.</param>
/// <param name="LabelProperty">The row property naming a record in English.</param>
/// <param name="LabelPropertyAr">The row property naming a record in Arabic.</param>
/// <param name="AllIds">The ids of every record of the workspace (seeding the demo's values; the
/// import and export pieces read it too); null when the kind offers none.</param>
public sealed record CustomFieldRecordType(
    string Table,
    string LabelKey,
    string ReadPermission,
    string ListKey,
    Func<IServiceProvider, Guid, CancellationToken, Task<bool>> Exists,
    Func<IServiceProvider, IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, (string En, string Ar)>>>? Names = null,
    string? LookupEndpoint = null,
    string? LabelProperty = null,
    string? LabelPropertyAr = null,
    Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<Guid>>>? AllIds = null);

/// <summary>
/// The custom fields of the workspace, for the modules that own records. Implemented by the custom
/// fields module and found through the container: a host without that module has no custom fields,
/// and the records behave as they always did.
/// </summary>
public interface ICustomFields
{
    /// <summary>The active fields of a kind of record, in display order.</summary>
    Task<IReadOnlyList<CustomFieldInfo>> FieldsAsync(string recordType, CancellationToken cancellationToken);

    /// <summary>The fields as a form shows them: with the record's values, or with the defaults for
    /// a record not yet saved (<paramref name="recordId"/> null).</summary>
    Task<IReadOnlyList<CustomFieldEntry>> EntriesAsync(string recordType, Guid? recordId, CancellationToken cancellationToken);

    /// <summary>Check the values sent with a record's save and, when they are valid, write them in
    /// the record's own transaction. <paramref name="values"/> maps a field's key to its value (a
    /// JSON string, number, boolean or null to clear it); a key left out keeps its value (on a new
    /// record, the default). Problems are added to <paramref name="validator"/> under
    /// <c>customFields.&lt;key&gt;</c>; nothing is written while the validator has any.</summary>
    Task SaveAsync(string recordType, Guid recordId, bool isNew, IReadOnlyDictionary<string, JsonElement>? values, Validator validator, CancellationToken cancellationToken);

    /// <summary>The values of many records for list pages, as plain values by field key (null for a
    /// record that holds none).</summary>
    Task<IReadOnlyDictionary<Guid, IDictionary<string, object?>>> ValuesAsync(string recordType, IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken);
}

public static class CustomFieldValues
{
    /// <summary>A record's values by field key, as a request carries them back (<c>customFields</c>
    /// of a save): only fields that hold a value.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Map(IEnumerable<CustomFieldEntry> entries) =>
        entries.Where(e => e.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) }).ToDictionary(e => e.Key, e => e.Value!.Value, StringComparer.Ordinal);
}
