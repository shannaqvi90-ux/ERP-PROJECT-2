using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Static second layer to the carrier matrices of G2: product code reads the places a session token
/// travels (the Authorization header, Bearer, the session cookie) only where reviewed in
/// <c>tests/Gates/session-token-reads.txt</c>. A permission decision keyed to how the caller sent
/// its token (critic p00 round 9, plant P3) is found in the source as well as at run time.
/// </summary>
public sealed class G2SessionCarrierSourceTests
{
    private static readonly Regex Carrier = new(
        @"Headers\s*\.\s*Authorization\b|HeaderNames\s*\.\s*Authorization\b|""\s*(Proxy-)?Authorization\s*""|""\s*Bearer\b|AuthenticationHeaderValue\s*\.\s*(Try)?Parse|""erp_session""|SessionAuthenticationDefaults\s*\.\s*CookieName\s*[,\])]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Product_code_reads_session_carriers_only_where_reviewed()
    {
        var files = Directory.EnumerateFiles(Repo.PathOf("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 100, $"only {files.Count} product source files found: the scan is blind");
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }
                if (Carrier.IsMatch(lines[i]))
                {
                    (found.TryGetValue(relative, out var list) ? list : found[relative] = []).Add($"{relative}:{i + 1}: {code}");
                }
            }
        }

        var reviewed = Repo.ReadReviewedList("tests/Gates/session-token-reads.txt");
        var problems = new List<string>();
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var count) || string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"session-token-reads.txt: '{entry}' must be '<file> <count> # reason'");
                continue;
            }
            expected[parts[0]] = count;
        }
        foreach (var (file, lines) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(file, out var count))
            {
                problems.Add("Not reviewed in tests/Gates/session-token-reads.txt:\n  " + string.Join("\n  ", lines));
            }
            else if (count != lines.Count)
            {
                problems.Add($"{file} has {lines.Count} lines reading a session carrier; the reviewed list says {count}:\n  " + string.Join("\n  ", lines));
            }
        }
        problems.AddRange(expected.Keys.Where(f => !found.ContainsKey(f)).Select(f => $"session-token-reads.txt: {f} reads no session carrier any more; remove the entry"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Self-check: the pattern finds the round-9 plant and the usual ways of reading a
    /// carrier, and not the unrelated device cookie.</summary>
    [Fact]
    public void The_carrier_pattern_finds_the_planted_bearer_bypass()
    {
        Assert.Matches(Carrier, "context.Resource is HttpContext http && http.Request.Headers.Authorization.ToString().StartsWith(\"Bearer \", StringComparison.OrdinalIgnoreCase))");
        Assert.Matches(Carrier, "var value = Request.Headers[\"Authorization\"];");
        Assert.Matches(Carrier, "var value = Request.Headers[HeaderNames.Authorization];");
        Assert.Matches(Carrier, "AuthenticationHeaderValue.TryParse(header, out var parsed)");
        Assert.Matches(Carrier, "if (http.Request.Cookies.ContainsKey(SessionAuthenticationDefaults.CookieName)) {");
        Assert.Matches(Carrier, "http.Request.Cookies[Kernel.Security.SessionAuthenticationDefaults.CookieName]");
        Assert.Matches(Carrier, "var api = scheme == \"bearer\";");
        Assert.DoesNotMatch(Carrier, "if (!http.Request.Cookies.TryGetValue(CookieName, out var cookie) || cookie is not { Length: > 0 and <= 2048 })");
    }
}
