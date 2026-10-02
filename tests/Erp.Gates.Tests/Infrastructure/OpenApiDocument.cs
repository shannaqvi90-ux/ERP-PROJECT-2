using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>The OpenAPI document served by the running app.</summary>
public sealed class OpenApiDocument(JsonElement root)
{
    public JsonElement Root { get; } = root;

    public static async Task<OpenApiDocument> LoadAsync(HttpClient client)
    {
        var json = await client.GetStringAsync("/api/openapi/v1.json");
        return new OpenApiDocument(JsonDocument.Parse(json).RootElement.Clone());
    }

    public bool TryGetOperation(string method, string pattern, out JsonElement operation)
    {
        operation = default;
        var path = NormalizePattern(pattern);
        return Root.GetProperty("paths").TryGetProperty(path, out var item) &&
               item.TryGetProperty(method.ToLowerInvariant(), out operation);
    }

    /// <summary>OpenAPI writes <c>{id}</c> where routing has <c>{id:guid}</c>.</summary>
    public static string NormalizePattern(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}", "{$1}");

    public JsonElement? RequestSchema(string method, string pattern)
    {
        if (!TryGetOperation(method, pattern, out var operation) ||
            !operation.TryGetProperty("requestBody", out var body) ||
            !body.GetProperty("content").TryGetProperty("application/json", out var content) ||
            !content.TryGetProperty("schema", out var schema))
        {
            return null;
        }
        return Resolve(schema);
    }

    public JsonElement Resolve(JsonElement schema)
    {
        while (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/').Last();
            schema = Root.GetProperty("components").GetProperty("schemas").GetProperty(name);
        }
        return schema;
    }

    /// <summary>Build a request body that satisfies the schema's shape, using
    /// <paramref name="valueFor"/> for every leaf.</summary>
    public JsonNode? BuildBody(JsonElement schema, Func<string, string?, string?, JsonNode?> valueFor, string? propertyName = null, int depth = 0)
    {
        schema = Resolve(schema);
        if (depth > 6)
        {
            return null;
        }
        var type = TypeOf(schema);
        var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;
        switch (type)
        {
            case "object":
                var obj = new JsonObject();
                if (schema.TryGetProperty("properties", out var properties))
                {
                    foreach (var property in properties.EnumerateObject())
                    {
                        obj[property.Name] = BuildBody(property.Value, valueFor, property.Name, depth + 1);
                    }
                }
                return obj;
            case "array":
                var items = schema.TryGetProperty("items", out var itemSchema) ? BuildBody(itemSchema, valueFor, propertyName, depth + 1) : null;
                return new JsonArray(items);
            default:
                return valueFor(type ?? "string", format, propertyName);
        }
    }

    private static string? TypeOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return schema.TryGetProperty("properties", out _) ? "object" : null;
        }
        if (type.ValueKind == JsonValueKind.String)
        {
            return type.GetString();
        }
        return type.EnumerateArray().Select(t => t.GetString()).FirstOrDefault(t => t != "null");
    }
}
