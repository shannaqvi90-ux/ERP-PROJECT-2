using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Attachments.Tests;

/// <summary>An upload and a removal of an attachment appear in the History of the record the file is attached to
/// (the file's kind of record names its parent through ParentKindField / ParentIdField).</summary>
public sealed class AttachmentHistoryTests(AttachmentsFixture fixture) : IClassFixture<AttachmentsFixture>
{
    [Fact]
    public async Task An_upload_and_a_removal_of_an_attachment_are_in_its_records_history()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        var contact = await Api.NewContactAsync(admin);
        var other = await Api.NewContactAsync(admin);
        var file = await Api.UploadedAsync(admin, contact, "History licence.pdf", Api.Pdf("history"));
        await Api.UploadedAsync(admin, other, "Someone else.pdf", Api.Pdf("other"));
        using (var removed = await admin.DeleteAsync(Api.File(file.GetProperty("id").GetGuid()), Api.Cancel))
        {
            Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync(Api.Cancel));
        }

        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/records/{contact}?type=contacts.contacts&take=50", Api.Cancel);
        string Names(string action) => string.Join("|", history.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("action").GetString() == action)
            .SelectMany(e => e.GetProperty("changes").EnumerateArray())
            .Where(c => c.GetProperty("field").GetString() == "name")
            .Select(c => (c.TryGetProperty("new", out var n) && n.ValueKind == JsonValueKind.String ? n : c.GetProperty("old")).GetString()));
        Assert.Contains("History licence.pdf", Names("insert"));
        Assert.Contains("History licence.pdf", Names("delete"));
        Assert.DoesNotContain("Someone else.pdf", Names("insert"));
    }
}
