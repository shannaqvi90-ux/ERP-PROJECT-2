using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, the reviewed cross-tenant lookups (SECURITY DEFINER functions). They are the only code
/// that reads across tenants, so: every one has reviewed callers and reviewed source files
/// (tests/Gates/security-definer-callers.txt); no other file under src/ names them; and the
/// database answers them only on a connection that is not bound to a tenant, so an endpoint
/// cannot use one inside its own request as a lookup into another tenant. Which code path calls
/// them at run time is traced by the HTTP attack (G1HttpIsolationTests).
/// </summary>
public sealed class G1ReviewedLookupTests(GateFixture fixture)
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public void Every_reviewed_function_has_reviewed_callers_and_sources()
    {
        var functions = Repo.ReadReviewedList("tests/Gates/security-definer-allowlist.txt")
            .Select(e => e.Entry.Split('(')[0]).ToHashSet(StringComparer.Ordinal);
        var entries = ReviewedCallers.Read();
        var problems = new List<string>();
        problems.AddRange(entries.Where(e => string.IsNullOrWhiteSpace(e.Reason)).Select(e => $"{e.Function} {e.Kind} {e.Value}: needs a reason"));
        problems.AddRange(entries.Where(e => !functions.Contains(e.Function)).Select(e => $"{e.Function}: not a reviewed security-definer function"));
        foreach (var function in functions)
        {
            if (!entries.Any(e => e.Function == function && e.Kind == "caller")) problems.Add($"{function}: no reviewed caller");
            if (!entries.Any(e => e.Function == function && e.Kind == "source")) problems.Add($"{function}: no reviewed source file");
        }
        foreach (var caller in entries.Where(e => e.Kind == "caller"))
        {
            if (!(caller.Value == "authentication" || caller.Value.StartsWith("endpoint:", StringComparison.Ordinal)))
            {
                problems.Add($"{caller.Function}: caller '{caller.Value}' must be 'authentication' or 'endpoint:<name>'");
            }
        }
        foreach (var source in entries.Where(e => e.Kind == "source"))
        {
            if (!File.Exists(Repo.PathOf(source.Value.Split('/')))) problems.Add($"{source.Function}: source file {source.Value} does not exist");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Only_reviewed_source_files_name_a_reviewed_function()
    {
        var entries = ReviewedCallers.Read();
        var allowed = entries.Where(e => e.Kind == "source").ToLookup(e => e.Function, e => e.Value);
        var names = entries.Select(e => e.Function).Distinct()
            .Select(f => (Function: f, Pattern: new Regex(Regex.Escape(f.Split('.').Last()), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .ToList();
        var problems = new List<string>();
        var scanned = 0;
        foreach (var file in SourceFiles())
        {
            scanned++;
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            foreach (var (function, pattern) in names)
            {
                if (pattern.IsMatch(text) && !allowed[function].Contains(relative))
                {
                    problems.Add($"{relative} names {function}; only its reviewed callers may (tests/Gates/security-definer-callers.txt)");
                }
            }
        }
        Assert.True(scanned > 20, $"Only {scanned} source files scanned; the scan is looking in the wrong place.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Reviewed_lookups_answer_nothing_inside_a_tenant_bound_transaction()
    {
        var victimEmail = Env.Email(Env.TenantB, "admin");
        await using var app = await Env.OpenAppAsync();

        // Unbound (how sign-in uses it): the account's hashing parameters are found.
        const string challenge = "SELECT count(*) FROM identity.verify_sign_in(@e, NULL, 'gate', NULL, NULL, 5, 15) WHERE challenge IS NOT NULL";
        var unbound = await DbCatalog.ScalarAsync<long>(app, challenge, ("e", victimEmail));
        Assert.Equal(1, unbound);

        // Bound to tenant A (how an endpoint's request runs): nothing, for either function, for
        // tenant B's address and for tenant A's own, with or without proofs.
        await using var tx = await app.BeginTransactionAsync();
        await G1DatabaseIsolationTests.BindAsync(app, tx, Env.TenantA.Id);
        foreach (var email in new[] { victimEmail, Env.Email(Env.TenantA, "admin") })
        {
            await using (var login = new NpgsqlCommand(challenge, app, tx))
            {
                login.Parameters.AddWithValue("e", email);
                Assert.Equal(0L, await login.ExecuteScalarAsync());
            }
            await using (var proofs = new NpgsqlCommand("SELECT count(*) FROM identity.verify_sign_in(@e, ARRAY['x'], 'gate', NULL, NULL, 5, 15)", app, tx))
            {
                proofs.Parameters.AddWithValue("e", email);
                Assert.Equal(0L, await proofs.ExecuteScalarAsync());
            }
        }
        await using (var sessions = new NpgsqlCommand("SELECT count(*) FROM identity.sessions", app, tx))
        {
            // Sanity: the transaction is bound (tenant A sees its own sessions).
            Assert.True((long)(await sessions.ExecuteScalarAsync())! > 0);
        }
        await using (var hashes = new NpgsqlCommand("""
            SELECT count(*) FROM (SELECT token_hash FROM identity.sessions) s, LATERAL identity.resolve_session(s.token_hash) r
            """, app, tx))
        {
            Assert.Equal(0L, await hashes.ExecuteScalarAsync());
        }
        await tx.RollbackAsync();

        // Superuser view: the functions keep their reviewed owner and pinned search_path.
        await using var admin = await Env.OpenAdminAsync();
        var owners = await DbCatalog.ReadAsync(admin, """
            SELECT pg_get_userbyid(p.proowner) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'identity' AND p.proname IN ('verify_sign_in', 'resolve_session')
            """, r => r.GetString(0));
        Assert.Equal([DatabaseRoles.AuthResolver, DatabaseRoles.AuthResolver], owners);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = Repo.PathOf("src");
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".sql", StringComparison.Ordinal))
            .Where(f =>
            {
                var parts = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar);
                return !parts.Contains("bin") && !parts.Contains("obj");
            });
    }
}
