using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Audit.Tests;

/// <summary>Its own environment: these tests change records to read their history.</summary>
public sealed class AuditFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// The audit trail as people use it (p07): a record's history on its form (who changed which
/// field from what to what, and when; newest first, page by page), the audit log list with its
/// filters, search and export, both in English and Arabic, and what each reader may see: only the
/// kinds of records their roles let them read, only in the companies where they may.
/// </summary>
public sealed class AuditTrailTests(AuditFixture fixture) : IClassFixture<AuditFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(Guid Id, string Name)> MeAsync(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var user = session.GetProperty("user");
        return (user.GetProperty("id").GetGuid(), user.GetProperty("displayName").GetString()!);
    }

    private static async Task<JsonElement> CreateRoleAsync(HttpClient admin, string name, params string[] permissions) =>
        await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = name, nameAr = $"دور {name}", permissions }));

    private static async Task<JsonElement> RenameRoleAsync(HttpClient admin, JsonElement role, string name, params string[] permissions) =>
        await Json(await admin.PutAsJsonAsync($"/api/identity/roles/{role.GetProperty("id").GetGuid()}", new
        {
            nameEn = name, nameAr = role.GetProperty("nameAr").GetString(), permissions, version = role.GetProperty("version").GetUInt32(),
        }));

    private static JsonElement Field(JsonElement change, string field) =>
        change.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == field);

    [Fact]
    public async Task A_records_history_shows_each_change_field_by_field_newest_first_with_who_and_when()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var (adminId, adminName) = await MeAsync(admin);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var role = await CreateRoleAsync(admin, "History clerk", "identity.users.read");
        var renamed = await RenameRoleAsync(admin, role, "History clerk II", "identity.users.read", "identity.roles.read");
        var id = role.GetProperty("id").GetGuid();

        var history = await Json(await admin.GetAsync($"/api/audit/records/{id}?type=identity.roles"));
        Assert.Equal("identity.roles", history.GetProperty("recordType").GetString());
        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["update", "insert"], items.Select(i => i.GetProperty("action").GetString()));
        Assert.All(items, i =>
        {
            Assert.Equal(adminId, i.GetProperty("actorId").GetGuid());
            Assert.Equal(adminName, i.GetProperty("actorName").GetString());
            Assert.Equal("user", i.GetProperty("actorKind").GetString());
            Assert.InRange(i.GetProperty("occurredAt").GetDateTimeOffset(), before, DateTimeOffset.UtcNow.AddMinutes(1));
        });
        var name = Field(items[0], "name_en");
        Assert.Equal("History clerk", name.GetProperty("old").GetString());
        Assert.Equal("History clerk II", name.GetProperty("new").GetString());
        Assert.Equal(["identity.users.read"], Field(items[0], "permissions").GetProperty("old").EnumerateArray().Select(p => p.GetString()));
        // Only what changed: the Arabic name stayed and is not in the change.
        Assert.DoesNotContain(items[0].GetProperty("changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "name_ar");
        // The creation lists every first value.
        Assert.Equal("History clerk", Field(items[1], "name_en").GetProperty("new").GetString());
        Assert.Equal($"دور History clerk", Field(items[1], "name_ar").GetProperty("new").GetString());
        Assert.Equal(JsonValueKind.Null, history.GetProperty("next").ValueKind);
        Assert.Equal(renamed.GetProperty("id").GetGuid(), id);
    }

    [Fact]
    public async Task A_long_history_reads_page_by_page_and_only_its_own_changes_continue_it()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await CreateRoleAsync(admin, "Paged 0", "identity.users.read");
        for (var i = 1; i <= 4; i++)
        {
            role = await RenameRoleAsync(admin, role, $"Paged {i}", "identity.users.read");
        }
        var id = role.GetProperty("id").GetGuid();
        var first = await Json(await admin.GetAsync($"/api/audit/records/{id}?type=identity.roles&take=2"));
        var seen = first.GetProperty("items").EnumerateArray().Select(i => Field(i, "name_en").GetProperty("new").GetString()).ToList();
        Assert.Equal(["Paged 4", "Paged 3"], seen);
        var next = first.GetProperty("next").GetGuid();
        var second = await Json(await admin.GetAsync($"/api/audit/records/{id}?type=identity.roles&take=2&before={next}"));
        Assert.Equal(["Paged 2", "Paged 1"], second.GetProperty("items").EnumerateArray().Select(i => Field(i, "name_en").GetProperty("new").GetString()));
        var third = await Json(await admin.GetAsync($"/api/audit/records/{id}?type=identity.roles&take=2&before={second.GetProperty("next").GetGuid()}"));
        Assert.Equal(["insert"], third.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()));
        Assert.Equal(JsonValueKind.Null, third.GetProperty("next").ValueKind);

        // A change of another record, or an id that is no change, does not continue this history.
        var other = await CreateRoleAsync(admin, "Paged other", "identity.users.read");
        var otherChange = (await Json(await admin.GetAsync($"/api/audit/records/{other.GetProperty("id").GetGuid()}?type=identity.roles"))).GetProperty("items")[0].GetProperty("id").GetGuid();
        foreach (var cursor in new[] { otherChange, Guid.NewGuid() })
        {
            var refused = await admin.GetAsync($"/api/audit/records/{id}?type=identity.roles&before={cursor}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("before", out _));
        }
        // An id with no record has no history (the same answer as one the caller cannot read).
        var none = await Json(await admin.GetAsync($"/api/audit/records/{Guid.NewGuid()}?type=identity.roles"));
        Assert.Equal(0, none.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_history_needs_the_audit_permission_and_shows_only_kinds_of_records_the_reader_may_read()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await CreateRoleAsync(admin, "Guarded history", "identity.users.read");
        var id = role.GetProperty("id").GetGuid();
        var (adminId, _) = await MeAsync(admin);

        using var nobody = await Env.SignInAsync(Env.Email(Env.TenantA, "noaccess"));
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"/api/audit/records/{id}?type=identity.roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync("/api/audit/log")).StatusCode);

        // Reading roles is not reading their history: the audit permission is needed as well.
        var rolesReader = await CreateRoleAsync(admin, "Roles reader", "identity.roles.read");
        var plain = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"audit.roles.plain@{Env.TenantA.EmailDomain}", displayName = "Roles Plain", language = "en", password = ErpTestEnvironment.Password,
            mustChangePassword = false, roleIds = new[] { rolesReader.GetProperty("id").GetGuid() },
        }));
        using (var plainReader = await Env.SignInAsync(plain.GetProperty("email").GetString()!))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await plainReader.GetAsync($"/api/audit/records/{id}?type=identity.roles")).StatusCode);
        }

        // With the audit permission, roles readers read roles' histories, never users' or sessions'.
        var auditor = await CreateRoleAsync(admin, "Roles auditor", "identity.roles.read", "audit.log.read");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"audit.roles.reader@{Env.TenantA.EmailDomain}", displayName = "Roles Reader", language = "en", password = ErpTestEnvironment.Password,
            mustChangePassword = false, roleIds = new[] { auditor.GetProperty("id").GetGuid() },
        }));
        using var reader = await Env.SignInAsync(user.GetProperty("email").GetString()!);
        Assert.NotEqual(0, (await Json(await reader.GetAsync($"/api/audit/records/{id}?type=identity.roles"))).GetProperty("items").GetArrayLength());
        Assert.NotEqual(0, (await Json(await reader.GetAsync($"/api/audit/records/{id}"))).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await Json(await reader.GetAsync($"/api/audit/records/{adminId}?type=identity.users"))).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await Json(await reader.GetAsync($"/api/audit/records/{adminId}"))).GetProperty("items").GetArrayLength());
        // A kind of record the trail does not know is refused, naming the parameter.
        var unknown = await reader.GetAsync($"/api/audit/records/{id}?type=no.such_table");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("type", out _));
    }

    [Fact]
    public async Task The_log_lists_changes_newest_first_with_filters_by_field_record_type_person_and_change_and_a_search_by_name()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var (adminId, adminName) = await MeAsync(admin);
        var role = await CreateRoleAsync(admin, "Logwatch Courier", "identity.users.read");
        await RenameRoleAsync(admin, role, "Logwatch Dispatcher", "identity.users.read");
        var id = role.GetProperty("id").GetGuid();

        var page = await Json(await admin.GetAsync("/api/audit/log?take=200"));
        var rows = page.GetProperty("items").EnumerateArray().ToList();
        Assert.True(page.GetProperty("total").GetInt32() >= rows.Count);
        var times = rows.Select(r => r.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        var filter = Uri.EscapeDataString($"recordType eq 'identity.roles' and recordId eq '{id}' and fields eq 'name_en' and action eq 'update' and actorId eq '{adminId}'");
        var found = (await Json(await admin.GetAsync($"/api/audit/log?filter={filter}"))).GetProperty("items").EnumerateArray().ToList();
        var change = Assert.Single(found);
        Assert.Equal("Logwatch Dispatcher", change.GetProperty("record").GetString());
        Assert.Equal(adminName, change.GetProperty("actorName").GetString());
        Assert.Equal(["name_en"], change.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("Name in English: from Logwatch Courier to Logwatch Dispatcher", change.GetProperty("changes").GetString());
        Assert.Equal($"/identity/roles/{id}", change.GetProperty("recordRoute").GetString());
        // In Arabic (a document's language, or the reader's).
        var arabic = Assert.Single((await Json(await admin.GetAsync($"/api/audit/log?filter={filter}&language=ar"))).GetProperty("items").EnumerateArray());
        Assert.Equal("الاسم بالإنجليزية: من Logwatch Courier إلى Logwatch Dispatcher", arabic.GetProperty("changes").GetString());
        // The change itself, with every field's stored values.
        var one = await Json(await admin.GetAsync($"/api/audit/log/{change.GetProperty("id").GetGuid()}"));
        Assert.Equal(change.GetProperty("id").GetGuid(), one.GetProperty("change").GetProperty("id").GetGuid());
        var stored = Assert.Single(one.GetProperty("details").EnumerateArray());
        Assert.Equal(("name_en", "Logwatch Courier", "Logwatch Dispatcher"), (stored.GetProperty("field").GetString(), stored.GetProperty("old").GetString(), stored.GetProperty("new").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/audit/log/{Guid.NewGuid()}")).StatusCode);

        // The record's name finds its changes, under its old name too.
        var byName = (await Json(await admin.GetAsync("/api/audit/log?search=logwatch%20courier"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(byName, r => r.GetProperty("recordId").GetGuid() == id && r.GetProperty("action").GetString() == "insert");
        // A field the change did not touch does not find it.
        var other = Uri.EscapeDataString($"recordId eq '{id}' and fields eq 'name_ar' and action eq 'update'");
        Assert.Equal(0, (await Json(await admin.GetAsync($"/api/audit/log?filter={other}"))).GetProperty("total").GetInt32());
        // "ne": the changes that did not record the field (the rename kept the permissions; the creation recorded them).
        var notField = Uri.EscapeDataString($"recordId eq '{id}' and fields ne 'permissions'");
        Assert.Equal(["update"], (await Json(await admin.GetAsync($"/api/audit/log?filter={notField}"))).GetProperty("items").EnumerateArray().Select(r => r.GetProperty("action").GetString()).Order());
        // A field that is not recorded anywhere is refused, naming the parameter.
        var unknown = await admin.GetAsync($"/api/audit/log?filter={Uri.EscapeDataString("fields eq 'no_such_field'")}");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Ids_in_values_read_as_the_names_of_the_records_they_name()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await CreateRoleAsync(admin, "Named in values", "identity.users.read");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"audit.named@{Env.TenantA.EmailDomain}", displayName = "Named Holder", language = "en", password = ErpTestEnvironment.Password,
            mustChangePassword = false, roleIds = new[] { role.GetProperty("id").GetGuid() },
        }));
        var userId = user.GetProperty("id").GetGuid();
        // The user's sign-in finds the user's changes (the record's e-mail or code is searched with its name).
        var bySignIn = (await Json(await admin.GetAsync("/api/audit/log?search=audit.named"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(bySignIn, r => r.GetProperty("recordType").GetString() == "identity.users" && r.GetProperty("recordId").GetGuid() == userId
                                       && r.GetProperty("recordCode").GetString() == $"audit.named@{Env.TenantA.EmailDomain}");
        var filter = Uri.EscapeDataString($"recordType eq 'identity.user_roles' and action eq 'insert' and fields eq 'user_id'");
        var rows = (await Json(await admin.GetAsync($"/api/audit/log?filter={filter}&take=200"))).GetProperty("items").EnumerateArray()
            .Where(r => r.GetProperty("changes").GetString()!.Contains("User: Named Holder", StringComparison.Ordinal))
            .ToList();
        var assignment = Assert.Single(rows);
        Assert.Contains("Role: Named in values", assignment.GetProperty("changes").GetString());
        var one = await Json(await admin.GetAsync($"/api/audit/log/{assignment.GetProperty("id").GetGuid()}"));
        Assert.Contains(one.GetProperty("details").EnumerateArray(), d => d.GetProperty("field").GetString() == "user_id" && d.GetProperty("new").GetString() == userId.ToString());
        Assert.Equal("Named Holder", one.GetProperty("names").GetProperty(userId.ToString()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Readers_see_only_the_kinds_of_records_their_roles_let_them_read()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer"));
        var sessions = Uri.EscapeDataString("recordType eq 'identity.sessions'");
        // The administrator reads the sign-in history; the read-only role does not (colleagues' addresses and browsers).
        Assert.True((await Json(await admin.GetAsync($"/api/audit/log?filter={sessions}"))).GetProperty("total").GetInt32() > 0);
        Assert.Equal(0, (await Json(await viewer.GetAsync($"/api/audit/log?filter={sessions}"))).GetProperty("total").GetInt32());
        var all = (await Json(await viewer.GetAsync("/api/audit/log?groupBy=recordType&take=1"))).GetProperty("groups").EnumerateArray()
            .Select(g => g.GetProperty("key").GetString()).ToList();
        Assert.DoesNotContain("identity.sessions", all);
        Assert.DoesNotContain("identity.passkeys", all);
        Assert.Contains("identity.roles", all);
    }

    [Fact]
    public async Task A_reader_whose_permission_is_held_in_one_company_sees_only_that_companys_records()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.True(companies.Count >= 2, "the fixture needs two companies");
        var (x, y) = (companies[0], companies[1]);
        var branchesOf = async (Guid company) => (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?take=200&filter={Uri.EscapeDataString($"companyId eq '{company}'")}"))
            .GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        var inX = await branchesOf(x);
        var inY = await branchesOf(y);
        Assert.NotEmpty(inX);
        Assert.NotEmpty(inY);

        // Reads the log everywhere, but branches only in company X.
        var logReader = await CreateRoleAsync(admin, "Log reader", "audit.log.read");
        var branchReader = await CreateRoleAsync(admin, "Branch reader", "tenancy.branches.read");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"audit.company@{Env.TenantA.EmailDomain}", displayName = "Company Auditor", language = "en", password = ErpTestEnvironment.Password,
            mustChangePassword = false, roleIds = new[] { logReader.GetProperty("id").GetGuid() },
            companyRoles = new[] { new { roleId = branchReader.GetProperty("id").GetGuid(), companyId = x } },
        }));
        var userId = user.GetProperty("id").GetGuid();
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}")).GetProperty("version").GetUInt32();
        await Json(await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new
        {
            companies = new[] { x, y }.Select(c => new { companyId = c, allBranches = true, branchIds = Array.Empty<Guid>() }), version,
        }));

        using var auditor = await Env.SignInAsync(user.GetProperty("email").GetString()!);
        var branchRows = (await Json(await auditor.GetAsync($"/api/audit/log?take=200&filter={Uri.EscapeDataString("recordType eq 'tenancy.branches'")}")))
            .GetProperty("items").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid()).ToHashSet();
        Assert.NotEmpty(branchRows);
        Assert.True(branchRows.IsSubsetOf(inX), "a branch of company Y was shown");
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync($"/api/audit/records/{inX[0]}?type=tenancy.branches")).StatusCode);
        var hidden = await Json(await auditor.GetAsync($"/api/audit/records/{inY[0]}?type=tenancy.branches"));
        Assert.Equal(0, hidden.GetProperty("items").GetArrayLength());
        // Companies are not readable to them at all.
        Assert.Equal(0, (await Json(await auditor.GetAsync($"/api/audit/log?filter={Uri.EscapeDataString("recordType eq 'tenancy.companies'")}"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_reader_limited_to_some_branches_of_a_company_sees_the_history_of_those_branches_only()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/companies")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        var x = companies[0];
        var inX = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/branches?take=200&filter={Uri.EscapeDataString($"companyId eq '{x}'")}"))
            .GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        if (inX.Count < 2)
        {
            // A second branch of company X, so one of them lies outside the reader's branches.
            var created = await Json(await admin.PostAsJsonAsync("/api/tenancy/branches", new { companyId = x, nameEn = "Audit second branch", nameAr = "فرع ثان للتدقيق", country = "AE" }));
            inX.Add(created.GetProperty("id").GetGuid());
        }
        var (mine, other) = (inX[0], inX[1]);
        var reader = await CreateRoleAsync(admin, "Branch log reader", "audit.log.read", "tenancy.branches.read");
        var user = await Json(await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = $"audit.branch@{Env.TenantA.EmailDomain}", displayName = "Branch Auditor", language = "en", password = ErpTestEnvironment.Password,
            mustChangePassword = false, roleIds = new[] { reader.GetProperty("id").GetGuid() },
        }));
        var userId = user.GetProperty("id").GetGuid();
        var version = (await admin.GetFromJsonAsync<JsonElement>($"/api/tenancy/access/{userId}")).GetProperty("version").GetUInt32();
        await Json(await admin.PutAsJsonAsync($"/api/tenancy/access/{userId}", new
        {
            companies = new[] { new { companyId = x, allBranches = false, branchIds = new[] { mine } } }, version,
        }));

        using var auditor = await Env.SignInAsync(user.GetProperty("email").GetString()!);
        var rows = (await Json(await auditor.GetAsync($"/api/audit/log?take=200&filter={Uri.EscapeDataString("recordType eq 'tenancy.branches'")}")))
            .GetProperty("items").EnumerateArray().Select(r => r.GetProperty("recordId").GetGuid()).ToHashSet();
        Assert.Contains(mine, rows);
        Assert.DoesNotContain(other, rows);
        Assert.Equal(0, (await Json(await auditor.GetAsync($"/api/audit/records/{other}?type=tenancy.branches"))).GetProperty("items").GetArrayLength());
        Assert.NotEqual(0, (await Json(await auditor.GetAsync($"/api/audit/records/{mine}?type=tenancy.branches"))).GetProperty("items").GetArrayLength());
        // Nothing of the other branch: no row whose branch it is (the access rows to it, the workplaces in it).
        var all = (await Json(await auditor.GetAsync("/api/audit/log?take=200"))).GetRawText();
        Assert.DoesNotContain(other.ToString(), all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_log_prints_and_exports_through_the_report_framework_in_both_languages()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var role = await CreateRoleAsync(admin, "Exported Steward", "identity.users.read");
        await RenameRoleAsync(admin, role, "Exported Warden", "identity.users.read");
        var filter = Uri.EscapeDataString($"recordId eq '{role.GetProperty("id").GetGuid()}' and action eq 'update'");
        var csv = await admin.GetStringAsync($"/api/reports/lists/audit.log?format=csv&language=en&filter={filter}");
        Assert.Contains("from Exported Steward to Exported Warden", csv);
        var arabic = await admin.GetStringAsync($"/api/reports/lists/audit.log?format=csv&language=ar&filter={filter}");
        Assert.Contains("من Exported Steward إلى Exported Warden", arabic);
    }

    [Fact]
    public async Task The_database_keeps_custom_fields_field_by_field_and_names_the_record_and_its_company()
    {
        await using var db = await Env.OpenAdminAsync();
        await using var tx = await db.BeginTransactionAsync();
        var company = Guid.NewGuid();
        var id = Guid.NewGuid();
        await using (var setup = new NpgsqlCommand($$"""
            CREATE TABLE public.audit_probe (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, company_id uuid, display_name text, display_name_ar text, custom_fields jsonb, secret text);
            CREATE TRIGGER audit_capture AFTER INSERT OR UPDATE OR DELETE ON public.audit_probe FOR EACH ROW EXECUTE FUNCTION audit.capture('secret');
            SELECT set_config('app.actor_kind', 'job', true);
            INSERT INTO public.audit_probe VALUES ('{{id}}', '{{Env.TenantA.Id}}', '{{company}}', 'Probe One', 'مسبار', '{"region": "North", "tier": 1}', 's3cret');
            UPDATE public.audit_probe SET custom_fields = '{"region": "South", "tier": 1, "colour": "red"}', secret = 'other', display_name = 'Probe Two' WHERE id = '{{id}}';
            UPDATE public.audit_probe SET custom_fields = '{"region": "South", "tier": 1, "colour": "red"}' WHERE id = '{{id}}';
            UPDATE public.audit_probe SET custom_fields = NULL WHERE id = '{{id}}';
            """, db, tx))
        {
            await setup.ExecuteNonQueryAsync();
        }
        var rows = new List<(string Action, string Changes, string? Label, string? LabelAr, Guid? Company, string[] Fields)>();
        await using (var read = new NpgsqlCommand("SELECT action, changes::text, record_label, record_label_ar, record_company_id, fields FROM audit.entries WHERE record_id = @id ORDER BY occurred_at, id", db, tx))
        {
            read.Parameters.AddWithValue("id", id);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetFieldValue<string[]>(5)));
            }
        }
        await tx.RollbackAsync();

        // The update that changed nothing left no row.
        Assert.Equal(["insert", "update", "update"], rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal(company, r.Company));
        Assert.Equal(["Probe One", "Probe Two", "Probe Two"], rows.Select(r => r.Label));
        Assert.All(rows, r => Assert.Equal("مسبار", r.LabelAr));
        var created = JsonDocument.Parse(rows[0].Changes).RootElement;
        Assert.Equal("North", created.GetProperty("custom_fields.region").GetProperty("new").GetString());
        Assert.Equal(1, created.GetProperty("custom_fields.tier").GetProperty("new").GetInt32());
        Assert.Equal("[redacted]", created.GetProperty("secret").GetProperty("new").GetString());
        Assert.False(created.TryGetProperty("custom_fields", out _));
        var changed = JsonDocument.Parse(rows[1].Changes).RootElement;
        Assert.Equal(["custom_fields.colour", "custom_fields.region", "display_name", "secret"], rows[1].Fields);
        Assert.Equal("North", changed.GetProperty("custom_fields.region").GetProperty("old").GetString());
        Assert.Equal("South", changed.GetProperty("custom_fields.region").GetProperty("new").GetString());
        Assert.False(changed.GetProperty("custom_fields.colour").TryGetProperty("old", out _));
        Assert.Equal("[redacted]", changed.GetProperty("secret").GetProperty("old").GetString());
        // Every custom field emptied at once is recorded field by field too.
        Assert.Equal(["custom_fields.colour", "custom_fields.region", "custom_fields.tier"], rows[2].Fields);
        Assert.DoesNotContain("s3cret", string.Join(" ", rows.Select(r => r.Changes)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_database_shows_a_companys_audit_rows_only_inside_that_companys_scope()
    {
        await using var admin = await Env.OpenAdminAsync();
        var companies = new List<Guid>();
        await using (var read = new NpgsqlCommand("SELECT DISTINCT record_company_id FROM audit.entries WHERE tenant_id = @t AND record_company_id IS NOT NULL", admin))
        {
            read.Parameters.AddWithValue("t", Env.TenantA.Id);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                companies.Add(reader.GetGuid(0));
            }
        }
        Assert.True(companies.Count >= 2, "the fixture needs audit rows of two companies");
        await using var app = await Env.OpenAppAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using (var bind = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), " +
            "set_config('app.company_scope', 'list', true), set_config('app.company_ids', @c, true), set_config('app.company_tx', extract(epoch from now())::text, true)", app, tx))
        {
            bind.Parameters.AddWithValue("t", Env.TenantA.Id.ToString());
            bind.Parameters.AddWithValue("c", companies[0].ToString());
            await bind.ExecuteNonQueryAsync();
        }
        await using var seen = new NpgsqlCommand("SELECT array_agg(DISTINCT record_company_id) FILTER (WHERE record_company_id IS NOT NULL), count(*) FILTER (WHERE record_company_id IS NULL) FROM audit.entries", app, tx);
        await using var result = await seen.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal([companies[0]], result.GetFieldValue<Guid[]>(0));
        Assert.True(result.GetInt64(1) > 0, "rows of no company stay readable");
    }
}
