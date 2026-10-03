using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

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
        Assert.Equal("required", Code("legalNameEn"));
        Assert.Equal("required", Code("legalNameAr"));
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
        var active = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?isActive=true&take=200");
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

        var ofFirst = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?companyId={first}");
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
}
