using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Header, query and cookie names the product's source reads by literal name (and every
/// <c>X-…</c> header literal), so the tenant-isolation gate also tries inputs that code reads only
/// on a path the attack's traffic never took. The running app's own reads are recorded by
/// <see cref="RequestInputRecorder"/>.
/// </summary>
public static partial class SourceInputNames
{
    public sealed record Names(IReadOnlyList<string> Headers, IReadOnlyList<string> Queries, IReadOnlyList<string> Cookies, int FilesScanned);

    public static Names Read() => From(SourceFiles().Select(File.ReadAllText));

    public static Names From(IEnumerable<string> texts)
    {
        var headers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var queries = new SortedSet<string>(StringComparer.Ordinal);
        var cookies = new SortedSet<string>(StringComparer.Ordinal);
        var files = 0;
        foreach (var text in texts)
        {
            files++;
            foreach (Match m in HeaderLiteral().Matches(text)) headers.Add(m.Groups[1].Value);
            foreach (Match m in HeaderRead().Matches(text)) headers.Add(m.Groups[1].Value);
            foreach (Match m in QueryRead().Matches(text)) queries.Add(m.Groups[1].Value);
            foreach (Match m in FromQueryName().Matches(text)) queries.Add(m.Groups[1].Value);
            foreach (Match m in CookieRead().Matches(text)) cookies.Add(m.Groups[1].Value);
        }
        return new Names([.. headers], [.. queries], [.. cookies], files);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = Repo.PathOf("src");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar);
                return !parts.Contains("bin") && !parts.Contains("obj") && !parts.Contains("Migrations");
            });
    }

    [GeneratedRegex("\"(X-[A-Za-z0-9][A-Za-z0-9-]*)\"")]
    private static partial Regex HeaderLiteral();

    [GeneratedRegex("Headers\\s*(?:\\[\\s*|\\.(?:TryGetValue|ContainsKey|GetCommaSeparatedValues)\\(\\s*)\"([A-Za-z0-9-]+)\"")]
    private static partial Regex HeaderRead();

    [GeneratedRegex("Query\\s*(?:\\[\\s*|\\.(?:TryGetValue|ContainsKey)\\(\\s*)\"([A-Za-z0-9_.\\-]+)\"")]
    private static partial Regex QueryRead();

    [GeneratedRegex("FromQuery\\s*\\(\\s*Name\\s*=\\s*\"([A-Za-z0-9_.\\-]+)\"")]
    private static partial Regex FromQueryName();

    [GeneratedRegex("Cookies\\s*(?:\\[\\s*|\\.(?:TryGetValue|ContainsKey)\\(\\s*)\"([A-Za-z0-9_.\\-]+)\"")]
    private static partial Regex CookieRead();
}
