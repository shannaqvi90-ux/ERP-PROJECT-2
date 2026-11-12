using Erp.Kernel.CustomFields;
using Erp.Kernel.Lists;
using Erp.Modules.CustomFields.Contracts;

namespace Erp.Modules.CustomFields;

/// <summary>
/// The workspace's custom field definitions. A workspace defines a few dozen at most (40 per kind of
/// record), so the endpoint loads them whole and the list is searched, filtered and sorted in memory.
/// </summary>
internal static class FieldsList
{
    public const string Key = CustomFieldsLists.Fields;

    public static ListBinding<FieldRow> Create(IReadOnlyList<CustomFieldRecordType> recordTypes) =>
        ListBinding<FieldRow>.For(new ListDefinition(
                Key, "customfields.fields.title", CustomFieldsPermissions.FieldsRead, "/api/customfields/fields",
                [
                    new ListColumn("labelEn", "customfields.field.name", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "labelAr", Width: ListColumnWidth.Wide),
                    new ListColumn("labelAr", "customfields.field.labelAr", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true, Width: ListColumnWidth.Wide),
                    new ListColumn("recordType", "customfields.field.recordType", ListColumnType.Choice, Sortable: true, Filterable: true, Groupable: true,
                        Choices: recordTypes.Select(t => new ListChoice(t.Table, t.LabelKey)).ToList()),
                    new ListColumn("fieldType", "customfields.field.type", ListColumnType.Choice, Sortable: true, Filterable: true, Groupable: true,
                        Choices: CustomFieldTypes.All.Select(t => new ListChoice(t, $"customfields.type.{t}")).ToList()),
                    new ListColumn("required", "customfields.field.required", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "customfields.required.true", FalseLabelKey: "customfields.required.false"),
                    new ListColumn("key", "customfields.field.key", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("searchable", "customfields.field.searchable", ListColumnType.Boolean, Sortable: true, Filterable: true, Hidden: true,
                        TrueLabelKey: "customfields.searchable.true", FalseLabelKey: "customfields.searchable.false"),
                    new ListColumn("sortOrder", "customfields.field.sortOrder", ListColumnType.Number, Sortable: true, Filterable: true, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("isActive", "customfields.field.status", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "customfields.status.active", FalseLabelKey: "customfields.status.inactive"),
                    new ListColumn("updatedAt", "customfields.field.updatedAt", ListColumnType.DateTime, Sortable: true, Hidden: true),
                ],
                SearchFields: ["labelEn", "labelAr", "key"],
                ArabicSearchFields: ["labelAr"],
                DefaultSort: "recordType,sortOrder,labelEn",
                Presets:
                [
                    new ListPreset("active", "customfields.view.active", Filter: "isActive eq true"),
                    new ListPreset("required", "customfields.view.required", Filter: "required eq true and isActive eq true"),
                    new ListPreset("inactive", "customfields.view.inactive", Filter: "isActive eq false"),
                ]),
                r => r.Id)
            .Column("labelEn", r => r.LabelEn)
            .Column("labelAr", r => r.LabelAr)
            .Column("recordType", r => r.RecordType)
            .Column("fieldType", r => r.FieldType)
            .Column("required", r => r.Required)
            .Column("key", r => r.Key)
            .Column("searchable", r => r.Searchable)
            .Column("sortOrder", r => r.SortOrder)
            .Column("isActive", r => r.IsActive)
            .Column("updatedAt", r => r.UpdatedAt)
            .InMemory("A workspace defines a few dozen custom fields at most (40 per kind of record): the endpoint loads them whole and the list is searched, filtered and sorted in memory.");
}
