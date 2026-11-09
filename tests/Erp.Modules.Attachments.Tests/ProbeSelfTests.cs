using System.Net;
using System.Text;
using System.Text.Json;
using Erp.Kernel.Attachments;
using Erp.Kernel.Security;

namespace Erp.Modules.Attachments.Tests;

/// <summary>
/// The isolation probe must catch the faults it exists for. It runs against a small planted server
/// (an in-memory copy of the attachments API for two tenants): a faithful one, which it must clear, and
/// ones with a planted cross-tenant file read, a delete that trusts the file id alone, an upload to
/// another tenant's record, a stored name that keeps a path, and a read that works for the wrong
/// tenant's key, each of which it must report.
/// </summary>
public sealed class ProbeSelfTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private const string Canary = "CNRYBFILE77";

    [Flags]
    private enum Fault { None = 0, ReadAnyTenant = 1, DeleteAnyTenant = 2, UploadToAnyRecord = 4, KeepsPath = 8 }

    private sealed record Stored(Guid Id, Guid Tenant, Guid Record, string Name, byte[] Bytes);

    /// <summary>The attachments API of two tenants, in memory. Whoever calls is the handler's tenant.</summary>
    private sealed class World(Fault fault)
    {
        public readonly Dictionary<Guid, Guid> Records = new() { [Guid.NewGuid()] = TenantA, [Guid.NewGuid()] = TenantB };
        public readonly List<Stored> Files = [];

        public Guid RecordOf(Guid tenant) => Records.First(r => r.Value == tenant).Key;

        public HttpClient Client(Guid tenant) => new(new Handler(this, tenant, fault)) { BaseAddress = new Uri("http://planted.test") };
    }

    private sealed class Handler(World world, Guid tenant, Fault fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Answer(request));

        private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/contacts/contacts")
            {
                return Json(HttpStatusCode.OK, new { items = world.Records.Where(r => r.Value == tenant).Select(r => new { id = r.Key }) });
            }
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // api attachments contacts.contacts {record} files [{file} content]
            if (parts.Length < 5 || parts[1] != "attachments" || !Guid.TryParse(parts[3], out var record))
            {
                return Json(HttpStatusCode.NotFound, new { code = "notFound" });
            }
            var mine = world.Records.TryGetValue(record, out var owner) && owner == tenant;
            if (parts.Length == 5)
            {
                if (request.Method == HttpMethod.Get)
                {
                    return mine ? Json(HttpStatusCode.OK, new { items = world.Files.Where(f => f.Record == record && f.Tenant == tenant).Select(f => new { id = f.Id, name = f.Name }) }) : Json(HttpStatusCode.NotFound, new { code = "notFound" });
                }
                if (!mine && !fault.HasFlag(Fault.UploadToAnyRecord) || !world.Records.ContainsKey(record))
                {
                    return Json(HttpStatusCode.NotFound, new { code = "notFound" });
                }
                var raw = Uri.UnescapeDataString(request.Headers.TryGetValues("X-File-Name", out var names) ? names.First() : "");
                var name = fault.HasFlag(Fault.KeepsPath) ? raw : raw.Replace('\\', '/').Replace('\0', '_').Split('/').Last();
                var file = new Stored(Guid.NewGuid(), world.Records[record], record, name, request.Content!.ReadAsByteArrayAsync().Result);
                world.Files.Add(file);
                return Json(HttpStatusCode.Created, new { id = file.Id, name = file.Name });
            }
            if (!Guid.TryParse(parts[5], out var fileId))
            {
                return Json(HttpStatusCode.NotFound, new { code = "notFound" });
            }
            if (request.Method == HttpMethod.Delete)
            {
                var victim = world.Files.FirstOrDefault(f => f.Id == fileId && (fault.HasFlag(Fault.DeleteAnyTenant) || (f.Tenant == tenant && f.Record == record && mine)));
                if (victim is null)
                {
                    return Json(HttpStatusCode.NotFound, new { code = "notFound" });
                }
                world.Files.Remove(victim);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            var found = world.Files.FirstOrDefault(f => f.Id == fileId && (fault.HasFlag(Fault.ReadAnyTenant) || (f.Tenant == tenant && mine && f.Record == record)));
            return found is null ? Json(HttpStatusCode.NotFound, new { code = "notFound" }) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(found.Bytes) };
        }
    }

    private static readonly AttachmentRecordType Contacts = new("contacts.contacts", "contacts.menu.contacts", "contacts.contacts.read", "contacts.contacts.update", (_, _, _) => Task.FromResult(true), ListPath: "/api/contacts/contacts");

    private static async Task<(IsolationProbeResult Result, World World)> RunAsync(Fault fault)
    {
        var world = new World(fault);
        // Tenant B's seeded file, carrying its canary.
        world.Files.Add(new Stored(Guid.NewGuid(), TenantB, world.RecordOf(TenantB), $"Trade licence {Canary}.pdf", Encoding.UTF8.GetBytes($"%PDF- tenant B {Canary}")));
        var context = new IsolationProbeContext(world.Client(TenantA), TenantA, TenantB, [.. world.Records.Where(r => r.Value == TenantB).Select(r => r.Key), .. world.Files.Select(f => f.Id)], [Canary], world.Client(TenantB));
        return (await AttachmentsIsolationProbe.RunAsync([Contacts], context, TestContext.Current.CancellationToken), world);
    }

    private static bool Leaks(IsolationProbeResult result) => result.Observed.Any(o => o.Contains(Canary, StringComparison.Ordinal));

    [Fact]
    public async Task A_faithful_server_is_cleared_and_the_probe_made_its_attempts()
    {
        var (result, _) = await RunAsync(Fault.None);
        Assert.Empty(result.Failures);
        Assert.False(Leaks(result), string.Join("\n", result.Observed.Where(o => o.Contains(Canary))));
        Assert.True(result.Attempts > 50, $"{result.Attempts} attempts");
    }

    [Fact]
    public async Task A_planted_read_of_another_tenants_file_by_id_alone_is_seen()
    {
        var (result, _) = await RunAsync(Fault.ReadAnyTenant);
        Assert.True(Leaks(result));
    }

    [Fact]
    public async Task A_planted_delete_of_another_tenants_file_is_seen()
    {
        var (result, _) = await RunAsync(Fault.DeleteAnyTenant);
        Assert.Contains(result.Failures, f => f.Contains("changed or removed"));
    }

    [Fact]
    public async Task A_planted_upload_to_another_tenants_record_is_seen()
    {
        var (result, _) = await RunAsync(Fault.UploadToAnyRecord);
        Assert.Contains(result.Failures, f => f.Contains("victim's record") || f.Contains("changed or removed"));
    }

    [Fact]
    public async Task A_planted_stored_name_that_keeps_a_path_is_seen()
    {
        var (result, _) = await RunAsync(Fault.KeepsPath);
        Assert.Contains(result.Failures, f => f.Contains("path separator"));
    }

    [Fact]
    public async Task A_server_that_cannot_attach_even_the_attackers_own_file_is_not_cleared()
    {
        using var broken = new HttpClient(new Refusing()) { BaseAddress = new Uri("http://planted.test") };
        var context = new IsolationProbeContext(broken, TenantA, TenantB, [], [Canary], null);
        var result = await AttachmentsIsolationProbe.RunAsync([Contacts], context, TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Failures);
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/api/contacts/contacts"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"" + Guid.NewGuid() + "\"}]}") }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
