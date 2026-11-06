using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.CustomFields.Tests;

public sealed class CustomFieldsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();

    /// <summary>What the server logged as an error (for a failure message).</summary>
    public string ServerErrors => string.Join("\n", Env.Factory.ErrorLog.Entries.TakeLast(3));
}

internal static class Api
{
    public static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    public static string Unique(string stem) => $"{stem} {Guid.NewGuid().ToString("N")[..8]}";

    public static async Task<JsonElement> DefineAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/customfields/fields", body, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static object Field(string type, string labelEn, string labelAr = "حقل", object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["recordType"] = "contacts.contacts", ["type"] = type, ["labelEn"] = labelEn, ["labelAr"] = labelAr, ["required"] = false };
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }
        return body;
    }

    public static Dictionary<string, object?> Contact(string name, Dictionary<string, object?>? customFields = null) => new()
    {
        ["kind"] = "organisation", ["nameEn"] = name, ["nameAr"] = "", ["country"] = "AE", ["defaultCurrency"] = "AED", ["tags"] = Array.Empty<string>(),
        ["customFields"] = customFields,
    };

    public static async Task<JsonElement> CreateContactAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/contacts/contacts", body, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static JsonElement Entry(JsonElement contact, string key) =>
        contact.GetProperty("customFields").EnumerateArray().Single(e => e.GetProperty("key").GetString() == key);
}

