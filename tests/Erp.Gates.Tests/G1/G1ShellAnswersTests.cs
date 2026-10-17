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

/// <summary>G1 on the shell's own answers, every byte (<see cref="ShellAnswers"/>).</summary>
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
}
