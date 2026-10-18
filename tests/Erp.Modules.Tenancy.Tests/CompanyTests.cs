using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>Companies and branches: validation in the caller's language, uniqueness within the
/// workspace, optimistic concurrency, deactivation, logos, and the company scope (a user sees and
/// changes only the companies they may work in).</summary>
public sealed class CompanyTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Company(string code, string nameEn = "Test Company LLC", string nameAr = "شركة اختبار ذ.م.م", object? extra = null) => new
    {
        code,
        legalNameEn = nameEn,
        legalNameAr = nameAr,
        tradeLicenceNumber = "DED-1",
        tradeLicenceAuthority = "Dubai DET",
        taxRegistrationNumber = "100000000000003",
        baseCurrency = "aed",
        fiscalYearStartMonth = 4,
        fiscalYearStartDay = 1,
        addressLine1 = "Office 1",
        addressLine2 = (string?)null,
        city = "Dubai",
        emirate = "dubai",
        poBox = "1234",
        country = "ae",
        addressAr = "مكتب 1، دبي",
        phone = "+971 4 000 0000",
        email = "accounts@test.example",
        website = "https://test.example",
        isActive = true,
        version = (uint?)null,
    };

    [Fact]
    public async Task An_administrator_creates_a_company_works_in_it_and_reads_it_back()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var created = await admin.PostAsJsonAsync("/api/tenancy/companies", Company("t-dxb"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var company = await Json(created);
        Assert.Equal("T-DXB", company.GetProperty("code").GetString());
        Assert.Equal("AED", company.GetProperty("baseCurrency").GetString());
        Assert.Equal("AE", company.GetProperty("country").GetString());
        Assert.Equal("dubai", company.GetProperty("emirate").GetString());
        Assert.Equal(4, company.GetProperty("fiscalYearStartMonth").GetInt32());
        var id = company.GetProperty("id").GetGuid();
        Assert.EndsWith($"/api/tenancy/companies/{id}", created.Headers.Location!.ToString(), StringComparison.Ordinal);

        // The creator may work in it at once: it is offered by the switcher.
        var workplace = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
        Assert.Contains(workplace.GetProperty("companies").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?search=t-dxb");
        Assert.Equal(1, list.GetProperty("total").GetInt32());

        // Codes are unique within the workspace.
        var duplicate = await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-DXB", "Other LLC"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("tenancy.companyCodeTaken", (await Json(duplicate)).GetProperty("code").GetString());

        // The same code is free in another workspace.
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/tenancy/companies", Company("T-DXB"))).StatusCode);
    }

    [Fact]
    public async Task A_company_or_branch_needs_only_a_name_and_gets_a_code_made_from_it()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        async Task<JsonElement> Create(object body)
        {
            var response = await admin.PostAsJsonAsync("/api/tenancy/companies", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await Json(response);
        }
        var falcon = await Create(new { legalNameEn = "Falcon Logistics LLC", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
        Assert.Equal("FALCON", falcon.GetProperty("code").GetString());
        Assert.Equal("", falcon.GetProperty("legalNameAr").GetString());
        Assert.Equal("FALCON-2", (await Create(new { legalNameEn = "Falcon Freight FZE", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true }))
            .GetProperty("code").GetString());
        Assert.Equal("AL-REEM", (await Create(new { legalNameEn = "Al Reem Trading L.L.C.", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true }))
            .GetProperty("code").GetString());
        // A name with no distinctive Latin word (only legal-form words) gets the fallback code.
        var formOnly = await Create(new { legalNameEn = "L.L.C.", legalNameAr = "مؤسسة الصقر", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
        Assert.Equal("CO-1", formOnly.GetProperty("code").GetString());
        // The English legal name is required even when the Arabic one is given (p06 round 3: an
        // empty English legal name was saved next to an Arabic one), on create and on change, and
        // nothing is saved.
        foreach (var english in new string?[] { null, "", "   " })
        {
            var arabicOnly = await admin.PostAsJsonAsync("/api/tenancy/companies", new { legalNameEn = english, legalNameAr = "مؤسسة الصقر للتجارة", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
            Assert.Equal(HttpStatusCode.BadRequest, arabicOnly.StatusCode);
            Assert.Equal("tenancyLegalNameEn", (await Json(arabicOnly)).GetProperty("errors").GetProperty("legalNameEn")[0].GetProperty("code").GetString());
        }
        // The English legal name is the Latin-letter one on the trade licence: Arabic script or
        // digits alone are not it (critic p02 round 11: "فرع ديرة 60473" was saved as the English
        // legal name), on create and on change; a name with one Latin letter is.
        foreach (var notEnglish in new[] { "فرع ديرة 60473", "60473", "٦٠٤٧٣ - 1" })
        {
            var refused = await admin.PostAsJsonAsync("/api/tenancy/companies", new { legalNameEn = notEnglish, legalNameAr = "مؤسسة الصقر للتجارة", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var error = (await Json(refused)).GetProperty("errors").GetProperty("legalNameEn")[0];
            Assert.Equal("tenancyLegalNameEnLatin", error.GetProperty("code").GetString());
        }
        var notLatin = JsonSerializer.SerializeToNode(new { legalNameEn = "فرع ديرة", legalNameAr = "مؤسسة الصقر", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true })!.AsObject();
        notLatin["version"] = formOnly.GetProperty("version").GetUInt32();
        var renamed = await admin.PutAsJsonAsync($"/api/tenancy/companies/{formOnly.GetProperty("id").GetGuid()}", notLatin);
        Assert.Equal("tenancyLegalNameEnLatin", (await Json(renamed)).GetProperty("errors").GetProperty("legalNameEn")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/tenancy/companies", new { legalNameEn = "3M Gulf 60473", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true })).StatusCode);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200");
        Assert.DoesNotContain(all.GetProperty("items").EnumerateArray(), c => c.GetProperty("legalNameAr").GetString() == "مؤسسة الصقر للتجارة");
        var change = JsonSerializer.SerializeToNode(new { legalNameEn = "", legalNameAr = "مؤسسة الصقر", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true })!.AsObject();
        change["version"] = formOnly.GetProperty("version").GetUInt32();
        var emptied = await admin.PutAsJsonAsync($"/api/tenancy/companies/{formOnly.GetProperty("id").GetGuid()}", change);
        Assert.Equal(HttpStatusCode.BadRequest, emptied.StatusCode);
        Assert.Equal("tenancyLegalNameEn", (await Json(emptied)).GetProperty("errors").GetProperty("legalNameEn")[0].GetProperty("code").GetString());
        Assert.Equal("L.L.C.", (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{formOnly.GetProperty("id").GetGuid()}")).GetProperty("legalNameEn").GetString());
        // The database holds the same line for any other writer: an empty or blank English legal
        // name next to an Arabic one is refused by ck_companies_legal_name.
        await using (var db = await Env.OpenAdminAsync())
        {
            foreach (var blank in new[] { "", "   " })
            {
                await using var write = new Npgsql.NpgsqlCommand("UPDATE tenancy.companies SET legal_name_en = @blank WHERE id = @id", db);
                write.Parameters.AddWithValue("blank", blank);
                write.Parameters.AddWithValue("id", formOnly.GetProperty("id").GetGuid());
                var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => write.ExecuteNonQueryAsync());
                Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, refused.SqlState);
                Assert.Equal("ck_companies_legal_name", refused.ConstraintName);
            }
        }

        var branch = await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = falcon.GetProperty("id").GetGuid(), nameEn = "Falcon Logistics LLC - Jebel Ali Branch", country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.Created, branch.StatusCode);
        // The branch's own part of the name, not the company's (critic p02 round 6), and HQ for the
        // branch named after the company alone.
        Assert.Equal("JEBEL", (await Json(branch)).GetProperty("code").GetString());
        var headOffice = await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = falcon.GetProperty("id").GetGuid(), nameEn = "Falcon Logistics LLC", country = "AE", isActive = true });
        Assert.Equal("HQ", (await Json(headOffice)).GetProperty("code").GetString());
        var dubai = await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = falcon.GetProperty("id").GetGuid(), nameEn = "Falcon Logistics LLC - Al Quoz Warehouse", country = "AE", isActive = true });
        Assert.Equal("AL-QUOZ", (await Json(dubai)).GetProperty("code").GetString());
        var noName = await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = falcon.GetProperty("id").GetGuid(), country = "AE", isActive = true });
        Assert.Equal("tenancyNameEnOrAr", (await Json(noName)).GetProperty("errors").GetProperty("nameEn")[0].GetProperty("code").GetString());
    }

    // Owner decision 2026-10-10 (docs/compliance/vat-trn-format.md, source: UAE Ministry of Finance,
    // Electronic Invoicing Guidelines v1.1): a TRN is exactly 15 digits 0 to 9; spaces and dashes a
    // user types are removed before the check; anything else is refused; no prefix and no
    // check-digit rule.
    [Fact]
    public async Task A_tax_registration_number_is_exactly_fifteen_digits_and_spaces_and_dashes_typed_are_removed()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var typed = new Dictionary<string, string>
        {
            ["T-TRN1"] = "100-1234-5678-9003",
            ["T-TRN2"] = " 100 1234 5678 9003 ",
            // No prefix rule: a number that does not start with 100 is accepted, and no check digit.
            ["T-TRN3"] = "987654321012345",
            // Arabic-Indic digits typed on an Arabic keyboard are turned into 0 to 9 (owner decision
            // 2026-10-17, needs-human #18), alone or mixed with 0 to 9, spaces and dashes.
            ["T-TRN4"] = "١٠٠١٢٣٤٥٦٧٨٩٠٠٣",
            ["T-TRN5"] = "١٠٠-1234 ٥٦٧٨-9004",
        };
        Guid first = default;
        foreach (var (code, number) in typed)
        {
            var body = JsonSerializer.SerializeToNode(Company(code))!.AsObject();
            body["taxRegistrationNumber"] = number;
            var created = await admin.PostAsJsonAsync("/api/tenancy/companies", body);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var company = await Json(created);
            var expected = new string(number.Where(c => c is >= '0' and <= '9' or >= '\u0660' and <= '\u0669')
                .Select(c => c >= '\u0660' ? (char)('0' + (c - '\u0660')) : c).ToArray());
            Assert.Matches("^[0-9]{15}$", expected);
            Assert.Equal(expected, company.GetProperty("taxRegistrationNumber").GetString());
            // Kept and shown in 0 to 9 only: the record read back, and the company profile printed
            // in Arabic with Arabic-Indic digits chosen for the document.
            var id = company.GetProperty("id").GetGuid();
            Assert.Equal(expected, (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{id}")).GetProperty("taxRegistrationNumber").GetString());
            if (code == "T-TRN4")
            {
                var printed = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={id}&format=json&language=ar&numerals=arab");
                var texts = new List<string>();
                void Walk(JsonElement e)
                {
                    switch (e.ValueKind)
                    {
                        case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Walk(p.Value); break;
                        case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Walk(x); break;
                        case JsonValueKind.String: texts.Add(e.GetString()!); break;
                    }
                }
                Walk(printed);
                Assert.Contains(expected, texts);
                Assert.DoesNotContain(texts, t => t.Contains("١٠٠١٢٣٤٥٦٧٨٩٠٠٣", StringComparison.Ordinal) || t.Contains("١٠٠١٢٣", StringComparison.Ordinal));
            }
            if (first == default) first = id;
        }
        foreach (var number in new[]
                 {
                     "12345", "10012345678900", "1001234567890031", "10012345678900A", "100.1234.5678.9003", "+100123456789003",
                     "100/1234/5678/9003", "TRN100123456789003", "100_123_456_789_003",
                     // Other digit sets stay refused: Persian (Eastern Arabic-Indic), full-width, Devanagari,
                     // and 14 Arabic-Indic digits are still too few.
                     "۱۰۰۱۲۳۴۵۶۷۸۹۰۰۳", "１００１２３４５６７８９００３", "१००१२३४५६७८९००३", "١٠٠١٢٣٤٥٦٧٨٩٠٠", "١٠٠١٢٣٤٥٦٧٨٩٠۰٣",
                 })
        {
            var body = JsonSerializer.SerializeToNode(Company("T-TRNX"))!.AsObject();
            body["taxRegistrationNumber"] = number;
            var refused = await admin.PostAsJsonAsync("/api/tenancy/companies", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var error = (await Json(refused)).GetProperty("errors").GetProperty("taxRegistrationNumber")[0];
            Assert.Equal("tenancyTaxNumber", error.GetProperty("code").GetString());
            Assert.Matches(@"\p{IsArabic}", error.GetProperty("message").GetString()!);
        }
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200")).GetProperty("items").EnumerateArray(),
            c => c.GetProperty("code").GetString() == "T-TRNX");
        // The database holds the same line for any other writer.
        await using var db = await Env.OpenAdminAsync();
        foreach (var bad in new[] { "12345", "100-1234-5678-9003", "10012345678900A" })
        {
            await using var write = new Npgsql.NpgsqlCommand("UPDATE tenancy.companies SET tax_registration_number = @bad WHERE id = @id", db);
            write.Parameters.AddWithValue("bad", bad);
            write.Parameters.AddWithValue("id", first);
            var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => write.ExecuteNonQueryAsync());
            Assert.True(refused.SqlState is Npgsql.PostgresErrorCodes.CheckViolation or Npgsql.PostgresErrorCodes.StringDataRightTruncation, refused.SqlState);
        }
    }

    // A number stored before the 15-digit rule is never lost: the migration keeps one that is 15
    // digits once its spaces and dashes are gone in that form, and moves any other, as it was, to
    // legacyTaxRegistrationNumber; the company stays editable, and the kept number goes once a
    // valid one is saved.
    [Fact]
    public async Task Numbers_stored_before_the_rule_are_kept_and_replaced_only_by_a_valid_one()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var ids = new List<Guid>();
        foreach (var code in new[] { "T-OLD1", "T-OLD2", "T-OLD3" })
        {
            ids.Add((await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company(code)))).GetProperty("id").GetGuid());
        }
        var before = new[] { "100-0000-0000-0003", "12345", "TRN 77" };
        await using (var db = await Env.OpenAdminAsync())
        {
            // The migration's data step, run on numbers as an older version stored them, then undone.
            await using var tx = await db.BeginTransactionAsync();
            await using (var widen = new Npgsql.NpgsqlCommand(
                "ALTER TABLE tenancy.companies DROP CONSTRAINT ck_companies_tax_registration_number; " +
                "ALTER TABLE tenancy.companies ALTER COLUMN tax_registration_number TYPE varchar(20)", db, tx))
            {
                await widen.ExecuteNonQueryAsync();
            }
            for (var i = 0; i < ids.Count; i++)
            {
                await using var old = new Npgsql.NpgsqlCommand("UPDATE tenancy.companies SET tax_registration_number = @n WHERE id = @id", db, tx);
                old.Parameters.AddWithValue("n", before[i]);
                old.Parameters.AddWithValue("id", ids[i]);
                await old.ExecuteNonQueryAsync();
            }
            await using (var migrate = new Npgsql.NpgsqlCommand(TenancySql.TaxNumbersToFifteenDigits, db, tx))
            {
                await migrate.ExecuteNonQueryAsync();
            }
            await using (var read = new Npgsql.NpgsqlCommand(
                "SELECT tax_registration_number, legacy_tax_registration_number FROM tenancy.companies WHERE id = ANY(@ids) ORDER BY code", db, tx))
            {
                read.Parameters.AddWithValue("ids", ids.ToArray());
                await using var rows = await read.ExecuteReaderAsync();
                var seen = new List<(string?, string?)>();
                while (await rows.ReadAsync())
                {
                    seen.Add((rows.IsDBNull(0) ? null : rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1)));
                }
                Assert.Equal([("100000000000003", null), (null, "12345"), (null, "TRN 77")], seen);
            }
            await tx.RollbackAsync();
            // As the migration leaves it: no valid number, the old one kept.
            await using var kept = new Npgsql.NpgsqlCommand(
                "UPDATE tenancy.companies SET tax_registration_number = NULL, legacy_tax_registration_number = '12345' WHERE id = @id", db);
            kept.Parameters.AddWithValue("id", ids[1]);
            await kept.ExecuteNonQueryAsync();
        }
        var url = $"/api/tenancy/companies/{ids[1]}";
        var company = (await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(url))!;
        Assert.Null(company["taxRegistrationNumber"]?.GetValue<string>());
        Assert.Equal("12345", company["legacyTaxRegistrationNumber"]!.GetValue<string>());
        // Saved without a number (another field changed): the old one is still kept.
        company["legalNameEn"] = "Old Number Trading LLC";
        var renamed = await admin.PutAsJsonAsync(url, company);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        company = (await renamed.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!;
        Assert.Equal("12345", company["legacyTaxRegistrationNumber"]!.GetValue<string>());
        // An invalid number is refused and keeps it too; a valid one replaces it.
        company["taxRegistrationNumber"] = "12345";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, company)).StatusCode);
        company["taxRegistrationNumber"] = "100-0000-1234-5003";
        var fixedNumber = await admin.PutAsJsonAsync(url, company);
        Assert.Equal(HttpStatusCode.OK, fixedNumber.StatusCode);
        var saved = await Json(fixedNumber);
        Assert.Equal("100000012345003", saved.GetProperty("taxRegistrationNumber").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("legacyTaxRegistrationNumber").ValueKind);
    }

    // Owner decision 2026-10-10 (docs/compliance/arabic-legal-name-on-documents.md): the Arabic legal
    // name stays optional, with a setting of each company (off by default) that makes it required.
    [Fact]
    public async Task A_company_setting_off_by_default_makes_the_arabic_legal_name_required()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var plain = await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-ARN0", nameAr: "")));
        Assert.False(plain.GetProperty("arabicLegalNameRequired").GetBoolean());
        Assert.Equal("", plain.GetProperty("legalNameAr").GetString());

        foreach (var arabic in new string?[] { null, "", "   " })
        {
            var body = JsonSerializer.SerializeToNode(Company("T-ARN1"))!.AsObject();
            body["legalNameAr"] = arabic;
            body["arabicLegalNameRequired"] = true;
            var refused = await admin.PostAsJsonAsync("/api/tenancy/companies", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var error = (await Json(refused)).GetProperty("errors").GetProperty("legalNameAr")[0];
            Assert.Equal("tenancyLegalNameArRequired", error.GetProperty("code").GetString());
            Assert.Matches(@"\p{IsArabic}", error.GetProperty("message").GetString()!);
        }
        var on = JsonSerializer.SerializeToNode(Company("T-ARN1"))!.AsObject();
        on["arabicLegalNameRequired"] = true;
        var created = await admin.PostAsJsonAsync("/api/tenancy/companies", on);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var company = (await created.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!;
        Assert.True(company["arabicLegalNameRequired"]!.GetValue<bool>());
        var url = $"/api/tenancy/companies/{company["id"]!.GetValue<string>()}";

        // A change that leaves the setting out keeps it: the Arabic name cannot be emptied.
        company.Remove("arabicLegalNameRequired");
        company["legalNameAr"] = " ";
        var emptied = await admin.PutAsJsonAsync(url, company);
        Assert.Equal(HttpStatusCode.BadRequest, emptied.StatusCode);
        Assert.Equal("tenancyLegalNameArRequired", (await Json(emptied)).GetProperty("errors").GetProperty("legalNameAr")[0].GetProperty("code").GetString());
        // Turned off, the Arabic name is optional again.
        company["arabicLegalNameRequired"] = false;
        var off = await admin.PutAsJsonAsync(url, company);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var saved = await Json(off);
        Assert.False(saved.GetProperty("arabicLegalNameRequired").GetBoolean());
        Assert.Equal("", saved.GetProperty("legalNameAr").GetString());
        // Turning it on for a company without an Arabic name is refused, and nothing changes.
        company = (await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(url))!;
        company["arabicLegalNameRequired"] = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, company)).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>(url)).GetProperty("arabicLegalNameRequired").GetBoolean());

        // The database holds the same line, and the setting's changes are in the audit trail.
        await using var db = await Env.OpenAdminAsync();
        await using (var write = new Npgsql.NpgsqlCommand("UPDATE tenancy.companies SET arabic_legal_name_required = true WHERE id = @id", db))
        {
            write.Parameters.AddWithValue("id", saved.GetProperty("id").GetGuid());
            var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => write.ExecuteNonQueryAsync());
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, refused.SqlState);
            Assert.Equal("ck_companies_legal_name_ar_required", refused.ConstraintName);
        }
        await using var audit = new Npgsql.NpgsqlCommand(
            "SELECT count(*) FROM audit.entries WHERE table_name = 'companies' AND record_id = @id AND actor_kind = 'user' AND changes::text LIKE '%arabic_legal_name_required%'", db);
        audit.Parameters.AddWithValue("id", saved.GetProperty("id").GetGuid());
        Assert.True((long)(await audit.ExecuteScalarAsync())! >= 2);
    }

    [Fact]
    public async Task Validation_names_every_bad_field_in_the_callers_language()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var response = await admin.PostAsJsonAsync("/api/tenancy/companies", new
        {
            code = "x",
            legalNameEn = "",
            legalNameAr = "",
            taxRegistrationNumber = "TRN-12",
            baseCurrency = "XYZ",
            fiscalYearStartMonth = 2,
            fiscalYearStartDay = 30,
            emirate = "dubai",
            country = "OM",
            phone = "call me",
            website = "example.ae",
            poBox = "P.O. #1",
            email = "not-an-email",
            isActive = (bool?)null,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        string Code(string field) => errors.GetProperty(field)[0].GetProperty("code").GetString()!;
        Assert.Equal("tenancyCode", Code("code"));
        Assert.Equal("tenancyLegalNameEn", Code("legalNameEn"));
        Assert.False(errors.TryGetProperty("legalNameAr", out _));
        Assert.Equal("tenancyTaxNumber", Code("taxRegistrationNumber"));
        Assert.Equal("tenancyCurrency", Code("baseCurrency"));
        Assert.Equal("tenancyDayOfMonth", Code("fiscalYearStartDay"));
        Assert.Equal("tenancyEmirateOutsideUae", Code("emirate"));
        Assert.Equal("tenancyPhone", Code("phone"));
        Assert.Equal("tenancyWebsite", Code("website"));
        Assert.Equal("tenancyPoBox", Code("poBox"));
        Assert.Equal("email", Code("email"));
        Assert.Equal("required", Code("isActive"));
        // The administrator works in Arabic: the messages are Arabic.
        Assert.Matches(@"\p{IsArabic}", errors.GetProperty("code")[0].GetProperty("message").GetString()!);
    }

    /// <summary>Critic p06 round 8: a typo in the twelve-key phone entry (9715055501) was kept as
    /// the "Fujairah" number +971 9 715 055501. A number with the UAE's code is refused on its field
    /// unless it has as many digits as the numbering plan gives its kind; another country's number,
    /// or one without its code, is not judged.</summary>
    [Theory]
    [InlineData("+971 9 715 055501", false)]
    [InlineData("+971 9 715 0555019", false)]
    [InlineData("+971 9 712 34567", false)]
    [InlineData("+971 50 555 019", false)]
    [InlineData("+971 50 555 01990", false)]
    [InlineData("00971 4 555 01", false)]
    [InlineData("+971 0 4 555 0100", false)]
    [InlineData("+971", false)]
    [InlineData("+971 9 712 3456", true)]
    [InlineData("+971 50 555 0199", true)]
    [InlineData("+971 58 555 0199", true)]
    [InlineData("+971 4 000 0000", true)]
    [InlineData("+97125557810", true)]
    [InlineData("00971 2 555 7810", true)]
    [InlineData("+971 600 522222", true)]
    [InlineData("+971 800 12", true)]
    [InlineData("+971 800 123456789", true)]
    [InlineData("+971 800 1234567890", false)]
    [InlineData("+44 20 7946 0958", true)]
    [InlineData("04 555 7810", true)]
    public void A_uae_number_has_the_digits_its_kind_has_in_the_numbering_plan(string phone, bool fits) =>
        Assert.Equal(fits, TenancyValidation.UaeNumberLengthFits(phone));

    [Fact]
    public async Task A_uae_number_of_the_wrong_length_is_refused_on_its_field_in_the_callers_language()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var body = JsonSerializer.SerializeToNode(Company("T-PHN"))!.AsObject();
        body["phone"] = "+971 9 715 055501";
        var response = await admin.PostAsJsonAsync("/api/tenancy/companies", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await Json(response)).GetProperty("errors").GetProperty("phone")[0];
        Assert.Equal("tenancyUaePhone", error.GetProperty("code").GetString());
        Assert.Contains("8 digits after +971", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_need_the_version_read_and_a_company_can_be_deactivated_and_reactivated()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var company = await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-VER")));
        var id = company.GetProperty("id").GetGuid();
        var version = company.GetProperty("version").GetUInt32();

        var noVersion = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}", Company("T-VER"));
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);

        var body = JsonSerializer.SerializeToNode(Company("T-VER", "Renamed LLC"))!.AsObject();
        body["version"] = version;
        body["isActive"] = false;
        var renamed = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}", body);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.False((await Json(renamed)).GetProperty("isActive").GetBoolean());

        var stale = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency", (await Json(stale)).GetProperty("code").GetString());

        // Inactive: hidden from the active list and the switcher, no new branches, still editable.
        var active = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies?filter={Uri.EscapeDataString("isActive eq true")}&take=200");
        Assert.DoesNotContain(active.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var workplace = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
        Assert.DoesNotContain(workplace.GetProperty("companies").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var branch = await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = id, code = "B1", nameEn = "Branch", nameAr = "فرع", country = "AE", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, branch.StatusCode);
        Assert.Equal("tenancyCompanyInactive", (await Json(branch)).GetProperty("errors").GetProperty("companyId")[0].GetProperty("code").GetString());

        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{id}");
        body["version"] = current.GetProperty("version").GetUInt32();
        body["isActive"] = true;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}", body)).StatusCode);

        // Every change is in the audit trail, attributed to the administrator.
        await using var db = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE table_name = 'companies' AND record_id = @id AND actor_kind = 'user'", db);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Branches_belong_to_one_company_and_codes_are_unique_within_it()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var first = (await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-BR1")))).GetProperty("id").GetGuid();
        var second = (await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-BR2")))).GetProperty("id").GetGuid();
        object Branch(Guid company, string code) => new { companyId = company, code, nameEn = "Warehouse", nameAr = "مستودع", city = "Dubai", emirate = "dubai", country = "AE", isActive = true };

        var created = await admin.PostAsJsonAsync("/api/tenancy/branches", Branch(first, "wh-1"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var branch = await Json(created);
        Assert.Equal("WH-1", branch.GetProperty("code").GetString());
        Assert.Equal("T-BR1", branch.GetProperty("companyCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/tenancy/branches", Branch(first, "WH-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/tenancy/branches", Branch(second, "WH-1"))).StatusCode);

        // A branch stays in its company.
        var move = JsonSerializer.SerializeToNode(Branch(second, "WH-1"))!.AsObject();
        move["version"] = branch.GetProperty("version").GetUInt32();
        var moved = await admin.PutAsJsonAsync($"/api/tenancy/branches/{branch.GetProperty("id").GetGuid()}", move);
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        Assert.Equal("tenancyBranchCompanyFixed", (await Json(moved)).GetProperty("errors").GetProperty("companyId")[0].GetProperty("code").GetString());

        var ofFirst = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?filter={Uri.EscapeDataString($"companyId eq '{first}'")}");
        Assert.Equal(1, ofFirst.GetProperty("total").GetInt32());
        var company = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{first}");
        Assert.Equal(1, company.GetProperty("branchCount").GetInt32());

        // Another workspace's company cannot take a branch.
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var foreign = await other.PostAsJsonAsync("/api/tenancy/branches", Branch(first, "WH-9"));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("unknownIds", (await Json(foreign)).GetProperty("errors").GetProperty("companyId")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logos_must_be_real_images_of_the_stated_type_and_are_served_back()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var id = (await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-LOGO")))).GetProperty("id").GetGuid();
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/tenancy/companies/{id}/logo")).StatusCode);
        var svg = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/svg+xml", data = Convert.ToBase64String("<svg/>"u8.ToArray()) });
        Assert.Equal(HttpStatusCode.BadRequest, svg.StatusCode);
        var lying = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/jpeg", data = png });
        Assert.Equal("tenancyLogoType", (await Json(lying)).GetProperty("errors").GetProperty("data")[0].GetProperty("code").GetString());
        var huge = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/png", data = Convert.ToBase64String(new byte[600 * 1024]) });
        Assert.Equal("tenancyLogoTooLarge", (await Json(huge)).GetProperty("errors").GetProperty("data")[0].GetProperty("code").GetString());

        var uploaded = await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/png", data = "data:image/png;base64," + png });
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.True((await Json(uploaded)).GetProperty("hasLogo").GetBoolean());
        var logo = await admin.GetAsync($"/api/tenancy/companies/{id}/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Convert.FromBase64String(png), await logo.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", logo.Headers.GetValues("X-Content-Type-Options").Single());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/tenancy/companies/{id}/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/tenancy/companies/{id}/logo")).StatusCode);

        // The audit trail records the logo by hash, never its bytes.
        await using var db = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT string_agg(changes::text, ' ') FROM audit.entries WHERE table_name = 'companies' AND record_id = @id", db);
        command.Parameters.AddWithValue("id", id);
        var changes = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("logo_hash", changes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"logo\"", changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_companies_list_never_reads_logo_bytes_and_still_searches_filters_sorts_and_groups()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        var id = (await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-LGL", "Logo List Trading LLC")))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/png", data = png })).StatusCode);

        // The SQL a list page runs selects the row columns only, whatever the query asks for (the
        // design-time context has no tenant, so its query filters are left out of this SQL).
        var factory = new Erp.Modules.Tenancy.TenancyDbContextDesignFactory();
        await using (var design = factory.CreateDbContext([]))
        {
            var binding = Erp.Modules.Tenancy.Companies.CompaniesList.Create();
            var sql = binding.Apply(Erp.Modules.Tenancy.Companies.CompanyEndpoints.ListRows(design.Companies.IgnoreQueryFilters()),
                new Erp.Kernel.Lists.ListRequest { Search = "list trading", Filter = "emirate eq 'dubai' and isActive eq true", Sort = "-legalNameAr" }).ToQueryString();
            Assert.DoesNotContain("logo", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("legal_name_ar", sql, StringComparison.Ordinal);
        }

        // And through HTTP the projection still serves search, filter, sort, keyset paging and groups.
        var found = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?search=list%20trading&filter=emirate%20eq%20'dubai'&sort=-legalNameAr");
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        Assert.Equal(id, found.GetProperty("items")[0].GetProperty("id").GetGuid());
        var paged = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=1&sort=code");
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        var next = paged.GetProperty("next").GetString();
        Assert.False(string.IsNullOrEmpty(next));
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies?take=1&sort=code&after={Uri.EscapeDataString(next!)}");
        Assert.NotEqual(paged.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        var grouped = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?groupBy=emirate");
        Assert.True(grouped.GetProperty("groups").GetArrayLength() > 0);
    }

    [Fact]
    public async Task The_same_logo_in_two_companies_has_a_different_tag_in_each()
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        async Task<(Guid Id, string Hash)> Upload(HttpClient client, string code)
        {
            var id = (await Json(await client.PostAsJsonAsync("/api/tenancy/companies", Company(code)))).GetProperty("id").GetGuid();
            var uploaded = await client.PutAsJsonAsync($"/api/tenancy/companies/{id}/logo", new { contentType = "image/png", data = png });
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            return (id, (await Json(uploaded)).GetProperty("logoHash").GetString()!);
        }
        using var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var first = await Upload(a, "T-TAG1");
        var second = await Upload(a, "T-TAG2");
        var other = await Upload(b, "T-TAG1");

        // A tag is keyed by its company: equal images in other companies (or workspaces) never
        // share it, and it is not the plain hash of the image anyone could compute.
        Assert.Equal(64, first.Hash.Length);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, other.Hash);
        Assert.NotEqual(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(png))), first.Hash);

        // The same image again keeps the company's tag (the ETag only changes with the image).
        var again = await a.PutAsJsonAsync($"/api/tenancy/companies/{first.Id}/logo", new { contentType = "image/png", data = png });
        Assert.Equal(first.Hash, (await Json(again)).GetProperty("logoHash").GetString());
        var logo = await a.GetAsync($"/api/tenancy/companies/{first.Id}/logo");
        Assert.Equal($"\"{first.Hash}\"", logo.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task A_company_with_no_arabic_legal_name_is_headed_by_its_english_name_in_an_arabic_profile()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var created = await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-NOAR", "No Arabic Name Trading LLC", ""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var arabic = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={id}&language=ar");
        Assert.Equal("T-NOAR \u00B7 No Arabic Name Trading LLC", arabic.GetProperty("subject").GetString());
        Assert.Contains(arabic.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("text").GetString() == "T-NOAR \u00B7 No Arabic Name Trading LLC");

        // A company that has an Arabic legal name is headed by it.
        var named = (await Json(await admin.PostAsJsonAsync("/api/tenancy/companies", Company("T-WITHAR", "With Arabic Name LLC", "شركة بالاسم العربي ذ.م.م")))).GetProperty("id").GetGuid();
        var withArabic = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={named}&language=ar");
        Assert.Equal("T-WITHAR \u00B7 شركة بالاسم العربي ذ.م.م", withArabic.GetProperty("subject").GetString());
        var english = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={named}&language=en");
        Assert.Equal("T-WITHAR \u00B7 With Arabic Name LLC", english.GetProperty("subject").GetString());
    }
}
