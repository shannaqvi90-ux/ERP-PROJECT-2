using System.Text.RegularExpressions;
using Erp.Gates.Tests.SelfTests;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>An environment of its own (a copy of the gate template, no preparation): the check
/// paces one tenant's turns against the other's and needs no third party's activity in between.</summary>
public sealed class G1ShellAnswersFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The shell answers check judges times by how far they sit from each request's start,
/// with a tolerance that grows with the longest request it saw (<see cref="ShellAnswers"/>). It runs
/// alone, after the parallel collections: beside the volume gate's seeding of 2 x 100,000 users
/// (G1ListVolumeTests, p05) its requests ran long enough that the self-test's planted HTTP-date time
/// for tenant B fell inside that tolerance and went uncaught (integration verify of p05 round 12).
/// Running alone changes when it runs, never what it checks.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShellAnswersCollection
{
    public const string Name = "Shell answers (timing judged, runs alone)";
}

/// <summary>G1 on the shell's own answers, every byte (<see cref="ShellAnswers"/>).</summary>
[Collection(ShellAnswersCollection.Name)]
public sealed partial class G1ShellAnswersTests(G1ShellAnswersFixture fixture) : IClassFixture<G1ShellAnswersFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task What_the_shell_is_answered_never_depends_on_the_other_tenant_cookie_values_and_times_included()
    {
        var result = await ShellAnswers.RunAsync(Env);
        Report(result);
        Assert.True(result.Findings.Count == 0, $"{result.Findings.Count} values of the shell's answers depend on the other tenant's activity:\n" + string.Join("\n", result.Findings.Take(40)));
        Assert.Equal(ShellAnswers.Endpoints.Select(e => $"{e.Method} {e.Path}").Order(StringComparer.Ordinal), result.EndpointsJudged.Order(StringComparer.Ordinal));
        AssertAtLeast(result.Comparisons, "g1.shellAnswerComparisons");
        AssertAtLeast(result.HeaderValuesCompared, "g1.shellAnswerHeaderValuesCompared");
        AssertAtLeast(result.NumbersJudged, "g1.shellAnswerNumbersJudged");
        AssertAtLeast(result.TimesJudged, "g1.shellAnswerTimesJudged");
        AssertAtLeast(result.CredentialCookiesShaped, "g1.shellAnswerCredentialCookiesShaped");
        AssertAtLeast(result.EndpointsJudged.Count, "g1.shellAnswerEndpoints");
        // Every body the preferences write can take was judged against the other tenant's
        // differing writes: each field alone, the whole body, the empty one (critic p04 round 11).
        var shapes = (await ShellAnswers.BodyVariantsAsync(Env)).Select(v => v.Shape).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(shapes.Order(StringComparer.Ordinal), result.BodyShapesJudged.Order(StringComparer.Ordinal));
        AssertAtLeast(result.BodyShapesJudged.Count, "g1.shellAnswerBodyShapes");
        AssertAtLeast(result.BodyVariantsJudged, "g1.shellAnswerBodyVariantsJudged");
        AssertAtLeast(result.TraceIdsShaped, "g1.shellAnswerTraceIdsShaped");

        // The audit rows of those writes carry the request's trace id as their correlation id: a
        // random one, never a count of the process's requests (Kestrel's connection and request
        // counters), so tenant A's own audit trail does not count tenant B's requests.
        await using var db = await Env.OpenAdminAsync();
        var correlations = await Infrastructure.DbCatalog.ReadAsync(db, """
            SELECT correlation_id FROM audit.entries
             WHERE tenant_id = ANY(@tenants) AND table_name = 'users' AND correlation_id IS NOT NULL AND correlation_id NOT LIKE 'seed:%'
             ORDER BY id DESC LIMIT 200
            """, r => r.GetString(0), ("tenants", new[] { Env.TenantA.Id, Env.TenantB.Id }));
        Assert.True(correlations.Count >= 40, $"only {correlations.Count} audit rows of the preference writes carry a correlation id");
        var shape = ShellAnswers.RandomShapes["body.traceId"].Shape;
        var counted = correlations.Where(c => !shape.IsMatch($"\"{c}\"")).ToList();
        Assert.True(counted.Count == 0, "audit correlation ids not of the random trace-id shape: " + string.Join(", ", counted.Take(10)));
        Assert.Equal(correlations.Count, correlations.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The bodies the check sends are the bodies the shell sends (critic p04 round 11: the checks
    /// sent whole bodies only, the shell sends <c>{language}</c> alone on Alt+L). The shell's client
    /// sends the preferences write from one place (web/src/kernel/preferences.ts) with a body of
    /// type <c>PreferenceChange = Partial&lt;Preferences&gt;</c>, so any subset of
    /// <c>Preferences</c>' fields; the fields it names are the request schema's, both ways; every
    /// body written out at a call of <c>savePreferences</c> (or the switches' <c>persist</c>) uses only those fields; and every
    /// subset of them is a shape the check judges (<see cref="ShellAnswers.BodyVariantsAsync"/>).
    /// </summary>
    [Fact]
    public async Task Every_body_the_shell_sends_to_its_own_write_is_judged()
    {
        var web = Repo.PathOf("web", "src");
        var preferences = File.ReadAllText(Path.Combine(web, "kernel", "preferences.ts"));
        var type = PreferencesTypeRegex().Match(preferences);
        Assert.True(type.Success, "web/src/kernel/preferences.ts no longer declares `export type Preferences = { ... }`");
        var clientFields = FieldRegex().Matches(type.Groups["body"].Value).Select(m => m.Groups["name"].Value).ToList();
        Assert.Matches(@"export type PreferenceChange = Partial<Preferences>;", preferences);
        Assert.Matches(@"api\(""PUT"", preferencesPath, change\b", preferences);

        var variants = await ShellAnswers.BodyVariantsAsync(Env);
        var schemaFields = variants.SelectMany(v => v.Prepared.Select(p => p.Key)).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(schemaFields.Order(StringComparer.Ordinal), clientFields.Order(StringComparer.Ordinal));

        // Every source of the web client: the preferences path is written in preferences.ts alone,
        // and every body written out at a savePreferences call names the client's fields only.
        var testHelpers = Path.Combine(web, "test") + Path.DirectorySeparatorChar;
        var sources = Directory.GetFiles(web, "*.ts*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".test.", StringComparison.Ordinal) && !f.StartsWith(testHelpers, StringComparison.Ordinal))
            .ToList();
        var literals = new List<string>();
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            if (!file.EndsWith(Path.Combine("kernel", "preferences.ts"), StringComparison.Ordinal))
            {
                Assert.DoesNotContain(ArabicSession.PreferencesPath, text);
            }
            foreach (Match call in SaveCallRegex().Matches(text))
            {
                var keys = FieldRegex().Matches(call.Groups["body"].Value).Select(m => m.Groups["name"].Value).ToList();
                Assert.True(keys.Count > 0 && keys.All(clientFields.Contains), $"{Path.GetFileName(file)}: savePreferences body {{{call.Groups["body"].Value}}} names a field the client's Preferences type does not");
                literals.Add($"{Path.GetFileName(file)}: {ShellAnswers.BodyVariant.ShapeOf(keys)}");
            }
        }
        Assert.True(literals.Count >= 2, "found no savePreferences body written out (Alt+L's {language}, My account's {language}): " + string.Join(", ", literals));

        var judged = variants.Select(v => v.Shape).ToHashSet(StringComparer.Ordinal);
        var subsets = Enumerable.Range(1, (1 << clientFields.Count) - 1)
            .Select(mask => ShellAnswers.BodyVariant.ShapeOf(clientFields.Where((_, i) => (mask & (1 << i)) != 0)))
            .ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"client fields {string.Join(", ", clientFields)}; bodies written out: {string.Join("; ", literals)}; judged shapes: {string.Join(" ", judged.Order(StringComparer.Ordinal))}");
        Assert.True(subsets.All(judged.Contains), "bodies the shell may send that the check does not judge: " + string.Join(", ", subsets.Where(s => !judged.Contains(s))));
        Assert.Contains("{}", judged);
    }

    /// <summary>
    /// The partial bodies must catch what only they can show (critic p04 round 11, plant SL1): on a
    /// host that answers a write leaving a field out with the value anyone, in any tenant, sent last
    /// for it (<see cref="ShellPartialPlantModule"/>), the check names the field left out, for
    /// {language} alone in an English session and {numerals} alone in an Arabic one, in both
    /// directions, and nothing else. The whole-body turns of the same run stay quiet on it.
    /// </summary>
    [Fact]
    public async Task The_shell_answers_check_catches_a_field_left_out_filled_from_another_tenant()
    {
        await using var planted = Env.OverHost(ShellPartialPlantModule.Settings(Env));
        var all = await ShellAnswers.BodyVariantsAsync(planted);
        var language = all.First(v => v.Shape == "{language}" && (string?)v.Body["language"] == "en");
        var numerals = all.First(v => v.Shape == "{numerals}" && (string?)v.Prepared["language"] == "ar");
        var whole = all.First(v => v.Shape == "{language,numerals}");
        var result = await ShellAnswers.RunAsync(planted, new ShellAnswers.Options([], v => v.Name == language.Name || v.Name == numerals.Name || v.Name == whole.Name, SignIn: false));
        Report(result);
        var preferences = $"PUT {ArabicSession.PreferencesPath}";
        var plants = 0;
        foreach (var (variant, field) in new[] { (language, "numerals"), (numerals, "language") })
        {
            foreach (var tenant in new[] { "tenant A", "tenant B" })
            {
                Assert.True(result.Findings.Any(f => f.StartsWith($"{tenant} {variant.Name}: {preferences} body.{field}:", StringComparison.Ordinal)),
                    $"not caught for {tenant}: {variant.Name} body.{field}\nfindings:\n{string.Join("\n", result.Findings)}");
            }
            plants++;
        }
        var others = result.Findings.Where(f => !(f.Contains(language.Name, StringComparison.Ordinal) && f.Contains($"{preferences} body.numerals:", StringComparison.Ordinal)) &&
                                                 !(f.Contains(numerals.Name, StringComparison.Ordinal) && f.Contains($"{preferences} body.language:", StringComparison.Ordinal))).ToList();
        Assert.True(others.Count == 0, "findings nothing planted explains: " + string.Join("\n", others));
        Assert.Equal(6, result.BodyVariantsJudged);
        AssertAtLeast(plants, "g1.shellAnswerPartialPlantsCaught");
    }

    /// <summary>The shell calls no endpoint of its own that the check does not judge: every API path
    /// written in the shell's sources (its screens, and the kernel's session and preferences that it
    /// runs) is one of <see cref="ShellAnswers.Endpoints"/>.</summary>
    [Fact]
    public void Every_endpoint_the_shell_calls_is_judged()
    {
        var web = Repo.PathOf("web", "src");
        var files = Directory.GetFiles(Path.Combine(web, "modules", "shell"), "*.ts*", SearchOption.AllDirectories)
            .Concat([Path.Combine(web, "kernel", "session.tsx"), Path.Combine(web, "kernel", "preferences.ts")])
            .Where(f => !f.Contains(".test.", StringComparison.Ordinal))
            .ToList();
        var judged = ShellAnswers.Endpoints.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var called = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (Match match in ApiPathRegex().Matches(File.ReadAllText(file)))
            {
                called.Add(match.Groups["path"].Value);
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{files.Count} shell sources, API paths called: {string.Join(", ", called.Order(StringComparer.Ordinal))}");
        Assert.True(files.Count >= 15, $"only {files.Count} shell sources found");
        Assert.True(called.All(judged.Contains), "called by the shell, not judged: " + string.Join(", ", called.Where(p => !judged.Contains(p))));
        // The sources name every judged endpoint (the scan reads them).
        Assert.True(judged.All(called.Contains), "judged, not found in the shell's sources: " + string.Join(", ", judged.Where(p => !called.Contains(p))));
    }

    /// <summary>The check must fail meaningfully: on a host whose shell endpoints carry planted notes
    /// of every tenant's activity kept outside the process (<see cref="ShellPlantModule"/>: a count in
    /// a cookie, the time of another tenant's switch or sign-out in a header in ISO 8601 or as an HTTP date, a sign-in count in a cookie), it
    /// names each, in both directions, and nothing else.</summary>
    [Fact]
    public async Task The_shell_answers_check_catches_counts_in_cookies_and_other_tenants_times_in_headers()
    {
        await using var planted = Env.OverHost(ShellPlantModule.Settings(Env));
        var result = await ShellAnswers.RunAsync(planted, [("ar", "arab")]);
        Report(result);
        var preferences = $"PUT {ArabicSession.PreferencesPath}";
        string[] caught =
        [
            $"{preferences} cookie:{ShellPlantModule.TipCookie}: number 1 moves by",
            $"{preferences} header:{ShellPlantModule.PreviousSwitchHeader.ToLowerInvariant()}: time 1 is",
            "GET /api/auth/session header:last-modified: time 1 is",
            $"POST /api/auth/sign-in cookie:{ShellPlantModule.SeenCookie}: number 1 moves by",
        ];
        var plants = 0;
        foreach (var expected in caught)
        {
            foreach (var tenant in new[] { "tenant A", "tenant B" })
            {
                Assert.True(result.Findings.Any(f => f.StartsWith(tenant, StringComparison.Ordinal) && f.Contains(expected, StringComparison.Ordinal)),
                    $"not caught for {tenant}: {expected}\nfindings:\n{string.Join("\n", result.Findings)}");
            }
            plants++;
        }
        // The ISO time of the other tenant's switch is also seen inside the other tenant's request.
        Assert.Contains(result.Findings, f => f.Contains(ShellPlantModule.PreviousSwitchHeader.ToLowerInvariant(), StringComparison.Ordinal) && f.Contains("falls inside a request of", StringComparison.Ordinal));
        // Nothing but the plants: the unplanted values of the same answers stay quiet.
        var plantedSlots = new[] { ShellPlantModule.TipCookie, ShellPlantModule.PreviousSwitchHeader.ToLowerInvariant(), "header:last-modified", ShellPlantModule.SeenCookie };
        var others = result.Findings.Where(f => !plantedSlots.Any(p => f.Contains(p, StringComparison.Ordinal))).ToList();
        Assert.True(others.Count == 0, "findings on values nothing planted: " + string.Join("\n", others));
        Assert.True(result.CookieValuesCompared > 0, "no cookie value was compared");
        AssertAtLeast(plants, "g1.shellAnswerPlantsCaught");
        AssertAtLeast(result.CookieValuesCompared, "g1.shellAnswerPlantCookieValuesCompared");
    }

    private static void Report(ShellAnswers.Result result)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{result.Requests} requests over {result.EndpointsJudged.Count} endpoints; {result.Comparisons} values compared " +
            $"({result.HeaderValuesCompared} headers, {result.CookieValuesCompared} cookie values, {result.CredentialCookiesShaped} credential cookies by shape), " +
            $"{result.NumbersJudged} numbers and {result.TimesJudged} times judged by how they change; {result.Unjudged.Count} reviewed as random");
        foreach (var finding in result.Findings)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"finding: {finding}");
        }
    }

    private static void AssertAtLeast(int value, string key) =>
        Assert.True(value >= Ratchet.Min(key), $"{key}: {value}; ratchet minimum {Ratchet.Min(key)}");

    [GeneratedRegex("[\"'`](?<path>/api/[A-Za-z0-9/_.-]+)")]
    private static partial Regex ApiPathRegex();

    [GeneratedRegex(@"export type Preferences = \{(?<body>[^}]*)\}")]
    private static partial Regex PreferencesTypeRegex();

    /// <summary>A field of a TypeScript object type or literal: <c>name:</c> or <c>name?:</c>.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_$.])(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\??\s*:")]
    private static partial Regex FieldRegex();

    /// <summary>A body written out where it goes to savePreferences: <c>savePreferences(x, { language: value })</c>,
    /// or the shell's switches' own funnel into it (usePreferenceActions: <c>persist({ numerals: next })</c>).</summary>
    [GeneratedRegex(@"(?:savePreferences\([^,()]+(?:\([^()]*\))?[^,()]*,|\bpersist\()\s*\{(?<body>[^}]*)\}\s*\)")]
    private static partial Regex SaveCallRegex();
}
