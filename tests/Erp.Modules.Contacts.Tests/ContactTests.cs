using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Contacts.Tests;

public sealed class ContactsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The contacts API: organisations and people, validation in the caller's language, the
/// organisation a person belongs to, duplicates, archive and restore, the credit limit as money,
/// optimistic concurrency, permissions, and the workspace boundary.</summary>
public sealed class ContactTests(ContactsFixture fixture) : IClassFixture<ContactsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);

    internal static Dictionary<string, object?> Organisation(string name, string? nameAr = null) => new()
    {
        ["kind"] = "organisation",
        ["nameEn"] = name,
        ["nameAr"] = nameAr ?? "",
        ["email"] = null,
        ["phone"] = null,
        ["country"] = "AE",
        ["defaultCurrency"] = "AED",
        ["tags"] = Array.Empty<string>(),
    };

    internal static Dictionary<string, object?> Person(string name, Guid? organisation = null) => new()
    {
        ["kind"] = "person",
        ["nameEn"] = name,
        ["nameAr"] = "",
        ["organisationId"] = organisation,
        ["country"] = "AE",
        ["defaultCurrency"] = "AED",
    };

    private static Dictionary<string, object?> With(Dictionary<string, object?> body, params (string Key, object? Value)[] changes)
    {
        var copy = new Dictionary<string, object?>(body);
        foreach (var (key, value) in changes)
        {
            copy[key] = value;
        }
        return copy;
    }

    private static Dictionary<string, object?> Editable(JsonElement contact)
    {
        var body = new Dictionary<string, object?>();
        foreach (var name in new[] { "kind", "code", "nameEn", "nameAr", "jobTitle", "email", "phone", "mobile", "website", "addressLine1", "addressLine2", "area", "city", "emirate", "poBox", "country", "addressAr", "tradeLicenceNumber", "taxRegistrationNumber", "defaultCurrency", "notes" })
        {
            body[name] = contact.GetProperty(name).ValueKind == JsonValueKind.Null ? null : contact.GetProperty(name).GetString();
        }
        body["organisationId"] = contact.GetProperty("organisationId").ValueKind == JsonValueKind.Null ? null : contact.GetProperty("organisationId").GetGuid();
        body["tags"] = contact.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        if (contact.GetProperty("creditLimit") is { ValueKind: JsonValueKind.Object } limit)
        {
            body["creditLimitAmount"] = limit.GetProperty("amount").GetString();
            body["creditLimitCurrency"] = limit.GetProperty("currency").GetString();
            body["creditLimitExchangeRate"] = limit.GetProperty("exchangeRate").GetString();
        }
        body["version"] = contact.GetProperty("version").GetUInt32();
        return body;
    }

    internal static async Task<JsonElement> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/contacts/contacts", body, Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Unique(string stem) => $"{stem} {Guid.NewGuid().ToString("N")[..8]}";

    [Fact]
    public async Task An_organisation_and_its_person_are_created_and_read_back_with_every_field()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var name = Unique("Horizon Steel Trading LLC");
        var organisation = await CreateAsync(admin, With(Organisation(name, "الأفق لتجارة الحديد ذ.م.م"),
            ("code", "T-ORG-1"), ("email", "  Accounts@Horizon.example "), ("phone", "+971 4 555 0100"), ("website", "www.horizon-steel.example"),
            ("addressLine1", "Warehouse 4"), ("area", "Al Quoz"), ("city", "Dubai"), ("emirate", "dubai"), ("poBox", "12345"), ("country", "ae"),
            ("tags", new[] { "Customer", " VIP ", "customer" }), ("tradeLicenceNumber", "DED-99"), ("taxRegistrationNumber", "١٠٠-١٢٣٤-٥٦٧٨-٩٠٠٣"),
            ("defaultCurrency", "aed"), ("creditLimitAmount", "250000.00"), ("notes", "Pays in 30 days.")));
        Assert.Equal("organisation", organisation.GetProperty("kind").GetString());
        Assert.Equal("T-ORG-1", organisation.GetProperty("code").GetString());
        Assert.Equal("Accounts@Horizon.example", organisation.GetProperty("email").GetString());
        Assert.Equal("https://www.horizon-steel.example", organisation.GetProperty("website").GetString());
        Assert.Equal("AE", organisation.GetProperty("country").GetString());
        Assert.Equal("AED", organisation.GetProperty("defaultCurrency").GetString());
        Assert.Equal(["Customer", "VIP"], organisation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        // docs/compliance/vat-trn-format.md: Arabic-Indic digits become 0 to 9, dashes go.
        Assert.Equal("100123456789003", organisation.GetProperty("taxRegistrationNumber").GetString());
        var limit = organisation.GetProperty("creditLimit");
        Assert.Equal("250000.00", decimal.Parse(limit.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("AED", limit.GetProperty("currency").GetString());
        Assert.Equal("AED", limit.GetProperty("baseCurrency").GetString());
        Assert.Equal(1m, decimal.Parse(limit.GetProperty("exchangeRate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(organisation.GetProperty("isActive").GetBoolean());

        var person = await CreateAsync(admin, With(Person(Unique("Salem Rashid Al Shamsi"), organisation.GetProperty("id").GetGuid()),
            ("jobTitle", "Purchasing Manager"), ("mobile", "+971 50 555 0102")));
        Assert.Equal(name, person.GetProperty("organisationName").GetString());
        Assert.Equal("الأفق لتجارة الحديد ذ.م.م", person.GetProperty("organisationNameAr").GetString());
        Assert.Equal("Purchasing Manager", person.GetProperty("jobTitle").GetString());

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{organisation.GetProperty("id").GetGuid()}", Cancel);
        Assert.Equal(1, read.GetProperty("peopleCount").GetInt32());
    }

    [Fact]
    public async Task Fields_are_refused_with_reasons_in_the_callers_language()
    {
        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var response = await arabic.PostAsJsonAsync("/api/contacts/contacts", With(Organisation(""),
            ("nameAr", " "), ("email", "not-an-email"), ("phone", "+971 50 123"), ("mobile", "abc"), ("website", "not a site"),
            ("emirate", "dubai"), ("country", "OM"), ("taxRegistrationNumber", "12345"), ("defaultCurrency", "XYZ"),
            ("creditLimitAmount", "10.005"), ("tags", Enumerable.Range(1, 11).Select(i => $"Tag {i}").ToArray()), ("code", "-bad")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        foreach (var field in new[] { "nameEn", "email", "phone", "mobile", "website", "emirate", "taxRegistrationNumber", "defaultCurrency", "tags", "code" })
        {
            Assert.True(errors.TryGetProperty(field, out var list), $"no error for {field}: {errors}");
            Assert.Matches("[؀-ۿ]", list[0].GetProperty("message").GetString()!);
        }

        // A credit limit with more decimals than its currency has is refused once the rest is valid.
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var decimals = await admin.PostAsJsonAsync("/api/contacts/contacts", With(Organisation(Unique("Decimals LLC")), ("creditLimitAmount", "10.005")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, decimals.StatusCode);
        Assert.Equal("contactsAmountDecimals", (await Json(decimals)).GetProperty("errors").GetProperty("creditLimitAmount")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_person_belongs_only_to_an_active_organisation_of_the_workspace()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var organisation = await CreateAsync(admin, Organisation(Unique("Pearl Packaging LLC")));
        var person = await CreateAsync(admin, Person(Unique("Reem Haddad"), organisation.GetProperty("id").GetGuid()));

        async Task<string> RefusedAsync(object body, string field)
        {
            var response = await admin.PostAsJsonAsync("/api/contacts/contacts", body, Cancel);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await Json(response)).GetProperty("errors").GetProperty(field)[0].GetProperty("code").GetString()!;
        }

        Assert.Equal("contactsOrganisationNotOrganisation", await RefusedAsync(Person(Unique("Under a person"), person.GetProperty("id").GetGuid()), "organisationId"));
        Assert.Equal("contactsOrganisationUnknown", await RefusedAsync(Person(Unique("Nowhere"), Guid.CreateVersion7()), "organisationId"));
        // An organisation belongs to no other contact and has no job title: they are left empty.
        var nested = await CreateAsync(admin, With(Organisation(Unique("Nested LLC")), ("organisationId", organisation.GetProperty("id").GetGuid()), ("jobTitle", "Boss")));
        Assert.Equal(JsonValueKind.Null, nested.GetProperty("organisationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, nested.GetProperty("jobTitle").ValueKind);

        // An organisation with people stays one.
        var asPerson = With(Editable(organisation), ("kind", "person"));
        var change = await admin.PutAsJsonAsync($"/api/contacts/contacts/{organisation.GetProperty("id").GetGuid()}", asPerson, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal("contactsOrganisationHasPeople", (await Json(change)).GetProperty("errors").GetProperty("kind")[0].GetProperty("code").GetString());

        // An archived organisation takes no new people; its people stay.
        var archived = await admin.PostAsync($"/api/contacts/contacts/{organisation.GetProperty("id").GetGuid()}/archive", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal("contactsOrganisationArchived", await RefusedAsync(Person(Unique("Late joiner"), organisation.GetProperty("id").GetGuid()), "organisationId"));
        var stays = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/{person.GetProperty("id").GetGuid()}", Cancel);
        var save = await admin.PutAsJsonAsync($"/api/contacts/contacts/{person.GetProperty("id").GetGuid()}", With(Editable(stays), ("jobTitle", "Accountant")), Cancel);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
    }

    [Fact]
    public async Task Archive_and_restore_keep_the_contact_and_the_list_filters_by_status()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var name = Unique("Coral Furniture Est.");
        var contact = await CreateAsync(admin, Organisation(name));
        var id = contact.GetProperty("id").GetGuid();
        var archived = await Json(await admin.PostAsync($"/api/contacts/contacts/{id}/archive", null, Cancel));
        Assert.False(archived.GetProperty("isActive").GetBoolean());
        var search = Uri.EscapeDataString(name);
        var active = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={search}&filter={Uri.EscapeDataString("isActive eq true")}", Cancel);
        Assert.Equal(0, active.GetProperty("total").GetInt32());
        var all = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={search}", Cancel);
        Assert.Equal(1, all.GetProperty("total").GetInt32());
        var restored = await Json(await admin.PostAsync($"/api/contacts/contacts/{id}/restore", null, Cancel));
        Assert.True(restored.GetProperty("isActive").GetBoolean());

        // The trail names who archived and restored it.
        await using var owner = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit.entries WHERE table_schema = 'contacts' AND table_name = 'contacts' AND record_id = @id AND action = 'update' AND changes ? 'is_active' AND actor_id IS NOT NULL", owner);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync(Cancel))!);
    }

    [Fact]
    public async Task Contacts_are_never_deleted_by_the_application()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var contact = await CreateAsync(admin, Organisation(Unique("Kept LLC")));
        await using var app = await Env.OpenAppAsync();
        await using var transaction = await app.BeginTransactionAsync(Cancel);
        await using (var bind = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", app, transaction))
        {
            bind.Parameters.AddWithValue("t", Env.TenantA.Id.ToString());
            await bind.ExecuteNonQueryAsync(Cancel);
        }
        await using var delete = new NpgsqlCommand("DELETE FROM contacts.contacts WHERE id = @id", app, transaction);
        delete.Parameters.AddWithValue("id", contact.GetProperty("id").GetGuid());
        var refused = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync(Cancel));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    [Fact]
    public async Task Possible_duplicates_are_found_by_name_email_number_or_tax_number_however_they_were_typed()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var stem = Guid.NewGuid().ToString("N")[..6];
        var original = await CreateAsync(admin, With(Organisation($"Desert Rose Trading {stem} LLC", $"وردة الصحراء {stem} للتجارة"),
            ("email", $"sales.{stem}@desertrose.example"), ("phone", "+971 4 555 0177"), ("mobile", "+971 52 555 0178"), ("taxRegistrationNumber", $"1009{Random.Shared.NextInt64(0, 99_999_999_999):00000000000}")));
        var id = original.GetProperty("id").GetGuid();
        var trn = original.GetProperty("taxRegistrationNumber").GetString()!;

        async Task<IReadOnlyList<string>> MatchesAsync(string query)
        {
            var found = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/duplicates?{query}", Cancel);
            var hit = found.EnumerateArray().FirstOrDefault(d => d.GetProperty("id").GetGuid() == id);
            return hit.ValueKind == JsonValueKind.Undefined ? [] : hit.GetProperty("matches").EnumerateArray().Select(m => m.GetString()!).ToList();
        }

        Assert.Contains("name", await MatchesAsync($"nameEn={Uri.EscapeDataString($"desert rose TRADING  {stem}, LLC.")}"));
        Assert.Contains("name", await MatchesAsync($"nameAr={Uri.EscapeDataString($"وردة الصحراء {stem} للتجاره")}"));
        Assert.Contains("email", await MatchesAsync($"email={Uri.EscapeDataString($"SALES.{stem}@DesertRose.example")}"));
        Assert.Contains("phone", await MatchesAsync($"phone={Uri.EscapeDataString("04 555 0177")}"));
        Assert.Contains("phone", await MatchesAsync($"mobile={Uri.EscapeDataString("00971525550178")}"));
        Assert.Contains("taxRegistrationNumber", await MatchesAsync($"taxRegistrationNumber={Uri.EscapeDataString(string.Join(" ", trn.Chunk(5).Select(c => new string(c))))}"));
        // The contact being changed is not its own duplicate.
        Assert.Empty(await MatchesAsync($"email={Uri.EscapeDataString($"sales.{stem}@desertrose.example")}&except={id}"));
        // Nothing typed, nothing found.
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts/duplicates?nameEn=ab", Cancel)).GetArrayLength());
    }

    [Fact]
    public async Task Another_workspaces_contacts_are_neither_seen_nor_matched_nor_linked()
    {
        using var attacker = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var victim = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var theirs = (await victim.GetFromJsonAsync<JsonElement>("/api/contacts/contacts?take=200&filter=" + Uri.EscapeDataString("kind eq 'organisation'"), Cancel))
            .GetProperty("items").EnumerateArray().First();
        var theirId = theirs.GetProperty("id").GetGuid();
        var theirName = theirs.GetProperty("nameEn").GetString()!;
        var before = await victim.GetStringAsync($"/api/contacts/contacts/{theirId}", Cancel);

        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/contacts/contacts/{theirId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/contacts/contacts/{theirId}/archive", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/contacts/contacts/{theirId}/restore", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PutAsJsonAsync($"/api/contacts/contacts/{theirId}", With(Organisation("Taken over LLC"), ("version", 1u)), Cancel)).StatusCode);
        var link = await attacker.PostAsJsonAsync("/api/contacts/contacts", Person("Spy", theirId), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
        Assert.Equal("contactsOrganisationUnknown", (await Json(link)).GetProperty("errors").GetProperty("organisationId")[0].GetProperty("code").GetString());
        var duplicates = await attacker.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts/duplicates?nameEn={Uri.EscapeDataString(theirName)}&email={Uri.EscapeDataString(theirs.GetProperty("email").GetString() ?? "")}", Cancel);
        Assert.Equal(0, duplicates.GetArrayLength());
        var search = await attacker.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(theirName)}", Cancel);
        Assert.Equal(0, search.GetProperty("total").GetInt32());
        Assert.Equal(before, await victim.GetStringAsync($"/api/contacts/contacts/{theirId}", Cancel));
    }

    [Fact]
    public async Task Each_action_needs_its_own_permission()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var contact = await CreateAsync(admin, Organisation(Unique("Union Plastics FZE")));
        var id = contact.GetProperty("id").GetGuid();
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/contacts/contacts/{id}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/contacts/contacts/duplicates?nameEn=Union", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/contacts/contacts", Organisation(Unique("Viewer LLC")), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/contacts/contacts/{id}", Editable(contact), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/contacts/contacts/{id}/archive", null, Cancel)).StatusCode);
        using var nobody = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/contacts/contacts", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"/api/contacts/contacts/{id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task A_credit_limit_in_another_currency_keeps_the_rate_and_the_base_amount()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var missing = await admin.PostAsJsonAsync("/api/contacts/contacts", With(Organisation(Unique("Import Partner Ltd")), ("defaultCurrency", "USD"), ("creditLimitAmount", "1000.00")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("contactsRateRequired", (await Json(missing)).GetProperty("errors").GetProperty("creditLimitExchangeRate")[0].GetProperty("code").GetString());

        var contact = await CreateAsync(admin, With(Organisation(Unique("Import Partner Ltd")), ("defaultCurrency", "USD"), ("creditLimitAmount", "1000.05"), ("creditLimitExchangeRate", "3.6725")));
        var limit = contact.GetProperty("creditLimit");
        Assert.Equal("USD", limit.GetProperty("currency").GetString());
        Assert.Equal(3.6725m, decimal.Parse(limit.GetProperty("exchangeRate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        // 1000.05 × 3.6725 = 3672.683625, rounded once half away from zero to fils.
        Assert.Equal(3672.68m, decimal.Parse(limit.GetProperty("baseAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("AED", limit.GetProperty("baseCurrency").GetString());

        // Saved again unchanged, the limit keeps its rate; cleared, it is gone (no limit, not zero).
        var body = Editable(contact);
        body.Remove("creditLimitExchangeRate");
        var same = await Json(await admin.PutAsJsonAsync($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", body, Cancel));
        Assert.Equal(3.6725m, decimal.Parse(same.GetProperty("creditLimit").GetProperty("exchangeRate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        var cleared = Editable(same);
        cleared["creditLimitAmount"] = null;
        cleared["creditLimitExchangeRate"] = null;
        var none = await Json(await admin.PutAsJsonAsync($"/api/contacts/contacts/{contact.GetProperty("id").GetGuid()}", cleared, Cancel));
        Assert.Equal(JsonValueKind.Null, none.GetProperty("creditLimit").ValueKind);
    }

    [Fact]
    public async Task A_change_made_on_an_old_version_is_refused()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var contact = await CreateAsync(admin, Organisation(Unique("Palm Logistics LLC")));
        var id = contact.GetProperty("id").GetGuid();
        var first = await admin.PutAsJsonAsync($"/api/contacts/contacts/{id}", With(Editable(contact), ("city", "Dubai")), Cancel);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stale = await admin.PutAsJsonAsync($"/api/contacts/contacts/{id}", With(Editable(contact), ("city", "Sharjah")), Cancel);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var reference = await admin.PostAsJsonAsync("/api/contacts/contacts", With(Organisation(Unique("Same ref LLC")), ("code", "T-DUP-REF")), Cancel);
        Assert.Equal(HttpStatusCode.Created, reference.StatusCode);
        var taken = await admin.PostAsJsonAsync("/api/contacts/contacts", With(Organisation(Unique("Same ref LLC")), ("code", "T-DUP-REF")), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal("contactsCodeTaken", (await Json(taken)).GetProperty("errors").GetProperty("code")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_list_searches_names_in_both_scripts_numbers_e_mail_and_initials_and_names_each_persons_organisation()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var stem = Guid.NewGuid().ToString("N")[..6];
        var organisation = await CreateAsync(admin, Organisation($"Sahara Textiles {stem} LLC", $"الصحراء للمنسوجات {stem}"));
        var person = await CreateAsync(admin, With(Person($"Zainab Qx{stem} Darwish", organisation.GetProperty("id").GetGuid()),
            ("nameAr", $"زينب {stem} درويش"), ("email", $"zainab.{stem}@sahara.example"), ("mobile", "+971 56 555 0199")));
        var id = person.GetProperty("id").GetGuid();

        async Task<JsonElement> FirstAsync(string search)
        {
            var page = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString(search)}", Cancel);
            Assert.True(page.GetProperty("items").EnumerateArray().Any(r => r.GetProperty("id").GetGuid() == id), $"'{search}' did not find the person");
            return page.GetProperty("items").EnumerateArray().First(r => r.GetProperty("id").GetGuid() == id);
        }

        var row = await FirstAsync($"zainab qx{stem}");
        Assert.Equal($"Sahara Textiles {stem} LLC", row.GetProperty("organisationName").GetString());
        await FirstAsync($"زينب {stem}");
        await FirstAsync($"zainab.{stem}@");
        await FirstAsync("555 0199");
        var definition = await admin.GetFromJsonAsync<JsonElement>("/api/lists/contacts.contacts/definition", Cancel);
        Assert.Equal("nameEn", definition.GetProperty("defaultSort").GetString());
    }

    [Fact]
    public async Task The_list_prints_and_a_contact_card_prints_in_both_languages()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var contact = await CreateAsync(admin, With(Organisation(Unique("Crescent Auto Spare Parts LLC"), "الهلال لقطع غيار السيارات"), ("creditLimitAmount", "1000.00")));
        foreach (var language in new[] { "en", "ar" })
        {
            var card = await admin.GetAsync($"/api/reports/run/contacts.contactCard?contact={contact.GetProperty("id").GetGuid()}&format=pdf&language={language}", Cancel);
            Assert.True(card.StatusCode == HttpStatusCode.OK, $"{language}: {(int)card.StatusCode} {await card.Content.ReadAsStringAsync(Cancel)}");
            Assert.Equal("application/pdf", card.Content.Headers.ContentType?.MediaType);
        }
        var csv = await admin.GetAsync($"/api/reports/lists/contacts.contacts?format=csv&language=en&search={Uri.EscapeDataString("Crescent Auto")}", Cancel);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("Crescent Auto Spare Parts", await csv.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_gate_workspace_is_seeded_with_its_own_named_and_generated_contacts()
    {
        foreach (var tenant in new[] { Env.TenantA, Env.TenantB })
        {
            using var admin = await Env.SignInAsync(Env.Email(tenant, "admin"));
            var page = await admin.GetFromJsonAsync<JsonElement>("/api/contacts/contacts?take=1", Cancel);
            Assert.True(page.GetProperty("total").GetInt32() >= tenant.Volume, $"{tenant.Code}: {page.GetProperty("total").GetInt32()} contacts");
            var falcon = await admin.GetFromJsonAsync<JsonElement>($"/api/contacts/contacts?search={Uri.EscapeDataString("Falcon Building Materials")}", Cancel);
            var row = falcon.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("code").GetString() == "C-DEMO-1");
            if (tenant.Canary is { } canary)
            {
                Assert.Contains(canary, row.GetProperty("nameEn").GetString(), StringComparison.Ordinal);
            }
        }
    }
}
