using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Reports.Tests;

public sealed class ReportsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The report API as a client uses it: the catalogue (only what the caller may run), a record
/// document in English and Arabic as JSON and PDF, a grouped report, list printing with the list's
/// own query, the CSV and XLSX exports, validation in the caller's language, and the tenant and
/// company scope (another tenant's record does not exist for the caller).
/// </summary>
public sealed class ReportApiTests(ReportsFixture fixture) : IClassFixture<ReportsFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<(HttpClient Client, Guid CompanyId)> AdminWithCompanyAsync()
    {
        var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=10");
        return (admin, companies.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_catalogue_lists_reports_and_lists_in_the_callers_language()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var catalogue = await admin.GetFromJsonAsync<JsonElement>("/api/reports/catalog");
        var keys = catalogue.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Contains("tenancy.companyProfile", keys);
        Assert.Contains("tenancy.branchDirectory", keys);
        Assert.Contains("identity.usersByRole", keys);
        var lists = catalogue.GetProperty("lists").EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Contains("identity.users", lists);
        Assert.Contains("tenancy.companies", lists);
        var profile = catalogue.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "tenancy.companyProfile");
        Assert.Equal("Company profile", profile.GetProperty("title").GetString());
        Assert.True(profile.GetProperty("isDocument").GetBoolean());
        var company = profile.GetProperty("parameters")[0];
        Assert.Equal("reference", company.GetProperty("type").GetString());
        Assert.Equal("/api/tenancy/companies", company.GetProperty("lookupEndpoint").GetString());

        var inArabic = await admin.GetFromJsonAsync<JsonElement>("/api/reports/catalog?language=ar");
        Assert.Contains(inArabic.GetProperty("items").EnumerateArray(), i => i.GetProperty("title").GetString() == "ملف الشركة");
    }

    [Fact]
    public async Task A_company_profile_prints_in_arabic_from_an_english_session()
    {
        var (admin, companyId) = await AdminWithCompanyAsync();
        using (admin)
        {
            var document = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={companyId}&language=ar&numerals=arab");
            Assert.Equal("ملف الشركة", document.GetProperty("title").GetString());
            Assert.Equal("rtl", document.GetProperty("direction").GetString());
            Assert.Equal("arab", document.GetProperty("numerals").GetString());
            var facts = document.GetProperty("facts").EnumerateArray().ToDictionary(f => f.GetProperty("label").GetString()!, f => f.GetProperty("text").GetString());
            Assert.Contains("الرمز", facts.Keys);
            Assert.True(document.GetProperty("rowCount").GetInt32() >= 1);
            Assert.Contains(document.GetProperty("columns").EnumerateArray(), c => c.GetProperty("label").GetString() == "المدينة");

            using var pdf = await admin.GetAsync($"/api/reports/run/tenancy.companyProfile?company={companyId}&language=ar&format=pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", pdf.Content.Headers.ContentDisposition?.DispositionType);
            Assert.True(pdf.Headers.Contains("X-Erp-Printed-At"));
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            var text = PdfText.Of(bytes);
            Assert.Contains("ملف الشركة", text, StringComparison.Ordinal);
            Assert.Contains("/Direction /R2L", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);

            using var inline = await admin.GetAsync($"/api/reports/run/tenancy.companyProfile?company={companyId}&format=pdf&disposition=inline");
            Assert.Equal("inline", inline.Content.Headers.ContentDisposition?.DispositionType);
        }
    }

    [Fact]
    public async Task Another_tenants_company_or_no_company_is_not_found_and_bad_parameters_are_explained()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var other = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var theirs = (await other.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=1")).GetProperty("items")[0].GetProperty("id").GetGuid();
        foreach (var id in new[] { theirs, Guid.NewGuid() })
        {
            using var response = await admin.GetAsync($"/api/reports/run/tenancy.companyProfile?company={id}&format=pdf");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var missing = await admin.GetAsync("/api/reports/run/tenancy.companyProfile");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("company", out _));

        using var bad = await admin.GetAsync("/api/reports/run/tenancy.branchDirectory?format=docx&language=fr&timeZone=Mars%2FOlympus&groupBy=phone&includeInactive=maybe");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var field in new[] { "format", "language", "timeZone", "groupBy", "includeInactive" })
        {
            Assert.True(errors.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task The_branch_directory_groups_by_company_with_counts_and_by_any_groupable_column()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var branches = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/branches?take=200&filter=" + Uri.EscapeDataString("isActive eq true"))).GetProperty("total").GetInt32();
        var document = await admin.GetFromJsonAsync<JsonElement>("/api/reports/run/tenancy.branchDirectory");
        Assert.Equal("company", document.GetProperty("groupBy").GetString());
        var groups = document.GetProperty("groups").EnumerateArray().ToList();
        Assert.All(groups, g => Assert.False(string.IsNullOrEmpty(g.GetProperty("label").GetString())));
        Assert.Equal(branches, groups.Sum(g => g.GetProperty("count").GetInt32()));
        Assert.Equal(branches, document.GetProperty("rowCount").GetInt32());

        var byStatus = await admin.GetFromJsonAsync<JsonElement>("/api/reports/run/tenancy.branchDirectory?groupBy=active&includeInactive=true");
        Assert.Equal("active", byStatus.GetProperty("groupBy").GetString());
        Assert.Contains(byStatus.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("text").GetString() == "Yes");

        // A chosen emirate prints when the directory found branches there, and an emirate with no
        // branch leaves an empty document with no criteria (the same for every value).
        var emirates = document.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("rows").EnumerateArray())
            .Select(r => r.GetProperty("cells")[document.GetProperty("columns").EnumerateArray().ToList().FindIndex(c => c.GetProperty("key").GetString() == "emirate")].GetProperty("value").GetString())
            .ToHashSet();
        var held = emirates.First()!;
        var inHeld = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.branchDirectory?emirate={held}");
        Assert.True(inHeld.GetProperty("rowCount").GetInt32() > 0);
        Assert.Contains(inHeld.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("label").GetString() == "Emirate");
        var unheld = new[] { "abuDhabi", "dubai", "sharjah", "ajman", "ummAlQuwain", "rasAlKhaimah", "fujairah" }.First(e => !emirates.Contains(e));
        var inUnheld = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.branchDirectory?emirate={unheld}&groupBy=");
        Assert.Equal(0, inUnheld.GetProperty("rowCount").GetInt32());
        Assert.Empty(inUnheld.GetProperty("parameters").EnumerateArray());

        var flat = await admin.GetFromJsonAsync<JsonElement>("/api/reports/run/tenancy.branchDirectory?groupBy=");
        Assert.Equal(JsonValueKind.Null, flat.GetProperty("groupBy").ValueKind);
        Assert.Single(flat.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task A_list_prints_with_its_own_query_columns_and_totals()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var filter = Uri.EscapeDataString("isActive eq true");
        var document = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/lists/identity.users?filter={filter}&sort=displayName&columns=displayName,email,language&groupBy=language");
        var expected = (await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users?filter={filter}&take=200")).GetProperty("total").GetInt32();
        Assert.Equal(expected, document.GetProperty("rowCount").GetInt32());
        Assert.Equal(new[] { "displayName", "email", "language" }, document.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()!).ToArray());
        Assert.Contains(document.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("label").GetString() == "Status" && p.GetProperty("text").GetString() == "is Yes");
        Assert.Contains(document.GetProperty("groups").EnumerateArray(), g => g.GetProperty("label").GetString() == "English");

        // Roles carry a total of their user counts.
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/reports/lists/identity.roles?columns=nameEn,userCount");
        var users = roles.GetProperty("groups")[0].GetProperty("rows").EnumerateArray().Sum(r => decimal.Parse(r.GetProperty("cells")[1].GetProperty("value").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(users.ToString(System.Globalization.CultureInfo.InvariantCulture), roles.GetProperty("totals")[1].GetProperty("value").GetString());

        // A document repeats only what it found: an id the caller cannot see prints as "Not found",
        // typed text an empty document found nothing for is left out (never echoed).
        var stranger = Guid.NewGuid().ToString();
        var byRole = await admin.GetStringAsync($"/api/reports/run/identity.usersByRole?role={stranger}");
        Assert.DoesNotContain(stranger, byRole, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(JsonDocument.Parse(byRole).RootElement.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("text").GetString() == "Not found");
        var nothing = await admin.GetStringAsync("/api/reports/lists/identity.users?search=zqxnomatch&filter=" + Uri.EscapeDataString("displayName eq 'zqxnomatch2'"));
        Assert.DoesNotContain("zqxnomatch", nothing, StringComparison.Ordinal);
        Assert.Equal(0, JsonDocument.Parse(nothing).RootElement.GetProperty("rowCount").GetInt32());
        var branches = await admin.GetStringAsync("/api/reports/lists/tenancy.branches?filter=" + Uri.EscapeDataString($"companyId eq '{stranger}'"));
        Assert.DoesNotContain(stranger, branches, StringComparison.OrdinalIgnoreCase);
        var searched = await admin.GetFromJsonAsync<JsonElement>("/api/reports/lists/identity.users?search=admin");
        Assert.Contains(searched.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("label").GetString() == "Search" && p.GetProperty("text").GetString() == "admin");

        using var bad = await admin.GetAsync("/api/reports/lists/identity.users?columns=displayName,password");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var badFilter = await admin.GetAsync("/api/reports/lists/identity.users?filter=" + Uri.EscapeDataString("nope eq 1"));
        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
    }

    [Fact]
    public async Task Exports_carry_the_rows_as_csv_and_as_a_workbook()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var csv = await admin.GetAsync("/api/reports/lists/tenancy.branches?format=csv&language=ar");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("﻿", text, StringComparison.Ordinal);
        Assert.Contains("الرمز", text, StringComparison.Ordinal);

        using var xlsx = await admin.GetAsync("/api/reports/run/identity.usersByRole?format=xlsx&language=ar");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync()));
        var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("rightToLeft=\"1\"", sheet, StringComparison.Ordinal);
        Assert.Contains(Env.Email(Env.TenantA, "admin"), sheet, StringComparison.Ordinal);
    }

    /// <summary>A list value (the companies a user may work in) prints as the companies' codes, not
    /// as the JSON it arrives in; and the access list prints quickly in both languages (its PDF
    /// once laid out a long JSON value without end).</summary>
    [Fact]
    public async Task A_list_of_records_in_a_cell_prints_as_their_codes()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var csv = await admin.GetAsync("/api/reports/lists/tenancy.access?format=csv&language=en");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain("\"companyId\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("allBranches", text, StringComparison.Ordinal);
        using var page = await admin.GetAsync("/api/tenancy/access?take=50");
        var codes = System.Text.Json.JsonDocument.Parse(await page.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray()
            .SelectMany(u => u.GetProperty("companies").EnumerateArray().Select(c => c.GetProperty("code").GetString()!)).Distinct().ToList();
        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Contains(code, text, StringComparison.Ordinal));
        foreach (var language in new[] { "en", "ar" })
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var pdf = await admin.GetAsync($"/api/reports/lists/tenancy.access?format=pdf&language={language}");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"{language}: {clock.Elapsed.TotalSeconds:F1} s");
        }
    }

    [Fact]
    public async Task A_role_without_the_data_permission_cannot_run_the_report_and_does_not_see_it()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Report reader", nameAr = "قارئ التقارير", permissions = new[] { "reports.catalog.read" } });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"reports.only@{Env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Reports only", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        using var reader = await Env.SignInAsync(email);
        var catalogue = await reader.GetFromJsonAsync<JsonElement>("/api/reports/catalog");
        Assert.Empty(catalogue.GetProperty("items").EnumerateArray());
        Assert.Empty(catalogue.GetProperty("lists").EnumerateArray());
        using var run = await reader.GetAsync("/api/reports/run/tenancy.branchDirectory");
        Assert.Equal(HttpStatusCode.Forbidden, run.StatusCode);
        using var list = await reader.GetAsync("/api/reports/lists/identity.users?format=pdf");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }
}
