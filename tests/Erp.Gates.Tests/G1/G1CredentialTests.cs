using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: calling the sign-in lookup records failed attempts.</summary>
public sealed class CredentialFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, stored credentials. A SQL injection anywhere in the application runs as the application
/// role, so that role must never be able to read a password hash or any other stored secret, of
/// any tenant, its own included. The only code that reads across tenants (the reviewed SECURITY
/// DEFINER functions) must not hand one out either: the sign-in lookup gives the hashing
/// parameters (algorithm, cost, salt) before the password is proven and only the workspace of an
/// account whose password was proven, never a hash, never another tenant's ids, and answers an
/// address that exists nowhere the same way as one that exists. The function owner may write only
/// where reviewed.
/// </summary>
public sealed partial class G1CredentialTests(CredentialFixture fixture) : IClassFixture<CredentialFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    /// <summary>Column names that hold or derive from a secret.</summary>
    [GeneratedRegex("(password|secret|hash|credential|token)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretColumn();

    [GeneratedRegex(@"^pbkdf2-sha512\$[0-9]+\$[A-Za-z0-9+/]+=*$")]
    private static partial Regex ChallengeShape();

    [Fact]
    public async Task The_application_role_can_read_no_stored_credential()
    {
        await using var admin = await Env.OpenAdminAsync();
        var reviewed = Repo.ReadReviewedList("tests/Gates/app-readable-credentials.txt");
        Assert.All(reviewed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason), $"{r.Entry} needs a reason"));
        var problems = new List<string>();
        var secretColumns = new List<string>();
        foreach (var table in await DbCatalog.AllTablesAsync(admin))
        {
            foreach (var column in await DbCatalog.ColumnsAsync(admin, table))
            {
                if (!SecretColumn().IsMatch(column.Name))
                {
                    continue;
                }
                var name = $"{table.Qualified}.{column.Name}";
                secretColumns.Add(name);
                var readable = await DbCatalog.ScalarAsync<bool>(admin,
                    "SELECT has_column_privilege(@r, to_regclass(@t), @c, 'SELECT')",
                    ("r", DatabaseRoles.App), ("t", table.Qualified), ("c", column.Name));
                var isReviewed = reviewed.Any(r => r.Entry == name);
                if (readable && !isReviewed)
                {
                    problems.Add($"{name}: the application role can read it; a SQL injection would read every such value of its tenant");
                }
                if (!readable && isReviewed)
                {
                    problems.Add($"{name}: listed in app-readable-credentials.txt but not readable; remove the entry");
                }
            }
        }
        foreach (var entry in reviewed.Where(r => !secretColumns.Contains(r.Entry)))
        {
            problems.Add($"{entry.Entry}: listed in app-readable-credentials.txt but no such secret column exists");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(secretColumns.Count >= Ratchet.Min("g1.credentialColumnsChecked"),
            $"{secretColumns.Count} secret columns checked; ratchet minimum {Ratchet.Min("g1.credentialColumnsChecked")}");
        Assert.Contains("identity.user_credentials.password_hash", secretColumns);
    }

    [Fact]
    public async Task No_reviewed_function_returns_a_credential_column_or_a_whole_row()
    {
        await using var admin = await Env.OpenAdminAsync();
        var functions = await DbCatalog.ReadAsync(admin, $"""
            SELECT n.nspname || '.' || p.proname,
                   coalesce(p.proargnames, ARRAY[]::text[]),
                   coalesce(p.proargmodes::text[], ARRAY[]::text[]),
                   t.typtype::text, coalesce(t.typrelid, 0)::bigint
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace JOIN pg_type t ON t.oid = p.prorettype
             WHERE p.prosecdef AND {DbCatalog.UserSchemaFilter}
            """, r => (Name: r.GetString(0), ArgNames: r.GetFieldValue<string[]>(1), ArgModes: r.GetFieldValue<string[]>(2),
                TypeKind: r.GetString(3), RelId: r.GetInt64(4)));
        Assert.NotEmpty(functions);
        var problems = new List<string>();
        foreach (var function in functions)
        {
            for (var i = 0; i < function.ArgNames.Length; i++)
            {
                var mode = i < function.ArgModes.Length ? function.ArgModes[i] : "i";
                if (mode is "o" or "t" or "b" && SecretColumn().IsMatch(function.ArgNames[i]))
                {
                    problems.Add($"{function.Name} returns a column named {function.ArgNames[i]}");
                }
            }
            if (function.TypeKind == "c" && function.RelId != 0 &&
                await DbCatalog.ScalarAsync<bool>(admin, "SELECT EXISTS (SELECT 1 FROM pg_class WHERE oid = @o AND relkind IN ('r', 'p', 'v'))", ("o", (uint)function.RelId)))
            {
                problems.Add($"{function.Name} returns a whole table row");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task The_sign_in_lookup_reveals_no_hash_and_no_account_before_the_password_is_proven()
    {
        var victimEmail = Env.Email(Env.TenantB, "admin");
        await using var admin = await Env.OpenAdminAsync();
        var hashes = await DbCatalog.ReadAsync(admin, "SELECT password_hash FROM identity.user_credentials", r => r.GetString(0));
        Assert.NotEmpty(hashes);
        var hashParts = hashes.Select(h => h.Split('$').Last()).Distinct().ToList();
        var secrets = (await DbCatalog.ReadAsync(admin, "SELECT id::text FROM identity.users UNION SELECT id::text FROM tenancy.tenants", r => r.GetString(0)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failuresBefore = await FailuresAsync(admin, victimEmail);

        await using var app = await Env.OpenAppAsync();
        var known = await ChallengeAsync(app, victimEmail);
        var unknown = await ChallengeAsync(app, $"nobody.{Guid.NewGuid():N}@nowhere.example");
        var unknownAgain = await ChallengeAsync(app, unknown.Email);

        Assert.Single(known.Rows);
        Assert.Single(unknown.Rows);
        foreach (var row in known.Rows.Concat(unknown.Rows))
        {
            Assert.Matches(ChallengeShape(), row.Challenge ?? "");
            Assert.Null(row.TenantId);
            Assert.DoesNotContain(hashParts, part => (row.Challenge ?? "").Contains(part, StringComparison.Ordinal));
        }
        // The decoy for an address that exists nowhere is stable, so asking twice tells nothing.
        Assert.Equal(unknown.Rows[0].Challenge, unknownAgain.Rows[0].Challenge);

        // Wrong proofs: nothing comes back, and the failure is kept in the account's own tenant.
        var wrong = await VerifyAsync(app, victimEmail, [known.Rows[0].Challenge + "$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="]);
        Assert.Empty(wrong);
        var hashGuess = await VerifyAsync(app, victimEmail, hashes.ToArray());
        Assert.True(hashGuess.Count <= 1, "At most the one account whose stored value was sent may match");
        foreach (var row in hashGuess)
        {
            Assert.DoesNotContain(row.Challenge ?? "", hashParts);
        }
        Assert.True(await FailuresAsync(admin, victimEmail) > failuresBefore, "A failed attempt must be recorded in the account's own sign-in history");

        // The lookup leaves the connection unbound.
        Assert.Equal("", await DbCatalog.ScalarAsync<string>(app, "SELECT coalesce(current_setting('app.tenant_id', true), '')"));
        Assert.Equal(0L, await DbCatalog.ScalarAsync<long>(app, "SELECT count(*) FROM identity.users"));

        // Nothing it returned is an id of any tenant or account.
        foreach (var value in known.Rows.Concat(unknown.Rows).SelectMany(r => new[] { r.Challenge, r.TenantId?.ToString() }).Where(v => v is not null))
        {
            Assert.DoesNotContain(value!, secrets);
        }
    }

    [Fact]
    public async Task The_function_owner_writes_only_where_reviewed()
    {
        await using var admin = await Env.OpenAdminAsync();
        var reviewed = Repo.ReadReviewedList("tests/Gates/auth-resolver-writes.txt");
        Assert.All(reviewed, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason), $"{r.Entry} needs a reason"));
        var actual = new List<string>();
        foreach (var table in await DbCatalog.AllTablesAsync(admin))
        {
            foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            {
                var held = await DbCatalog.ScalarAsync<bool>(admin,
                    privilege is "INSERT" or "UPDATE"
                        ? "SELECT has_table_privilege(@r, to_regclass(@t), @p) OR has_any_column_privilege(@r, to_regclass(@t), @p)"
                        : "SELECT has_table_privilege(@r, to_regclass(@t), @p)",
                    ("r", DatabaseRoles.AuthResolver), ("t", table.Qualified), ("p", privilege));
                if (held)
                {
                    actual.Add($"{table.Qualified} {privilege}");
                }
            }
        }
        Assert.Equal(reviewed.Select(r => r.Entry).Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    private sealed record LookupRow(string? Challenge, Guid? TenantId);

    private static async Task<(string Email, List<LookupRow> Rows)> ChallengeAsync(NpgsqlConnection app, string email) =>
        (email, await DbCatalog.ReadAsync(app,
            "SELECT challenge, tenant_id FROM identity.verify_sign_in(@e, NULL, 'gate-source', NULL, NULL, 5, 15)",
            r => new LookupRow(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetGuid(1)), ("e", email)));

    private static async Task<List<LookupRow>> VerifyAsync(NpgsqlConnection app, string email, string[] proofs) =>
        await DbCatalog.ReadAsync(app,
            "SELECT challenge, tenant_id FROM identity.verify_sign_in(@e, @p, 'gate-source', NULL, NULL, 5, 15)",
            r => new LookupRow(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetGuid(1)), ("e", email), ("p", proofs));

    private static Task<long> FailuresAsync(NpgsqlConnection admin, string email) =>
        DbCatalog.ScalarAsync<long>(admin, """
            SELECT count(*) FROM identity.sign_in_attempts a JOIN identity.users u ON u.tenant_id = a.tenant_id AND u.id = a.user_id
             WHERE u.email_normalized = lower(@e) AND a.outcome <> 'succeeded'
            """, ("e", email));
}
