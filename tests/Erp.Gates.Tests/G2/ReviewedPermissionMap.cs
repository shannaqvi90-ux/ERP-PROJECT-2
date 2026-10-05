using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, the reviewed endpoint-to-permission map. A module may keep a map in
/// <c>tests/Gates/endpoint-permissions/</c>: the route prefixes it covers, then one line per
/// endpoint, <c>METHOD pattern permission # why</c> (<c>anonymous</c> for a reviewed anonymous
/// endpoint). Every endpoint of the running app under a covered prefix must be in a map with
/// exactly the permission it declares, and every line must match an endpoint. So an endpoint
/// guarded by a weaker permission than reviewed (critic p03 round 2, plant P2: sign-in history
/// under identity.users.read) fails here directly, whether or not the right permission is still
/// used elsewhere.
/// </summary>
public static class ReviewedPermissionMap
{
    public const string Folder = "tests/Gates/endpoint-permissions";

    public sealed record Map(string File, IReadOnlyList<string> Prefixes, IReadOnlyDictionary<string, string> Permissions, IReadOnlyList<string> Problems);

    public static IReadOnlyList<Map> Load()
    {
        var directory = Repo.PathOf(Folder.Split('/'));
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.txt").Order(StringComparer.Ordinal).Select(f => Parse(Path.GetFileName(f), File.ReadAllLines(f))).ToList()
            : [];
    }

    public static Map Parse(string file, IEnumerable<string> lines)
    {
        var prefixes = new List<string>();
        var permissions = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var raw in lines)
        {
            var parts = raw.Split('#', 2);
            var line = parts[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words is ["prefix", var prefix])
            {
                prefixes.Add(prefix);
            }
            else if (words.Length == 3 && parts.Length == 2 && parts[1].Trim().Length > 0)
            {
                if (!permissions.TryAdd($"{words[0]} {words[1]}", words[2]))
                {
                    problems.Add($"{file}: {words[0]} {words[1]} is listed twice");
                }
            }
            else
            {
                problems.Add($"{file}: '{raw.Trim()}' is not 'prefix <path>' or 'METHOD pattern permission # reason'");
            }
        }
        if (prefixes.Count == 0)
        {
            problems.Add($"{file}: covers no prefix");
        }
        return new Map(file, prefixes, permissions, problems);
    }

    /// <summary>Endpoints that differ from the reviewed maps, and lines that match no endpoint.</summary>
    public static (IReadOnlyList<string> Problems, int Checked) Check(IReadOnlyList<ApiEndpoint> endpoints, IReadOnlyList<Map> maps)
    {
        var problems = maps.SelectMany(m => m.Problems).ToList();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var checkedCount = 0;
        foreach (var endpoint in endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var covering = maps.Where(m => m.Prefixes.Any(p => endpoint.Pattern.StartsWith(p, StringComparison.Ordinal))).ToList();
            if (covering.Count == 0)
            {
                continue;
            }
            checkedCount++;
            var declared = endpoint.IsAnonymous ? "anonymous" : string.Join(",", endpoint.Permissions);
            var reviewed = covering.Select(m => m.Permissions.TryGetValue(endpoint.Key, out var p) ? (m.File, Permission: p) : default).Where(x => x.File is not null).ToList();
            if (reviewed.Count == 0)
            {
                problems.Add($"{endpoint.Key} declares {declared} but is not in the reviewed map ({string.Join(", ", covering.Select(m => m.File))}); add it with the reason for that permission");
                continue;
            }
            foreach (var (file, permission) in reviewed)
            {
                matched.Add($"{file}|{endpoint.Key}");
                if (permission != declared)
                {
                    problems.Add($"{endpoint.Key} declares {declared}; the reviewed map {file} says {permission}");
                }
            }
        }
        foreach (var map in maps)
        {
            foreach (var key in map.Permissions.Keys.Where(k => !matched.Contains($"{map.File}|{k}")))
            {
                problems.Add($"{map.File}: {key} matches no endpoint of the running app");
            }
        }
        return (problems, checkedCount);
    }
}
