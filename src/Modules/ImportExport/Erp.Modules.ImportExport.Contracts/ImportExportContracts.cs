namespace Erp.Modules.ImportExport.Contracts;

/// <summary>
/// Permissions of the import and export module. Importing a kind of record needs the permission that
/// creates that kind of record (its module registers it), so this module has permissions only for the
/// full tenant export.
/// </summary>
public static class ImportExportPermissions
{
    /// <summary>See the workspace's full exports: when they were made, their state, progress and size.</summary>
    public const string ExportsRead = "importexport.exports.read";

    /// <summary>Start a full export of the workspace's data.</summary>
    public const string ExportsCreate = "importexport.exports.create";

    /// <summary>Download a finished full export. Separate from reading it: the file holds all the workspace's data.</summary>
    public const string ExportsDownload = "importexport.exports.download";

    public static readonly IReadOnlyList<string> All = [ExportsRead, ExportsCreate, ExportsDownload];
}

/// <summary>Where an import stands.</summary>
public static class ImportStatus
{
    /// <summary>The file is read and its columns are being matched to fields.</summary>
    public const string Uploaded = "uploaded";

    /// <summary>A check or an import waits for a worker or is running.</summary>
    public const string Running = "running";

    /// <summary>A check ran: rows are counted as valid or not; nothing was written.</summary>
    public const string Checked = "checked";

    /// <summary>The rows were written.</summary>
    public const string Completed = "completed";

    /// <summary>Nothing was written: some rows have errors and the run was set to refuse them, or the file could not be processed.</summary>
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Uploaded, Running, Checked, Completed, Failed];
}

/// <summary>What the import does with rows that have errors.</summary>
public static class ImportOnError
{
    /// <summary>Write nothing when any row has an error.</summary>
    public const string Reject = "reject";

    /// <summary>Write the valid rows and leave the others in the error file.</summary>
    public const string Skip = "skip";

    public static readonly IReadOnlyList<string> All = [Reject, Skip];
}

/// <summary>Limits an imported file keeps.</summary>
public static class ImportLimits
{
    public const int MaxFileBytes = 25 * 1024 * 1024;
    public const int MaxRows = 50_000;
    public const int MaxColumns = 80;
    public const int MaxCellLength = 4000;
    public const int MaxFileNameLength = 150;

    /// <summary>The most error lines the screen shows (the error file holds all of them).</summary>
    public const int MaxShownIssues = 200;

    /// <summary>The largest full export the workspace's file can hold, in bytes.</summary>
    public const long MaxExportBytes = 256L * 1024 * 1024;
}

/// <summary>Where a full export stands.</summary>
public static class ExportStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Expired = "expired";

    public static readonly IReadOnlyList<string> All = [Queued, Running, Succeeded, Failed, Expired];
}

/// <summary>A started full export.</summary>
public sealed record TenantExportTicket(Guid Id, string Status);

/// <summary>
/// Starts a full export of the current workspace's data. The export screen calls it through the API; the
/// platform operator's tenant closing (p17) calls it through this contract while a workspace is being closed.
/// The caller has checked who may ask: this does not look at permissions.
/// </summary>
public interface ITenantExports
{
    /// <summary>Queue the export of everything the workspace holds. The job runs in the workspace, in the background.</summary>
    Task<TenantExportTicket> StartAsync(string format, CancellationToken cancellationToken);
}
