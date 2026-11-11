using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Kernel.Attachments;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;

namespace Erp.Modules.Attachments;

/// <summary>
/// Tenant-isolation attack on attachments (a file surface), run for every kind of record that has
/// files. Signed in to tenant A, it uploads a file of its own, then tries to list, read and delete
/// tenant B's files: every tenant B identifier as the record and as the file, B's identifiers under
/// A's own record, with tenant switch headers, and with names and keys that try to leave the tenant's
/// folder (folders in a name, parent-folder segments, encoded separators, another tenant's key). Every
/// byte of every answer goes back to the gate, which looks for tenant B's identifiers and canaries.
/// Tenant B's own files (its seeded ones, and one it uploads right before) must still be listed, readable
/// and unchanged afterwards, and A's own file must still be readable (an attack that cannot reach even
/// its own file shows nothing).
/// </summary>
internal sealed class AttachmentsIsolationProbe(ModuleCatalog catalog) : IIsolationProbe
{
    /// <summary>Most victim identifiers tried as record and as file (the gate's tenant holds hundreds; its real files are always tried).</summary>
    private const int MaxVictimIds = 120;

    private static readonly ImmutableArray<string> TraversalNames =
    [
        "../../../etc/passwd.txt", "..\\..\\..\\windows\\win.txt", "/etc/shadow.txt", "C:\\Windows\\system.txt", "....//....//x.txt",
        "%2e%2e%2f%2e%2e%2fx.txt", "..%2f..%2fx.txt", "a/../../b.txt", "x.txt\0.png", ".htaccess.txt", "con.txt",
    ];

    public SurfaceKind Kind => SurfaceKind.File;

    public string Name => "attachments files";

    public Task<IsolationProbeResult> RunAsync(IsolationProbeContext context, CancellationToken cancellationToken) =>
        RunAsync(catalog.AttachmentRecordTypes, context, cancellationToken);

