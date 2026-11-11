namespace Erp.Kernel.Attachments;

/// <summary>
/// A kind of record files can be attached to (p11). A module registers its own with
/// <c>ModuleBuilder.AttachmentRecord</c>; the attachments module then serves the record's files at
/// <c>/api/attachments/{Table}/{id}/files</c>, guarded by the record's own permissions: reading a
/// record's files needs <see cref="ReadPermission"/>, adding and deleting need
/// <see cref="WritePermission"/> (the permission that changes the record itself).
/// </summary>
/// <param name="Table"><c>schema.table</c> of the records (the registering module's schema); also the
/// route segment and the stored record type.</param>
/// <param name="LabelKey">Web string key of what the records are called ("Contacts"), in the module's own strings.</param>
/// <param name="ReadPermission">Lists, downloads and previews a record's files.</param>
/// <param name="WritePermission">Adds and deletes a record's files.</param>
/// <param name="Exists">Whether a record of the caller's workspace has this id (row-level security
/// limits it): a file is never listed for, or added to, another workspace's record or none.</param>
/// <param name="FirstIds">The ids of the first records of the workspace in the order the kind's list shows
/// them (seeding the demo's files on the records a person sees first, and the ones the isolation probe finds on
/// the list's first page); null when the kind offers none.</param>
/// <param name="ListPath">The API path of a GET that lists the records, answering <c>items</c> with an
/// <c>id</c> each (the list contract): the isolation gate's probe finds the caller's own record there.</param>
public sealed record AttachmentRecordType(
    string Table,
    string LabelKey,
    string ReadPermission,
    string WritePermission,
    Func<IServiceProvider, Guid, CancellationToken, Task<bool>> Exists,
    Func<IServiceProvider, int, CancellationToken, Task<IReadOnlyList<Guid>>>? FirstIds = null,
    string? ListPath = null);
