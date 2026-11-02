namespace Erp.Modules.Audit.Contracts;

public static class AuditPermissions
{
    /// <summary>Open the audit log: every change to the records the caller may read (each kind of
    /// record also needs its own read permission, in the record's company). A record's own history
    /// on its form needs only the record's read permission.</summary>
    public const string LogRead = "audit.log.read";

    public static readonly IReadOnlyList<string> All = [LogRead];
}

/// <summary>Keys of the audit module's registered lists.</summary>
public static class AuditLists
{
    /// <summary>The audit log: one row per change of a business record.</summary>
    public const string Log = "audit.log";
}
