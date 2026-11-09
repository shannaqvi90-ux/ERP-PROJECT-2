using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.CustomFields.Tests;

/// <summary>Round 2 of the custom fields: print and export with custom columns and filters, sorting
/// and grouping by a custom field, a required field on every save, a select's default, a label that
/// is another field's, and the trail naming a field by its label and decimals.</summary>
public sealed class CustomFieldRoundTwoTests(CustomFieldsFixture fixture) : IClassFixture<CustomFieldsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private sealed record Defined(string Segment, string Credit, string Since, string Vip, string Stem);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Defined? _defined;

    private async Task<(HttpClient Admin, Defined Fields)> DefineAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        await Gate.WaitAsync(Api.Cancel);
        try
        {
            if (_defined is null)
            {
                var stem = "Two" + Guid.NewGuid().ToString("N")[..6];
                var segment = await Api.DefineAsync(admin, Api.Field("select", $"{stem} segment", "شريحة الجولة", new
                {
                    options = new[] { new { value = (string?)null, labelEn = "Retail", labelAr = "تجزئة" }, new { value = (string?)null, labelEn = "Wholesale", labelAr = "جملة" } },
                    @default = "Wholesale",
                }));
                var credit = await Api.DefineAsync(admin, Api.Field("number", $"{stem} limit", "حد الجولة", new { decimals = 2 }));
                var since = await Api.DefineAsync(admin, Api.Field("date", $"{stem} since", "منذ الجولة"));
                var vip = await Api.DefineAsync(admin, Api.Field("boolean", $"{stem} vip", "مهم الجولة"));
                _defined = new Defined(segment.GetProperty("key").GetString()!, credit.GetProperty("key").GetString()!, since.GetProperty("key").GetString()!, vip.GetProperty("key").GetString()!, stem);
            }
        }
        finally
        {
            Gate.Release();
        }
        return (admin, _defined);
    }

    [Fact]
    public async Task A_selects_default_may_be_named_by_its_label_and_is_stored_as_its_value()
    {
        var (admin, f) = await DefineAsync();
        using var _ = admin;
        var contact = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{f.Stem} Default LLC")));
        Assert.Equal("wholesale", Api.Entry(contact, f.Segment).GetProperty("value").GetString());
        // By the Arabic label, or by the option's own value, too.
        var byArabic = await admin.PostAsJsonAsync("/api/customfields/fields", Api.Field("select", Api.Unique("Incoterm"), "شرط", new
        {
            options = new[] { new { value = (string?)null, labelEn = "EXW", labelAr = "تسليم المصنع" }, new { value = (string?)null, labelEn = "FOB", labelAr = "تسليم السفينة" } },
            @default = "fob",
        }), Api.Cancel);
        Assert.Equal(HttpStatusCode.Created, byArabic.StatusCode);
        Assert.Equal("fOB", (await Api.Json(byArabic)).GetProperty("default").GetString());
    }

    [Fact]
    public async Task A_label_that_is_a_built_in_fields_label_is_refused_in_both_languages()
    {
        var (admin, _) = await DefineAsync();
        using var _ = admin;
        var english = await admin.PostAsJsonAsync("/api/customfields/fields", Api.Field("number", "  credit   LIMIT ", "حد آخر"), Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, english.StatusCode);
        Assert.Contains("labelEn", await english.Content.ReadAsStringAsync(Api.Cancel));
        var arabic = await admin.PostAsJsonAsync("/api/customfields/fields", Api.Field("number", Api.Unique("Second limit"), "حد الائتمان"), Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, arabic.StatusCode);
        Assert.Contains("labelAr", await arabic.Content.ReadAsStringAsync(Api.Cancel));
        var fine = await admin.PostAsJsonAsync("/api/customfields/fields", Api.Field("number", Api.Unique("Credit limit review"), "مراجعة"), Api.Cancel);
        Assert.Equal(HttpStatusCode.Created, fine.StatusCode);
    }

    [Fact]
    public async Task A_required_field_holds_on_every_save_and_an_existing_record_gets_the_default()
    {
        var (admin, _) = await DefineAsync();
        using var _ = admin;
        var before = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique("Before required LLC")));
        var field = await Api.DefineAsync(admin, Api.Field("text", Api.Unique("Licence class"), "فئة", new { required = true, @default = "General" }));
        var key = field.GetProperty("key").GetString()!;

        // The record was saved before the field existed: it holds no value until it is saved again.
        var update = Api.Contact(before.GetProperty("nameEn").GetString()!);
        update["version"] = before.GetProperty("version").GetUInt32();
        var saved = await admin.PutAsJsonAsync($"/api/contacts/contacts/{before.GetProperty("id").GetGuid()}", update, Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("General", Api.Entry(await Api.Json(saved), key).GetProperty("value").GetString());

        // Once it holds one, clearing it is refused on an update as on a creation.
        var again = Api.Contact(before.GetProperty("nameEn").GetString()!, new() { [key] = "" });
        again["version"] = (await Api.Json(await admin.GetAsync($"/api/contacts/contacts/{before.GetProperty("id").GetGuid()}", Api.Cancel))).GetProperty("version").GetUInt32();
        var refused = await admin.PutAsJsonAsync($"/api/contacts/contacts/{before.GetProperty("id").GetGuid()}", again, Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains($"customFields.{key}", await refused.Content.ReadAsStringAsync(Api.Cancel));
    }

    [Fact]
    public async Task A_list_is_sorted_and_grouped_by_a_custom_field_through_every_page()
    {
        var (admin, f) = await DefineAsync();
        using var _ = admin;
        var values = new (string Segment, double? Limit)[] { ("retail", 30.5), ("wholesale", 10.25), ("retail", null), ("wholesale", 20), ("retail", 5) };
        var made = new List<Guid>();
        foreach (var (segment, limit) in values)
        {
            var c = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{f.Stem} Sorted LLC"), new() { [f.Segment] = segment, [f.Credit] = limit }));
            made.Add(c.GetProperty("id").GetGuid());
        }
        var filter = "filter=" + Uri.EscapeDataString($"nameEn contains '{f.Stem} Sorted'");

        async Task<List<string?>> PagedAsync(string sort)
        {
            var seen = new List<string?>();
            string? after = null;
            do
            {
                var url = $"/api/contacts/contacts?{filter}&sort={sort}&take=2" + (after is null ? "" : $"&after={Uri.EscapeDataString(after)}");
                var response = await admin.GetAsync(url, Api.Cancel);
                var text = await response.Content.ReadAsStringAsync(Api.Cancel);
                Assert.True(response.StatusCode == HttpStatusCode.OK, text + fixture.ServerErrors);
                var page = JsonDocument.Parse(text).RootElement;
                seen.AddRange(page.GetProperty("items").EnumerateArray().Select(r => r.TryGetProperty(f.Credit, out var v) ? v.GetString() : null));
                after = page.GetProperty("next").ValueKind == JsonValueKind.String ? page.GetProperty("next").GetString() : null;
            }
            while (after is not null);
            return seen;
        }

        // Ascending: values in numeric order (nulls last in the store), every record once across pages.
        Assert.Equal(["5.00", "10.25", "20.00", "30.50", null], await PagedAsync(f.Credit));
        Assert.Equal([null, "30.50", "20.00", "10.25", "5.00"], await PagedAsync("-" + f.Credit));

        var grouped = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?{filter}&groupBy={f.Segment}&take=50", Api.Cancel);
        var groups = grouped.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetString()!, g => g.GetProperty("count").GetInt32());
        Assert.Equal(3, groups["retail"]);
        Assert.Equal(2, groups["wholesale"]);

        // A lookup is neither sorted nor grouped by.
        var lookup = await Api.DefineAsync(admin, Api.Field("lookup", Api.Unique("Parent firm"), "أصل", new { lookupTarget = "contacts.contacts" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/contacts/contacts?sort={lookup.GetProperty("key").GetString()}", Api.Cancel)).StatusCode);
        var definition = await admin.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/definition", Api.Cancel);
        var column = definition.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("key").GetString() == f.Credit);
        Assert.True(column.GetProperty("sortable").GetBoolean());
        Assert.Equal(2, column.GetProperty("decimals").GetInt32());
    }

    [Fact]
    public async Task Print_and_export_take_custom_columns_and_filters_in_both_languages()
    {
        var (admin, f) = await DefineAsync();
        using var _ = admin;
        var name = Api.Unique($"{f.Stem} Printed LLC");
        await Api.CreateContactAsync(admin, Api.Contact(name, new() { [f.Segment] = "retail", [f.Credit] = 1234.5, [f.Since] = "2026-03-20", [f.Vip] = true }));
        var filter = Uri.EscapeDataString($"{f.Segment} eq 'retail' and {f.Credit} ge 1000 and nameEn eq '{name}'");

        foreach (var format in new[] { "csv", "xlsx", "pdf", "json" })
        {
            foreach (var language in new[] { "en", "ar" })
            {
                var url = $"/api/reports/lists/contacts.contacts?filter={filter}&columns=nameEn,{f.Segment},{f.Credit},{f.Since},{f.Vip}&format={format}&language={language}&numerals=latn";
                var response = await admin.GetAsync(url, Api.Cancel);
                var bytes = await response.Content.ReadAsByteArrayAsync(Api.Cancel);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{format} {language}: {System.Text.Encoding.UTF8.GetString(bytes)}{fixture.ServerErrors}");
                Assert.NotEmpty(bytes);
                if (format != "json")
                {
                    continue;
                }
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.Contains(name, text);
                // The tenant's own words for the columns and the filter, and the declared decimals.
                Assert.Contains(language == "ar" ? "حد الجولة" : $"{f.Stem} limit", text);
                Assert.Contains("1,234.50", text);
            }
        }
        // A filter alone, with the default columns (the crash of round 1).
        var plain = await admin.GetAsync($"/api/reports/lists/contacts.contacts?filter={filter}&columns=nameEn,email&format=csv&language=en&numerals=latn", Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        // A column that is not there is still refused.
        var unknown = await admin.GetAsync("/api/reports/lists/contacts.contacts?columns=nameEn,cfNothing&format=csv&language=en&numerals=latn", Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task The_trail_names_a_custom_field_by_its_labels_and_decimals()
    {
        var (admin, f) = await DefineAsync();
        using var _ = admin;
        var contact = await Api.CreateContactAsync(admin, Api.Contact(Api.Unique($"{f.Stem} Trail LLC"), new() { [f.Credit] = 1234.5 }));
        var update = Api.Contact(contact.GetProperty("nameEn").GetString()!, new() { [f.Credit] = 1234.56 });
        update["version"] = contact.GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", update, Api.Cancel)).StatusCode);
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/records/{contact.GetProperty("id").GetGuid()}?type=contacts.contacts", Api.Cancel);
        var field = history.GetProperty("fields").GetProperty($"custom_fields.{f.Credit}");
        Assert.Equal($"{f.Stem} limit", field.GetProperty("labelEn").GetString());
        Assert.Equal("حد الجولة", field.GetProperty("labelAr").GetString());
        Assert.Equal(2, field.GetProperty("decimals").GetInt32());
        // The audit log's line for the change says the same, in the reader's language.
        var log = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/log?filter={Uri.EscapeDataString($"recordId eq '{contact.GetProperty("id").GetGuid()}'")}&language=ar", Api.Cancel);
        Assert.Contains(log.GetProperty("items").EnumerateArray(), r => r.GetProperty("changes").GetString()!.Contains("حد الجولة"));
    }
}
