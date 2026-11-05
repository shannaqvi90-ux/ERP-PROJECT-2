using System.Text.Json;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// The text the product itself ships: every string value in the server's resource files
/// (src/**/Resources/*.json) and in the web strings the server serves (web/src/**/i18n/*.json).
/// The server sends these to every tenant as its own text (a job type's Arabic name, a role's
/// label), so a tenant value that equals one of them cannot show that one tenant's data reached
/// another: it is not a leak marker. Only an equal value is left out (trimmed, any letter case);
/// a tenant value that merely contains a product string, plus anything else, stays a marker.
/// </summary>
public sealed class ProductStrings
{
    private readonly HashSet<string> _texts;

    public ProductStrings(IEnumerable<string> texts) =>
        _texts = new HashSet<string>(texts.Select(t => t.Trim()).Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);

    public static ProductStrings Empty { get; } = new([]);

    public int Count => _texts.Count;

    /// <summary>Whether the value equals a string the product ships.</summary>
    public bool Contains(string value) => _texts.Contains(value.Trim());

    /// <summary>The markers without those equal to a product string (distinct, in order), and how many were left out.</summary>
    public List<string> LeaveOut(IEnumerable<string> markers, out int leftOut)
    {
        var distinct = markers.Distinct(StringComparer.Ordinal).ToList();
        var kept = distinct.Where(m => !Contains(m)).ToList();
        leftOut = distinct.Count - kept.Count;
        return kept;
    }

    private static readonly Lazy<ProductStrings> Shipped = new(() => FromFolders(FindRepositoryRoot()));

    /// <summary>The strings of this repository, read once.</summary>
    public static ProductStrings FromRepository() => Shipped.Value;

    public static ProductStrings FromFolders(string root)
    {
        var texts = new List<string>();
        foreach (var (folder, pattern, keep) in new (string, string, Func<string, bool>)[]
                 {
                     ("src", "*.json", p => p.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
                     ("web/src", "*.json", p => p.Contains($"{Path.DirectorySeparatorChar}i18n{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
                 })
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
                         .Where(p => keep(p) && !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(file));
                    Collect(document.RootElement, texts);
                }
                catch (JsonException)
                {
                    // a file that is not JSON ships no strings
                }
            }
        }
        return new ProductStrings(texts);
    }

    private static void Collect(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, into);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Collect(property.Value, into);
                break;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "erp")) && Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
        }
        throw new InvalidOperationException("The repository root (the folder with 'erp' and 'src') was not found above " + AppContext.BaseDirectory);
    }
}
