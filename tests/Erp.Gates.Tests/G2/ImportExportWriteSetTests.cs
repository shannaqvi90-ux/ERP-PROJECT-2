using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Modules.Jobs.Engine;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what contacts.contacts.create lets an import write and what importexport.exports.download lets a download write. The generic
/// write-set gate (<see cref="WriteSets"/>) cannot reach the run of an uploaded file or the download of a finished export, so both are
/// listed in tests/Gates/write-set-unexercised.txt; this gate sends them itself, as a user holding exactly the permission the endpoint
/// declares, and reads the audit trail: a check writes only the file's own record, an import writes the contacts of the file (and the
/// file's record) and nothing else, a download changes only the export's download count, moment and person; users holding any other
/// contacts or export permission are refused with nothing written.
/// </summary>
public sealed class ImportExportWriteSetTests(ImportExportWriteSetFixture fixture) : IClassFixture<ImportExportWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string Contacts = "/api/importexport/contacts.contacts";

    private static async Task<Guid> UploadAsync(HttpClient client, string csv)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Headers.Add("X-File-Name", "write-set.csv");
        using var response = await client.PostAsync($"{Contacts}/imports", content);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_check_writes_only_the_files_own_record_and_an_import_writes_only_the_contacts_of_the_file()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var writer = await UserHoldingAsync(admin, "iws.writer", ["contacts.contacts.create"]);
        var name = $"Write set import {Guid.NewGuid():N}";
        var id = await UploadAsync(writer, $"Name,Email\r\n{name} One,one@example.ae\r\n{name} Two,\r\n");

        var before = await LastAuditIdAsync();
        using (var check = await writer.PostAsJsonAsync($"{Contacts}/imports/{id}/run", new { dryRun = true }))
        {
            Assert.True(check.StatusCode == HttpStatusCode.OK, await check.Content.ReadAsStringAsync());
        }
        var checkWrites = await WritesSinceAsync(before);
        Assert.All(checkWrites, w => Assert.StartsWith("importexport.", w.Table));
        Assert.Contains(checkWrites, w => w.Table == "importexport.import_sessions" && w.Action == "update" && w.Record == id);
        Assert.DoesNotContain(checkWrites, w => w.Table == "contacts.contacts");

        before = await LastAuditIdAsync();
        using (var run = await writer.PostAsJsonAsync($"{Contacts}/imports/{id}/run", new { dryRun = false }))
        {
            Assert.True(run.StatusCode == HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        }
        var writes = await WritesSinceAsync(before);
        Assert.Equal(2, writes.Count(w => w.Table == "contacts.contacts" && w.Action == "insert"));
        Assert.All(writes, w => Assert.True(w.Table is "contacts.contacts" or "importexport.import_sessions" or "importexport.import_files" or "customfields.field_values",
            $"an import wrote {w.Action} {w.Table}"));
    }

    [Fact]
    public async Task Only_the_permission_that_creates_contacts_checks_or_imports_a_file()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var id = await UploadAsync(admin, $"Name\r\nRefused {Guid.NewGuid():N}\r\n");
        var before = await LastAuditIdAsync();
        foreach (var held in new[] { "contacts.contacts.read", "contacts.contacts.update", "contacts.contacts.archive", "importexport.exports.create" })
        {
            using var user = await UserHoldingAsync(admin, $"iws.{held.Replace('.', '-')}", [held]);
            var content = new ByteArrayContent("Name\r\nx\r\n"u8.ToArray());
            content.Headers.Add("X-File-Name", "refused.csv");
            using var upload = await user.PostAsync($"{Contacts}/imports", content);
            Assert.True(upload.StatusCode == HttpStatusCode.Forbidden, $"{held}: uploading got {(int)upload.StatusCode}");
            using var run = await user.PostAsJsonAsync($"{Contacts}/imports/{id}/run", new { dryRun = false });
            Assert.True(run.StatusCode == HttpStatusCode.Forbidden, $"{held}: running got {(int)run.StatusCode}");
            using var discard = await user.DeleteAsync($"{Contacts}/imports/{id}");
            Assert.True(discard.StatusCode == HttpStatusCode.Forbidden, $"{held}: discarding got {(int)discard.StatusCode}");
        }
        Assert.DoesNotContain(await WritesSinceAsync(before), w => w.Table.StartsWith("importexport.", StringComparison.Ordinal) && w.Table != "importexport.import_sessions" || w.Table == "contacts.contacts");
    }

    [Fact]
    public async Task A_download_changes_only_the_count_the_moment_and_the_person_on_the_export_and_only_its_permission_downloads()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var start = await admin.PostAsJsonAsync("/api/importexport/tenant-exports", new { format = "csv" });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var export = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var runner = Env.Factory.Services.GetRequiredService<JobRunner>();
        for (var i = 0; i < 5 && await runner.RunOnceAsync(100) > 0; i++)
        {
        }
        using var downloader = await UserHoldingAsync(admin, "iws.downloader", ["importexport.exports.download"]);
        var before = await LastAuditIdAsync();
        using (var response = await downloader.PostAsync($"/api/importexport/tenant-exports/{export}/download", null))
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
        var writes = await WritesSinceAsync(before);
        var only = Assert.Single(writes);
        Assert.Equal(("importexport.tenant_exports", "update", (Guid?)export), (only.Table, only.Action, only.Record));
        Assert.All(only.Columns, c => Assert.Contains(c, new[] { "download_count", "downloaded_at", "downloaded_by" }));

        before = await LastAuditIdAsync();
        foreach (var held in new[] { "importexport.exports.read", "importexport.exports.create", "contacts.contacts.read" })
        {
            using var user = await UserHoldingAsync(admin, $"iws.{held.Replace('.', '-')}", [held]);
            using var refused = await user.PostAsync($"/api/importexport/tenant-exports/{export}/download", null);
            Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, $"{held}: downloading got {(int)refused.StatusCode}");
        }
        Assert.DoesNotContain(await WritesSinceAsync(before), w => w.Table.StartsWith("importexport.", StringComparison.Ordinal));
    }

    private async Task<HttpClient> UserHoldingAsync(HttpClient admin, string name, string[] permissions)
    {
        var local = $"{name}.{Guid.NewGuid().ToString("N")[..6]}";
        using var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Role {local}", nameAr = $"دور {local}", permissions });
        Assert.True(role.StatusCode == HttpStatusCode.Created, await role.Content.ReadAsStringAsync());
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var user = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"{local}@{Env.TenantA.EmailDomain}", displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { roleId },
        });
        Assert.True(user.StatusCode == HttpStatusCode.Created, await user.Content.ReadAsStringAsync());
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}");
        var given = access.GetProperty("options").EnumerateArray()
            .Select(o => new { companyId = o.GetProperty("id").GetGuid(), allBranches = true, branchIds = Array.Empty<Guid>() }).ToList();
        using var put = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies = given, version = access.GetProperty("version").GetUInt32() });
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return await Env.SignInAsync($"{local}@{Env.TenantA.EmailDomain}");
    }

    private async Task<long> LastAuditIdAsync()
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<long>(owner, "SELECT COALESCE(max(id), 0) FROM audit.entries");
    }

    private async Task<List<(string Table, string Action, Guid? Record, string[] Columns)>> WritesSinceAsync(long after)
    {
        await using var owner = await Env.OpenAdminAsync();
        return await DbCatalog.ReadAsync(owner,
            "SELECT table_schema || '.' || table_name, action, record_id, ARRAY(SELECT jsonb_object_keys(changes) ORDER BY 1) FROM audit.entries WHERE id > @after ORDER BY id",
            r => (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2), r.GetFieldValue<string[]>(3)), ("after", after));
    }
}

/// <summary>Its own environment: the gate imports into and exports tenant A.</summary>
public sealed class ImportExportWriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