/// <summary>Defining custom fields: the six kinds, names made from the English label, validation in
/// the caller's language, what never changes, concurrency and the permissions.</summary>
public sealed class CustomFieldDefinitionTests(CustomFieldsFixture fixture) : IClassFixture<CustomFieldsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task An_administrator_defines_a_field_of_every_kind_and_reads_it_back()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var stem = Api.Unique("Kind");
        var text = await Api.DefineAsync(admin, Api.Field("text", $"{stem} text", "نص", new { searchable = true, @default = "hello" }));
        Assert.StartsWith("cf", text.GetProperty("key").GetString());
        Assert.Equal("hello", text.GetProperty("default").GetString());
        Assert.True(text.GetProperty("searchable").GetBoolean());
        var number = await Api.DefineAsync(admin, Api.Field("number", $"{stem} number", "رقم", new { decimals = 3, @default = "1.5" }));
        Assert.Equal("1.500", number.GetProperty("default").GetString());
        Assert.Equal(3, number.GetProperty("decimals").GetInt32());
        var date = await Api.DefineAsync(admin, Api.Field("date", $"{stem} date", "تاريخ", new { @default = "2026-03-01" }));
        Assert.Equal("2026-03-01", date.GetProperty("default").GetString());
        var flag = await Api.DefineAsync(admin, Api.Field("boolean", $"{stem} flag", "علامة", new { @default = "true" }));
        Assert.Equal("true", flag.GetProperty("default").GetString());
        var select = await Api.DefineAsync(admin, Api.Field("select", $"{stem} choice", "اختيار", new
        {
            options = new[] { new { value = (string?)null, labelEn = "Gold tier", labelAr = "ذهبي" }, new { value = (string?)"silver", labelEn = "Silver", labelAr = "فضي" } },
            @default = "silver",
        }));
        Assert.Equal(["goldTier", "silver"], select.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()));
        var lookup = await Api.DefineAsync(admin, Api.Field("lookup", $"{stem} parent", "أصل", new { lookupTarget = "contacts.contacts" }));
        Assert.Equal("contacts.contacts", lookup.GetProperty("lookupTarget").GetString());

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/customfields/fields/{select.GetProperty("id").GetGuid()}", Api.Cancel);
        Assert.Equal(select.GetProperty("key").GetString(), read.GetProperty("key").GetString());
        Assert.True(read.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_blank_arabic_label_shows_the_english_one()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var label = Api.Unique("English only");
        var field = await Api.DefineAsync(admin, new { recordType = "contacts.contacts", type = "select", labelEn = label, labelAr = "  ", options = new[] { new { value = (string?)null, labelEn = "Alpha", labelAr = (string?)null } } });
        Assert.Equal(label, field.GetProperty("labelAr").GetString());
        Assert.Equal("Alpha", field.GetProperty("options")[0].GetProperty("labelAr").GetString());
    }

    [Fact]
    public async Task Two_fields_with_the_same_label_get_different_names()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var label = Api.Unique("Same label");
        var first = await Api.DefineAsync(admin, Api.Field("text", label));
        var second = await Api.DefineAsync(admin, Api.Field("text", label));
        Assert.NotEqual(first.GetProperty("key").GetString(), second.GetProperty("key").GetString());
    }

    [Fact]
    public async Task A_bad_definition_is_refused_with_reasons_in_the_callers_language()
    {
        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var response = await arabic.PostAsJsonAsync("/api/customfields/fields", new
        {
            recordType = "contacts.contacts", type = "number", labelEn = "عربي فقط", labelAr = "نص", decimals = 12, @default = "abc",
        }, Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Api.Json(response)).GetProperty("errors");
        foreach (var field in new[] { "labelEn", "decimals" })
        {
            Assert.True(errors.TryGetProperty(field, out var list), $"no error for {field}: {errors}");
            Assert.Matches("[؀-ۿ]", list[0].GetProperty("message").GetString()!);
        }

        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        async Task<string> RefusedAsync(object body, string field)
        {
            var refused = await admin.PostAsJsonAsync("/api/customfields/fields", body, Api.Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            return (await Api.Json(refused)).GetProperty("errors").GetProperty(field)[0].GetProperty("code").GetString()!;
        }
        Assert.Equal("customFieldRecordType", await RefusedAsync(new { recordType = "nowhere.nothing", type = "text", labelEn = "X1", labelAr = "س" }, "recordType"));
        Assert.Equal("customFieldType", await RefusedAsync(new { recordType = "contacts.contacts", type = "image", labelEn = "X1", labelAr = "س" }, "type"));
        Assert.Equal("customFieldDefault", await RefusedAsync(Api.Field("number", "Bad default", "س", new { @default = "twelve" }), "default"));
        Assert.Equal("customFieldDefault", await RefusedAsync(Api.Field("date", "Bad date", "س", new { @default = "2026-02-30" }), "default"));
        Assert.Equal("customFieldOptionCount", await RefusedAsync(Api.Field("select", "No options", "س"), "options"));
        Assert.Equal("customFieldLookupTarget", await RefusedAsync(Api.Field("lookup", "No target", "س", new { lookupTarget = "contacts.nothing" }), "lookupTarget"));
        Assert.Equal("customFieldSearchText", await RefusedAsync(Api.Field("number", "Searchable number", "س", new { searchable = true }), "searchable"));
        Assert.Equal("customFieldNotNumber", await RefusedAsync(Api.Field("text", "Decimal text", "س", new { decimals = 2 }), "decimals"));
    }

    [Fact]
    public async Task A_fields_kind_and_record_type_never_change_and_a_stale_edit_is_refused()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var field = await Api.DefineAsync(admin, Api.Field("text", Api.Unique("Fixed kind")));
        var id = field.GetProperty("id").GetGuid();
        var body = new Dictionary<string, object?>
        {
            ["type"] = "number", ["labelEn"] = "Renamed", ["labelAr"] = "جديد", ["required"] = true, ["version"] = field.GetProperty("version").GetUInt32(),
        };
        var refused = await admin.PutAsJsonAsync($"/api/customfields/fields/{id}", body, Api.Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("customFieldFixed", (await Api.Json(refused)).GetProperty("errors").GetProperty("type")[0].GetProperty("code").GetString());

        body["type"] = "text";
        var changed = await admin.PutAsJsonAsync($"/api/customfields/fields/{id}", body, Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var after = await Api.Json(changed);
        Assert.Equal("Renamed", after.GetProperty("labelEn").GetString());
        Assert.True(after.GetProperty("required").GetBoolean());
        Assert.Equal(field.GetProperty("key").GetString(), after.GetProperty("key").GetString());

        var stale = await admin.PutAsJsonAsync($"/api/customfields/fields/{id}", body, Api.Cancel);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Defining_fields_needs_the_manage_permission_and_reading_the_read_permission()
    {
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        var list = await viewer.GetAsync("/api/customfields/fields", Api.Cancel);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var denied = await viewer.PostAsJsonAsync("/api/customfields/fields", Api.Field("text", "Not allowed"), Api.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var nobody = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/customfields/fields", Api.Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/customfields/record-types", Api.Cancel)).StatusCode);
    }

    [Fact]
    public async Task The_record_types_name_the_kinds_that_can_have_fields()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var types = await admin.GetFromJsonAsync<JsonElement>("/api/customfields/record-types", Api.Cancel);
        var contacts = types.EnumerateArray().Single(t => t.GetProperty("table").GetString() == "contacts.contacts");
        Assert.True(contacts.GetProperty("canBePointedAt").GetBoolean());
        Assert.True(contacts.GetProperty("fieldCount").GetInt32() >= 1);
    }
}
