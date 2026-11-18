using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.ImportExport.Tests;

/// <summary>Importing contacts from a file: upload and column matching, the memory of mappings, the check, the import, the error file,
/// custom fields, permissions and the workspace boundary.</summary>
public sealed class ImportTests(ImportExportFixture fixture) : IClassFixture<ImportExportFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static CancellationToken Cancel => Api.Cancel;

    private async Task<HttpClient> AdminAsync() => await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

    private static async Task<JsonElement> ListContactsAsync(HttpClient client, string search) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(search)}&take=50", Cancel);

    [Fact]
    public async Task Plain_headers_are_matched_to_fields_and_the_file_is_imported_after_a_check()
    {
        using var admin = await AdminAsync();
        var tag = Api.Unique("Plain");
        var csv = $"Name,Email,Phone,Street,City,Country\r\n{tag} One FZCO,one@{tag.Replace(' ', '-').ToLowerInvariant()}.example,+971 4 123 4567,12 Street 3,Dubai,United Arab Emirates\r\n" +
                  $"{tag} Two Trading,,+968 2412 3456,Port Road,Muscat,Oman\r\n";
        var upload = await Api.UploadCsvAsync(admin, csv);
        var id = upload.GetProperty("id").GetGuid();
        Assert.Equal("uploaded", upload.GetProperty("status").GetString());
        Assert.Equal(2, upload.GetProperty("rowCount").GetInt32());
        var fields = upload.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("header").GetString()!, c => c.GetProperty("field").GetString());
        Assert.Equal("name", fields["Name"]);
        Assert.Equal("email", fields["Email"]);
        Assert.Equal("phone", fields["Phone"]);
        Assert.Equal("addressLine1", fields["Street"]);
        Assert.Equal("city", fields["City"]);
        Assert.Equal("country", fields["Country"]);

        // A check writes nothing.
        var checkedSession = await Api.RunAsync(fixture, admin, id, dryRun: true);
        Assert.Equal("checked", checkedSession.GetProperty("status").GetString());
        Assert.Equal(2, checkedSession.GetProperty("rowsValid").GetInt32());
        Assert.Equal(0, checkedSession.GetProperty("rowsImported").GetInt32());
        Assert.Equal(0, (await ListContactsAsync(admin, tag)).GetProperty("items").GetArrayLength());

        var done = await Api.RunAsync(fixture, admin, id, dryRun: false);
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(2, done.GetProperty("rowsImported").GetInt32());
        var items = (await ListContactsAsync(admin, tag)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var one = items.Single(i => i.GetProperty("nameEn").GetString()!.Contains("One"));
        Assert.Equal("organisation", one.GetProperty("kind").GetString());
        Assert.Equal("Dubai", one.GetProperty("city").GetString());
        var oman = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{items.Single(i => i.GetProperty("nameEn").GetString()!.Contains("Two")).GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal("OM", oman.GetProperty("country").GetString());

        // Importing the same file again writes nothing again: the answer is the finished file.
        using var again = await admin.PostAsJsonAsync($"{Api.Contacts}/imports/{id}/run", new { dryRun = false }, Cancel);
        var repeated = await Api.ReadAsync(again, HttpStatusCode.OK);
        Assert.Equal("completed", repeated.GetProperty("status").GetString());
        Assert.Equal(2, (await ListContactsAsync(admin, tag)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_changed_by_of_an_imported_contact_is_the_person_who_ran_the_import()
    {
        using var admin = await AdminAsync();
        var name = Api.Unique("Audited") + " LLC";
        var session = await Api.UploadCsvAsync(admin, $"Name\r\n{name}\r\n");
        await Api.RunAsync(fixture, admin, session.GetProperty("id").GetGuid(), dryRun: false);
        var contact = (await ListContactsAsync(admin, name)).GetProperty("items")[0];
        await using var db = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT id FROM identity.users WHERE email = @email", db);
        command.Parameters.AddWithValue("email", Env.Email(Env.TenantA, "admin"));
        var adminId = (Guid)(await command.ExecuteScalarAsync(Cancel))!;
        Assert.Equal(adminId, await CreatedByAsync(contact.GetProperty("id").GetGuid()));
    }

    private async Task<Guid> CreatedByAsync(Guid contact)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT created_by FROM contacts.contacts WHERE id = @id", admin);
        command.Parameters.AddWithValue("id", contact);
        return (Guid)(await command.ExecuteScalarAsync(Cancel))!;
    }

    [Fact]
    public async Task Rows_with_problems_are_reported_in_both_languages_and_in_an_error_file_and_nothing_is_written_unless_asked()
    {
        using var admin = await AdminAsync();
        var tag = Api.Unique("Mixed");
        var csv = $"Name,Email,Phone,TRN\r\n{tag} Good LLC,good@example.ae,+971 4 123 4567,\r\n,nobody@example.ae,,\r\n{tag} BadMail LLC,not-an-email,,\r\n{tag} BadTrn LLC,,,12345\r\n";
        var id = (await Api.UploadCsvAsync(admin, csv)).GetProperty("id").GetGuid();

        var rejected = await Api.RunAsync(fixture, admin, id, dryRun: false, onError: "reject");
        Assert.Equal("failed", rejected.GetProperty("status").GetString());
        Assert.Equal("importRowsRejected", rejected.GetProperty("failureCode").GetString());
        Assert.Equal(3, rejected.GetProperty("rowsFailed").GetInt32());
        Assert.Equal(0, rejected.GetProperty("rowsImported").GetInt32());
        Assert.Equal(0, (await ListContactsAsync(admin, tag)).GetProperty("items").GetArrayLength());
        var issues = rejected.GetProperty("issues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("row").GetInt32() == 3 && i.GetProperty("field").GetString() is "nameEn" or "nameAr");
        Assert.Contains(issues, i => i.GetProperty("row").GetInt32() == 4 && i.GetProperty("field").GetString() == "email");
        Assert.Contains(issues, i => i.GetProperty("row").GetInt32() == 5 && i.GetProperty("field").GetString() == "taxRegistrationNumber");
        Assert.True(rejected.GetProperty("hasErrorFile").GetBoolean());

        // The error file lists the rows with their problems in English and in Arabic.
        using var errors = await admin.GetAsync($"{Api.Contacts}/imports/{id}/errors", Cancel);
        Assert.Equal(HttpStatusCode.OK, errors.StatusCode);
        var text = Encoding.UTF8.GetString(await errors.Content.ReadAsByteArrayAsync(Cancel));
        Assert.Contains("Row number", text);
        Assert.Contains("not-an-email", text);
        Assert.Matches("[؀-ۿ]", text);
        Assert.DoesNotContain("Good LLC", text);

        // Asked to skip, it writes the valid row and keeps the others in the error file.
        var skipped = await Api.RunAsync(fixture, admin, id, dryRun: false, onError: "skip");
        Assert.Equal("completed", skipped.GetProperty("status").GetString());
        Assert.Equal(1, skipped.GetProperty("rowsImported").GetInt32());
        Assert.Equal(3, skipped.GetProperty("rowsFailed").GetInt32());
        Assert.Equal(1, (await ListContactsAsync(admin, tag)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Issues_are_shown_in_the_callers_language()
    {
        using var admin = await AdminAsync();
        var id = (await Api.UploadCsvAsync(admin, "Name,Email\r\nAli Co,bad\r\n")).GetProperty("id").GetGuid();
        await Api.RunAsync(fixture, admin, id, dryRun: true);
        var english = (await Api.GetSessionAsync(admin, id)).GetProperty("issues")[0].GetProperty("message").GetString()!;
        using var arabicUser = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "arabic", ["contacts.contacts.create"], "ar"));
        var arabic = (await arabicUser.GetFromJsonAsync<JsonElement>($"{Api.Contacts}/imports/{id}", Cancel)).GetProperty("issues")[0].GetProperty("message").GetString()!;
        Assert.DoesNotMatch("[؀-ۿ]", english);
        Assert.Matches("[؀-ۿ]", arabic);
    }

    [Fact]
    public async Task A_mapping_the_person_chose_is_remembered_for_the_next_file_with_the_same_header()
    {
        using var admin = await AdminAsync();
        var header = "Ref No " + Guid.NewGuid().ToString("N")[..6];
        var first = await Api.UploadCsvAsync(admin, $"Name,{header}\r\nAcme Remember LLC,CODE-{Guid.NewGuid().ToString("N")[..6]}\r\n");
        var column = first.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("header").GetString() == header);
        Assert.Equal(JsonValueKind.Null, column.GetProperty("field").ValueKind);

        using var set = await admin.PutAsJsonAsync($"{Api.Contacts}/imports/{first.GetProperty("id").GetGuid()}/mapping",
            new { columns = new[] { new { index = 1, field = "code" } } }, Cancel);
        var updated = await Api.ReadAsync(set, HttpStatusCode.OK);
        Assert.Equal("code", updated.GetProperty("columns")[1].GetProperty("field").GetString());
        Assert.Equal("you", updated.GetProperty("columns")[1].GetProperty("source").GetString());

        var second = await Api.UploadCsvAsync(admin, $"Name,{header}\r\nOther LLC,X1\r\n");
        var remembered = second.GetProperty("columns")[1];
        Assert.Equal("code", remembered.GetProperty("field").GetString());
        Assert.Equal("memory", remembered.GetProperty("source").GetString());

        // Clearing it forgets it.
        using var clear = await admin.PutAsJsonAsync($"{Api.Contacts}/imports/{second.GetProperty("id").GetGuid()}/mapping", new { columns = new[] { new { index = 1, field = (string?)null } } }, Cancel);
        await Api.ReadAsync(clear, HttpStatusCode.OK);
        var third = await Api.UploadCsvAsync(admin, $"Name,{header}\r\nThird LLC,X2\r\n");
        Assert.Equal(JsonValueKind.Null, third.GetProperty("columns")[1].GetProperty("field").ValueKind);
    }

    [Fact]
    public async Task A_mapping_must_name_known_columns_and_fields_and_leave_no_required_field_open()
    {
        using var admin = await AdminAsync();
        var session = await Api.UploadCsvAsync(admin, "A,B\r\nx,y\r\n");
        var id = session.GetProperty("id").GetGuid();
        foreach (var body in new object[]
        {
            new { columns = new[] { new { index = 9, field = "email" } } },
            new { columns = new[] { new { index = 0, field = "noSuchField" } } },
            new { columns = Array.Empty<object>() },
        })
        {
            using var response = await admin.PutAsJsonAsync($"{Api.Contacts}/imports/{id}/mapping", body, Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        // A field has one column: choosing it again takes it from the column that had it.
        using var taken = await admin.PutAsJsonAsync($"{Api.Contacts}/imports/{id}/mapping", new { columns = new[] { new { index = 0, field = "email" }, new { index = 1, field = "email" } } }, Cancel);
        var moved = await Api.ReadAsync(taken, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, moved.GetProperty("columns")[0].GetProperty("field").ValueKind);
        Assert.Equal("email", moved.GetProperty("columns")[1].GetProperty("field").GetString());
        // No column fills a name: the file says which fields are open, and a run reports every row's missing name (nothing is written).
        var open = await Api.ReadAsync(await admin.GetAsync($"{Api.Contacts}/imports/{id}", Cancel), HttpStatusCode.OK);
        Assert.Contains("Name", open.GetProperty("missingFields").EnumerateArray().Select(f => f.GetString()).ToList());
        using var run = await admin.PostAsJsonAsync($"{Api.Contacts}/imports/{id}/run", new { dryRun = false }, Cancel);
        var rejected = await Api.ReadAsync(run, HttpStatusCode.OK);
        Assert.Equal("failed", rejected.GetProperty("status").GetString());
        Assert.Equal(1, rejected.GetProperty("rowsFailed").GetInt32());
    }

    [Fact]
    public async Task A_person_is_attached_to_the_organisation_named_in_the_file_even_when_it_comes_from_the_same_file()
    {
        using var admin = await AdminAsync();
        var org = Api.Unique("Parent") + " LLC";
        var csv = $"Name,Kind,Organisation,Job title\r\n{org},organisation,,\r\nSara Person,person,{org},Buyer\r\nNoOrg Person,person,Does Not Exist LLC {Guid.NewGuid():N},\r\n";
        var id = (await Api.UploadCsvAsync(admin, csv)).GetProperty("id").GetGuid();
        var check = await Api.RunAsync(fixture, admin, id, dryRun: true, onError: "skip");
        Assert.Equal(2, check.GetProperty("rowsValid").GetInt32());
        Assert.Equal("organisation", check.GetProperty("issues")[0].GetProperty("field").GetString());
        var done = await Api.RunAsync(fixture, admin, id, dryRun: false, onError: "skip");
        Assert.Equal(2, done.GetProperty("rowsImported").GetInt32());
        var parent = (await ListContactsAsync(admin, org)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "organisation");
        var person = (await ListContactsAsync(admin, "Sara Person")).GetProperty("items").EnumerateArray().First(i => i.GetProperty("nameEn").GetString() == "Sara Person");
        Assert.Equal(parent.GetProperty("id").GetGuid(), person.GetProperty("organisationId").GetGuid());
    }

    [Fact]
    public async Task A_reference_used_twice_in_the_file_or_already_taken_is_refused()
    {
        using var admin = await AdminAsync();
        var code = "IMP-" + Guid.NewGuid().ToString("N")[..8];
        var first = await Api.UploadCsvAsync(admin, $"Name,Reference\r\nFirst {code},{code}\r\n");
        await Api.RunAsync(fixture, admin, first.GetProperty("id").GetGuid(), dryRun: false);
        var second = await Api.UploadCsvAsync(admin, $"Name,Reference\r\nSecond {code},{code}\r\nThird {code},NEW-{code}\r\nFourth {code},NEW-{code}\r\n");
        var check = await Api.RunAsync(fixture, admin, second.GetProperty("id").GetGuid(), dryRun: true, onError: "skip");
        var issues = check.GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal(2, check.GetProperty("rowsFailed").GetInt32());
        Assert.Contains(issues, i => i.GetProperty("row").GetInt32() == 2 && i.GetProperty("code").GetString() == "contactsCodeTaken");
        Assert.Contains(issues, i => i.GetProperty("row").GetInt32() == 4 && i.GetProperty("code").GetString() == "contactsCodeTaken");
    }

    [Fact]
    public async Task An_xlsx_file_is_imported_like_a_csv_file()
    {
        using var admin = await AdminAsync();
        var name = Api.Unique("Sheet") + " LLC";
        var bytes = TabularFileTests.Workbook([["Name", "Mobile", "Credit limit", "Tags"], [name, "#n:971501234567", "#n:2500.5", "Customer; VIP"]]);
        var upload = await Api.UploadAsync(admin, "contacts.xlsx", bytes);
        Assert.Equal("xlsx", upload.GetProperty("format").GetString());
        var done = await Api.RunAsync(fixture, admin, upload.GetProperty("id").GetGuid(), dryRun: false);
        Assert.Equal("completed", done.GetProperty("status").GetString());
        var contact = (await ListContactsAsync(admin, name)).GetProperty("items")[0];
        var full = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal(2500.5m, decimal.Parse(full.GetProperty("creditLimit").GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("971501234567", full.GetProperty("mobile").GetString());
        Assert.Equal(2, full.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Custom_fields_are_columns_of_the_file()
    {
        using var admin = await AdminAsync();
        var label = "Region " + Guid.NewGuid().ToString("N")[..6];
        using var created = await admin.PostAsJsonAsync("/api/customfields/fields", new
        {
            recordType = "contacts.contacts", labelEn = label, labelAr = "منطقة " + label[7..], type = "select", required = false,
            options = new[] { new { value = "north", labelEn = "North", labelAr = "الشمال" }, new { value = "south", labelEn = "South", labelAr = "الجنوب" } },
        }, Cancel);
        var field = await Api.ReadAsync(created, HttpStatusCode.Created);
        var key = field.GetProperty("key").GetString()!;
        var fieldInfo = await admin.GetFromJsonAsync<JsonElement>($"{Api.Contacts}/fields", Cancel);
        Assert.Contains(fieldInfo.GetProperty("items").EnumerateArray(), f => f.GetProperty("key").GetString() == "custom." + key && f.GetProperty("custom").GetBoolean());

        var name = Api.Unique("Custom") + " LLC";
        var upload = await Api.UploadCsvAsync(admin, $"Name,{label}\r\n{name},South\r\n{name} Bad,Middle\r\n");
        Assert.Equal("custom." + key, upload.GetProperty("columns")[1].GetProperty("field").GetString());
        var done = await Api.RunAsync(fixture, admin, upload.GetProperty("id").GetGuid(), dryRun: false, onError: "skip");
        Assert.Equal(1, done.GetProperty("rowsImported").GetInt32());
        Assert.Equal("custom." + key, done.GetProperty("issues")[0].GetProperty("field").GetString());
        var contact = (await ListContactsAsync(admin, name)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("nameEn").GetString() == name);
        var full = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal("south", full.GetProperty("customFields").GetProperty(key).GetString());
    }

    [Fact]
    public async Task A_template_lists_the_fields_and_a_file_that_is_not_a_table_is_refused_with_a_reason()
    {
        using var admin = await AdminAsync();
        using var template = await admin.GetAsync($"{Api.Contacts}/template", Cancel);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var text = await template.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("E-mail", text);
        foreach (var (name, bytes, code) in new (string, byte[], string)[]
        {
            ("notes.pdf", [1, 2, 3], "importUnknownFormat"),
            ("empty.csv", [], "importEmpty"),
            ("broken.xlsx", Encoding.UTF8.GetBytes("a,b"), "importDamaged"),
        })
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.Add("X-File-Name", name);
            using var response = await admin.PostAsync($"{Api.Contacts}/imports", content, Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(code, (await response.Content.ReadAsStringAsync(Cancel)));
        }
        using var noName = await admin.PostAsync($"{Api.Contacts}/imports", new ByteArrayContent(Encoding.UTF8.GetBytes("A\n1\n")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
    }

    [Fact]
    public async Task A_json_upload_with_base64_is_read_like_the_bytes()
    {
        using var admin = await AdminAsync();
        using var response = await admin.PostAsJsonAsync($"{Api.Contacts}/imports", new { file = "api.csv", data = Convert.ToBase64String(Encoding.UTF8.GetBytes("Name\r\nApi LLC\r\n")) }, Cancel);
        var session = await Api.ReadAsync(response, HttpStatusCode.Created);
        Assert.Equal(1, session.GetProperty("rowCount").GetInt32());
        using var bad = await admin.PostAsJsonAsync($"{Api.Contacts}/imports", new { file = "api.csv", data = "%%%" }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Five_thousand_rows_are_imported_in_one_run_and_the_error_file_is_not_needed()
    {
        using var admin = await AdminAsync();
        var tag = Api.Unique("Bulk");
        var csv = new StringBuilder("Name,Email,Phone,Street,City,Country\r\n");
        for (var i = 1; i <= 5000; i++)
        {
            csv.Append($"{tag} Trading {i},bulk{i}@{tag.Replace(' ', '-').ToLowerInvariant()}.example,+971 4 {100 + i % 800} {1000 + i % 8000},{i} Street 3,Dubai,United Arab Emirates\r\n");
        }
        var session = await Api.UploadCsvAsync(admin, csv.ToString());
        var id = session.GetProperty("id").GetGuid();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var check = await Api.RunAsync(fixture, admin, id, dryRun: true);
        Assert.Equal("checked", check.GetProperty("status").GetString());
        watch.Restart();
        var done = await Api.RunAsync(fixture, admin, id, dryRun: false);
        watch.Stop();
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(5000, done.GetProperty("rowsImported").GetInt32());
        Assert.False(done.GetProperty("hasErrorFile").GetBoolean());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(90), $"5,000 rows took {watch.Elapsed.TotalSeconds:0} s");
        var filtered = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(tag + " Trading 5000")}&take=5", Cancel);
        Assert.Contains(filtered.GetProperty("items").EnumerateArray(), i => i.GetProperty("nameEn").GetString() == tag + " Trading 5000");
    }

    [Fact]
    public async Task Only_people_who_may_create_contacts_may_import_them_and_a_file_is_the_workspaces_own()
    {
        using var admin = await AdminAsync();
        var session = await Api.UploadCsvAsync(admin, "Name\r\nSecret Co\r\n");
        var id = session.GetProperty("id").GetGuid();
        using var reader = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "reader", ["contacts.contacts.read"]));
        using var creator = await Env.SignInAsync(await Api.UserHoldingAsync(Env, admin, "creator", ["contacts.contacts.create"]));
        foreach (var path in new[] { $"{Api.Contacts}/fields", $"{Api.Contacts}/imports", $"{Api.Contacts}/imports/{id}", $"{Api.Contacts}/template" })
        {
            using var denied = await reader.GetAsync(path, Cancel);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await creator.GetAsync(path, Cancel);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var upload = await reader.PostAsync($"{Api.Contacts}/imports", new ByteArrayContent([65, 10, 49]), Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);

        // Another workspace's administrator reaches none of it.
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        foreach (var (method, path) in new[] { (HttpMethod.Get, $"{Api.Contacts}/imports/{id}"), (HttpMethod.Get, $"{Api.Contacts}/imports/{id}/errors"), (HttpMethod.Post, $"{Api.Contacts}/imports/{id}/run") })
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
            {
                request.Content = JsonContent.Create(new { dryRun = true });
            }
            using var response = await other.SendAsync(request, Cancel);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var deleted = await other.DeleteAsync($"{Api.Contacts}/imports/{id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Api.Contacts}/imports/{id}", Cancel)).StatusCode);
        var otherList = await other.GetFromJsonAsync<JsonElement>($"{Api.Contacts}/imports", Cancel);
        Assert.DoesNotContain(otherList.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Another_workspaces_organisations_and_references_are_not_found_by_an_import()
    {
        using var admin = await AdminAsync();
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var theirs = Api.Unique("Their org") + " LLC";
        var code = "THEIR-" + Guid.NewGuid().ToString("N")[..8];
        using var made = await other.PostAsJsonAsync("/api/contacts/contacts", new { kind = "organisation", code, nameEn = theirs, nameAr = "", country = "AE", defaultCurrency = "AED", tags = Array.Empty<string>() }, Cancel);
        await Api.ReadAsync(made, HttpStatusCode.Created);
        // A person naming their organisation finds none; a reference they use is free here.
        var id = (await Api.UploadCsvAsync(admin, $"Name,Kind,Organisation,Reference\r\nCross Person,person,{theirs},{code}\r\n")).GetProperty("id").GetGuid();
        var checkedSession = await Api.RunAsync(fixture, admin, id, dryRun: true, onError: "skip");
        var issues = checkedSession.GetProperty("issues").EnumerateArray().ToList();
        Assert.Contains(issues, i => i.GetProperty("field").GetString() == "organisation");
        Assert.DoesNotContain(issues, i => i.GetProperty("code").GetString() == "contactsCodeTaken");
    }

    [Fact]
    public async Task A_discarded_file_is_gone_and_a_file_that_is_running_cannot_be_discarded()
    {
        using var admin = await AdminAsync();
        var id = (await Api.UploadCsvAsync(admin, "Name\r\nTemp Co\r\n")).GetProperty("id").GetGuid();
        // A file past the inline limit runs in the background: until a worker has taken it, it cannot be discarded.
        var rows = new StringBuilder("Name\r\n");
        for (var i = 0; i < 5001; i++)
        {
            rows.Append("Queued ").Append(i).Append(" Co\r\n");
        }
        id = (await Api.UploadCsvAsync(admin, rows.ToString())).GetProperty("id").GetGuid();
        using var run = await admin.PostAsJsonAsync($"{Api.Contacts}/imports/{id}/run", new { dryRun = true }, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
        using var busy = await admin.DeleteAsync($"{Api.Contacts}/imports/{id}", Cancel);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        await fixture.RunJobsAsync();
        var finished = await Api.GetSessionAsync(admin, id);
        Assert.Equal("checked", finished.GetProperty("status").GetString());
        Assert.Equal(5001, finished.GetProperty("rowsValid").GetInt32());
        Assert.NotNull(finished.GetProperty("jobId").GetString());
        using var removed = await admin.DeleteAsync($"{Api.Contacts}/imports/{id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var gone = await admin.GetAsync($"{Api.Contacts}/imports/{id}", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
