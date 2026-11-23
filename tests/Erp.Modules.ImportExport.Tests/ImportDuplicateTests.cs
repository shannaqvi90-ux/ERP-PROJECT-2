using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.ImportExport.Tests;

/// <summary>Importing a file whose rows are contacts already there: the warning, the choice to add, skip or update, the workspace's own rate
/// for a foreign credit limit, and the error file naming columns the way the file does.</summary>
public sealed class ImportDuplicateTests(ImportExportFixture fixture) : IClassFixture<ImportExportFixture>
{
    private static CancellationToken Cancel => Api.Cancel;

    private async Task<HttpClient> AdminAsync() => await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));

    private static async Task<JsonElement> ListAsync(HttpClient client, string search) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(search)}&take=50", Cancel);

    [Fact]
    public async Task The_same_file_imported_twice_warns_of_the_rows_already_there_and_the_run_can_add_skip_or_update_them()
    {
        using var admin = await AdminAsync();
        var tag = Api.Unique("Twice").Replace(" ", "");
        var csv = $"Name,Email,City\r\n{tag} Alpha LLC,alpha@{tag.ToLowerInvariant()}.example,Dubai\r\n{tag} Beta LLC,beta@{tag.ToLowerInvariant()}.example,Dubai\r\n";
        var first = (await Api.UploadCsvAsync(admin, csv)).GetProperty("id").GetGuid();
        var firstCheck = await Api.RunAsync(fixture, admin, first, dryRun: true);
        Assert.Equal(0, firstCheck.GetProperty("rowsDuplicate").GetInt32());
        Assert.Equal(2, (await Api.RunAsync(fixture, admin, first, dryRun: false)).GetProperty("rowsImported").GetInt32());

        // The same file again: the check names both rows as already there and writes nothing.
        var second = (await Api.UploadCsvAsync(admin, csv)).GetProperty("id").GetGuid();
        var check = await Api.RunAsync(fixture, admin, second, dryRun: true);
        Assert.Equal(2, check.GetProperty("rowsDuplicate").GetInt32());
        Assert.Equal(2, check.GetProperty("rowsValid").GetInt32());
        Assert.Equal(2, (await ListAsync(admin, tag)).GetProperty("items").GetArrayLength());

        // Skip: nothing is added.
        var skipped = await Api.RunAsync(fixture, admin, second, dryRun: false, onDuplicate: "skip");
        Assert.Equal("completed", skipped.GetProperty("status").GetString());
        Assert.Equal(0, skipped.GetProperty("rowsImported").GetInt32());
        Assert.Equal(2, (await ListAsync(admin, tag)).GetProperty("items").GetArrayLength());

        // Update: the records already there take the file's values and none is added.
        var changed = $"Name,Email,City\r\n{tag} Alpha LLC,alpha@{tag.ToLowerInvariant()}.example,Sharjah\r\n{tag} Beta LLC,beta@{tag.ToLowerInvariant()}.example,\r\n";
        var third = (await Api.UploadCsvAsync(admin, changed)).GetProperty("id").GetGuid();
        var updated = await Api.RunAsync(fixture, admin, third, dryRun: false, onDuplicate: "update");
        Assert.Equal("completed", updated.GetProperty("status").GetString());
        Assert.Equal(2, updated.GetProperty("rowsUpdated").GetInt32());
        Assert.Equal(0, updated.GetProperty("rowsImported").GetInt32());
        var rows = (await ListAsync(admin, tag)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var alpha = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{rows.Single(r => r.GetProperty("nameEn").GetString()!.Contains("Alpha")).GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal("Sharjah", alpha.GetProperty("city").GetString());
        // A blank cell leaves the value as it was.
        var beta = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{rows.Single(r => r.GetProperty("nameEn").GetString()!.Contains("Beta")).GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal("Dubai", beta.GetProperty("city").GetString());

        // Add anyway (the default) writes them again, as the check warned.
        var fourth = (await Api.UploadCsvAsync(admin, csv)).GetProperty("id").GetGuid();
        var added = await Api.RunAsync(fixture, admin, fourth, dryRun: false);
        Assert.Equal(2, added.GetProperty("rowsImported").GetInt32());
        Assert.Equal(2, added.GetProperty("rowsDuplicate").GetInt32());
        Assert.Equal(4, (await ListAsync(admin, tag)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_foreign_credit_limit_without_a_rate_takes_the_workspaces_own_rate()
    {
        using var admin = await AdminAsync();
        var chf = (await admin.GetFromJsonAsync<JsonElement>("/api/currency/currencies?search=CHF", Cancel)).GetProperty("items").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "CHF");
        if (!chf.GetProperty("isActive").GetBoolean())
        {
            using var enable = await admin.PutAsJsonAsync($"/api/currency/currencies/{chf.GetProperty("id").GetGuid()}", new { isActive = true, maxRateAgeDays = 7, version = chf.GetProperty("version").GetUInt32() }, Cancel);
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using (await admin.PostAsJsonAsync("/api/currency/rates", new { currency = "CHF", rateDate = today, rate = "4.5" }, Cancel))
        {
        }
        var rate = (await admin.GetFromJsonAsync<JsonElement>($"/api/currency/rates/lookup?currency=CHF&date={today}", Cancel)).GetProperty("rate").GetString()!;

        var name = Api.Unique("Franc") + " AG";
        var id = (await Api.UploadCsvAsync(admin, $"Name,Credit limit,Credit currency\r\n{name},1000.00,CHF\r\n")).GetProperty("id").GetGuid();
        var done = await Api.RunAsync(fixture, admin, id, dryRun: false);
        Assert.Equal(1, done.GetProperty("rowsImported").GetInt32());
        var contact = (await ListAsync(admin, name)).GetProperty("items")[0];
        var limit = (await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", Cancel)).GetProperty("creditLimit");
        Assert.Equal("CHF", limit.GetProperty("currency").GetString());
        Assert.Equal(decimal.Parse(rate, CultureInfo.InvariantCulture), decimal.Parse(limit.GetProperty("exchangeRate").GetString()!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_error_file_names_a_problem_by_the_columns_own_header_or_the_fields_label_never_its_key()
    {
        using var admin = await AdminAsync();
        var id = (await Api.UploadCsvAsync(admin, "Name,Mail,Ref\r\nBad Mail Co,not-an-email,R1\r\n")).GetProperty("id").GetGuid();
        // "Mail" is not a name the importer knows, so the column is mapped by hand.
        using var map = await admin.PutAsJsonAsync($"{Api.Contacts}/imports/{id}/mapping", new { columns = new[] { new { index = 1, field = "email" } } }, Cancel);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        await Api.RunAsync(fixture, admin, id, dryRun: true);
        var bytes = await admin.GetByteArrayAsync($"{Api.Contacts}/imports/{id}/errors", Cancel);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Mail:", text);
        Assert.DoesNotContain("email:", text);
        // The Arabic problem names the field by its Arabic label.
        Assert.DoesNotContain("email:", text.Split("\r\n")[1].Split(',').Last());
    }
}
