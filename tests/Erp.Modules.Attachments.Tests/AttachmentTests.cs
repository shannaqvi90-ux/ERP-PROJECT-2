using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Attachments.Tests;

/// <summary>Its own gate environment, with a storage folder of its own so the tests can look at the disk.</summary>
public sealed class AttachmentsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "erp-attachments-tests-" + Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?> { ["Erp:Attachments:Root"] = Root });

    public async ValueTask DisposeAsync()
    {
        await Env.DisposeAsync();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class Api
{
    public static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public const string Record = "contacts.contacts";

    /// <summary>A valid, if tiny, PDF.</summary>
    public static byte[] Pdf(string text = "hello") => Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n% {text}\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static async Task<Guid> NewContactAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/contacts/contacts", new
        {
            kind = "organisation", nameEn = "Files LLC " + Guid.NewGuid().ToString("N")[..8], nameAr = "", country = "AE", defaultCurrency = "AED", tags = Array.Empty<string>(),
        }, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    public static string Root(Guid contact) => $"/api/attachments/{Record}/{contact}/files";

    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid contact, string name, byte[] bytes, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(contentType);
        content.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        return client.PostAsync(Root(contact), content, Cancel);
    }

    public static async Task<JsonElement> UploadedAsync(HttpClient client, Guid contact, string name, byte[] bytes)
    {
        using var response = await UploadAsync(client, contact, name, bytes);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> ListAsync(HttpClient client, Guid contact) =>
        await client.GetFromJsonAsync<JsonElement>(Root(contact), Cancel);

    public static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).RootElement.GetProperty("code").GetString()!;

    public static async Task<string> UserHoldingAsync(ErpTestEnvironment env, HttpClient admin, string local, params string[] permissions)
    {
        var email = $"{local}-{Guid.NewGuid().ToString("N")[..6]}@{env.TenantA.EmailDomain}";
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Files " + local, nameAr = "ملفات " + local, permissions }, Cancel);
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Files " + local, language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } }, Cancel);
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return email;
    }
}

