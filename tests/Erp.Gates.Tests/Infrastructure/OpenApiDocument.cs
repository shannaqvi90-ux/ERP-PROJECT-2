using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>A documented route, query or header parameter.</summary>
/// <param name="Enum">The values the document publishes for it (an enumeration), when it does.</param>
public sealed record ApiParameter(string Name, string In, string Type, string? Format, IReadOnlyList<string>? Enum = null);

/// <summary>A request-body leaf with the values the document allows for it.</summary>
/// <param name="Path">Property names from the body's root (<see cref="OpenApiDocument.ArrayItems"/> for an array's items).</param>
public sealed record EnumLeaf(IReadOnlyList<string> Path, IReadOnlyList<JsonNode> Values)
{
    public string Name => string.Join('.', Path);
}

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

    /// <summary>Every documented parameter (path, query, header) of an operation.</summary>
    public IReadOnlyList<ApiParameter> Parameters(string method, string pattern)
    {
        var list = new List<ApiParameter>();
        var path = NormalizePattern(pattern);
        if (!Root.GetProperty("paths").TryGetProperty(path, out var item))
        {
            return list;
        }
        var sources = new List<JsonElement>();
        if (item.TryGetProperty("parameters", out var shared)) sources.Add(shared);
        if (item.TryGetProperty(method.ToLowerInvariant(), out var operation) && operation.TryGetProperty("parameters", out var own)) sources.Add(own);
        foreach (var parameter in sources.SelectMany(s => s.EnumerateArray()).Select(Resolve))
        {
            var schema = parameter.TryGetProperty("schema", out var sc) ? Resolve(sc) : default;
            var type = schema.ValueKind == JsonValueKind.Object ? TypeOf(schema) ?? "string" : "string";
            var format = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("format", out var f) ? f.GetString() : null;
            var members = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
                : null;
            list.Add(new ApiParameter(parameter.GetProperty("name").GetString()!, parameter.GetProperty("in").GetString()!, type, format,
                members is { Count: > 0 } ? members : null));
        }
        return list;
    }

    /// <summary>Names of the string leaves of a request body (uuid strings excluded: they get ids).</summary>
    public IReadOnlyList<string> StringLeaves(JsonElement schema)
    {
        var names = new List<string>();
        void Walk(JsonElement s, string? name, int depth)
        {
            s = Resolve(s);
            if (depth > 6) return;
            switch (TypeOf(s))
            {
                case "object":
                    if (s.TryGetProperty("properties", out var properties))
                    {
                        foreach (var property in properties.EnumerateObject()) Walk(property.Value, property.Name, depth + 1);
                    }
                    break;
                case "array":
                    if (s.TryGetProperty("items", out var items)) Walk(items, name, depth + 1);
                    break;
                case "string" or null:
                    var format = s.TryGetProperty("format", out var f) ? f.GetString() : null;
                    if (name is not null && format is not ("uuid" or "date-time" or "date")) names.Add(name);
                    break;
            }
        }
        Walk(schema, null, 0);
        return names.Distinct().ToList();
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
    /// <paramref name="valueFor"/> for every leaf. With <paramref name="useDocumentedValues"/> a
    /// leaf the document constrains takes its first documented value instead: the first value of
    /// its <c>enum</c>, else its first <c>examples</c> entry (column keys, sort and filter
    /// expressions), so a body meant to pass validation does.</summary>
    public JsonNode? BuildBody(JsonElement schema, Func<string, string?, string?, JsonNode?> valueFor, string? propertyName = null, int depth = 0, bool useDocumentedValues = false) =>
        BuildBody(schema, (_, type, format, name) => valueFor(type, format, name), propertyName, depth, useDocumentedValues);

    /// <summary>Build a request body, handing <paramref name="valueFor"/> each leaf's own schema
    /// too (so a caller can make a leaf conform to it, see <see cref="Conform"/>).</summary>
    public JsonNode? BuildBody(JsonElement schema, Func<JsonElement, string, string?, string?, JsonNode?> valueFor, string? propertyName = null, int depth = 0, bool useDocumentedValues = false)
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
                        obj[property.Name] = BuildBody(property.Value, valueFor, property.Name, depth + 1, useDocumentedValues);
                    }
                }
                return obj;
            case "array":
                var items = schema.TryGetProperty("items", out var itemSchema) ? BuildBody(itemSchema, valueFor, propertyName, depth + 1, useDocumentedValues) : null;
                return new JsonArray(items);
            default:
                if (useDocumentedValues)
                {
                    foreach (var documented in new[] { "enum", "examples" })
                    {
                        if (schema.TryGetProperty(documented, out var values) && values.ValueKind == JsonValueKind.Array &&
                            values.EnumerateArray().FirstOrDefault(v => v.ValueKind != JsonValueKind.Null) is { ValueKind: not JsonValueKind.Undefined } first)
                        {
                            return JsonNode.Parse(first.GetRawText());
                        }
                    }
                    if (type == "null")
                    {
                        return null;
                    }
                }
                return valueFor(schema, type ?? "string", format, propertyName);
        }
    }

    /// <summary>
    /// The value, or one the leaf's documented constraints accept when the value breaks them: the
    /// first allowed value of an enum, the documented example when a pattern does not match, a
    /// string cut to its maximum length, a number moved into its range. Used wherever a request
    /// must pass validation so the handler runs to the end (own-tenant writes, and every field but
    /// the attacked one).
    /// </summary>
    public JsonNode? Conform(JsonElement leaf, JsonNode? value)
    {
        leaf = Resolve(leaf);
        foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (leaf.TryGetProperty(combinator, out var options))
            {
                var concrete = options.EnumerateArray().Select(Resolve).FirstOrDefault(o => TypeOf(o) != "null" && (o.TryGetProperty("type", out _) || o.TryGetProperty("enum", out _)));
                if (concrete.ValueKind == JsonValueKind.Object)
                {
                    return Conform(concrete, value);
                }
            }
        }
        if (leaf.TryGetProperty("enum", out var allowed))
        {
            var choices = allowed.EnumerateArray().Where(e => e.ValueKind != JsonValueKind.Null).ToList();
            if (choices.Count > 0 && !choices.Any(c => value is not null && JsonNode.DeepEquals(JsonNode.Parse(c.GetRawText()), value)))
            {
                return JsonNode.Parse(choices[0].GetRawText());
            }
            return value;
        }
        if (value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
        {
            var current = text.GetValue<string>();
            if (leaf.TryGetProperty("pattern", out var pattern) && !System.Text.RegularExpressions.Regex.IsMatch(current, pattern.GetString()!))
            {
                var example = Examples(leaf).FirstOrDefault(e => e.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(e.GetString()!, pattern.GetString()!));
                if (example.ValueKind == JsonValueKind.String)
                {
                    current = example.GetString()!;
                }
            }
            if (leaf.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var maxLength) && current.Length > maxLength)
            {
                current = current[..maxLength];
            }
            return JsonValue.Create(current);
        }
        if (value is JsonValue number && number.GetValueKind() == JsonValueKind.Number &&
            decimal.TryParse(number.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            if (leaf.TryGetProperty("minimum", out var min) && min.ValueKind == JsonValueKind.Number && n < min.GetDecimal())
            {
                return JsonValue.Create((long)Math.Ceiling(min.GetDecimal()));
            }
            if (leaf.TryGetProperty("maximum", out var maxValue) && maxValue.ValueKind == JsonValueKind.Number && n > maxValue.GetDecimal())
            {
                return JsonValue.Create((long)Math.Floor(maxValue.GetDecimal()));
            }
        }
        return value;
    }

    /// <summary>Every example value the document publishes (public text: it identifies no tenant).</summary>
    public IReadOnlySet<string> ExampleValues()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "example" && property.Value.ValueKind == JsonValueKind.String)
                        {
                            found.Add(property.Value.GetString()!);
                        }
                        else if (property.Name == "examples" && property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var example in property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String))
                            {
                                found.Add(example.GetString()!);
                            }
                        }
                        else
                        {
                            Walk(property.Value);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Walk(item);
                    break;
            }
        }
        Walk(Root);
        return found;
    }

    /// <summary>The leaf's documented example values (<c>examples</c>, then <c>example</c>).</summary>
    public static IEnumerable<JsonElement> Examples(JsonElement leaf)
    {
        if (leaf.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array)
        {
            foreach (var example in examples.EnumerateArray()) yield return example;
        }
        if (leaf.TryGetProperty("example", out var single))
        {
            yield return single;
        }
    }

    /// <summary>
    /// Every leaf of a request body whose document publishes its allowed values (an <c>enum</c>,
    /// which <c>AllowedTextValues</c> fields and enumerations both become), with those values
    /// (null left out), in document order. A body built with documented values always takes the
    /// first one; the code behind every other value (the Arabic side of a language or digits
    /// field, critic p04 round 4) is only reached when a caller sends it, so the isolation gates
    /// send each of them (<see cref="SetLeaf"/>).
    /// </summary>
    public IReadOnlyList<EnumLeaf> EnumLeaves(JsonElement schema)
    {
        var leaves = new List<EnumLeaf>();
        void Walk(JsonElement s, List<string> path, int depth)
        {
            s = Resolve(s);
            if (depth > 6 || s.ValueKind != JsonValueKind.Object) return;
            switch (TypeOf(s))
            {
                case "object":
                    if (s.TryGetProperty("properties", out var properties))
                    {
                        foreach (var property in properties.EnumerateObject()) Walk(property.Value, [.. path, property.Name], depth + 1);
                    }
                    return;
                case "array":
                    if (s.TryGetProperty("items", out var items)) Walk(items, [.. path, ArrayItems], depth + 1);
                    return;
            }
            var allowed = AllowedValues(s);
            if (allowed.Count > 0 && path.Count > 0)
            {
                leaves.Add(new EnumLeaf(path, allowed));
            }
        }
        Walk(schema, [], 0);
        return leaves;
    }

    /// <summary>The segment of an <see cref="EnumLeaf"/> path that stands for every item of an array.</summary>
    public const string ArrayItems = "[]";

    private List<JsonNode> AllowedValues(JsonElement leaf)
    {
        if (leaf.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            return values.EnumerateArray().Where(v => v.ValueKind != JsonValueKind.Null).Select(v => JsonNode.Parse(v.GetRawText())!).ToList();
        }
        foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (leaf.TryGetProperty(combinator, out var options) && options.ValueKind == JsonValueKind.Array)
            {
                var found = options.EnumerateArray().Select(Resolve).Where(o => o.ValueKind == JsonValueKind.Object).SelectMany(AllowedValues).ToList();
                if (found.Count > 0)
                {
                    return found.DistinctBy(v => v.ToJsonString()).ToList();
                }
            }
        }
        return [];
    }

    /// <summary>Puts <paramref name="value"/> at the leaf's path in a body built for the same
    /// schema (every item of an array on the path; missing objects are created). Returns whether
    /// the body now holds the value somewhere.</summary>
    public static bool SetLeaf(JsonNode? body, IReadOnlyList<string> path, JsonNode value)
    {
        if (body is null || path.Count == 0)
        {
            return false;
        }
        var segment = path[0];
        var rest = path.Skip(1).ToList();
        if (segment == ArrayItems)
        {
            if (body is not JsonArray array)
            {
                return false;
            }
            var set = false;
            for (var i = 0; i < array.Count; i++)
            {
                if (rest.Count == 0)
                {
                    array[i] = value.DeepClone();
                    set = true;
                }
                else
                {
                    set |= SetLeaf(array[i], rest, value);
                }
            }
            return set;
        }
        if (body is not JsonObject obj)
        {
            return false;
        }
        var key = obj.FirstOrDefault(p => string.Equals(p.Key, segment, StringComparison.OrdinalIgnoreCase)).Key ?? segment;
        if (rest.Count == 0)
        {
            obj[key] = value.DeepClone();
            return true;
        }
        if (obj[key] is null)
        {
            obj[key] = rest[0] == ArrayItems ? new JsonArray(new JsonObject()) : new JsonObject();
        }
        return SetLeaf(obj[key], rest, value);
    }

    /// <summary>The schema's type (the first non-null one when it allows several), after
    /// resolving references; <c>object</c> for a schema with properties and no type.</summary>
    public string? TypeOfSchema(JsonElement schema) => TypeOf(Resolve(schema));

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
