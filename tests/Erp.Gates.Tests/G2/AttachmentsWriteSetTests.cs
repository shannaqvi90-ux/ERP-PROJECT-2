using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what contacts.contacts.update lets a request write through the attachments endpoints. The generic
/// write-set gate (<see cref="WriteSets"/>) cannot reach them: the record an upload names in {id} is a
/// contact, made by another module's endpoint, and a delete needs a file that exists, so POST and DELETE under
/// /api/attachments/ are listed in tests/Gates/write-set-unexercised.txt. This gate sends both itself, as a user holding exactly the
/// permission the endpoint declares: the audit trail must show one new
/// attachments.files row for an upload and the removal of that row for a delete, and nothing else (no
/// change to the contact), and users holding every other contacts permission, or only the read one,
/// must be refused with nothing written.
/// </summary>
public sealed class AttachmentsWriteSetTests(AttachmentsWriteSetFixture fixture) : IClassFixture<AttachmentsWriteSetFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static readonly byte[] Pdf = "%PDF-1.4\n% write set\n%%EOF\n"u8.ToArray();

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid contact, string name)
    {
        var content = new ByteArrayContent(Pdf);
        content.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        return await client.PostAsync($"/api/attachments/contacts.contacts/{contact}/files", content);
    }

    private async Task<Guid> ContactAsync(HttpClient admin)
    {
        using var created = await admin.PostAsJsonAsync("/api/contacts/contacts", new
        {
            kind = "organisation", nameEn = $"Write set files {Guid.NewGuid():N}", nameAr = "", country = "AE", defaultCurrency = "AED", tags = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Attaching_and_deleting_a_file_write_only_the_file_row()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var writer = await UserHoldingAsync(admin, "aws.writer", ["contacts.contacts.update"]);
        var contact = await ContactAsync(admin);

        var before = await LastAuditIdAsync();
        using var uploaded = await UploadAsync(writer, contact, "write-set.pdf");
        var text = await uploaded.Content.ReadAsStringAsync();
        Assert.True(uploaded.StatusCode == HttpStatusCode.Created, text);
        var file = JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
        var added = await WritesSinceAsync(before);
        Assert.Equal([("attachments.files", "insert", (Guid?)file)], added.Select(w => (w.Table, w.Action, w.Record)));
        Assert.DoesNotContain(added.Single().Columns, c => c is "storage_key");
        Assert.Contains("name", added.Single().Columns);

        before = await LastAuditIdAsync();
        using var removed = await writer.DeleteAsync($"/api/attachments/contacts.contacts/files/{file}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal([("attachments.files", "delete", (Guid?)file)], (await WritesSinceAsync(before)).Select(w => (w.Table, w.Action, w.Record)));
    }

    [Fact]
    public async Task Only_the_permission_that_changes_the_record_attaches_or_deletes_and_only_the_one_that_reads_it_lists_or_downloads()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var contact = await ContactAsync(admin);
        using var seeded = await UploadAsync(admin, contact, "seeded.pdf");
        var file = (await seeded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var others = new[] { "contacts.contacts.read", "contacts.contacts.create", "contacts.contacts.archive" };
        var before = await LastAuditIdAsync();
        foreach (var held in others)
        {
            using var user = await UserHoldingAsync(admin, $"aws.{held.Replace('.', '-')}", [held]);
            using var add = await UploadAsync(user, contact, "refused.pdf");
            Assert.True(add.StatusCode == HttpStatusCode.Forbidden, $"{held}: attaching got {(int)add.StatusCode}");
            using var delete = await user.DeleteAsync($"/api/attachments/contacts.contacts/files/{file}");
            Assert.True(delete.StatusCode == HttpStatusCode.Forbidden, $"{held}: deleting got {(int)delete.StatusCode}");
            using var list = await user.GetAsync($"/api/attachments/contacts.contacts/{contact}/files");
            using var content = await user.GetAsync($"/api/attachments/contacts.contacts/files/{file}/content");
            var mayRead = held == "contacts.contacts.read";
            Assert.True(mayRead == list.IsSuccessStatusCode, $"{held}: listing got {(int)list.StatusCode}");
            Assert.True(mayRead == content.IsSuccessStatusCode, $"{held}: downloading got {(int)content.StatusCode}");
        }
        Assert.DoesNotContain(await WritesSinceAsync(before), w => w.Table.StartsWith("attachments.", StringComparison.Ordinal));
    }

    private async Task<HttpClient> UserHoldingAsync(HttpClient admin, string local, string[] permissions)
    {
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

/// <summary>Its own environment: the gate writes files of tenant A.</summary>
public sealed class AttachmentsWriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}
