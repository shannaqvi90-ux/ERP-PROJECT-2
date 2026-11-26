using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The names tenant A's attack writes (round 23). A running counter ("attack-116772") can equal a
/// number tenant B holds, so tenant A's own role or audit row then read as tenant B's value. A
/// written name now carries a per-run random token and the counter spelled in letters, so it
/// contains no digit run that another tenant's data could hold; and a name an example would give
/// both tenants (the API's example company name) is tagged with the writing tenant's own tag.
/// </summary>
internal static class AttackNames
{
    /// <summary>Six random lower-case letters, new for every test process.</summary>
    public static string RunToken { get; } = Letters(Random.Shared.NextInt64(308915776L / 2, 308915776L), 6);

    /// <summary>"attack-&lt;run token&gt;-&lt;n in letters&gt;": no digits at all.</summary>
    public static string Text(int n) => $"attack-{RunToken}-{Letters(n, 1)}";

    /// <summary>The counter in base 26, letters only.</summary>
    public static string Letters(long n, int minLength)
    {
        var chars = new List<char>();
        do
        {
            chars.Insert(0, (char)('a' + (int)(n % 26)));
            n /= 26;
        }
        while (n > 0 || chars.Count < minLength);
        return new string([.. chars]);
    }

    /// <summary>Appends <paramref name="tag"/> to every top-level name field of a body that took the
    /// API's example, where the longer text still conforms to the field's documented limits, so two
    /// tenants never hold the same example name (and a label derived from it, such as a numbering
    /// sequence's "document type · company name", is the writing tenant's alone).</summary>
    public static void TagExampleNames(OpenApiDocument openApi, JsonElement schema, JsonObject body, string tag)
    {
        var resolved = openApi.Resolve(schema);
        if (!resolved.TryGetProperty("properties", out var properties))
        {
            return;
        }
        foreach (var property in properties.EnumerateObject())
        {
            if (!property.Name.Contains("name", StringComparison.OrdinalIgnoreCase) ||
                body[property.Name] is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }
            var candidate = $"{value.GetValue<string>()} {tag}";
            if (openApi.Conform(property.Value, JsonValue.Create(candidate)) is JsonValue conformed &&
                conformed.GetValueKind() == JsonValueKind.String && conformed.GetValue<string>() == candidate)
            {
                body[property.Name] = candidate;
            }
        }
    }
}
