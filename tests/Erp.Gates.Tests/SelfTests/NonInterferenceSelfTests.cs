using Erp.Gates.Tests.G1;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// The non-interference check must fail meaningfully. It runs on an environment of its own: before
/// comparing, both tenants make every write the app offers, and the planted module's writes copy
/// one tenant's text into the other's records, which would blunt the marker-based self-tests that
/// share <see cref="GateSelfTests"/>'s environment.
/// </summary>
[Collection(LeakyModuleCollection.Name)]
[Trait(SelfTestProcess.Trait, SelfTestProcess.NonInterference)]
public sealed class NonInterferenceSelfTests(LeakyFixture fixture) : IClassFixture<LeakyFixture>
{
    /// <summary>State shared across requests that leaks only a number (lead, round 4): a count
    /// cached by search text without the tenant (bug 40) and a captured variable that answers the
    /// change since the previous caller's head count (bug 41). Neither answer carries anything of
    /// the other tenant's, so only the non-interference check can see them.</summary>
    [Fact]
    public async Task The_non_interference_check_catches_shared_state_that_leaks_no_marker()
    {
        var result = await NonInterference.RunAsync(fixture.Env);
        foreach (var finding in result.Findings)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(finding);
        }
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("GET /api/leaky/head-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("GET /api/leaky/head-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("GET /api/leaky/people-count?search=", StringComparison.Ordinal));
        // An offset page that reuses the total the other tenant's first page of the same query
        // counted, kept in a pooled scratch object (critic p05 round 5, plant L10): only an offset
        // page judged right after the other tenant opened the query shows it.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("GET /api/leaky/jump?", StringComparison.Ordinal) && f.Contains("skip=", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("GET /api/leaky/jump?", StringComparison.Ordinal) && f.Contains("skip=", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("GET /api/leaky/jump?", StringComparison.Ordinal) && !f.Contains("skip=", StringComparison.Ordinal));
        // The scroll list's sorted search reuses the total first counted for the same search, filter
        // and sort, whatever its tenant (critic p05 round 7, plant L11's behaviour, kept in a
        // singleton): only a request carrying both a sort and a search shows it.
        Assert.Contains(result.Findings, f => f.Contains("GET /api/leaky/scroll?", StringComparison.Ordinal) && f.Contains("sort=", StringComparison.Ordinal) &&
                                              f.Contains("search=", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("GET /api/leaky/scroll", StringComparison.Ordinal) &&
                                                    (!f.Contains("sort=", StringComparison.Ordinal) || !f.Contains("search=", StringComparison.Ordinal)));
        // Writes, on the Arabic side only (critic p04 round 4): a number handed on between Arabic
        // callers (bug 46) and the previous Arabic-Indic-digits caller's e-mail (bug 45, plant L1).
        // The same writes with "en" and "latn" interfere with nothing.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("PUT /api/leaky/me/script (language=\"ar\")", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("PUT /api/leaky/me/script (language=\"ar\")", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("PUT /api/leaky/me/digits (numerals=\"arab\")", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("/api/leaky/me/script", StringComparison.Ordinal) && !f.Contains("language=\"ar\"", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("/api/leaky/me/digits", StringComparison.Ordinal) && !f.Contains("numerals=\"arab\"", StringComparison.Ordinal));
        Assert.True(result.WriteVariants > 0, "no write was compared with a documented value other than the default");
        // Reads in Arabic (critic p04 round 4, "language=ar"): a number handed on between callers
        // whose request runs in Arabic (bug 48), and the previous Arabic caller's e-mail (bug 47).
        // English sessions reach neither.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A in Arabic", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/greeting-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B in Arabic", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/greeting-count", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("in Arabic", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/greeting ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("/api/leaky/me/greeting", StringComparison.Ordinal) && !f.StartsWith("tenant A in Arabic", StringComparison.Ordinal) &&
                                                     !f.StartsWith("tenant B in Arabic", StringComparison.Ordinal));
        Assert.True(result.ArabicComparisons > 0, "no request was compared in Arabic");
        Assert.True(result.ArabicWriteComparisons > 0, "no write was compared in Arabic");
        // Leaks in response headers only, carrying no marker (critic p04 round 8, plant L3). Bug 66:
        // a count of every tenant's Arabic switches kept in the configuration, told in a header on
        // the Arabic write; each answer differs in every process, so only how it changes (one more
        // alone, two more after the other tenant's switch) shows it, both directions; and the
        // configuration watch sees the count written while serving.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("PUT /api/leaky/me/switches (language=\"ar\") changes by", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("PUT /api/leaky/me/switches (language=\"ar\") changes by", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.Contains("PUT /api/leaky/me/switches (language=\"ar\")", StringComparison.Ordinal) &&
                                              f.Contains("changed the configuration of the shared process", StringComparison.Ordinal) && f.Contains(LeakyModule.ArabicSwitchesKey, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.Contains("/api/leaky/me/switches", StringComparison.Ordinal) && !f.Contains("language=\"ar\"", StringComparison.Ordinal));
        // Bug 67: every caller's visits counted outside the process, told in a header on a read.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/visits changes by", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/visits changes by", StringComparison.Ordinal));
        // Bug 68: the change since the previous caller's directory size in a header, the body the
        // same for everyone: stable in the fresh process, so the header comparison itself shows it.
        Assert.Contains(result.Findings, f => f.StartsWith("tenant A", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/directory-change is answered", StringComparison.Ordinal) &&
                                              f.Contains("x-directory-change", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.StartsWith("tenant B", StringComparison.Ordinal) && f.Contains("GET /api/leaky/me/directory-change is answered", StringComparison.Ordinal));
        Assert.True(result.HeadersCompared > 0, "no response header was compared");
        Assert.True(result.ChangeProbes > 0, "no answer was judged by how it changes");
        // Nothing outside the planted module interferes, and the check was not blind.
        Assert.DoesNotContain(result.Findings, f => !f.Contains("/api/leaky/", StringComparison.Ordinal));
        Assert.Empty(result.BlindSpots);
        Assert.True(result.Discriminating > 0);
    }

    [Fact]
    public void The_header_comparison_ignores_only_transport_tracing_dates_and_cookie_values()
    {
        Assert.Equal(
            NonInterference.NormalizeHeaders("Date: Sat, 11 Oct 2026 09:00:00 GMT\nContent-Length: 12\ntraceparent: 00-ab-cd-01\nX-Request-Id: 1\nCache-Control: no-store\nSet-Cookie: erp_session=abc; path=/; expires=Sat, 18 Oct 2026 09:00:00 GMT; httponly"),
            NonInterference.NormalizeHeaders("Set-Cookie: erp_session=xyz; path=/; expires=Sun, 19 Oct 2026 10:00:00 GMT; httponly\nCache-Control: no-store\nDate: Sun, 12 Oct 2026 10:00:00 GMT\nContent-Length: 13\ntraceparent: 00-ef-01-01\nX-Request-Id: 2"));
        // A count, a version, a tag or a cookie of another name is part of the answer.
        Assert.NotEqual(NonInterference.NormalizeHeaders("X-Arabic-Switches: 2"), NonInterference.NormalizeHeaders("X-Arabic-Switches: 3"));
        Assert.NotEqual(NonInterference.NormalizeHeaders("ETag: \"v1\""), NonInterference.NormalizeHeaders("ETag: \"v2\""));
        Assert.NotEqual(NonInterference.NormalizeHeaders("X-Total-Count: 25"), NonInterference.NormalizeHeaders(""));
        Assert.NotEqual(NonInterference.NormalizeHeaders("Set-Cookie: erp_session=a; path=/"), NonInterference.NormalizeHeaders("Set-Cookie: erp_other=a; path=/"));
        Assert.NotEqual(NonInterference.NormalizeHeaders("X-Erp-Printed-At: not-a-time;B"), NonInterference.NormalizeHeaders("X-Erp-Printed-At: not-a-time;A"));
        // An answer as compared carries its headers.
        Assert.NotEqual(NonInterference.Answer(200, "{}", NonInterference.NormalizeHeaders("X-Visits: 1")), NonInterference.Answer(200, "{}", NonInterference.NormalizeHeaders("X-Visits: 2")));
    }

    [Fact]
    public void How_an_answer_changes_is_told_by_its_numbers()
    {
        Assert.Equal("nothing", NonInterference.Change("200 {}", "200 {}"));
        Assert.Equal("#1 (after \" {} x-arabic-switches: \") +1", NonInterference.Change("200 {}\nx-arabic-switches: 7", "200 {}\nx-arabic-switches: 8"));
        Assert.NotEqual(NonInterference.Change("x: 7", "x: 8"), NonInterference.Change("x: 8", "x: 10"));
        // 9 to 10 keeps the shape; Arabic-Indic digits count as numbers.
        Assert.Equal("#0 (after \"x: \") +1", NonInterference.Change("x: 9", "x: 10"));
        Assert.Equal("#0 (after \"x: \") +2", NonInterference.Change("x: \u0669", "x: \u0661\u0661"));
        // A fresh random value changes the text, the same way each time.
        Assert.Equal("the text", NonInterference.Change("tag: a1b2", "tag: ff9c"));
        Assert.Equal(NonInterference.Change("tag: a1b2", "tag: ff9c"), NonInterference.Change("tag: ff9c", "tag: 0e7d"));
    }

    [Fact]
    public void A_finding_shows_where_a_long_answer_differs()
    {
        var body = "{\"items\":[" + string.Join(",", Enumerable.Range(0, 60).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]}";
        var a = NonInterference.Answer(200, body, "x-directory-change: 0");
        var b = NonInterference.Answer(200, body, "x-directory-change: 4");
        Assert.Contains("x-directory-change: 4", NonInterference.Difference(b, a), StringComparison.Ordinal);
        Assert.Contains("\"total\":26", NonInterference.Difference(NonInterference.Answer(200, body + "{\"total\":26}", ""), NonInterference.Answer(200, body + "{\"total\":25}", "")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_write_comparison_also_ignores_only_versions()
    {
        Assert.Equal(NonInterference.NormalizeWrite("""{"id":"x","version":41,"at":"2026-10-03T19:01:41Z"}"""),
            NonInterference.NormalizeWrite("""{"id":"x","version":42,"at":"2026-10-04T19:01:41Z"}"""));
        Assert.NotEqual(NonInterference.NormalizeWrite("""{"displayName":"a"}"""), NonInterference.NormalizeWrite("""{"displayName":"a (also b)"}"""));
        Assert.NotEqual(NonInterference.NormalizeWrite("""{"change":0}"""), NonInterference.NormalizeWrite("""{"change":12}"""));
        Assert.NotEqual(NonInterference.NormalizeWrite("""{"versions":[1]}"""), NonInterference.NormalizeWrite("""{"versions":[2]}"""));
    }

    [Fact]
    public void The_non_interference_check_ignores_only_times_and_trace_ids()
    {
        Assert.Equal(NonInterference.Normalize("""{"at":"2026-10-03T19:01:41.123Z","traceId":"00-ab","n":1}"""),
            NonInterference.Normalize("""{"at":"2026-10-04T07:00:00+04:00","traceId":"00-cd","n":1}"""));
        Assert.NotEqual(NonInterference.Normalize("""{"total":25}"""), NonInterference.Normalize("""{"total":26}"""));
        Assert.NotEqual(NonInterference.Normalize("""{"name":"a"}"""), NonInterference.Normalize("""{"name":"b"}"""));
    }
}
