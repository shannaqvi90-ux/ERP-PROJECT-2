using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, reports and roles held in one company (p02 round 12: a permission counts only in the
/// companies whose roles grant it). A report's own permission narrows what it reads to the
/// companies where that permission is held (the kernel). A report that prints another area's data
/// (branches on a company's profile, a company's legal name in the branch directory, role names in
/// users by role) needs that area's permission too, and a role giving it in company X must not
/// print company Y's data. Every report is printed in every format and both languages by users who
/// hold the report's permission in X and Y and the other area's in X only, with a control that
/// shows the same data printed where the permission is held.
/// </summary>
public sealed partial class G2CompanyRoleTests
{
    private static readonly string[] ReportFormats = ["json", "csv", "xlsx", "pdf"];
    private static readonly string[] ReportLanguages = ["en", "ar"];

    [Fact]
    public async Task A_report_prints_another_areas_data_only_in_the_companies_where_that_permission_is_held()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = await GateCompanies.OfAsync(admin);
        Assert.True(companies.Ids.Count == 2, "the gate fixture needs two companies in tenant A");
        var (x, y) = (companies.Ids[0], companies.Ids[1]);
        var staff = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate staff", nameAr = "موظفو بوابة التقارير", permissions = Switching.Append("reports.catalog.read").ToArray() });
        var readCompanies = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate companies", nameAr = "شركات بوابة التقارير", permissions = new[] { "tenancy.companies.read" } });
        var readBranches = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate branches", nameAr = "فروع بوابة التقارير", permissions = new[] { "tenancy.branches.read" } });
        var readUsers = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate users", nameAr = "مستخدمو بوابة التقارير", permissions = new[] { "identity.users.read" } });
        var readRoles = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate roles", nameAr = "أدوار بوابة التقارير", permissions = new[] { "identity.roles.read" } });
        // A branch of each company that the other's documents can never print by chance.
        await CreateAsync(admin, "/api/tenancy/branches", new { companyId = x, code = "G2-RPT-X", nameEn = "Report gate branch Xavier", nameAr = "فرع بوابة التقارير س", country = "AE", isActive = true });
        await CreateAsync(admin, "/api/tenancy/branches", new { companyId = y, code = "G2-RPT-Y", nameEn = "Report gate branch Yarrow", nameAr = "فرع بوابة التقارير ص", country = "AE", isActive = true });
        var branchNames = (await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/branches?take=200")).GetProperty("items").EnumerateArray()
            .SelectMany(b => new[] { b.GetProperty("nameEn").GetString(), b.GetProperty("nameAr").GetString() })
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).OrderByDescending(n => n.Length).ToList();
        var nameX = await LegalNamesAsync(admin, x);
        var nameY = await LegalNamesAsync(admin, y);
        Assert.False(nameY.Any(n => nameX.Any(m => m.Contains(n, StringComparison.Ordinal))), "company Y's legal names must not be part of company X's");
        // A role held only in Y, so users by role has a row whose role name only roles.read in Y may print.
        var onlyInY = await CreateAsync(admin, "/api/identity/roles", new { nameEn = "Report gate Yonder role", nameAr = "دور بوابة التقارير ص", permissions = new[] { "tenancy.workplace.read" } });
        var holderY = await CreateAsync(admin, "/api/identity/users", new
        {
            email = $"g2.report.holder@{Env.TenantA.EmailDomain}", displayName = "Report gate holder", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { staff }, companyRoles = new[] { new { roleId = onlyInY, companyId = y } },
        });
        await companies.GiveAccessAsync(holderY);

        var problems = new List<string>();
        var checks = 0;

        // Branches on a company's profile: companies read in X and Y, branches in X only.
        using (var reader = await ReportUserAsync(admin, companies, "g2.report.profile", staff, (readCompanies, x), (readCompanies, y), (readBranches, x)))
        {
            foreach (var (format, language) in Formats())
            {
                var ofY = await PrintAsync(reader, $"/api/reports/run/tenancy.companyProfile?company={y}&format={format}&language={language}", problems);
                foreach (var hidden in new[] { "G2-RPT-Y", "Report gate branch Yarrow", "فرع بوابة التقارير ص" }.Where(h => ofY?.Contains(h, StringComparison.Ordinal) == true))
                {
                    problems.Add($"company profile of Y ({format}, {language}) prints Y's branch '{hidden}' to a user who reads branches only in X");
                }
                var ofX = await PrintAsync(reader, $"/api/reports/run/tenancy.companyProfile?company={x}&format={format}&language={language}", problems);
                if (ofX is not null && !ofX.Contains("G2-RPT-X", StringComparison.Ordinal))
                {
                    problems.Add($"control: company profile of X ({format}, {language}) does not print X's branch to a user who reads branches in X");
                }
                checks += 2;
            }
        }

        // A company's legal name in the branch directory: branches read in X and Y, companies in X only.
        using (var reader = await ReportUserAsync(admin, companies, "g2.report.branches", staff, (readBranches, x), (readBranches, y), (readCompanies, x)))
        {
            foreach (var (format, language) in Formats())
            {
                foreach (var query in new[] { "", $"&company={y}", "&groupBy=city" })
                {
                    var printed = await PrintAsync(reader, $"/api/reports/run/tenancy.branchDirectory?format={format}&language={language}{query}", problems);
                    if (printed is null)
                    {
                        continue;
                    }
                    // A branch's own name may hold its company's ("Alpha Steel Industries branch
                    // one"): branch names are the branches' data, printed to a reader of branches.
                    var text = branchNames.Aggregate(printed, (t, n) => t.Replace(n, "\u2027", StringComparison.Ordinal));
                    foreach (var hidden in nameY.Where(n => text.Contains(n, StringComparison.Ordinal)))
                    {
                        var at = text.IndexOf(hidden, StringComparison.Ordinal);
                        problems.Add($"branch directory{query} ({format}, {language}) prints company Y's legal name '{hidden}' to a user who reads companies only in X: …{text[Math.Max(0, at - 60)..Math.Min(text.Length, at + hidden.Length + 30)]}…");
                    }
                    if (!text.Contains("G2-RPT-Y", StringComparison.Ordinal))
                    {
                        problems.Add($"control: branch directory{query} ({format}, {language}) does not print Y's branch to a user who reads branches in Y");
                    }
                    if (query == "" && !nameX.Any(n => text.Contains(n, StringComparison.Ordinal)))
                    {
                        problems.Add($"control: branch directory ({format}, {language}) does not print company X's legal name to a user who reads companies in X");
                    }
                    checks++;
                }
            }
        }

        // Role names in users by role: users read in X and Y, roles in X only; the control reads roles in both.
        using (var reader = await ReportUserAsync(admin, companies, "g2.report.users", staff, (readUsers, x), (readUsers, y), (readRoles, x)))
        using (var control = await ReportUserAsync(admin, companies, "g2.report.users.control", staff, (readUsers, x), (readUsers, y), (readRoles, x), (readRoles, y)))
        {
            foreach (var (format, language) in Formats())
            {
                var text = await PrintAsync(reader, $"/api/reports/run/identity.usersByRole?format={format}&language={language}", problems);
                foreach (var hidden in new[] { "Report gate Yonder role", "دور بوابة التقارير ص" }.Where(h => text?.Contains(h, StringComparison.Ordinal) == true))
                {
                    problems.Add($"users by role ({format}, {language}) prints the role '{hidden}' held in Y to a user who reads roles only in X");
                }
                var shown = await PrintAsync(control, $"/api/reports/run/identity.usersByRole?format={format}&language={language}", problems);
                if (shown is not null && !shown.Contains(language == "ar" ? "دور بوابة التقارير ص" : "Report gate Yonder role", StringComparison.Ordinal))
                {
                    problems.Add($"control: users by role ({format}, {language}) does not print the role held in Y to a user who reads roles in X and Y");
                }
                checks += 2;
            }
            // Nothing the screen offers is refused: the catalogue leaves out what the run withholds.
            var entry = (await reader.GetFromJsonAsync<JsonElement>("/api/reports/catalog")).GetProperty("items").EnumerateArray()
                .Single(r => r.GetProperty("key").GetString() == "identity.usersByRole");
            var columns = entry.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToList();
            var parameters = entry.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("key").GetString()).ToList();
            if (columns.Contains("role") || columns.Contains("company") || parameters.Contains("role") || (entry.TryGetProperty("defaultGroupBy", out var group) && group.ValueKind != JsonValueKind.Null))
            {
                problems.Add($"the catalogue offers users by role's role columns, role parameter or role grouping to a user who reads roles only in X: columns {string.Join(",", columns)}, parameters {string.Join(",", parameters)}");
            }
            using var grouped = await reader.GetAsync("/api/reports/run/identity.usersByRole?format=json&groupBy=role");
            using var byRole = await reader.GetAsync($"/api/reports/run/identity.usersByRole?format=json&role={onlyInY}");
            if (grouped.StatusCode != HttpStatusCode.BadRequest || byRole.StatusCode != HttpStatusCode.BadRequest)
            {
                problems.Add($"users by role grouped by role answered {(int)grouped.StatusCode} and filtered by a role answered {(int)byRole.StatusCode} to a user who reads roles only in X (expected 400 each)");
            }
            checks += 3;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{checks} report prints and refusals judged for roles held in one company");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(60)));
        Assert.True(checks >= Ratchet.Min("g2.companyRoleReportChecks"), $"g2.companyRoleReportChecks: {checks}; ratchet minimum {Ratchet.Min("g2.companyRoleReportChecks")}");
    }

    private static IEnumerable<(string Format, string Language)> Formats() =>
        ReportFormats.SelectMany(f => ReportLanguages.Select(l => (f, l)));

    /// <summary>A user holding <paramref name="staff"/> everywhere and each (role, company) given,
    /// working in X and Y, signed in and working in X.</summary>
    private async Task<HttpClient> ReportUserAsync(HttpClient admin, GateCompanies companies, string local, Guid staff, params (Guid Role, Guid Company)[] held)
    {
        var email = $"{local}@{Env.TenantA.EmailDomain}";
        var user = await CreateAsync(admin, "/api/identity/users", new
        {
            email, displayName = local, language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false,
            roleIds = new[] { staff }, companyRoles = held.Select(h => new { roleId = h.Role, companyId = h.Company }).ToArray(),
        });
        await companies.GiveAccessAsync(user);
        var client = await Env.SignInAsync(email);
        await SwitchAsync(client, companies.Ids[0]);
        return client;
    }

    /// <summary>The text of a printed report as a reader sees it (PDF text, workbook cells), or
    /// null with a problem when it did not print.</summary>
    private static async Task<string?> PrintAsync(HttpClient client, string path, List<string> problems)
    {
        using var response = await client.GetAsync(path);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            problems.Add($"GET {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return null;
        }
        return await ResponseText.ReadAsync(response);
    }

    private static async Task<string[]> LegalNamesAsync(HttpClient admin, Guid company)
    {
        var record = await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/companies/{company}");
        return new[] { record.GetProperty("legalNameEn").GetString(), record.GetProperty("legalNameAr").GetString() }
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToArray();
    }
}
