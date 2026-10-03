using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.Rules;

/// <summary>Its own environment: these tests write.</summary>
public sealed class AuditFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// CLAUDE.md rule 4: every business record keeps who changed what, and when. Every tenant table
/// (except the reviewed exemptions) carries the audit trigger, and inserts, updates and deletes
/// through the API and through raw SQL each leave a field-level audit row in the same
/// transaction, attributed to the signed-in user and tenant.
/// </summary>
public sealed class AuditGateTests(AuditFixture fixture) : IClassFixture<AuditFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Every_tenant_table_carries_the_audit_trigger_unless_reviewed_exempt()
    {
        await using var admin = await Env.OpenAdminAsync();
        var exempt = Repo.ReadReviewedList("tests/Gates/audit-exempt.txt");
        Assert.All(exempt, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason), $"{e.Entry} needs a reason"));
        var problems = new List<string>();
        var audited = 0;
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            var triggers = await DbCatalog.ReadAsync(admin, """
                SELECT t.tgname, p.proname, n.nspname, t.tgtype, t.tgenabled
                  FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid JOIN pg_namespace n ON n.oid = p.pronamespace
                 WHERE t.tgrelid = to_regclass(@t) AND NOT t.tgisinternal
                """, r => (Name: r.GetString(0), Function: $"{r.GetString(2)}.{r.GetString(1)}", Type: r.GetInt16(3), Enabled: r.GetChar(4)),
                ("t", table.Qualified));
            // tgtype bits: ROW 1, INSERT 4, DELETE 8, UPDATE 16; AFTER means BEFORE bit (2) clear.
            var capture = triggers.Where(t => t.Function == "audit.capture" && (t.Type & 1) == 1 && (t.Type & 2) == 0
                                              && (t.Type & 4) != 0 && (t.Type & 8) != 0 && (t.Type & 16) != 0 && t.Enabled == 'O').ToList();
            var isExempt = exempt.Any(e => e.Entry == table.Qualified);
            if (capture.Count == 1)
            {
                audited++;
                if (isExempt) problems.Add($"{table}: audited but listed as exempt; remove it from audit-exempt.txt");
            }
            else if (!isExempt)
            {
                problems.Add($"{table}: no enabled AFTER INSERT OR UPDATE OR DELETE FOR EACH ROW audit.capture() trigger");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(audited >= Ratchet.Min("rules.auditedTables"), $"{audited} audited tables; ratchet minimum {Ratchet.Min("rules.auditedTables")}");
        Assert.True(exempt.Count <= Ratchet.Max("rules.auditExemptTables"));
    }

    [Fact]
    public async Task Writes_through_the_API_leave_field_level_audit_rows_attributed_to_the_user()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var adminId = session.GetProperty("user").GetProperty("id").GetGuid();

        var created = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Audited role", nameAr = "دور مدقق", permissions = new[] { "identity.users.read" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = role.GetProperty("id").GetGuid();

        var updated = await admin.PutAsJsonAsync($"/api/identity/roles/{roleId}", new
        {
            nameEn = "Audited role renamed",
            nameAr = "دور مدقق",
            permissions = new[] { "identity.users.read", "identity.roles.read" },
            version = role.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var deleted = await admin.DeleteAsync($"/api/identity/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var db = await Env.OpenAdminAsync();
        var rows = await DbCatalog.ReadAsync(db, """
            SELECT action, actor_id, actor_kind, tenant_id, changes::text, correlation_id
              FROM audit.entries WHERE table_name = 'roles' AND record_id = @id ORDER BY id
            """, r => (Action: r.GetString(0), Actor: r.IsDBNull(1) ? (Guid?)null : r.GetGuid(1), Kind: r.GetString(2), Tenant: r.GetGuid(3),
                Changes: JsonDocument.Parse(r.GetString(4)).RootElement, Correlation: r.IsDBNull(5) ? null : r.GetString(5)),
            ("id", roleId));

        Assert.Equal(["insert", "update", "delete"], rows.Select(r => r.Action));
        Assert.All(rows, r =>
        {
            Assert.Equal(adminId, r.Actor);
            Assert.Equal("user", r.Kind);
            Assert.Equal(Env.TenantA.Id, r.Tenant);
            Assert.False(string.IsNullOrEmpty(r.Correlation));
        });
        Assert.Equal("Audited role", rows[0].Changes.GetProperty("name_en").GetProperty("new").GetString());
        var nameChange = rows[1].Changes.GetProperty("name_en");
        Assert.Equal("Audited role", nameChange.GetProperty("old").GetString());
        Assert.Equal("Audited role renamed", nameChange.GetProperty("new").GetString());
        Assert.True(rows[1].Changes.TryGetProperty("permissions", out _));
        Assert.False(rows[1].Changes.TryGetProperty("name_ar", out _), "Unchanged fields must not be recorded as changes");
        Assert.Equal("Audited role renamed", rows[2].Changes.GetProperty("name_en").GetProperty("old").GetString());
    }

    [Fact]
    public async Task Changing_ones_own_interface_preferences_is_audited_field_by_field()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var adminId = session.GetProperty("user").GetProperty("id").GetGuid();
        var before = session.GetProperty("user").GetProperty("numerals").GetString();
        var after = before == "arab" ? "latn" : "arab";

        var response = await admin.PutAsJsonAsync("/api/identity/me/preferences", new { numerals = after });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = await Env.OpenAdminAsync();
        var rows = await DbCatalog.ReadAsync(db, """
            SELECT actor_id, changes::text FROM audit.entries
             WHERE table_name = 'users' AND record_id = @id AND action = 'update' ORDER BY id DESC LIMIT 1
            """, r => (Actor: r.IsDBNull(0) ? (Guid?)null : r.GetGuid(0), Changes: JsonDocument.Parse(r.GetString(1)).RootElement),
            ("id", adminId));
        var row = Assert.Single(rows);
        Assert.Equal(adminId, row.Actor);
        Assert.Equal(before, row.Changes.GetProperty("numerals").GetProperty("old").GetString());
        Assert.Equal(after, row.Changes.GetProperty("numerals").GetProperty("new").GetString());
        Assert.False(row.Changes.TryGetProperty("language", out _), "The language did not change and must not be recorded");
    }

    [Fact]
    public async Task Passwords_are_redacted_in_the_audit_trail()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var response = await admin.PostAsJsonAsync("/api/identity/users", new { email = "audit.redact@alpha.example", displayName = "Redact", language = "ar", password = "Secret-Password-9", roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var db = await Env.OpenAdminAsync();
        // The user and the user's credential are both recorded; the hash only as "[redacted]".
        var rows = await DbCatalog.ReadAsync(db, "SELECT table_name, changes::text FROM audit.entries WHERE record_id = @id",
            r => (Table: r.GetString(0), Changes: r.GetString(1)), ("id", id));
        Assert.Contains(rows, r => r.Table == "user_credentials" && r.Changes.Contains("\"password_hash\": {\"new\": \"[redacted]\"}", StringComparison.Ordinal));
        Assert.All(rows, r => Assert.DoesNotContain("pbkdf2", r.Changes, StringComparison.Ordinal));
        var anywhere = await DbCatalog.ScalarAsync<long>(db, "SELECT count(*) FROM audit.entries WHERE changes::text LIKE '%pbkdf2%'");
        Assert.Equal(0L, anywhere);
    }

    [Fact]
    public async Task Writes_that_bypass_the_ORM_are_audited_too()
    {
        await using var app = await Env.OpenAppAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using (var bind = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.actor_kind', 'job', true)", app, tx))
        {
            bind.Parameters.AddWithValue("t", Env.TenantA.Id.ToString());
            await bind.ExecuteNonQueryAsync();
        }
        var id = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO identity.roles (id, tenant_id, name_en, name_ar, permissions, is_system) VALUES (@id, @t, 'Raw SQL role', 'دور مباشر', '{}', false)", app, tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("t", Env.TenantA.Id);
            await insert.ExecuteNonQueryAsync();
        }
        var audited = await DbCatalog.ScalarAsync<long>(app, "SELECT count(*) FROM audit.entries WHERE record_id = @id AND actor_kind = 'job'", ("id", id));
        await tx.RollbackAsync();
        Assert.Equal(1, audited);
    }
}
