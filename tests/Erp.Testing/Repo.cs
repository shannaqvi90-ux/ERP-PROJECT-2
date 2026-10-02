using System.Text.Json;

namespace Erp.Testing;

/// <summary>Files in the repository the gates read (allowlists, ratchet, source).</summary>
public static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Read a reviewed list: one entry per line, <c>#</c> starts a comment (the reason).
    /// Blank lines and comment-only lines are ignored.</summary>
    public static IReadOnlyList<string> ReadList(string relativePath)
    {
        var path = PathOf(relativePath.Split('/'));
        return File.ReadAllLines(path)
            .Select(line => line.Split('#', 2)[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>Entries of a reviewed list that must carry a reason after <c>#</c>.</summary>
    public static IReadOnlyList<(string Entry, string Reason)> ReadReviewedList(string relativePath)
    {
        var path = PathOf(relativePath.Split('/'));
        return File.ReadAllLines(path)
            .Where(line => !line.TrimStart().StartsWith('#') && line.Trim().Length > 0)
            .Select(line =>
            {
                var parts = line.Split('#', 2);
                return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
            })
            .ToList();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Erp.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Erp.slnx) not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// <c>gauntlet/ratchet.json</c>: minimum counts the gates must reach and maximum counts they must
/// stay under. Values may only move in the stricter direction (checked by <c>./erp verify</c>
/// against the committed version).
/// </summary>
public static class Ratchet
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Repo.PathOf("gauntlet", "ratchet.json"))));

    public static int Min(string key) => Read("minimums", key);

    public static int Max(string key) => Read("maximums", key);

    private static int Read(string section, string key)
    {
        if (!Document.Value.RootElement.GetProperty(section).TryGetProperty(key, out var value))
        {
            throw new InvalidOperationException($"gauntlet/ratchet.json has no {section}.{key}. Add it; it may only become stricter.");
        }
        return value.GetInt32();
    }
}
