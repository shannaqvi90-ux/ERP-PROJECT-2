namespace Erp.Modules.Attachments.Contracts;

/// <summary>What the server accepts as an attachment. Checked on the server, whatever the browser
/// says: the size while the file streams in, the type from the file's own bytes.</summary>
public static class AttachmentLimits
{
    /// <summary>Largest file, in bytes (20 MB: inside the web server's default body limit).</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>Most files one record may hold.</summary>
    public const int MaxFilesPerRecord = 250;

    /// <summary>Longest file name kept (a longer one is shortened, its extension kept).</summary>
    public const int MaxNameLength = 150;
}

/// <summary>What the store keeps of one file it saved.</summary>
/// <param name="Key">The store's own name for the file (made by the store, never from client input).</param>
/// <param name="Size">Bytes stored.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the bytes.</param>
/// <param name="Head">The first bytes (up to 8 KB), which the caller checks the file's type from.</param>
public sealed record StoredBlob(string Key, long Size, string Sha256, byte[] Head);

/// <summary>Raised by a store when a file passes the size limit while it streams in.</summary>
public sealed class BlobTooLargeException(long limit) : Exception($"The file is larger than {limit} bytes.")
{
    public long Limit { get; } = limit;
}

/// <summary>
/// Where attachment bytes live. The product ships a local volume; another implementation (object
/// storage) replaces it by registering this interface. A store separates tenants itself: every
/// call names the tenant, a key made for one tenant opens nothing for another, and no name or path
/// from a client ever reaches it.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>Stream <paramref name="content"/> into a new file of the tenant, at most <paramref name="maxBytes"/>
    /// (otherwise <see cref="BlobTooLargeException"/> and nothing is kept).</summary>
    Task<StoredBlob> SaveAsync(Guid tenantId, Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>Open a saved file for reading; null when the tenant has no such file.</summary>
    Task<Stream?> OpenAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    /// <summary>Remove a file; nothing happens when the tenant has none.</summary>
    Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken);
}
