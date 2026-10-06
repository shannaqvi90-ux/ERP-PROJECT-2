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

    /// <summary>A user holding exactly these permissions, working in every company.</summary>
    private async Task<HttpClient> UserWithAsync(HttpClient admin, string name, params string[] permissions)
    {
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = $"Only {name}", nameAr = $"فقط {name}", permissions });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"{name}@{Env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = name, language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/companies?take=200")).GetProperty("items").EnumerateArray()
            .Select(c => new { companyId = c.GetProperty("id").GetGuid(), allBranches = true }).ToList();
        using var access = await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new { companies });
        Assert.True(access.IsSuccessStatusCode, await access.Content.ReadAsStringAsync());
        return await Env.SignInAsync(email);
    }

    [Fact]
    public async Task Another_areas_columns_and_parameters_are_left_out_for_a_caller_who_cannot_read_that_area()
    {
        var (admin, companyId) = await AdminWithCompanyAsync();
        using (admin)
        {
            // A company profile under the companies' read permission: the branches (another area)
            // only with the branches' read permission as well; the document says what it left out.
            using var companiesOnly = await UserWithAsync(admin, "profile.companies", "tenancy.companies.read");
            var profile = await companiesOnly.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={companyId}&language=en");
            Assert.Empty(profile.GetProperty("columns").EnumerateArray());
            Assert.Equal(0, profile.GetProperty("rowCount").GetInt32());
            Assert.Contains(profile.GetProperty("notes").EnumerateArray(), n => n.GetString()!.StartsWith("Not shown, because your roles do not allow reading it", StringComparison.Ordinal));
            Assert.NotEmpty(profile.GetProperty("facts").EnumerateArray());
            var pdf = await companiesOnly.GetByteArrayAsync($"/api/reports/run/tenancy.companyProfile?company={companyId}&format=pdf&language=ar");
            Assert.Equal("%PDF-1.7\n", Encoding.Latin1.GetString(pdf, 0, 9));
            using var withBranches = await UserWithAsync(admin, "profile.both", "tenancy.companies.read", "tenancy.branches.read");
            var full = await withBranches.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.companyProfile?company={companyId}&language=en");
            Assert.NotEmpty(full.GetProperty("columns").EnumerateArray());
            Assert.Empty(full.GetProperty("notes").EnumerateArray());

            // Users by role under the users' read permission: no role column, no grouping by role,
            // the role parameter refused; the catalogue offers neither.
            using var usersOnly = await UserWithAsync(admin, "byrole.users", "identity.users.read", "reports.catalog.read");
            var byRole = await usersOnly.GetFromJsonAsync<JsonElement>("/api/reports/run/identity.usersByRole?language=en");
            Assert.DoesNotContain(byRole.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == "role");
            Assert.True(byRole.GetProperty("groupBy").ValueKind == JsonValueKind.Null);
            using var refused = await usersOnly.GetAsync($"/api/reports/run/identity.usersByRole?role={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("reportParameterPermission", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var catalogue = await usersOnly.GetFromJsonAsync<JsonElement>("/api/reports/catalog?language=en");
            var item = catalogue.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "identity.usersByRole");
            Assert.DoesNotContain(item.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == "role");
            Assert.DoesNotContain(item.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("key").GetString() == "role");
            Assert.True(item.GetProperty("defaultGroupBy").ValueKind == JsonValueKind.Null);

            // The branch directory under the branches' read permission names a branch's company by
            // its code (a branch shows it); the legal name only to a reader of companies.
            var company = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{companyId}");
            using var branchesOnly = await UserWithAsync(admin, "directory.branches", "tenancy.branches.read");
            var directory = await branchesOnly.GetFromJsonAsync<JsonElement>($"/api/reports/run/tenancy.branchDirectory?company={companyId}&groupBy=city&language=en");
            var companyCells = directory.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("rows").EnumerateArray()).Select(r => r.GetProperty("cells")[0].GetProperty("text").GetString()).Distinct().ToList();
            Assert.Equal([company.GetProperty("code").GetString()], companyCells);
            // Nowhere in the rows, groups or parameters (the letterhead names the caller's own working company).
            foreach (var section in new[] { "groups", "parameters" })
            {
                Assert.DoesNotContain(company.GetProperty("legalNameEn").GetString()!, directory.GetProperty(section).GetRawText(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task A_printed_users_list_names_the_roles_for_a_reader_of_roles_and_exports_moments_as_the_wall_clock()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var csv = Encoding.UTF8.GetString(await admin.GetByteArrayAsync("/api/reports/lists/identity.users?format=csv&language=en&columns=email,roleIds,lastSignInAt&search=admin"));
        var line = csv.Split("\r\n").Single(l => l.StartsWith(Env.Email(Env.TenantA, "admin"), StringComparison.Ordinal));
        Assert.Contains("Administrator", line, StringComparison.Ordinal);
        Assert.Matches(@",\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", line);
        var arabic = await admin.GetFromJsonAsync<JsonElement>("/api/reports/lists/identity.users?language=ar&columns=email,roleIds&search=admin");
        Assert.Contains(arabic.GetProperty("groups")[0].GetProperty("rows").EnumerateArray(), r => r.GetProperty("cells")[1].GetProperty("text").GetString()!.Length > 1);

        // Without the roles' read permission the roles are counted, never named.
        using var usersOnly = await UserWithAsync(admin, "list.users", "identity.users.read");
        var counted = Encoding.UTF8.GetString(await usersOnly.GetByteArrayAsync("/api/reports/lists/identity.users?format=csv&language=en&columns=email,roleIds&search=admin"));
        var countedLine = counted.Split("\r\n").Single(l => l.StartsWith(Env.Email(Env.TenantA, "admin"), StringComparison.Ordinal));
        Assert.DoesNotContain("Administrator", countedLine, StringComparison.Ordinal);
        Assert.EndsWith(",1", countedLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_roles_report_totals_users_and_permissions_per_kind_and_overall()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var document = await admin.GetFromJsonAsync<JsonElement>("/api/reports/run/identity.roleSummary?language=en");
        Assert.Equal("kind", document.GetProperty("groupBy").GetString());
        var rows = document.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("rows").EnumerateArray()).ToList();
        Assert.NotEmpty(rows);
        foreach (var (index, column) in new[] { (2, "users"), (3, "permissions") })
        {
            var sum = rows.Sum(r => r.GetProperty("cells")[index].GetProperty("value").GetInt64());
            Assert.Equal(sum.ToString(System.Globalization.CultureInfo.InvariantCulture), document.GetProperty("totals")[index].GetProperty("value").GetString());
            foreach (var group in document.GetProperty("groups").EnumerateArray())
            {
                var groupSum = group.GetProperty("rows").EnumerateArray().Sum(r => r.GetProperty("cells")[index].GetProperty("value").GetInt64());
                Assert.Equal(groupSum.ToString(System.Globalization.CultureInfo.InvariantCulture), group.GetProperty("totals")[index].GetProperty("value").GetString());
            }
            Assert.Contains(document.GetProperty("columns").EnumerateArray(), c => c.GetProperty("key").GetString() == column && c.GetProperty("total").GetBoolean());
        }
        var pdf = PdfText.Of(await admin.GetByteArrayAsync("/api/reports/run/identity.roleSummary?format=pdf&language=en"));
        Assert.Contains("Total", pdf, StringComparison.Ordinal);
    }
}
