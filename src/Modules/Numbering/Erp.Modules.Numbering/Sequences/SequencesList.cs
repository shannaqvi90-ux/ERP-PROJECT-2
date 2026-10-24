using Erp.Kernel.Lists;
using Erp.Modules.Numbering.Contracts;

namespace Erp.Modules.Numbering.Sequences;

/// <summary>
/// Document numbering: one row per company and kind of document, the working company's first, with
/// the number its next document would get today. A workspace holds a sequence per document type
/// per company (eleven types; 44 rows in the four-company demo workspace): the endpoint loads them
/// whole with today's counters (two queries) and the list is searched, filtered and sorted in memory.
/// </summary>
internal static class SequencesList
{
    public const string Key = "numbering.sequences";

    public static ListBinding<SequenceRow> Create() =>
        ListBinding<SequenceRow>.For(new ListDefinition(
                Key, "numbering.sequences.title", NumberingPermissions.SequencesRead, "/api/numbering/sequences",
                [
                    new ListColumn("nameEn", "numbering.sequence.documentType", ListColumnType.Text, Sortable: true, Filterable: true, ArabicField: "nameAr", Width: ListColumnWidth.Wide),
                    new ListColumn("nameAr", "numbering.sequence.documentTypeAr", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("companyId", "numbering.sequence.company", ListColumnType.Reference, Filterable: true, Groupable: true, LabelField: "companyCode", Width: ListColumnWidth.Narrow),
                    new ListColumn("companyCode", "numbering.sequence.companyCode", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("nextNumber", "numbering.sequence.nextNumber", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("prefix", "numbering.sequence.prefix", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("padding", "numbering.sequence.padding", ListColumnType.Number, Sortable: true, Filterable: true, Width: ListColumnWidth.Narrow),
                    new ListColumn("suffix", "numbering.sequence.suffix", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("reset", "numbering.sequence.reset", ListColumnType.Choice, Sortable: true, Filterable: true, Groupable: true,
                        Choices:
                        [
                            new ListChoice(NumberReset.Yearly, "numbering.reset.yearly"),
                            new ListChoice(NumberReset.Monthly, "numbering.reset.monthly"),
                            new ListChoice(NumberReset.Never, "numbering.reset.never"),
                        ]),
                    new ListColumn("gapless", "numbering.sequence.gapless", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "numbering.gapless.true", FalseLabelKey: "numbering.gapless.false"),
                    new ListColumn("perBranch", "numbering.sequence.perBranch", ListColumnType.Boolean, Sortable: true, Filterable: true, Groupable: true,
                        TrueLabelKey: "numbering.perBranch.true", FalseLabelKey: "numbering.perBranch.false"),
                    new ListColumn("documentType", "numbering.sequence.documentTypeKey", ListColumnType.Text, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("nextValue", "numbering.sequence.nextValue", ListColumnType.Number, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("workingCompany", "numbering.sequence.workingCompany", ListColumnType.Boolean, Sortable: true, Filterable: true, Hidden: true),
                    new ListColumn("updatedAt", "numbering.sequence.updatedAt", ListColumnType.DateTime, Sortable: true, Hidden: true),
                ],
                SearchFields: ["nameEn", "nameAr", "companyCode", "prefix", "suffix", "nextNumber", "documentType"],
                DefaultSort: "-workingCompany,companyCode,documentType",
                Presets:
                [
                    new ListPreset("gapless", "numbering.view.gapless", Filter: "gapless eq true"),
                    new ListPreset("perBranch", "numbering.view.perBranch", Filter: "perBranch eq true"),
                ],
                ArabicSearchFields: ["nameAr"]),
                r => r.Id)
            .Column("nameEn", r => r.NameEn)
            .Column("nameAr", r => r.NameAr)
            .Column("companyId", r => r.CompanyId)
            .Column("companyCode", r => r.CompanyCode)
            .Column("nextNumber", r => r.NextNumber)
            .Column("prefix", r => r.Prefix)
            .Column("padding", r => r.Padding)
            .Column("suffix", r => r.Suffix)
            .Column("reset", r => r.Reset)
            .Column("gapless", r => r.Gapless)
            .Column("perBranch", r => r.PerBranch)
            .Column("documentType", r => r.DocumentType)
            .Column("nextValue", r => r.NextValue)
            .Column("workingCompany", r => r.WorkingCompany)
            .Column("updatedAt", r => r.UpdatedAt)
            .InMemory("A workspace holds one sequence per document type per company (eleven types; 44 rows in the four-company demo): the endpoint loads them whole with today's counters and the list is searched, filtered and sorted in memory.");
}
