namespace Erp.Modules.Audit.Contracts;

public static class AuditPermissions
{
    /// <summary>Open the audit log: every change to the records the caller may read (each kind of
    /// record also needs its own read permission, in the record's company, as the history below).</summary>
    public const string LogRead = "audit.log.read";

    /// <summary>See a record's history on its form (p07: a history panel on every form). A form
    /// is the record's own screen, so the history shows what the reader may read of the record:
    /// the record's read permission in the record's company and branch is checked as well, which
    /// the log needs too. A user who reads a record sees its history without being given the whole
    /// log; every role that reads records is given this permission with them.</summary>
    public const string HistoryRead = "audit.history.read";

    public static readonly IReadOnlyList<string> All = [LogRead, HistoryRead];
}

/// <summary>Keys of the audit module's registered lists.</summary>
public static class AuditLists
{
    /// <summary>The audit log: one row per change of a business record.</summary>
    public const string Log = "audit.log";
}
