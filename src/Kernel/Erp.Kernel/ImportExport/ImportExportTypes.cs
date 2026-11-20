namespace Erp.Kernel.ImportExport;

/// <summary>One column a kind of record can be imported into (p14).</summary>
/// <param name="Key">The field's name in the importer's rows (lower camel case).</param>
/// <param name="LabelKey">Server string key of the field's name, in English and Arabic.</param>
/// <param name="Required">True when a row without a value for it is refused.</param>
/// <param name="Example">A sample value shown on the template and next to the field.</param>
/// <param name="Aliases">Other names a file's column header may carry for this field (compared after
/// lower-casing and dropping everything but letters and digits), so a plain header such as "Street" finds its field.</param>
public sealed record ImportField(string Key, string LabelKey, bool Required = false, string? Example = null, IReadOnlyList<string>? Aliases = null);

/// <summary>One row of the file, as text. <paramref name="Number"/> is the row as a spreadsheet numbers it (the header is row 1).</summary>
public sealed record ImportRow(int Number, IReadOnlyDictionary<string, string?> Values);

/// <summary>A problem found in one row, in both languages (a background job has no reader's language).</summary>
public sealed record ImportIssue(int Row, string Field, string Code, string MessageEn, string MessageAr);

/// <summary>A batch of rows handed to a kind of record's importer.</summary>
/// <param name="DryRun">True: check only, write nothing.</param>
/// <param name="State">Kept by the engine for the whole run, across batches (so a reference used twice in one file is found).</param>
public sealed record ImportBatch(IReadOnlyList<ImportRow> Rows, bool DryRun, IDictionary<string, object?> State);

/// <summary>
/// A kind of record that can be imported from a file (p14). A module registers its own with
/// <c>ModuleBuilder.ImportRecord</c>; the import module then serves the upload, column mapping, check,
/// import and error file at <c>/api/importexport/{Table}/imports</c>, guarded by <see cref="Permission"/>
/// (the permission that creates such records). The module's importer validates every row with the
/// record's own rules and, unless the batch is a dry run, writes the valid rows in the caller's unit of work.
/// </summary>
/// <param name="Table"><c>schema.table</c> of the records (the registering module's schema); also the route segment.</param>
/// <param name="LabelKey">Server string key of what the records are called ("Contacts").</param>
/// <param name="Permission">The permission that creates such records: it starts and runs an import.</param>
/// <param name="Fields">The columns a file can fill, in the order a template shows them.</param>
/// <param name="Process">Checks the batch's rows and, when it is not a dry run, writes the valid ones (all or none of the
/// batch's valid rows; the engine never passes a row that failed an earlier check). Returns the problems found.</param>
/// <param name="CustomFieldRecord">The kind of record custom fields are added to (<c>CustomFieldRecordType.Table</c>), or null:
/// the tenant's custom fields of that kind are offered as fields named <c>custom.&lt;key&gt;</c>.</param>
/// <param name="ListPath">The API path of the records' list (the screen links the finished import to it).</param>
/// <param name="OneOfRequired">Fields of which at least one must be mapped (a contact needs a name in English or in Arabic); null: none.</param>
public sealed record ImportRecordType(
    string Table,
    string LabelKey,
    string Permission,
    IReadOnlyList<ImportField> Fields,
    Func<IServiceProvider, ImportBatch, CancellationToken, Task<IReadOnlyList<ImportIssue>>> Process,
    string? CustomFieldRecord = null,
    string? ListPath = null,
    IReadOnlyList<string>? OneOfRequired = null);

/// <summary>One record of a full tenant export: its id and its cells in the order of the type's columns.</summary>
public sealed record ExportRecord(Guid Id, IReadOnlyList<object?> Cells);

/// <summary>
/// A kind of record a full tenant export writes (p14): every row of the workspace, as CSV or JSON, in the
/// open, documented column names of <see cref="Columns"/>. A module registers its own with <c>ModuleBuilder.ExportRecord</c>.
/// </summary>
/// <param name="Table"><c>schema.table</c> of the records (the registering module's schema); also the file's name in the export.</param>
/// <param name="LabelKey">Server string key of what the records are called.</param>
/// <param name="Columns">The column names, lower camel case, in the order of each record's cells.</param>
/// <param name="Rows">Every record of the workspace in id order, read in the job's unit of work (row-level security limits it to the workspace).</param>
/// <param name="CustomFieldRecord">The kind of custom fields to add as extra columns (<c>custom.&lt;key&gt;</c>), or null.</param>
public sealed record ExportRecordType(
    string Table,
    string LabelKey,
    IReadOnlyList<string> Columns,
    Func<IServiceProvider, CancellationToken, IAsyncEnumerable<ExportRecord>> Rows,
    string? CustomFieldRecord = null);