    /// <summary>The attack over the given kinds of record (the self-tests run it against a planted server).</summary>
    internal static async Task<IsolationProbeResult> RunAsync(IEnumerable<AttachmentRecordType> types, IsolationProbeContext context, CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        var failures = new List<string>();
        var attempts = 0;

        async Task<(HttpStatusCode Status, byte[] Body)> Send(HttpClient client, HttpMethod method, string path, byte[]? body = null, IDictionary<string, string>? headers = null, bool record = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method != HttpMethod.Get)
            {
                request.Headers.TryAddWithoutValidation("X-Erp-Request", "1");
            }
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new("application/octet-stream");
            }
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (record)
                {
                    attempts++;
                    observed.Add($"{(int)response.StatusCode} {Encoding.UTF8.GetString(bytes)} {Encoding.Latin1.GetString(bytes)}");
                    observed.Add(string.Join(' ', response.Headers.Select(h => $"{h.Key}: {string.Join(',', h.Value)}")) + " " +
                                 string.Join(' ', response.Content.Headers.Select(h => $"{h.Key}: {string.Join(',', h.Value)}")));
                }
                return (response.StatusCode, bytes);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("null characters", StringComparison.Ordinal))
            {
                // A path with a NUL byte never reaches the application: the in-process server refuses to build the
                // request (a real server answers 400). It reached nothing, so it is neither a leak nor a failure.
                attempts++;
                return (HttpStatusCode.BadRequest, []);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                failures.Add($"{method} {path}: {exception.Message}");
                return (HttpStatusCode.RequestTimeout, []);
            }
        }

        var victimIds = context.VictimIds.Append(context.VictimTenantId).Distinct().Take(MaxVictimIds).ToList();
        foreach (var type in types.Where(t => t.ListPath is not null))
        {
            var root = $"/api/attachments/{type.Table}";

            // The records and files that exist before the attack: the attacker's own, and the victim's (its seeded ones: the probe
            // writes nothing as the victim, so tenant B's data is the same before and after).
            var ownRecord = await FirstRecordAsync(context.Attacker, type.ListPath!, cancellationToken);
            if (ownRecord is null)
            {
                failures.Add($"{type.Table}: the attacker has no record to attach a file to (nothing to compare the attack with)");
                continue;
            }
            var own = await Send(context.Attacker, HttpMethod.Post, $"{root}/{ownRecord}/files", "attacker's own file"u8.ToArray(), Named("attacker-own.txt"));
            var ownFile = own.Status == HttpStatusCode.Created ? ReadId(own.Body) : null;
            if (ownFile is null)
            {
                failures.Add($"{type.Table}: the attacker could not attach a file of its own ({(int)own.Status})");
                continue;
            }

            var realIds = new List<Guid>();
            Guid? victimRecord = null;
            Guid? victimFile = null;
            byte[]? victimBytes = null;
            if (context.Victim is not null)
            {
                // The victim's seeded files sit on some of its records: the first one of its list's first page that has any.
                foreach (var theirRecord in await RecordsAsync(context.Victim, type.ListPath!, cancellationToken))
                {
                    var theirs = await Send(context.Victim, HttpMethod.Get, $"{root}/{theirRecord}/files", record: false);
                    var theirFiles = ReadItemIds(theirs.Body).ToList();
                    realIds.Add(theirRecord);
                    if (theirFiles.Count == 0)
                    {
                        continue;
                    }
                    realIds.AddRange(theirFiles);
                    victimRecord = theirRecord;
                    victimFile = theirFiles[0];
                    victimBytes = (await Send(context.Victim, HttpMethod.Get, $"{root}/files/{theirFiles[0]}/content", record: false)).Body;
                    break;
                }
                if (victimFile is null)
                {
                    failures.Add($"{type.Table}: the victim has no file to defend (its seeded files are missing)");
                }
            }

            // 1. Every victim identifier as the record and as the file.
            var tenantHeaders = new Dictionary<string, string> { ["X-Tenant-Id"] = context.VictimTenantId.ToString() };
            foreach (var victim in realIds.Concat(victimIds).Distinct())
            {
                await Send(context.Attacker, HttpMethod.Get, $"{root}/{victim}/files");
                await Send(context.Attacker, HttpMethod.Get, $"{root}/{victim}/files", headers: tenantHeaders);
                await Send(context.Attacker, HttpMethod.Post, $"{root}/{victim}/files", "planted by tenant A"u8.ToArray(), Named("planted.txt"));
                await Send(context.Attacker, HttpMethod.Get, $"{root}/files/{victim}/content");
                await Send(context.Attacker, HttpMethod.Get, $"{root}/files/{victim}/content?inline=true");
                await Send(context.Attacker, HttpMethod.Get, $"{root}/files/{victim}/content", headers: new Dictionary<string, string>(tenantHeaders) { ["X-Company-Id"] = victim.ToString() });
                await Send(context.Attacker, HttpMethod.Delete, $"{root}/files/{victim}");
            }

            // 2. Names and routes that try to leave the tenant's folder or name another tenant's file.
            foreach (var name in TraversalNames)
            {
                var sent = await Send(context.Attacker, HttpMethod.Post, $"{root}/{ownRecord}/files", "traversal"u8.ToArray(), Named(name));
                if (sent.Status == HttpStatusCode.Created && ReadName(sent.Body) is { } stored && stored.IndexOfAny(['/', '\\', '\0']) >= 0)
                {
                    failures.Add($"{type.Table}: a stored file name keeps a path separator ({stored})");
                }
            }
            foreach (var segment in new[] { "..", "%2e%2e", "..%2f", $"..%2f{context.VictimTenantId:N}", $"{context.VictimTenantId:N}", $"{context.VictimTenantId:N}%2f{ownFile:N}", "%00", ownFile.Value.ToString("N") + "/../" + context.VictimTenantId.ToString("N") })
            {
                await Send(context.Attacker, HttpMethod.Get, $"{root}/files/{segment}/content");
                await Send(context.Attacker, HttpMethod.Get, $"{root}/{segment}/files");
                await Send(context.Attacker, HttpMethod.Delete, $"{root}/files/{segment}");
            }
            foreach (var other in new[] { $"{root}/files/../../../../../../etc/passwd", $"{root}/../{type.Table}", $"/api/attachments/..%2f..%2fauth/{ownRecord}/files" })
            {
                await Send(context.Attacker, HttpMethod.Get, other);
            }

            // 3. What must still hold: the attacker reaches its own file, and the victim's files are untouched.
            var again = await Send(context.Attacker, HttpMethod.Get, $"{root}/files/{ownFile}/content", record: false);
            if (again.Status != HttpStatusCode.OK || Encoding.UTF8.GetString(again.Body) != "attacker's own file")
            {
                failures.Add($"{type.Table}: the attacker's own file could not be read back ({(int)again.Status})");
            }
            if (victimRecord is { } record2 && victimFile is { } file2 && victimBytes is not null)
            {
                var back = await Send(context.Victim!, HttpMethod.Get, $"{root}/files/{file2}/content", record: false);
                if (back.Status != HttpStatusCode.OK || !back.Body.SequenceEqual(victimBytes))
                {
                    failures.Add($"{type.Table}: the victim's file was changed or removed by the attack ({(int)back.Status})");
                }
                var listed = await Send(context.Victim!, HttpMethod.Get, $"{root}/{record2}/files", record: false);
                if (Encoding.UTF8.GetString(listed.Body).Contains("planted.txt", StringComparison.Ordinal))
                {
                    failures.Add($"{type.Table}: the attacker attached a file to the victim's record");
                }
            }
            await Send(context.Attacker, HttpMethod.Delete, $"{root}/files/{ownFile}", record: false);
        }
        return new IsolationProbeResult(attempts, observed) { Failures = failures };
    }

    /// <summary>The header an upload names its file in.</summary>
    private static Dictionary<string, string> Named(string name) => new() { [AttachmentEndpoints.NameHeader] = Uri.EscapeDataString(name) };

    private static async Task<Guid?> FirstRecordAsync(HttpClient client, string listPath, CancellationToken cancellationToken) =>
        (await RecordsAsync(client, listPath, cancellationToken)).Cast<Guid?>().FirstOrDefault();

    /// <summary>The ids of the records on the first page of the kind's list.</summary>
    private static async Task<List<Guid>> RecordsAsync(HttpClient client, string listPath, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(listPath, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Select(i => i.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid) ? guid : Guid.Empty).Where(g => g != Guid.Empty)]
            : [];
    }

    private static Guid? ReadId(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid) ? guid : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<Guid> ReadItemIds(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? [.. items.EnumerateArray().Select(i => i.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid) ? guid : Guid.Empty).Where(g => g != Guid.Empty)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadName(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
