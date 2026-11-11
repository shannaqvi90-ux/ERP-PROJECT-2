using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Erp.Modules.Attachments.Contracts;
using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Attachments;

/// <summary>
/// Keeps attachment bytes on a local volume (the demo and ./erp verify; setting <c>Erp:Attachments:Root</c>,
/// by default a folder beside the application). One folder per tenant, one file per attachment, the file
/// named by a random identifier the store makes: <c>{root}/{tenant}/{id}</c>. A path is built only from a
/// tenant identifier the session supplied and an identifier the store itself made (parsed back from the
/// stored key as exactly 32 hex digits), so no client name, extension or path reaches the file system.
/// A key made for one tenant opens nothing for another (docs/decisions/p11-attachments-storage.md).
/// </summary>
internal sealed partial class LocalAttachmentStore : IAttachmentStore
{
    private const int Buffer = 81920;
    private readonly string _root;

    public LocalAttachmentStore(IConfiguration configuration)
    {
        var configured = configuration["Erp:Attachments:Root"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "attachments-data") : configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredBlob> SaveAsync(Guid tenantId, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var folder = FolderOf(tenantId);
        var id = Guid.NewGuid().ToString("N");
        var final = Path.Combine(folder, id);
        var partial = final + ".part";
        Directory.CreateDirectory(folder);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var head = new byte[(int)Math.Min(FileTypes.SampleBytes, maxBytes + 1)];
            var headLength = 0;
            long total = 0;
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, Buffer, FileOptions.Asynchronous))
            {
                var buffer = new byte[Buffer];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new BlobTooLargeException(maxBytes);
                    }
                    if (headLength < head.Length)
                    {
                        var take = Math.Min(read, head.Length - headLength);
                        Array.Copy(buffer, 0, head, headLength, take);
                        headLength += take;
                    }
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await file.FlushAsync(cancellationToken);
            }
            File.Move(partial, final);
            return new StoredBlob($"{tenantId:N}/{id}", total, Convert.ToHexStringLower(hash.GetHashAndReset()), head[..headLength]);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    public Task<Stream?> OpenAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var path = PathOf(tenantId, key);
        Stream? stream = path is not null && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Buffer, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        if (PathOf(tenantId, key) is { } path)
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string FolderOf(Guid tenantId) => Path.Combine(_root, tenantId.ToString("N"));

    /// <summary>The file of a key made for this tenant, or null for any other key.</summary>
    private string? PathOf(Guid tenantId, string key)
    {
        var match = KeyPattern().Match(key);
        return match.Success && match.Groups[1].Value == tenantId.ToString("N") ? Path.Combine(FolderOf(tenantId), match.Groups[2].Value) : null;
    }

    [GeneratedRegex("^([0-9a-f]{32})/([0-9a-f]{32})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