public sealed class AttachmentTests(AttachmentsFixture fixture) : IClassFixture<AttachmentsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<HttpClient> AdminAsync() => await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

    [Fact]
    public async Task A_file_is_attached_listed_downloaded_and_removed()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var pdf = Api.Pdf("licence");
        var file = await Api.UploadedAsync(admin, contact, "Trade licence.pdf", pdf);
        Assert.Equal("Trade licence.pdf", file.GetProperty("name").GetString());
        Assert.Equal("application/pdf", file.GetProperty("contentType").GetString());
        Assert.Equal(pdf.Length, file.GetProperty("sizeBytes").GetInt64());
        Assert.True(file.GetProperty("previewable").GetBoolean());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf)), file.GetProperty("sha256").GetString());
        Assert.False(string.IsNullOrEmpty(file.GetProperty("createdByName").GetString()));

        var list = await Api.ListAsync(admin, contact);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(file.GetProperty("id").GetGuid(), list.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Contains("pdf", list.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()));

        var id = file.GetProperty("id").GetGuid();
        using var download = await admin.GetAsync($"{Api.Root(contact)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(Api.Cancel));
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Trade licence.pdf", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));

        using var inline = await admin.GetAsync($"{Api.Root(contact)}/{id}/content?inline=true", Api.Cancel);
        Assert.Equal("inline", inline.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("SAMEORIGIN", inline.Headers.GetValues("X-Frame-Options"));

        using var removed = await admin.DeleteAsync($"{Api.Root(contact)}/{id}", Api.Cancel);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, (await Api.ListAsync(admin, contact)).GetProperty("total").GetInt32());
        using var gone = await admin.GetAsync($"{Api.Root(contact)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Several_files_can_be_attached_at_once_and_a_picture_is_previewed_in_the_page()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var uploads = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Api.UploadAsync(admin, contact, $"scan-{i}.png", Api.Png)));
        Assert.All(uploads, u => Assert.Equal(HttpStatusCode.Created, u.StatusCode));
        var list = await Api.ListAsync(admin, contact);
        Assert.Equal(6, list.GetProperty("total").GetInt32());
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var picture = await admin.GetAsync($"{Api.Root(contact)}/{id}/content?inline=true", Api.Cancel);
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sandbox", string.Join(' ', picture.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task The_server_checks_the_type_the_size_and_the_content()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        // A page or a script is not on the list, whatever the browser calls it.
        using var script = await Api.UploadAsync(admin, contact, "evil.html", "<script>alert(1)</script>"u8.ToArray(), "application/pdf");
        Assert.True(script.StatusCode == HttpStatusCode.UnsupportedMediaType, $"{(int)script.StatusCode} {await script.Content.ReadAsStringAsync(Api.Cancel)}");
        Assert.Equal("attachmentsType", await Api.ProblemCodeAsync(script));
        using var svg = await Api.UploadAsync(admin, contact, "logo.svg", "<svg onload=alert(1)/>"u8.ToArray(), "image/svg+xml");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, svg.StatusCode);
        using var noExtension = await Api.UploadAsync(admin, contact, "README", "text"u8.ToArray());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, noExtension.StatusCode);
        // The extension says PDF, the bytes say otherwise.
        using var fake = await Api.UploadAsync(admin, contact, "fake.pdf", "MZ this is a program"u8.ToArray(), "application/pdf");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, fake.StatusCode);
        Assert.Equal("attachmentsContent", await Api.ProblemCodeAsync(fake));
        using var pngAsJpg = await Api.UploadAsync(admin, contact, "photo.jpg", Api.Png);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, pngAsJpg.StatusCode);
        // A zip is not a Word document.
        using var zip = await Api.UploadAsync(admin, contact, "letter.docx", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, zip.StatusCode);
        using var binaryText = await Api.UploadAsync(admin, contact, "notes.txt", [0x41, 0x00, 0x42, 0xFF]);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, binaryText.StatusCode);
        using var empty = await Api.UploadAsync(admin, contact, "empty.txt", []);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var noName = await admin.PostAsync(Api.Root(contact), new ByteArrayContent(Api.Pdf()), Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        // Too large: announced, and streamed without a length.
        var big = new byte[21 * 1024 * 1024];
        "%PDF-1.4"u8.CopyTo(big);
        using var large = await Api.UploadAsync(admin, contact, "big.pdf", big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Equal("attachmentsTooLarge", await Api.ProblemCodeAsync(large));
        using var chunked = new HttpRequestMessage(HttpMethod.Post, Api.Root(contact)) { Content = new StreamContent(new MemoryStream(big)) };
        chunked.Headers.Add("X-File-Name", "big.pdf");
        using var chunkedAnswer = await admin.SendAsync(chunked, Api.Cancel);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunkedAnswer.StatusCode);
        // Nothing of the refused files was kept.
        Assert.Equal(0, (await Api.ListAsync(admin, contact)).GetProperty("total").GetInt32());
        // A real Word package passes.
        using var docx = await Api.UploadAsync(admin, contact, "letter.docx", Docx());
        Assert.Equal(HttpStatusCode.Created, docx.StatusCode);
        using var csv = await Api.UploadAsync(admin, contact, "rates.csv", Encoding.UTF8.GetBytes("﻿code,اسم\nAED,درهم\n"));
        Assert.Equal(HttpStatusCode.Created, csv.StatusCode);
    }

    private static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in new[] { "[Content_Types].xml", "word/document.xml" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write("<x/>");
            }
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task A_record_holds_a_limited_number_of_files()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var results = new List<HttpStatusCode>();
        for (var i = 0; i < 101; i++)
        {
            using var response = await Api.UploadAsync(admin, contact, $"n{i}.txt", Encoding.ASCII.GetBytes("note " + i));
            results.Add(response.StatusCode);
            if (i == 100)
            {
                Assert.Equal("attachmentsTooMany", await Api.ProblemCodeAsync(response));
            }
        }
        Assert.Equal(100, results.Count(r => r == HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.Conflict, results[100]);
    }

    [Fact]
    public async Task A_client_name_never_reaches_the_file_system()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var names = new[] { "../../../etc/passwd.txt", "..\\..\\boot.txt", "/etc/shadow.txt", "C:\\Windows\\win.txt", "a/../../b.txt", "x\u202Etxt.exe.pdf", "..txt" };
        foreach (var name in names)
        {
            using var response = await Api.UploadAsync(admin, contact, name, name.EndsWith("pdf", StringComparison.Ordinal) ? Api.Pdf() : "plain text"u8.ToArray());
            var text = await response.Content.ReadAsStringAsync(Api.Cancel);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var stored = JsonDocument.Parse(text).RootElement.GetProperty("name").GetString()!;
                Assert.DoesNotContain('/', stored);
                Assert.DoesNotContain('\\', stored);
                Assert.DoesNotContain('\u202E', stored);
                Assert.False(stored.StartsWith('.'));
            }
        }
        // Everything on disk sits in a tenant folder, under a name of 32 hex digits, and nothing is outside the root.
        var root = Path.GetFullPath(fixture.Root);
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.All(files, f =>
        {
            Assert.StartsWith(root, Path.GetFullPath(f));
            var relative = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar);
            Assert.Equal(2, relative.Length);
            Assert.Matches("^[0-9a-f]{32}$", relative[0]);
            Assert.Matches("^[0-9a-f]{32}$", relative[1]);
        });
        Assert.False(File.Exists(Path.Combine(root, "..", "etc", "passwd.txt")));
    }

    [Fact]
    public async Task Files_follow_the_permissions_of_the_record()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var file = await Api.UploadedAsync(admin, contact, "terms.pdf", Api.Pdf());
        var id = file.GetProperty("id").GetGuid();

        using var reader = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "reader", "contacts.contacts.read"));
        Assert.Equal(1, (await Api.ListAsync(reader, contact)).GetProperty("total").GetInt32());
        using var read = await reader.GetAsync($"{Api.Root(contact)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var cannotAdd = await Api.UploadAsync(reader, contact, "x.pdf", Api.Pdf());
        Assert.Equal(HttpStatusCode.Forbidden, cannotAdd.StatusCode);
        using var cannotDelete = await reader.DeleteAsync($"{Api.Root(contact)}/{id}", Api.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, cannotDelete.StatusCode);

        using var writer = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "writer", "contacts.contacts.update"));
        using var cannotList = await writer.GetAsync(Api.Root(contact), Api.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, cannotList.StatusCode);
        using var cannotRead = await writer.GetAsync($"{Api.Root(contact)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, cannotRead.StatusCode);
        using var added = await Api.UploadAsync(writer, contact, "added.pdf", Api.Pdf());
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        using var nothing = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "nothing", "currency.rates.read"));
        using var denied = await nothing.GetAsync(Api.Root(contact), Api.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = Env.CreateClient();
        using var unsigned = await anonymous.GetAsync(Api.Root(contact), Api.Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
    }

    [Fact]
    public async Task Another_workspace_sees_nothing_of_the_files_and_changes_nothing()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var file = await Api.UploadedAsync(admin, contact, "secret.pdf", Api.Pdf("private"));
        var id = file.GetProperty("id").GetGuid();

        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        foreach (var path in new[] { Api.Root(contact), $"{Api.Root(contact)}/{id}/content" })
        {
            using var response = await other.GetAsync(path, Api.Cancel);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var delete = await other.DeleteAsync($"{Api.Root(contact)}/{id}", Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var plant = await Api.UploadAsync(other, contact, "planted.pdf", Api.Pdf());
        Assert.Equal(HttpStatusCode.NotFound, plant.StatusCode);
        // B's own contact with A's file id: nothing either.
        var theirs = await Api.NewContactAsync(other);
        using var mixed = await other.GetAsync($"{Api.Root(theirs)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);
        Assert.Equal(1, (await Api.ListAsync(admin, contact)).GetProperty("total").GetInt32());
        using var still = await admin.GetAsync($"{Api.Root(contact)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
    }

    [Fact]
    public async Task A_file_of_one_record_is_not_reachable_through_another()
    {
        using var admin = await AdminAsync();
        var first = await Api.NewContactAsync(admin);
        var second = await Api.NewContactAsync(admin);
        var file = await Api.UploadedAsync(admin, first, "mine.pdf", Api.Pdf());
        var id = file.GetProperty("id").GetGuid();
        using var read = await admin.GetAsync($"{Api.Root(second)}/{id}/content", Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var delete = await admin.DeleteAsync($"{Api.Root(second)}/{id}", Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var unknown = await admin.GetAsync(Api.Root(Guid.NewGuid()), Api.Cancel);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Every_upload_and_every_delete_is_in_the_audit_trail()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        var file = await Api.UploadedAsync(admin, contact, "audited.pdf", Api.Pdf());
        var id = file.GetProperty("id").GetGuid();
        using var removed = await admin.DeleteAsync($"{Api.Root(contact)}/{id}", Api.Cancel);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        await using var db = await Env.OpenAdminAsync();
        await using var read = new NpgsqlCommand("SELECT action, record_label, actor_id, table_schema || '.' || table_name FROM audit.entries WHERE record_id = @id ORDER BY occurred_at, id", db);
        read.Parameters.AddWithValue("id", id);
        var rows = new List<(string Action, string? Label, Guid? Actor, string Table)>();
        await using (var reader = await read.ExecuteReaderAsync(Api.Cancel))
        {
            while (await reader.ReadAsync(Api.Cancel))
            {
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3)));
            }
        }
        Assert.Equal(["insert", "delete"], rows.Select(r => r.Action));
        Assert.All(rows, r =>
        {
            Assert.Equal("attachments.files", r.Table);
            Assert.Equal("audited.pdf", r.Label);
            Assert.NotNull(r.Actor);
        });
    }

    [Fact]
    public async Task The_database_keeps_the_files_rows_apart_by_workspace_and_refuses_another_workspaces_key()
    {
        using var admin = await AdminAsync();
        var contact = await Api.NewContactAsync(admin);
        await Api.UploadedAsync(admin, contact, "rls.pdf", Api.Pdf());
        await using var app = await Env.OpenAppAsync();
        await using (var none = new NpgsqlCommand("SELECT count(*) FROM attachments.files", app))
        {
            Assert.Equal(0L, await none.ExecuteScalarAsync(Api.Cancel));
        }
        await using var owner = await Env.OpenAdminAsync();
        await using var policy = new NpgsqlCommand("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'attachments.files'::regclass", owner);
        Assert.Equal(true, await policy.ExecuteScalarAsync(Api.Cancel));
        // A row cannot name bytes of another tenant, even for a superuser.
        await using var bad = new NpgsqlCommand("""
            INSERT INTO attachments.files (id, tenant_id, record_type, record_id, name, content_type, size_bytes, sha256, storage_key, created_at, updated_at)
            VALUES (gen_random_uuid(), @t, 'contacts.contacts', gen_random_uuid(), 'x.pdf', 'application/pdf', 5, repeat('a', 64), @k, now(), now())
            """, owner);
        bad.Parameters.AddWithValue("t", Env.TenantA.Id);
        bad.Parameters.AddWithValue("k", $"{Env.TenantB.Id:N}/{Guid.NewGuid():N}");
        var error = await Assert.ThrowsAsync<PostgresException>(() => bad.ExecuteNonQueryAsync(Api.Cancel));
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task The_demo_workspaces_come_with_files_on_their_first_contacts()
    {
        using var admin = await AdminAsync();
        var contacts = await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts", Api.Cancel);
        var withFiles = 0;
        foreach (var item in contacts.GetProperty("items").EnumerateArray())
        {
            var list = await Api.ListAsync(admin, item.GetProperty("id").GetGuid());
            if (list.GetProperty("total").GetInt32() > 0)
            {
                withFiles++;
                var first = list.GetProperty("items")[0].GetProperty("id").GetGuid();
                using var content = await admin.GetAsync($"{Api.Root(item.GetProperty("id").GetGuid())}/{first}/content", Api.Cancel);
                Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            }
        }
        Assert.True(withFiles >= 3, $"{withFiles} contacts with files");
    }
}
