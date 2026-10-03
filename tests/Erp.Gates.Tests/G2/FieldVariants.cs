using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, one field at a time. A handler can take a path that runs only when one particular field
/// changes (an e-mail correction, a password reset without a password, a deactivation), and an
/// access check skipped on that path is invisible to a gate that always sends the same body
/// (critic p03 round 2, plant P5: an e-mail-only edit of the Administrator skipped the check and
/// a clerk moved the Administrator's sign-in to their own address). So for every writable
/// property in an endpoint's OpenAPI request schema, the gates send one request that changes that
/// property alone to another valid value, and one that leaves it out; every other field is the
/// record's own value (an edit) or a fresh valid value (an action).
///
/// A property the gate cannot give a different valid value is a blind spot, reported as a
/// problem: the gate is extended, the field is not skipped. Only the concurrency token
/// (<c>version</c>) is copied, never varied: a stale one is refused before any access check.
/// </summary>
public static class FieldVariants
{
    public enum Kind { Changed, Omitted }

    public sealed record Spec(string Field, Kind Kind)
    {
        public override string ToString() => Kind == Kind.Changed ? $"[{Field} changed]" : $"[{Field} left out]";
    }

    /// <summary>Copied from the record, never varied.</summary>
    public static readonly string[] ConcurrencyTokens = ["version"];

    /// <summary>One change and one omission per writable property of the request schema.</summary>
    public static IReadOnlyList<Spec> Specs(OpenApiDocument openApi, JsonElement schema)
    {
        var specs = new List<Spec>();
        if (!openApi.Resolve(schema).TryGetProperty("properties", out var properties))
        {
            return specs;
        }
        foreach (var property in properties.EnumerateObject())
        {
            if (ConcurrencyTokens.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            specs.Add(new Spec(property.Name, Kind.Changed));
            specs.Add(new Spec(property.Name, Kind.Omitted));
        }
        return specs;
    }

    /// <summary>
    /// The base body with the one property of <paramref name="spec"/> changed or left out, or null
    /// when no different valid value can be built (a blind spot the caller reports).
    /// <paramref name="grantValue"/> gives a grant field (<c>roleIds</c>, <c>permissions</c>) its
    /// changed value: what is a valid change of a grant depends on who sends it and at whom.
    /// </summary>
    public static JsonObject? Apply(OpenApiDocument openApi, JsonElement schema, Spec spec, JsonObject baseBody, string tag, string emailDomain,
        Func<string, JsonArray, JsonArray?> grantValue)
    {
        var body = (JsonObject)baseBody.DeepClone();
        if (spec.Kind == Kind.Omitted)
        {
            body.Remove(spec.Field);
            return body;
        }
        var current = baseBody[spec.Field];
        JsonNode? changed;
        if (GrantEscalation.GrantFields.Contains(spec.Field))
        {
            changed = grantValue(spec.Field, current as JsonArray ?? []);
        }
        else
        {
            var properties = openApi.Resolve(schema).GetProperty("properties");
            changed = ChangedValue(openApi, properties.GetProperty(spec.Field), spec.Field, current, tag, emailDomain);
        }
        if (changed is null || JsonNode.DeepEquals(changed, current))
        {
            return null;
        }
        body[spec.Field] = changed;
        return body;
    }

    private static JsonNode? ChangedValue(OpenApiDocument openApi, JsonElement propertySchema, string name, JsonNode? current, string tag, string emailDomain)
    {
        var schema = openApi.Resolve(propertySchema);
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            var other = values.EnumerateArray().FirstOrDefault(v => v.ValueKind != JsonValueKind.Null && !JsonNode.DeepEquals(JsonNode.Parse(v.GetRawText()), current));
            return other.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(other.GetRawText());
        }
        var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;
        var lower = name.ToLowerInvariant();
        var currentText = current is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        return openApi.TypeOfSchema(schema) switch
        {
            "boolean" => JsonValue.Create(!(current is JsonValue b && b.GetValueKind() == JsonValueKind.True)),
            "string" when format is "uuid" or "date" or "date-time" => null,
            "string" when lower.Contains("email") => JsonValue.Create($"g2.field.{tag}@{emailDomain}"),
            "string" when lower.Contains("password") => JsonValue.Create($"Field-Varied-{tag}-Pass9"),
            "string" when lower == "language" => JsonValue.Create(currentText == "ar" ? "en" : "ar"),
            "string" when name.EndsWith("Ar", StringComparison.Ordinal) => JsonValue.Create($"تعديل {tag}"),
            "string" => JsonValue.Create($"Field {tag}"),
            _ => null,
        };
    }
}
