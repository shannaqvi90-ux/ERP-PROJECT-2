using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.CustomFields.Tests;

/// <summary>Custom fields on contacts, the first consumer: the form's fields and defaults, values
/// saved with the contact and checked (required, kind, option, lookup), the API, the list (columns,
/// filters, search, saved views), the audit trail, and the workspace boundary.</summary>
public sealed class ContactFieldTests(CustomFieldsFixture fixture) : IClassFixture<CustomFieldsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    internal sealed record Fields(string Segment, string Region, string Score, string Since, string Vip, string Parent);

    private static string Key(JsonElement field) => field.GetProperty("key").GetString()!;

    /// <summary>The fields the tests share, defined once (a kind of record holds at most 40).</summary>
    private async Task<(HttpClient Admin, Fields Fields, string Stem)> DefineAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        await Shared.WaitAsync(Api.Cancel);
        try
        {
            if (_fields is null)
            {
                var stem = "Probe" + Guid.NewGuid().ToString("N")[..6];
                var segment = await Api.DefineAsync(admin, Api.Field("select", $"{stem} segment", "شريحة", new
                {
                    options = new[] { new { value = (string?)"retail", labelEn = "Retail", labelAr = "تجزئة" }, new { value = (string?)"wholesale", labelEn = "Wholesale", labelAr = "جملة" } },
                }));
                var region = await Api.DefineAsync(admin, Api.Field("text", $"{stem} region", "منطقة", new { searchable = true, required = true, @default = "North" }));
                var score = await Api.DefineAsync(admin, Api.Field("number", $"{stem} score", "درجة", new { decimals = 1 }));
                var since = await Api.DefineAsync(admin, Api.Field("date", $"{stem} since", "منذ"));
                var vip = await Api.DefineAsync(admin, Api.Field("boolean", $"{stem} vip", "مهم"));
                var parent = await Api.DefineAsync(admin, Api.Field("lookup", $"{stem} parent", "أصل", new { lookupTarget = "contacts.contacts" }));
                _fields = (new Fields(Key(segment), Key(region), Key(score), Key(since), Key(vip), Key(parent)), stem);
            }
        }
        finally
        {
            Shared.Release();
        }
        return (admin, _fields.Value.Fields, _fields.Value.Stem);
    }

    private static readonly SemaphoreSlim Shared = new(1, 1);
    private static (Fields Fields, string Stem)? _fields;

    [Fact]
    public async Task A_new_contacts_form_starts_with_the_defaults_and_a_saved_contact_keeps_its_values()
    {
        var (admin, f, _) = await DefineAsync();
        using var _ = admin;
        var defaults = await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts/defaults", Api.Cancel);
        var region = defaults.GetProperty("customFields").EnumerateArray().Single(e => e.GetProperty("key").GetString() == f.Region);
        Assert.Equal("North", region.GetProperty("value").GetString());
        Assert.True(region.GetProperty("required").GetBoolean());

        var parent = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Parent LLC")));
        var created = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Child LLC"), new()
        {
            [f.Segment] = "wholesale", [f.Region] = "South", [f.Score] = 12.5, [f.Since] = "2026-02-01", [f.Vip] = true, [f.Parent] = parent.GetProperty("id").GetGuid(),
        }));
        Assert.Equal("wholesale", Api.Entry(created, f.Segment).GetProperty("value").GetString());
        Assert.Equal("12.5", Api.Entry(created, f.Score).GetProperty("value").GetString());
        Assert.Equal("2026-02-01", Api.Entry(created, f.Since).GetProperty("value").GetString());
        Assert.True(Api.Entry(created, f.Vip).GetProperty("value").GetBoolean());
        var lookup = Api.Entry(created, f.Parent);
        Assert.Equal(parent.GetProperty("id").GetGuid(), lookup.GetProperty("value").GetGuid());
        Assert.Equal(parent.GetProperty("nameEn").GetString(), lookup.GetProperty("valueLabel").GetString());

        // A key left out keeps its value; null clears it; the contact's own fields are saved too.
        var id = created.GetProperty("id").GetGuid();
        var update = Api.Contact(created.GetProperty("nameEn").GetString()!, new() { [f.Region] = "East", [f.Vip] = null });
        update["version"] = created.GetProperty("version").GetUInt32();
        var saved = await admin.PutAsJsonAsync($"/api/contacts/contacts/{id}", update, Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await Api.Json(saved);
        Assert.Equal("East", Api.Entry(after, f.Region).GetProperty("value").GetString());
        Assert.Equal("wholesale", Api.Entry(after, f.Segment).GetProperty("value").GetString());
        Assert.False(Api.Entry(after, f.Vip).TryGetProperty("value", out var cleared) && cleared.ValueKind != JsonValueKind.Null);
        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{id}", Api.Cancel);
        Assert.Equal("East", Api.Entry(read, f.Region).GetProperty("value").GetString());
    }

    [Fact]
    public async Task Values_are_checked_against_the_field_and_a_required_field_without_one_is_refused()
    {
        var (admin, f, _) = await DefineAsync();
        using var _ = admin;
        async Task<JsonElement> RefusedAsync(Dictionary<string, object?> values, string key)
        {
            var response = await admin.PostAsJsonAsync("/api/contacts/contacts", Api.Contact(Api.Unique("Refused LLC"), values), Api.Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await Api.Json(response)).GetProperty("errors");
            Assert.True(errors.TryGetProperty($"customFields.{key}", out var list), $"no error for {key}: {errors}");
            return list[0];
        }
        Assert.Equal("customFieldOption", (await RefusedAsync(new() { [f.Segment] = "mystery" }, f.Segment)).GetProperty("code").GetString());
        Assert.Equal("customFieldNumber", (await RefusedAsync(new() { [f.Score] = "12.55" }, f.Score)).GetProperty("code").GetString());
        Assert.Equal("customFieldNumber", (await RefusedAsync(new() { [f.Score] = "abc" }, f.Score)).GetProperty("code").GetString());
        Assert.Equal("customFieldDate", (await RefusedAsync(new() { [f.Since] = "01/02/2026" }, f.Since)).GetProperty("code").GetString());
        Assert.Equal("customFieldBoolean", (await RefusedAsync(new() { [f.Vip] = "yes" }, f.Vip)).GetProperty("code").GetString());
        Assert.Equal("customFieldLookup", (await RefusedAsync(new() { [f.Parent] = Guid.NewGuid() }, f.Parent)).GetProperty("code").GetString());
        Assert.Equal("customFieldUnknown", (await RefusedAsync(new() { ["cfNoSuchField"] = "x" }, "cfNoSuchField")).GetProperty("code").GetString());
        // The required region cannot be cleared.
        Assert.Equal("customFieldRequired", (await RefusedAsync(new() { [f.Region] = null }, f.Region)).GetProperty("code").GetString());

        // A refused save writes nothing: no contact, no values.
        var name = Api.Unique("Nothing saved LLC");
        await admin.PostAsJsonAsync("/api/contacts/contacts", Api.Contact(name, new() { [f.Segment] = "mystery" }), Api.Cancel);
        var foundResponse = await admin.GetAsync($"/api/contacts/contacts?search={Uri.EscapeDataString(name)}", Api.Cancel);
        Assert.True(foundResponse.IsSuccessStatusCode, fixture.ServerErrors);
        var found = await Api.Json(foundResponse);
        Assert.Empty(found.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_field_without_a_default_that_is_required_must_be_given_on_a_new_contact()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var must = await Api.DefineAsync(admin, Api.Field("text", Api.Unique("Must have"), "إلزامي", new { required = true }));
        var response = await admin.PostAsJsonAsync("/api/contacts/contacts", Api.Contact(Api.Unique("Needs it LLC")), Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("customFieldRequired", (await Api.Json(response)).GetProperty("errors").GetProperty($"customFields.{Key(must)}")[0].GetProperty("code").GetString());
        // Switch it off and the contact saves again.
        var off = await admin.PutAsJsonAsync($"/api/customfields/fields/{must.GetProperty("id").GetGuid()}",
            new { labelEn = must.GetProperty("labelEn").GetString(), labelAr = must.GetProperty("labelAr").GetString(), required = true, isActive = false, version = must.GetProperty("version").GetUInt32() }, Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Free again LLC")));
    }

    [Fact]
    public async Task The_contacts_list_gets_the_fields_as_columns_with_filters_and_search()
    {
        var (admin, f, stem) = await DefineAsync();
        using var _ = admin;
        var one = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{stem} One LLC"), new() { [f.Segment] = "retail", [f.Region] = "Alpha harbour", [f.Score] = 3, [f.Since] = "2026-01-10", [f.Vip] = true }));
        var two = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{stem} Two LLC"), new() { [f.Segment] = "wholesale", [f.Region] = "Beta ridge", [f.Score] = 8.5, [f.Since] = "2026-03-20", [f.Vip] = false }));
        var three = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{stem} Three LLC"), new() { [f.Region] = "Gamma_100%" }));

        var definition = await admin.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/definition", Api.Cancel);
        var column = definition.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("key").GetString() == f.Score);
        Assert.Equal("number", column.GetProperty("type").GetString()!.ToLowerInvariant());
        Assert.True(column.GetProperty("filterable").GetBoolean());
        Assert.True(column.GetProperty("hidden").GetBoolean());
        var strings = definition.GetProperty("strings");
        Assert.Equal($"{stem} score", strings.GetProperty(column.GetProperty("labelKey").GetString()!).GetProperty("en").GetString());
        Assert.Equal("درجة", strings.GetProperty(column.GetProperty("labelKey").GetString()!).GetProperty("ar").GetString());

        async Task<List<Guid>> IdsAsync(string query)
        {
            var response = await admin.GetAsync($"/api/contacts/contacts?{query}&take=200", Api.Cancel);
            var text = await response.Content.ReadAsStringAsync(Api.Cancel);
            Assert.True(response.StatusCode == HttpStatusCode.OK, text + fixture.ServerErrors);
            return JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        }
        Guid Id(JsonElement c) => c.GetProperty("id").GetGuid();
        string F(string filter) => "filter=" + Uri.EscapeDataString($"{filter} and nameEn contains '{stem}'");

        Assert.Equal([Id(two)], await IdsAsync(F($"{f.Segment} eq 'wholesale'")));
        Assert.Equal([Id(one)], await IdsAsync(F($"{f.Segment} in ('retail')")));
        Assert.Equal([Id(two)], await IdsAsync(F($"{f.Score} gt 5")));
        Assert.Equal([Id(one)], await IdsAsync(F($"{f.Score} le 3.0")));
        Assert.Equal(new[] { Id(one), Id(two) }.Order().ToList(), (await IdsAsync(F($"{f.Score} is not null"))).Order().ToList());
        Assert.Equal([Id(three)], await IdsAsync(F($"{f.Score} is null")));
        Assert.Equal([Id(two)], await IdsAsync(F($"{f.Since} ge '2026-03-01'")));
        Assert.Equal([Id(one)], await IdsAsync(F($"{f.Since} lt '2026-02-01'")));
        Assert.Equal([Id(one)], await IdsAsync(F($"{f.Vip} eq true")));
        Assert.Equal([Id(three)], await IdsAsync(F($"{f.Region} contains '_100%'")));
        Assert.Equal([Id(one)], await IdsAsync(F($"{f.Region} startswith 'alpha'")));
        Assert.Equal([Id(two)], await IdsAsync(F($"{f.Region} endswith 'RIDGE'")));
        Assert.Equal(new[] { Id(one), Id(three) }.Order().ToList(), (await IdsAsync(F($"not {f.Segment} eq 'wholesale'"))).Order().ToList());

        // The text field is searched by the list's search box.
        Assert.Equal([Id(two)], await IdsAsync($"search={Uri.EscapeDataString("beta ridge")}"));
        Assert.Equal([Id(three)], await IdsAsync($"search={Uri.EscapeDataString("100%")}"));

        // Rows carry the values as properties named by the key.
        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(stem)}&take=50", Api.Cancel);
        var row = page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == Id(two));
        Assert.Equal("wholesale", row.GetProperty(f.Segment).GetString());
        Assert.Equal("8.5", row.GetProperty(f.Score).GetString());
        Assert.Equal("2026-03-20", row.GetProperty(f.Since).GetString());

        // A filter on a field that is not there is refused like any unknown column.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/contacts/contacts?filter={Uri.EscapeDataString("cfNothing eq 'x'")}", Api.Cancel)).StatusCode);
        // A value of the wrong kind is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/contacts/contacts?filter={Uri.EscapeDataString($"{f.Score} gt 'abc'")}", Api.Cancel)).StatusCode);
        // Custom fields are not sortable.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/contacts/contacts?sort={f.Score}", Api.Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_saved_view_may_use_a_custom_column_and_a_switched_off_field_leaves_the_list()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var field = await Api.DefineAsync(admin, Api.Field("text", Api.Unique("View column"), "عمود"));
        var key = Key(field);
        var saved = await admin.PostAsJsonAsync("/api/lists/contacts.contacts/views", new
        {
            name = Api.Unique("Column view"), columns = new[] { "nameEn", key }, filter = $"{key} eq 'x'",
        }, Api.Cancel);
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var off = await admin.PutAsJsonAsync($"/api/customfields/fields/{field.GetProperty("id").GetGuid()}", new
        {
            labelEn = field.GetProperty("labelEn").GetString(), labelAr = field.GetProperty("labelAr").GetString(), required = false, isActive = false,
            version = field.GetProperty("version").GetUInt32(),
        }, Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var definition = await admin.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/definition", Api.Cancel);
        Assert.DoesNotContain(definition.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == key);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/contacts/contacts?filter={Uri.EscapeDataString($"{key} eq 'x'")}", Api.Cancel)).StatusCode);
        // The saved view is still listed; the screen drops the column it can no longer show.
        var views = await admin.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/views", Api.Cancel);
        Assert.Contains(views.GetProperty("items").EnumerateArray(), v => v.GetProperty("columns").EnumerateArray().Any(c => c.GetString() == key));
    }

    [Fact]
    public async Task Changes_to_values_and_definitions_are_in_the_audit_trail()
    {
        var (admin, f, _) = await DefineAsync();
        using var _ = admin;
        var contact = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Audited LLC"), new() { [f.Segment] = "retail" }));
        await using var connection = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE table_schema = 'customfields' AND table_name = 'field_values' AND changes::text LIKE '%' || @record || '%'", connection);
        command.Parameters.AddWithValue("record", contact.GetProperty("id").GetGuid().ToString());
        Assert.True((long)(await command.ExecuteScalarAsync(Api.Cancel))! >= 2, "the contact's values (region default and segment) are audited");
        await using var definitions = new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE table_schema = 'customfields' AND table_name = 'definitions'", connection);
        Assert.True((long)(await definitions.ExecuteScalarAsync(Api.Cancel))! >= 6);
    }

    [Fact]
    public async Task Another_workspace_sees_none_of_the_fields_or_values()
    {
        var (admin, f, _) = await DefineAsync();
        using var _ = admin;
        var contact = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Private LLC"), new() { [f.Segment] = "retail" }));
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var theirs = await other.GetFromJsonAsync<JsonElement>("/api/customfields/fields?take=200", Api.Cancel);
        Assert.DoesNotContain(theirs.GetProperty("items").EnumerateArray(), x => x.GetProperty("key").GetString() == f.Segment);
        var definition = await other.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/definition", Api.Cancel);
        Assert.DoesNotContain(definition.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == f.Segment);
        // Their filter on our field is an unknown column, and our contact's id opens nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await other.GetAsync($"/api/contacts/contacts?filter={Uri.EscapeDataString($"{f.Segment} eq 'retail'")}", Api.Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", Api.Cancel)).StatusCode);
        // They cannot save a value for our contact, or point a lookup at it.
        var theirContact = await Api.CreateContactAsync(other, Api.Contact(Api.Unique("Theirs LLC")));
        var theirField = await Api.DefineAsync(other, Api.Field("lookup", Api.Unique("Their parent"), "أصل", new { lookupTarget = "contacts.contacts" }));
        var response = await other.PostAsJsonAsync("/api/contacts/contacts", Api.Contact(Api.Unique("Pointing LLC"), new() { [Key(theirField)] = contact.GetProperty("id").GetGuid() }), Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(Guid.Empty, theirContact.GetProperty("id").GetGuid());
        // Their values table holds none of ours (row-level security, as the application role).
        await using var app = await Env.OpenAppAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM customfields.field_values", app);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(Api.Cancel))!);
    }
}
