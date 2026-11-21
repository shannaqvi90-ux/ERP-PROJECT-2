using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.ImportExport.Tests;

/// <summary>The full export of a workspace: start, progress, the file's manifest, records, attachments and custom fields,
/// the one-at-a-time rule, expiry, permissions, the audit of downloads and the workspace boundary.</summary>
public sealed class TenantExportTests(ImportExportFixture fixture) : IClassFixture<ImportExportFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static CancellationToken Cancel => Api.Cancel;

    private const string Root = "/api/importexport/tenant-exports";

    private async Task<HttpClient> AdminAsync(bool other = false) => await Env.SignInAsync(Env.Email(other ? Env.TenantB : Env.TenantA, "admin"));

    private async Task<JsonElement> ExportAsync(HttpClient admin, string format = "csv")
    {
        using var start = await admin.PostAsJsonAsync(Root, new { format }, Cancel);
        var started = await Api.ReadAsync(start, HttpStatusCode.Accepted);
        Assert.Equal("queued", started.GetProperty("status").GetString());
        await fixture.RunJobsAsync();
        var done = await admin.GetFromJsonAsync<JsonElement>($"{Root}/{started.GetProperty("id").GetGuid()}", Cancel);
        Assert.True(done.GetProperty("status").GetString() == "succeeded", done.ToString() + await fixture.JobProblemsAsync());
        return done;
    }

    private static async Task<ZipArchive> DownloadAsync(HttpClient client, Guid id)
    {
        using var response = await client.PostAsync($"{Root}/{id}/download", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        return new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Cancel)), ZipArchiveMode.Read);
    }

    private static string Text(ZipArchive zip, string path)
    {
        using var reader = new StreamReader(zip.GetEntry(path)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task A_full_export_holds_a_manifest_the_contacts_and_the_attachments_of_the_workspace_only()
    {
        using var admin = await AdminAsync();
        var name = Api.Unique("Exported") + " LLC";
        var created = await admin.PostAsJsonAsync("/api/contacts/contacts", new { kind = "organisation", nameEn = name, nameAr = "", country = "AE", defaultCurrency = "AED", tags = new[] { "Keep" } }, Cancel);
        var contact = (await Api.ReadAsync(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Delivery note 42\n"));
        file.Headers.Add("X-File-Name", "note-42.txt");
        using (var upload = await admin.PostAsync($"/api/attachments/contacts.contacts/{contact}/files", file, Cancel))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        var export = await ExportAsync(admin);
        Assert.True(export.GetProperty("downloadable").GetBoolean());
        Assert.True(export.GetProperty("byteSize").GetInt64() > 0);
        Assert.True(export.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(6));
        using var zip = await DownloadAsync(admin, export.GetProperty("id").GetGuid());

        var manifest = JsonDocument.Parse(Text(zip, "manifest.json")).RootElement;
        Assert.Equal("erp-tenant-export", manifest.GetProperty("format").GetString());
        var contacts = manifest.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("recordType").GetString() == "contacts.contacts");
        Assert.Equal("data/contacts.contacts.csv", contacts.GetProperty("path").GetString());
        var csv = Text(zip, "data/contacts.contacts.csv");
        Assert.Contains(name, csv);
        Assert.StartsWith("﻿id,kind", csv);
        Assert.Contains("custom.", csv.Split("\r\n")[0]);
        // The checksum in the manifest is the checksum of the file.
        using (var raw = new MemoryStream())
        {
            using (var entry = zip.GetEntry("data/contacts.contacts.csv")!.Open())
            {
                await entry.CopyToAsync(raw, Cancel);
            }
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(raw.ToArray())), contacts.GetProperty("sha256").GetString());
            // The file reads back as a table with as many rows as the manifest says.
            Assert.Equal(contacts.GetProperty("records").GetInt64(), Erp.Modules.ImportExport.Files.TabularFile.Read(raw.ToArray(), "contacts.csv").Rows.Count);
        }
        var attachment = zip.Entries.Single(e => e.FullName.StartsWith("attachments/") && e.FullName.EndsWith("note-42.txt"));
        using (var reader = new StreamReader(attachment.Open()))
        {
            Assert.Equal("Delivery note 42\n", await reader.ReadToEndAsync(Cancel));
        }
        Assert.Contains("note-42.txt", Text(zip, "data/attachments.csv"));
        Assert.Contains("README", string.Join(",", zip.Entries.Select(e => e.FullName)));
        Assert.Matches("[؀-ۿ]", Text(zip, "README.txt"));

        // Another workspace's data is not in it.
        using var other = await AdminAsync(other: true);
        var otherContacts = await other.GetFromJsonAsync<JsonElement>("/api/contacts/contacts?take=3", Cancel);
        foreach (var row in otherContacts.GetProperty("items").EnumerateArray())
        {
            Assert.DoesNotContain(row.GetProperty("id").GetGuid().ToString(), csv);
        }
    }

    [Fact]
    public async Task A_full_export_holds_every_modules_records_and_none_of_the_secrets()
    {
        using var admin = await AdminAsync();
        var export = await ExportAsync(admin);
        using var zip = await DownloadAsync(admin, export.GetProperty("id").GetGuid());
        var manifest = JsonDocument.Parse(Text(zip, "manifest.json")).RootElement;
        var files = manifest.GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("recordType").GetString()!, f => f.GetProperty("records").GetInt64());
        foreach (var table in new[] { "contacts.contacts", "identity.users", "identity.roles", "identity.user_roles", "tenancy.companies", "tenancy.branches", "currency.currencies", "currency.exchange_rates", "numbering.sequences", "customfields.definitions", "lists.saved_views", "attachments.files" })
        {
            Assert.True(files.ContainsKey(table), $"the export has no file for {table}: {string.Join(", ", files.Keys)}");
        }
        Assert.True(files["identity.users"] > 0 && files["identity.roles"] > 0 && files["tenancy.companies"] > 0 && files["currency.currencies"] > 0);
        var users = Text(zip, "data/identity.users.csv");
        var header = users.Split("\r\n")[0];
        Assert.Contains("email", header);
        foreach (var secret in new[] { "password", "hash", "secret", "token", "salt" })
        {
            Assert.DoesNotContain(secret, header, StringComparison.OrdinalIgnoreCase);
        }
        // The roles' permissions come out as a JSON list, the exchange rates keep their rate and currency.
        Assert.Contains("[\"", Text(zip, "data/identity.roles.csv").Replace("\"\"", "\""));
        Assert.Contains("rate", Text(zip, "data/currency.exchange_rates.csv").Split("\r\n")[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_json_export_holds_an_array_of_objects_per_kind_of_record()
    {
        using var admin = await AdminAsync();
        var export = await ExportAsync(admin, "json");
        using var zip = await DownloadAsync(admin, export.GetProperty("id").GetGuid());
        var rows = JsonDocument.Parse(Text(zip, "data/contacts.contacts.json")).RootElement;
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.True(rows.GetArrayLength() > 0);
        Assert.True(rows[0].TryGetProperty("nameEn", out _));
        Assert.True(rows[0].TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Only_one_export_runs_at_a_time_and_a_finished_one_can_be_started_again()
    {
        using var admin = await AdminAsync();
        using var first = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        var started = await Api.ReadAsync(first, HttpStatusCode.Accepted);
        // Asking again while it is queued answers that export, not a second one.
        using var second = await admin.PostAsJsonAsync(Root, new { format = "json" }, Cancel);
        var again = await Api.ReadAsync(second, HttpStatusCode.Accepted);
        Assert.Equal(started.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal("csv", again.GetProperty("format").GetString());
        using var bad = await admin.PostAsJsonAsync(Root, new { format = "xml" }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await fixture.RunJobsAsync();
        using var third = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        Assert.NotEqual(started.GetProperty("id").GetGuid(), (await Api.ReadAsync(third, HttpStatusCode.Accepted)).GetProperty("id").GetGuid());
        await fixture.RunJobsAsync();
    }

    [Fact]
    public async Task A_download_is_counted_audited_and_refused_after_the_export_expires()
    {
        using var admin = await AdminAsync();
        var export = await ExportAsync(admin);
        var id = export.GetProperty("id").GetGuid();
        (await DownloadAsync(admin, id)).Dispose();
        (await DownloadAsync(admin, id)).Dispose();
        var after = await admin.GetFromJsonAsync<JsonElement>($"{Root}/{id}", Cancel);
        Assert.Equal(2, after.GetProperty("downloadCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("downloadedAt").ValueKind);

        await using (var db = await Env.OpenAdminAsync())
        {
            await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE table_schema = 'importexport' AND table_name = 'tenant_exports' AND record_id = @id AND action = 'update' AND changes ? 'download_count'", db);
            count.Parameters.AddWithValue("id", id);
            Assert.True((long)(await count.ExecuteScalarAsync(Cancel))! >= 2, "each download is a change of the export's record, so it is in the audit trail");
            await using var expire = new Npgsql.NpgsqlCommand("UPDATE importexport.tenant_exports SET expires_at = now() - interval '1 minute' WHERE id = @id", db);
            expire.Parameters.AddWithValue("id", id);
            await expire.ExecuteNonQueryAsync(Cancel);
        }
        using var expired = await admin.PostAsync($"{Root}/{id}/download", null, Cancel);
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        var listed = await admin.GetFromJsonAsync<JsonElement>($"{Root}/{id}", Cancel);
        Assert.Equal("expired", listed.GetProperty("status").GetString());
        Assert.False(listed.GetProperty("downloadable").GetBoolean());
        // Starting the next export removes the file of the expired one.
        await ExportAsync(admin);
        await using var check = await Env.OpenAdminAsync();
        await using var files = new Npgsql.NpgsqlCommand("SELECT count(*) FROM importexport.export_files WHERE export_id = @id", check);
        files.Parameters.AddWithValue("id", id);
        Assert.Equal(0L, (long)(await files.ExecuteScalarAsync(Cancel))!);
    }

    [Fact]
    public async Task Reading_starting_and_downloading_are_three_permissions_and_a_workspace_reaches_only_its_own_exports()
    {
        using var admin = await AdminAsync();
        var export = await ExportAsync(admin);
        var id = export.GetProperty("id").GetGuid();
        using var reader = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "exportreader", ["importexport.exports.read"]));
        using var starter = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "exportstarter", ["importexport.exports.create"]));
        using var downloader = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "exportdownloader", ["importexport.exports.download"]));
        using var nobody = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "exportnobody", ["contacts.contacts.read"]));

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(Root, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{Root}/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsync($"{Root}/{id}/download", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(Root, new { format = "csv" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await starter.GetAsync(Root, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await downloader.GetAsync(Root, Cancel)).StatusCode);
        (await DownloadAsync(downloader, id)).Dispose();
        foreach (var client in new[] { nobody })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root, Cancel)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Root}/{id}/download", null, Cancel)).StatusCode);
        }
        using var starterStart = await starter.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, starterStart.StatusCode);
        await fixture.RunJobsAsync();

        using var other = await AdminAsync(other: true);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Root}/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"{Root}/{id}/download", null, Cancel)).StatusCode);
        var otherList = await other.GetFromJsonAsync<JsonElement>(Root, Cancel);
        Assert.DoesNotContain(otherList.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task An_export_that_has_not_finished_cannot_be_downloaded()
    {
        using var admin = await AdminAsync();
        using var start = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        var id = (await Api.ReadAsync(start, HttpStatusCode.Accepted)).GetProperty("id").GetGuid();
        using var early = await admin.PostAsync($"{Root}/{id}/download", null, Cancel);
        Assert.Equal("queued", (await Api.ReadAsync(early, HttpStatusCode.Accepted)).GetProperty("status").GetString());
        await fixture.RunJobsAsync();
    }
}
