using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Numbering.Tests;

/// <summary>
/// The Document numbering screen's API on the gate seed (each workspace: two companies, a sequence
/// for each of the eleven document types in each, tenant A's purchase orders counted to 11, tenant
/// B's own prefixes): the list and the record with their next numbers, changing a sequence and its
/// next number, every validation rule in both languages, the company and branch rules, versions, the
/// audit trail and a new company's sequences.
/// </summary>
public sealed class SequenceApiTests(NumberingFixture fixture) : IClassFixture<NumberingFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Year => DateTime.UtcNow.AddHours(4).Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The gate seed makes every sequence show the month (so every reset is a valid edit):
    /// a sequence that showed none ends "/{MM}".</summary>
    private static string Month => "/" + DateTime.UtcNow.AddHours(4).Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpClient> SignInAsync(string local = "admin") => await Env.SignInAsync(Env.Email(Env.TenantA, local));

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client, string query = "take=200")
    {
        var response = await client.GetAsync($"/api/numbering/sequences?{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private static async Task<JsonElement> RowAsync(HttpClient client, string type, Guid? company = null) =>
        (await RowsAsync(client)).First(r => r.GetProperty("documentType").GetString() == type && (company is null || r.GetProperty("companyId").GetGuid() == company));

    [Fact]
    public async Task Switching_from_every_year_to_never_and_back_in_the_same_year_never_offers_a_number_again_that_the_other_run_moved_past()
    {
        using var admin = await SignInAsync();
        var row = await RowAsync(admin, "sales.quotation");
        async Task<JsonElement> Step(JsonElement from, string reset, long? next = null)
        {
            var response = await PutAsync(admin, from, Body(from, reset: reset, nextValue: next));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await Json(response);
        }
        var yearly = await Step(row, "yearly", 4100);
        var never = await Step(yearly, "never");
        var raised = await Step(never, "never", 4200);
        var back = await Step(raised, "yearly");
        Assert.Contains("04200", back.GetProperty("nextNumber").GetString(), StringComparison.Ordinal);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));

    private static JsonElement FirstError(JsonElement refusal, string field) => refusal.GetProperty("errors").GetProperty(field)[0];

    [Fact]
    public async Task Owner_rule_1_a_tax_documents_sequence_is_gapless_always_and_neither_the_screen_nor_the_api_turns_it_off()
    {
        using var english = await SignInAsync();
        using var arabic = await SignInAsync("admin.ar");
        foreach (var type in new[] { "sales.invoice", "sales.creditNote" })
        {
            var row = await RowAsync(english, type);
            Assert.True(row.GetProperty("tax").GetBoolean());
            Assert.True(row.GetProperty("gapless").GetBoolean());
            var refused = await PutAsync(english, row, Body(row, gapless: false));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var error = FirstError(await Json(refused), "gapless");
            Assert.Equal("numberingTaxGapless", error.GetProperty("code").GetString());
            Assert.Contains("cannot be turned off", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            var inArabic = await PutAsync(arabic, row, Body(row, gapless: false));
            Assert.Matches(@"\p{IsArabic}", FirstError(await Json(inArabic), "gapless").GetProperty("message").GetString()!);
            // A request that leaves it out is refused as any missing field is.
            var left = await english.PutAsJsonAsync($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}",
                new { padding = 5, reset = row.GetProperty("reset").GetString(), perBranch = false, version = row.GetProperty("version").GetUInt32() });
            Assert.Equal(HttpStatusCode.BadRequest, left.StatusCode);
            // Nothing was saved.
            var after = await RowAsync(english, type);
            Assert.True(after.GetProperty("gapless").GetBoolean());
            Assert.Equal(row.GetProperty("version").GetUInt32(), after.GetProperty("version").GetUInt32());
        }
        // A sequence of any other kind keeps its choice.
        var order = await RowAsync(english, "purchasing.order");
        Assert.False(order.GetProperty("tax").GetBoolean());
        var free = await PutAsync(english, order, Body(order, gapless: false));
        Assert.True(free.StatusCode == HttpStatusCode.OK, await free.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_rule_3_a_tax_documents_next_number_is_set_only_before_the_first_number_of_the_period_is_given_and_never_moves_back()
    {
        using var admin = await SignInAsync();
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var row = await RowAsync(admin, "sales.invoice", companies[1]);
        var raised = await PutAsync(admin, row, Body(row, nextValue: 500));
        Assert.True(raised.StatusCode == HttpStatusCode.OK, await raised.Content.ReadAsStringAsync());
        var raisedRecord = await Json(raised);
        var lowered = await PutAsync(admin, raisedRecord, Body(row, nextValue: 300, version: raisedRecord.GetProperty("version").GetUInt32()));
        Assert.True(lowered.StatusCode == HttpStatusCode.OK, "before the first number of the period is given the start may still be corrected: " + await lowered.Content.ReadAsStringAsync());
        var record = await Json(lowered);
        Assert.Equal(300, record.GetProperty("nextValue").GetInt64());

        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            Assert.Equal(300, (await unit.TakeAsync("sales.invoice", companies[1], Today)).Value);
            await unit.CommitAsync();
        }
        record = await admin.GetFromJsonAsync<JsonElement>($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}");
        foreach (var next in new long[] { 100, 300, 302, 900 })
        {
            var moved = await PutAsync(admin, record, Body(record, nextValue: next));
            Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
            var code = FirstError(await Json(moved), "nextValue").GetProperty("code").GetString();
            Assert.Equal(next <= 300 ? "numberingNextNumberBelowIssued" : "numberingGaplessNextNumberFixed", code);
        }
        // Sending the number it already stands at changes nothing and is accepted.
        var kept = await PutAsync(admin, record, Body(record, nextValue: record.GetProperty("nextValue").GetInt64()));
        Assert.True(kept.StatusCode == HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        Assert.Equal(301, (await Json(kept)).GetProperty("nextValue").GetInt64());
    }

    [Fact]
    public async Task Owner_rule_4_a_tax_documents_sequence_starts_again_only_when_its_numbers_hold_the_period()
    {
        using var admin = await SignInAsync();
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var row = await RowAsync(admin, "sales.creditNote", companies[0]);
        async Task<string> Refused(object body, string field)
        {
            var response = await PutAsync(admin, row, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return FirstError(await Json(response), field).GetProperty("code").GetString()!;
        }
        Assert.Equal("numberingYearlyNeedsYear", await Refused(Body(row, prefix: "CN-", suffix: "", reset: "yearly"), "reset"));
        Assert.Equal("numberingMonthlyNeedsMonth", await Refused(Body(row, prefix: "CN-{YYYY}-", suffix: "", reset: "monthly"), "reset"));
        Assert.Equal("numberingMonthlyNeedsMonth", await Refused(Body(row, prefix: "CN-{MM}-", suffix: "", reset: "monthly"), "reset"));
        // With the period in the number the reset is allowed; without any it never resets.
        var monthly = await PutAsync(admin, row, Body(row, prefix: "CN-{YYYY}{MM}-", suffix: "", reset: "monthly"));
        Assert.True(monthly.StatusCode == HttpStatusCode.OK, await monthly.Content.ReadAsStringAsync());
        var monthlyRecord = await Json(monthly);
        var never = await PutAsync(admin, monthlyRecord, Body(row, prefix: "CN-", suffix: "", reset: "never", version: monthlyRecord.GetProperty("version").GetUInt32()));
        Assert.True(never.StatusCode == HttpStatusCode.OK, await never.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Owner_rule_5_a_pattern_change_applies_forward_and_one_that_could_give_a_number_already_given_is_refused_in_both_languages()
    {
        using var english = await SignInAsync();
        using var arabic = await SignInAsync("admin.ar");
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var company = companies[0];
        // Numbers INV-<year>-100001 and -100002 (prefix ends in a 1, five digits) are given.
        await Db.SetAsync(Env, company, "sales.invoice", "INV-{YYYY}-1", NumberReset.Yearly, gapless: true);
        var date = Today;
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            Assert.Equal($"INV-{date.Year}-100001", (await unit.TakeAsync("sales.invoice", company, date)).Number);
            Assert.Equal($"INV-{date.Year}-100002", (await unit.TakeAsync("sales.invoice", company, date)).Number);
            await unit.CommitAsync();
        }
        var row = await RowAsync(english, "sales.invoice", company);
        // Six digits without the 1 would give INV-<year>-100001 again.
        var refused = await PutAsync(english, row, Body(row, prefix: "INV-{YYYY}-", padding: 6));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var error = FirstError(await Json(refused), "prefix");
        Assert.Equal("numberingPatternRepeatsNumber", error.GetProperty("code").GetString());
        Assert.Contains($"INV-{date.Year}-100001", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        var inArabic = await PutAsync(arabic, row, Body(row, prefix: "INV-{YYYY}-", padding: 6));
        Assert.Matches(@"\p{IsArabic}", FirstError(await Json(inArabic), "prefix").GetProperty("message").GetString()!);
        Assert.Equal("INV-{YYYY}-1", (await RowAsync(english, "sales.invoice", company)).GetProperty("prefix").GetString());

        // Fewer digits cannot give a number already given: it applies forward, numbers given stay.
        var forward = await PutAsync(english, row, Body(row, padding: 3));
        Assert.True(forward.StatusCode == HttpStatusCode.OK, await forward.Content.ReadAsStringAsync());
        Assert.Equal($"INV-{date.Year}-1003", (await Json(forward)).GetProperty("nextNumber").GetString());
        Assert.Equal(2, await Db.ScalarAsync(Env, "SELECT count(*) FROM numbering.issued_numbers WHERE company_id = @c AND document_type = 'sales.invoice' AND number LIKE 'INV-%-10000_'", ("c", company)));
    }

    [Fact]
    public async Task Owner_rule_6_each_company_has_one_sequence_for_each_tax_document_type_and_none_can_be_added()
    {
        using var admin = await SignInAsync();
        var rows = await RowsAsync(admin);
        foreach (var type in new[] { "sales.invoice", "sales.creditNote" })
        {
            var ofType = rows.Where(r => r.GetProperty("documentType").GetString() == type).ToList();
            Assert.Equal(ofType.Select(r => r.GetProperty("companyId").GetGuid()).Distinct().Count(), ofType.Count);
            Assert.Equal((await Db.CompaniesAsync(Env, Env.TenantA.Id)).Count, ofType.Count);
        }
        // No endpoint makes another one, and the database refuses a second row for a company and type.
        var post = await admin.PostAsJsonAsync("/api/numbering/sequences", new { documentType = "sales.invoice" });
        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        var company = (await Db.CompaniesAsync(Env, Env.TenantA.Id))[0];
        await using var db = await Env.OpenAdminAsync();
        await using var duplicate = new Npgsql.NpgsqlCommand(
            "INSERT INTO numbering.sequences (id, tenant_id, company_id, document_type, prefix, suffix, padding, reset, gapless, per_branch) SELECT gen_random_uuid(), tenant_id, company_id, document_type, prefix, suffix, padding, reset, gapless, per_branch FROM numbering.sequences WHERE company_id = @c AND document_type = 'sales.invoice'", db);
        duplicate.Parameters.AddWithValue("c", company);
        var failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }

    private static object Body(JsonElement record, string? prefix = null, int? padding = null, string? reset = null, bool? gapless = null, bool? perBranch = null,
        long? nextValue = null, string? suffix = null, uint? version = null) => new
    {
        prefix = prefix ?? record.GetProperty("prefix").GetString(),
        suffix = suffix ?? record.GetProperty("suffix").GetString(),
        padding = padding ?? record.GetProperty("padding").GetInt32(),
        reset = reset ?? record.GetProperty("reset").GetString(),
        gapless = gapless ?? record.GetProperty("gapless").GetBoolean(),
        perBranch = perBranch ?? record.GetProperty("perBranch").GetBoolean(),
        nextValue,
        version = version ?? record.GetProperty("version").GetUInt32(),
    };

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, JsonElement record, object body) =>
        await client.PutAsJsonAsync($"/api/numbering/sequences/{record.GetProperty("id").GetGuid()}", body);

    [Fact]
    public async Task Every_company_holds_a_sequence_for_each_document_type_with_its_next_number_the_working_company_first()
    {
        using var viewer = await SignInAsync("viewer");
        var rows = await RowsAsync(viewer);
        // The viewer works in the first company only.
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        Assert.Equal(11, rows.Count);
        Assert.All(rows, r => Assert.Equal(companies[0], r.GetProperty("companyId").GetGuid()));
        Assert.All(rows, r => Assert.False(r.GetProperty("canUpdate").GetBoolean()));

        using var admin = await SignInAsync();
        var all = await RowsAsync(admin);
        Assert.Equal(22, all.Count);
        Assert.Equal(11, all.TakeWhile(r => r.GetProperty("workingCompany").GetBoolean()).Count());
        var order = all.Single(r => r.GetProperty("documentType").GetString() == "purchasing.order" && r.GetProperty("workingCompany").GetBoolean());
        Assert.Equal(("Purchase order", "أمر شراء", "PO-{YYYY}-", 5, "yearly", false, false),
            (order.GetProperty("nameEn").GetString(), order.GetProperty("nameAr").GetString(), order.GetProperty("prefix").GetString(), order.GetProperty("padding").GetInt32(),
                order.GetProperty("reset").GetString(), order.GetProperty("gapless").GetBoolean(), order.GetProperty("perBranch").GetBoolean()));
        // The gate seed counted tenant A's purchase orders to 11 this year.
        Assert.Equal(($"PO-{Year}-00012{Month}", 12L), (order.GetProperty("nextNumber").GetString(), order.GetProperty("nextValue").GetInt64()));
        Assert.True(order.GetProperty("canUpdate").GetBoolean());
        var invoice = all.First(r => r.GetProperty("documentType").GetString() == "sales.invoice");
        Assert.True(invoice.GetProperty("gapless").GetBoolean());
        Assert.Equal($"INV-{Year}-00001{Month}", invoice.GetProperty("nextNumber").GetString());
        // Tenant B's own prefixes never appear.
        Assert.DoesNotContain(all, r => r.GetProperty("prefix").GetString() is "BPO7-{YYYY}-" or "BQ73-" or "BDN4-{BRANCH}-");

        // Search in English and Arabic, and the list's filters.
        Assert.Equal(2, (await RowsAsync(admin, "search=purchase%20order")).Count(r => r.GetProperty("documentType").GetString() == "purchasing.order"));
        Assert.Contains(await RowsAsync(admin, "search=" + Uri.EscapeDataString("أمر شراء")), r => r.GetProperty("documentType").GetString() == "purchasing.order");
        var gapless = await RowsAsync(admin, "filter=" + Uri.EscapeDataString("gapless eq true"));
        // Tax invoices, tax credit notes and journal vouchers are gapless by default in both companies.
        Assert.Equal(6, gapless.Count(r => r.GetProperty("documentType").GetString() is "sales.invoice" or "sales.creditNote" or "accounting.journal"));
        Assert.All(gapless, r => Assert.True(r.GetProperty("gapless").GetBoolean()));
    }

    [Fact]
    public async Task The_record_previews_the_next_number_and_shows_its_counters_its_last_numbers_and_its_companys_branches()
    {
        using var admin = await SignInAsync();
        var row = await RowAsync(admin, "purchasing.order");
        var record = await admin.GetFromJsonAsync<JsonElement>($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}");
        Assert.Equal($"PO-{Year}-00012{Month}", record.GetProperty("nextNumber").GetString());
        var counter = record.GetProperty("counters")[0];
        Assert.Equal((Year, 12L, 11L), (counter.GetProperty("period").GetString(), counter.GetProperty("nextValue").GetInt64(), counter.GetProperty("lastIssued").GetInt64()));
        Assert.Equal(new[] { $"PO-{Year}-00011{Month}", $"PO-{Year}-00010{Month}" }, record.GetProperty("recent").EnumerateArray().Select(i => i.GetProperty("number").GetString()).ToArray());
        Assert.Equal(2, record.GetProperty("branches").GetArrayLength());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", record.GetProperty("today").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/numbering/sequences/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_sequence_is_changed_with_its_version_and_the_change_is_audited()
    {
        using var admin = await SignInAsync();
        var row = await RowAsync(admin, "sales.order");
        var saved = await PutAsync(admin, row, Body(row, prefix: "SO-2026-", padding: 6, reset: "never"));
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var record = await Json(saved);
        Assert.Equal(("SO-2026-", 6, "never", $"SO-2026-000001{Month}"),
            (record.GetProperty("prefix").GetString(), record.GetProperty("padding").GetInt32(), record.GetProperty("reset").GetString(), record.GetProperty("nextNumber").GetString()));

        // A version read before that change is refused, and nothing changes.
        var stale = await PutAsync(admin, row, Body(row, prefix: "X-", reset: "never"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("numbering.sequenceChanged", (await Json(stale)).GetProperty("code").GetString());
        Assert.Equal("SO-2026-", (await RowAsync(admin, "sales.order")).GetProperty("prefix").GetString());

        var user = await Db.UserAsync(Env, Env.Email(Env.TenantA, "admin"));
        var changes = await Db.TextAsync(Env,
            "SELECT string_agg(changes::text, ' ') FROM audit.entries WHERE table_schema = 'numbering' AND table_name = 'sequences' AND record_id = @id AND actor_id = @u AND actor_kind = 'user'",
            ("id", row.GetProperty("id").GetGuid()), ("u", user));
        Assert.Contains("\"prefix\"", changes, StringComparison.Ordinal);
        Assert.Contains("SO-2026-", changes, StringComparison.Ordinal);
        Assert.Contains("\"padding\"", changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sequence_that_starts_again_at_another_time_goes_on_from_todays_count()
    {
        using var admin = await SignInAsync();
        var row = await RowAsync(admin, "purchasing.order");
        var changed = await PutAsync(admin, row, Body(row, prefix: "PO-", reset: "never", padding: 6));
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var record = await Json(changed);
        Assert.Equal($"PO-000012{Month}", record.GetProperty("nextNumber").GetString());
        // Back to a yearly run: this year's counter is where it was.
        var back = await PutAsync(admin, record, Body(record, prefix: "PO-{YYYY}-", reset: "yearly", padding: 5));
        Assert.Equal($"PO-{Year}-00012{Month}", (await Json(back)).GetProperty("nextNumber").GetString());
    }

    [Fact]
    public async Task The_next_number_moves_up_never_below_a_given_number_and_never_on_a_gapless_sequence_once_numbers_were_given()
    {
        using var admin = await SignInAsync();
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var row = await RowAsync(admin, "purchasing.order", companies[1]);
        var raised = await PutAsync(admin, row, Body(row, nextValue: 1500));
        Assert.True(raised.StatusCode == HttpStatusCode.OK, await raised.Content.ReadAsStringAsync());
        var record = await Json(raised);
        Assert.Equal($"PO-{Year}-01500{Month}", record.GetProperty("nextNumber").GetString());

        // Take one so that 1500 is given, then try to go back.
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            Assert.Equal(1500, (await unit.TakeAsync("purchasing.order", companies[1], DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4)))).Value);
            await unit.CommitAsync();
        }
        record = await admin.GetFromJsonAsync<JsonElement>($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}");
        var lower = await PutAsync(admin, record, Body(record, nextValue: 1200));
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
        Assert.Contains("1,500", (await Json(lower)).GetProperty("errors").GetProperty("nextValue")[0].GetProperty("message").GetString(), StringComparison.Ordinal);

        // The same sequence made gapless: its next number can no longer move.
        var gapless = await PutAsync(admin, record, Body(record, gapless: true, nextValue: 1600));
        Assert.Equal(HttpStatusCode.BadRequest, gapless.StatusCode);
        Assert.Equal("numberingGaplessNextNumberFixed", (await Json(gapless)).GetProperty("errors").GetProperty("nextValue")[0].GetProperty("code").GetString());
        var unchanged = await PutAsync(admin, record, Body(record, gapless: true, nextValue: 1501));
        Assert.True(unchanged.StatusCode == HttpStatusCode.OK, await unchanged.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_pattern_rule_is_checked_and_named_in_the_callers_language()
    {
        using var english = await SignInAsync();
        using var arabic = await SignInAsync("admin.ar");
        var row = await RowAsync(english, "purchasing.bill");
        async Task<JsonElement> Refused(HttpClient client, object body)
        {
            var response = await PutAsync(client, row, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            return (await Json(response)).GetProperty("errors");
        }
        var yearly = await Refused(english, Body(row, prefix: "B-", reset: "yearly"));
        Assert.Equal("numberingYearlyNeedsYear", yearly.GetProperty("reset")[0].GetProperty("code").GetString());
        Assert.Contains("{YYYY}", yearly.GetProperty("reset")[0].GetProperty("message").GetString(), StringComparison.Ordinal);
        var arabicYearly = await Refused(arabic, Body(row, prefix: "B-", reset: "yearly"));
        Assert.Matches(@"\p{IsArabic}", arabicYearly.GetProperty("reset")[0].GetProperty("message").GetString()!);
        Assert.Equal("numberingCharacters", (await Refused(english, Body(row, prefix: "B {YYYY}"))).GetProperty("prefix")[0].GetProperty("code").GetString());
        Assert.Equal("numberingToken", (await Refused(english, Body(row, prefix: "B-{YEAR}-", reset: "never"))).GetProperty("prefix")[0].GetProperty("code").GetString());
        Assert.Equal("numberingMonthlyNeedsMonth", (await Refused(english, Body(row, suffix: "", reset: "monthly"))).GetProperty("reset")[0].GetProperty("code").GetString());
        Assert.Equal("numberingPerBranchNeedsBranch", (await Refused(english, Body(row, perBranch: true))).GetProperty("perBranch")[0].GetProperty("code").GetString());
        Assert.Equal("numberingPadding", (await Refused(english, Body(row, padding: 13))).GetProperty("padding")[0].GetProperty("code").GetString());
        Assert.Equal("numberingReset", (await Refused(english, Body(row, reset: "weekly"))).GetProperty("reset")[0].GetProperty("code").GetString());
        Assert.Equal("numberingNextNumberPerBranch", (await Refused(english, Body(row, prefix: "B-{BRANCH}-{YYYY}-", perBranch: true, nextValue: 9))).GetProperty("nextValue")[0].GetProperty("code").GetString());
        var missing = await english.PutAsJsonAsync($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}", new { prefix = "B-" });
        var errors = (await Json(missing)).GetProperty("errors");
        Assert.All(new[] { "padding", "reset", "gapless", "perBranch", "version" }, field => Assert.True(errors.TryGetProperty(field, out _), field));
        // Nothing was saved by any of them.
        Assert.Equal("BILL-{YYYY}-", (await RowAsync(english, "purchasing.bill")).GetProperty("prefix").GetString());
    }

    [Fact]
    public async Task Only_someone_who_holds_the_permission_and_every_branch_of_the_company_changes_its_sequences()
    {
        using var viewer = await SignInAsync("viewer");
        var row = await RowAsync(viewer, "inventory.receipt");
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(viewer, row, Body(row))).StatusCode);
        using var nobody = await SignInAsync("noaccess");
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/numbering/sequences")).StatusCode);

        // The Arabic administrator works in one branch of the second company only.
        using var limited = await SignInAsync("admin.ar");
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var shared = await RowAsync(limited, "inventory.receipt", companies[1]);
        Assert.False(shared.GetProperty("canUpdate").GetBoolean());
        var refused = await PutAsync(limited, shared, Body(shared, prefix: "GRN-X-{YYYY}-"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var problem = await Json(refused);
        Assert.Equal("numbering.companyNeedsEveryBranch", problem.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", problem.GetProperty("title").GetString()!);
        Assert.Equal("GRN-{YYYY}-", (await RowAsync(limited, "inventory.receipt", companies[1])).GetProperty("prefix").GetString());
        Assert.True((await RowAsync(limited, "inventory.receipt", companies[0])).GetProperty("canUpdate").GetBoolean());
    }

    [Fact]
    public async Task A_prefix_or_suffix_left_out_keeps_the_sequences_own_and_an_empty_one_clears_it()
    {
        using var admin = await SignInAsync();
        var row = await RowAsync(admin, "payments.receipt");
        Assert.Equal("/{MM}", row.GetProperty("suffix").GetString());
        var kept = await admin.PutAsJsonAsync($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}",
            new { padding = 6, reset = "monthly", gapless = false, perBranch = false, version = row.GetProperty("version").GetUInt32() });
        Assert.True(kept.StatusCode == HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
        var record = await Json(kept);
        Assert.Equal(("RV-{YYYY}-", "/{MM}", 6, "monthly"),
            (record.GetProperty("prefix").GetString(), record.GetProperty("suffix").GetString(), record.GetProperty("padding").GetInt32(), record.GetProperty("reset").GetString()));
        var cleared = await PutAsync(admin, record, Body(record, suffix: "", reset: "yearly"));
        Assert.True(cleared.StatusCode == HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        Assert.Equal("", (await Json(cleared)).GetProperty("suffix").GetString());
    }

    [Fact]
    public async Task A_workspaces_sequences_sort_by_id_company_by_company_its_first_company_first()
    {
        // As its companies and branches do: a record taken "first" of the table is the first
        // company's (where the gates' branch-limited Arabic administrator works in every branch).
        foreach (var tenant in new[] { Env.TenantA, Env.TenantB })
        {
            var order = await Db.TextAsync(Env,
                "SELECT string_agg(s.company_id::text, ',' ORDER BY s.id) FROM numbering.sequences s WHERE s.tenant_id = @t AND s.created_by IS NULL", ("t", tenant.Id));
            var companies = order.Split(',');
            var first = await Db.TextAsync(Env, "SELECT id::text FROM tenancy.companies WHERE tenant_id = @t ORDER BY id LIMIT 1", ("t", tenant.Id));
            Assert.All(companies.Take(11), c => Assert.Equal(first, c));
            Assert.Equal(companies.Distinct().Count(), companies.Select((c, i) => (c, i)).Count(x => x.i == 0 || companies[x.i - 1] != x.c));
        }
    }

    [Fact]
    public void Every_registered_document_type_has_its_name_in_English_and_Arabic()
    {
        var strings = Env.Factory.Services.GetRequiredService<Erp.Kernel.Localization.StringCatalog>();
        var types = Env.Factory.Services.GetServices<Erp.Modules.Numbering.Contracts.NumberedDocumentType>().ToList();
        Assert.Equal(11, types.Count);
        foreach (var type in types)
        {
            Assert.True(strings.Contains(type.LabelKey), type.LabelKey);
            Assert.NotEqual(type.LabelKey, strings.Get(type.LabelKey, "en"));
            Assert.Matches(@"\p{IsArabic}", strings.Get(type.LabelKey, "ar"));
            Assert.Empty(type.Problems());
        }
    }

    [Fact]
    public async Task A_new_company_gets_a_sequence_for_every_document_type_from_the_defaults()
    {
        // In tenant B, so tenant A's lists stay as the other tests count them.
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var created = await admin.PostAsJsonAsync("/api/tenancy/companies", new
        {
            legalNameEn = "Numbering Test Trading LLC", baseCurrency = "AED", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, country = "AE", isActive = true,
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var company = (await Json(created)).GetProperty("id").GetGuid();
        var rows = (await RowsAsync(admin)).Where(r => r.GetProperty("companyId").GetGuid() == company).ToList();
        Assert.Equal(11, rows.Count);
        Assert.Equal($"QT-{Year}-00001", rows.Single(r => r.GetProperty("documentType").GetString() == "sales.quotation").GetProperty("nextNumber").GetString());
        Assert.Equal(11, await Db.ScalarAsync(Env, "SELECT count(*) FROM audit.entries WHERE table_schema = 'numbering' AND table_name = 'sequences' AND action = 'insert' AND changes::text LIKE @c",
            ("c", $"%{company}%")));
    }
}
