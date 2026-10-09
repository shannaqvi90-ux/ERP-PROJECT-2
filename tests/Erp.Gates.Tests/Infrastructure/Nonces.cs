using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Values a product makes afresh for every answer (a sign-in challenge, the second a document was
/// printed): two answers to the very same request differ in them. A differential check that finds a
/// pair of answers different asks its control once more; the strings that differ between the two
/// control answers, at the same place and with the same length, are nonces, and all three answers
/// are compared with each nonce replaced by its length. An answer whose difference is anything else
/// (another length, another place, another status) still tells.
/// </summary>
public static class Nonces
{
    /// <summary>The places (JSON paths) of strings that differ between two answers to the very same
    /// request, with the same length on both sides: values made afresh for each answer.</summary>
    public static HashSet<string> Paths(string first, string second)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        JsonNode? a, b;
        try
        {
            a = JsonNode.Parse(first);
            b = JsonNode.Parse(second);
        }
        catch (JsonException)
        {
            return paths;
        }
        void Walk(JsonNode? x, JsonNode? y, string path)
        {
            switch (x)
            {
                case JsonObject ox when y is JsonObject oy:
                    foreach (var (key, child) in ox)
                    {
                        if (oy.ContainsKey(key)) Walk(child, oy[key], path + "/" + key);
                    }
                    break;
                case JsonArray ax when y is JsonArray ay && ax.Count == ay.Count:
                    for (var i = 0; i < ax.Count; i++) Walk(ax[i], ay[i], path + "/" + i);
                    break;
                case JsonValue vx when y is JsonValue vy && vx.GetValueKind() == JsonValueKind.String && vy.GetValueKind() == JsonValueKind.String:
                    var sx = vx.GetValue<string>();
                    var sy = vy.GetValue<string>();
                    if (sx != sy && sx.Length == sy.Length) paths.Add(path);
                    break;
            }
        }
        Walk(a, b, "");
        return paths;
    }

    /// <summary>The answer with each nonce replaced by its length.</summary>
    public static string Without(string text, IReadOnlySet<string> paths)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return text;
        }
        foreach (var path in paths)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            JsonNode? parent = root;
            for (var i = 0; i < parts.Length - 1 && parent is not null; i++)
            {
                parent = parent is JsonArray arr && int.TryParse(parts[i], out var at) && at < arr.Count ? arr[at] : parent is JsonObject o ? o[parts[i]] : null;
            }
            var last = parts.Length == 0 ? null : parts[^1];
            switch (parent)
            {
                case JsonObject o when last is not null && o[last] is JsonValue v && v.GetValueKind() == JsonValueKind.String:
                    o[last] = $"<nonce:{v.GetValue<string>().Length}>";
                    break;
                case JsonArray arr when last is not null && int.TryParse(last, out var at) && at < arr.Count && arr[at] is JsonValue v && v.GetValueKind() == JsonValueKind.String:
                    arr[at] = $"<nonce:{v.GetValue<string>().Length}>";
                    break;
            }
        }
        return root?.ToJsonString() ?? text;
    }
}
