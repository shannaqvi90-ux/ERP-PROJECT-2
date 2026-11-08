namespace Erp.Modules.CustomFields.Contracts;

/// <summary>The permissions of custom fields. Defining fields changes every form, list, filter and
/// API answer of a kind of record for the whole workspace, so it is its own reviewed permission;
/// the values a record holds follow the record's own permissions (they travel with the record).</summary>
public static class CustomFieldsPermissions
{
    public const string FieldsRead = "customfields.fields.read";
    public const string FieldsManage = "customfields.fields.manage";

    public static readonly IReadOnlyList<string> All = [FieldsRead, FieldsManage];
}

/// <summary>Keys of the lists the custom fields module registers.</summary>
public static class CustomFieldsLists
{
    /// <summary>The definitions of the workspace's custom fields.</summary>
    public const string Fields = "customfields.fields";
}
