using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, existence oracles on writes through record ids (critic p03 rounds 5 and 6, plant L3: a
/// company role naming another workspace's company answered "not their company" while an id that
/// exists nowhere answered "unknown ids"; the write oracle judged only text fields, so it passed
/// twice). Every non-anonymous POST, PUT and PATCH is found from routing and the OpenAPI document,
/// and every uuid leaf of its body is judged wherever it sits: a field (<c>companyId</c>), an item
/// of a list of ids (<c>roleIds[]</c>) or a field of an item of a list of objects
/// (<c>companyRoles[].companyId</c>, <c>companies[].companyId</c>). Line items of later modules
/// (invoice lines naming a product, a tax, a cost centre) are judged the same way.
///
/// The record named is tenant B's own record of the table named like the field (roles for
/// <c>roleId</c>, companies for <c>companyId</c>). Tenant B first reads every list it can (a
/// registry kept from reads fills the way everyday use fills it) and sends the request with its
/// own record there. Tenant A then sends the same request twice, with every other id one of its
/// own records: once with tenant B's record and once with an id that exists nowhere. The two
/// answers must be the same: the same status, and for a refusal the same problem once trace ids,
/// the request's own address and every id are blanked out. An accepted request on both sides
/// counts as the same (each creates its own record).
/// </summary>
public static partial class G1WriteOracle
{
    /// <param name="Problems">Pairs whose answers differ.</param>
    /// <param name="Checks">Pairs compared.</param>
    /// <param name="Judged">Every endpoint and leaf compared, as <c>"POST /path [leaf]"</c>.</param>
    /// <param name="Unjudged">Leaves left out, with the reason (no table named like the field).</param>
    public sealed record IdResult(IReadOnlyList<string> Problems, int Checks, IReadOnlyList<string> Judged, IReadOnlyList<string> Unjudged);

    public static async Task<IdResult> RunIdsAsync(ErpTestEnvironment env, Func<ApiEndpoint, bool>? only = null)
    {
        var problems = new List<string>();
        var judged = new List<string>();
        var unjudged = new List<string>();
        var checks = 0;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        using var a = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        using var b = await env.SignInAsync(env.Email(env.TenantB, "admin"));
        await UseWorkingCompanyAsync(a);
        await UseWorkingCompanyAsync(b);
        var aAdmin = (await a.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        var bAdmin = (await b.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("user").GetProperty("id").GetGuid();
        var ownA = await OwnRecords.LoadAsync(env, env.TenantA.Id, CompanyOf(a), aAdmin);
        var ownB = await OwnRecords.LoadAsync(env, env.TenantB.Id, CompanyOf(b), bAdmin);

        var endpoints = EndpointInventory.From(env.Factory.Services).Where(e => !e.IsAnonymous).OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        // Records of their own (a workspace's settings) are put back after the run (G1WriteOracleRestore.cs).
        var singletons = await Singletons.TakeAsync(openApi, endpoints, (a, "tenant A"), (b, "tenant B"));
        // Tenant B reads every list it can, once.
        foreach (var endpoint in endpoints.Where(e => e.Method == "GET" && e.RouteParameters.Count == 0))
        {
            using var read = await b.GetAsync(endpoint.Pattern);
        }

        var n = 0;
        foreach (var endpoint in endpoints.Where(e => e.Method is "POST" or "PUT" or "PATCH" && (only is null || only(e))))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
            {
                continue;
            }
            var leaves = UuidLeaves(openApi, schema);
            if (leaves.Count == 0)
            {
                continue;
            }
            // An edit is sent to a record each tenant creates first, or, without a collection to
            // create one in, to an existing record of its own named like the route's id.
            var collection = endpoint.RouteParameters.Count == 0 ? null : endpoint.Pattern[..endpoint.Pattern.LastIndexOf("/{", StringComparison.Ordinal)];
            string? routeA = null, routeB = null;
            if (collection is not null && openApi.RequestSchema("POST", collection) is null)
            {
                var parameter = endpoint.RouteParameters[^1];
                routeA = ownA.IdFor(parameter);
                routeB = ownB.IdFor(parameter);
                if (routeA is null || routeB is null)
                {
                    unjudged.Add($"{endpoint}: no record of each tenant named like the route's {parameter} to send it to");
                    continue;
                }
            }
            foreach (var leaf in leaves)
            {
                var label = $"{endpoint} [{string.Join("", leaf.Path.Select((p, i) => p == OpenApiDocument.ArrayItems ? "[]" : i == 0 ? p : "." + p))}]";
                if (ownB.IdFor(leaf.Name) is not { } theirs)
                {
                    unjudged.Add($"{label}: no table named like {leaf.Name} holds a record of tenant B");
                    continue;
                }
                n++;
                var tag = $"id{n}{Guid.NewGuid():N}"[..12];
                var nowhere = Guid.NewGuid().ToString();

                // Tenant B uses its own record there.
                await SendAsync(b, openApi, endpoint, collection, schema, env, $"{tag}b", body => Place(openApi, schema, body, leaf, theirs, ownB), routeB);
                var (withB, withBText) = await SendAsync(a, openApi, endpoint, collection, schema, env, $"{tag}u", body => Place(openApi, schema, body, leaf, theirs, ownA), routeA);
                var (withNowhere, withNowhereText) = await SendAsync(a, openApi, endpoint, collection, schema, env, $"{tag}f", body => Place(openApi, schema, body, leaf, nowhere, ownA), routeA);
                checks++;
                judged.Add(label);
                var answer = Comparable(withB, withBText, theirs, nowhere);
                var control = Comparable(withNowhere, withNowhereText, nowhere, theirs);
                if (answer != control)
                {
                    var at = 0;
                    while (at < answer.Length && at < control.Length && answer[at] == control[at])
                    {
                        at++;
                    }
                    at = Math.Max(0, at - 60);
                    problems.Add($"{label}: tenant A naming tenant B's record answered {Short(answer)}, an id that exists nowhere answered {Short(control)}: the answer tells tenant A what tenant B holds " +
                                 $"(first difference: …{Short(answer[at..])} | …{Short(control[at..])})");
                }
            }
        }
        problems.AddRange(await singletons.RestoreAsync(openApi, env));
        return new IdResult(problems, checks, judged, unjudged);
    }

    /// <summary>A uuid leaf of a request body: its path (<see cref="OpenApiDocument.ArrayItems"/>
    /// for an array's items) and the name it goes by (the field's, or the array's for its items).</summary>
    public sealed record UuidLeaf(IReadOnlyList<string> Path, string Name);

    /// <summary>Every uuid leaf of the schema, at any depth (objects, arrays of ids, arrays of objects).</summary>
    public static List<UuidLeaf> UuidLeaves(OpenApiDocument openApi, JsonElement schema)
    {
        var leaves = new List<UuidLeaf>();
        void Walk(JsonElement node, List<string> path, string? name, int depth)
        {
            node = openApi.Resolve(node);
            if (depth > 6)
            {
                return;
            }
            switch (openApi.TypeOfSchema(node))
            {
                case "object":
                    if (node.TryGetProperty("properties", out var properties))
                    {
                        foreach (var property in properties.EnumerateObject())
                        {
                            Walk(property.Value, [.. path, property.Name], property.Name, depth + 1);
                        }
                    }
                    break;
                case "array":
                    if (node.TryGetProperty("items", out var items))
                    {
                        Walk(items, [.. path, OpenApiDocument.ArrayItems], name, depth + 1);
                    }
                    break;
                case "string":
                    if (node.TryGetProperty("format", out var format) && format.GetString() == "uuid" && name is not null)
                    {
                        leaves.Add(new UuidLeaf(path, name));
                    }
                    break;
            }
        }
        Walk(schema, [], null, 0);
        return leaves;
    }

    /// <summary>Put <paramref name="value"/> at the leaf: the top-level field holding it is built
    /// afresh with one item per array and the caller's own records in every other id (so a check
    /// of the other ids never answers first), then the leaf is set.</summary>
    private static void Place(OpenApiDocument openApi, JsonElement schema, JsonObject body, UuidLeaf leaf, string value, OwnRecords own)
    {
        var top = leaf.Path[0];
        if (leaf.Path.Count > 1 && openApi.Resolve(schema).GetProperty("properties").TryGetProperty(top, out var topSchema))
        {
            body[top] = openApi.BuildBody(topSchema, (node, type, format, name) => openApi.Conform(node, type switch
            {
                "string" when format == "uuid" => name is null ? null : own.IdFor(name),
                "string" when format is "date" => DateTime.UtcNow.ToString("yyyy-MM-dd"),
                "string" when format is "date-time" => DateTimeOffset.UtcNow.ToString("O"),
                "string" => $"Ids {name}",
                "boolean" => true,
                "integer" => 1,
                "number" => 1,
                _ => null,
            }), top, useDocumentedValues: true);
        }
        OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(value));
    }

    /// <summary>An answer as compared: any success alike; a refusal by its status and problem, with
    /// trace ids, the request's address and every id blanked out (the two values first).</summary>
    private static string Comparable(int status, string text, string value, string other)
    {
        if (status is >= 200 and < 300)
        {
            return "accepted";
        }
        var scrubbed = text;
        try
        {
            if (JsonNode.Parse(text) is JsonObject problem)
            {
                problem.Remove("traceId");
                problem.Remove("instance");
                scrubbed = problem.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }
        scrubbed = scrubbed.Replace(value, "<value>", StringComparison.OrdinalIgnoreCase).Replace(other, "<value>", StringComparison.OrdinalIgnoreCase);
        return $"{status} {AnyId().Replace(scrubbed, "<id>")}";
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex AnyId();

    /// <summary>One tenant's own records by table, read with the superuser: what a field named like
    /// a table refers to. Companies start with the working company, users leave out the signed-in
    /// administrator (an edit of one's own access is refused before anything else), system rows
    /// (the Administrator role) come last, and branches of the working company come first.</summary>
    private sealed class OwnRecords
    {
        private readonly Dictionary<string, List<Guid>> _byTable = new(StringComparer.Ordinal);

        public static async Task<OwnRecords> LoadAsync(ErpTestEnvironment env, Guid tenant, string workingCompany, Guid administrator)
        {
            var records = new OwnRecords();
            await using var admin = await env.OpenAdminAsync();
            foreach (var table in await DbCatalog.TenantTablesAsync(admin))
            {
                var columns = await DbCatalog.ColumnsAsync(admin, table);
                if (!columns.Any(c => c.Name == "id" && c.Type == "uuid"))
                {
                    continue;
                }
                var order = new List<string>();
                if (Guid.TryParse(workingCompany, out var company))
                {
                    if (table.Name == "companies")
                    {
                        order.Add($"(id = '{company}') DESC");
                    }
                    else if (columns.Any(c => c.Name == "company_id"))
                    {
                        order.Add($"(company_id = '{company}') DESC");
                    }
                }
                if (columns.Any(c => c.Name == "is_system"))
                {
                    order.Add("is_system");
                }
                order.Add("id");
                var ids = await DbCatalog.ReadAsync(admin, $"SELECT id FROM {table.Qualified} WHERE tenant_id = @t AND id <> @a ORDER BY {string.Join(", ", order)} LIMIT 5",
                    r => r.GetGuid(0), ("t", tenant), ("a", administrator));
                if (ids.Count > 0)
                {
                    records._byTable.TryAdd(table.Name, ids);
                }
            }
            return records;
        }

        /// <summary>The first own record of the table named like the field (<c>roleIds</c>,
        /// <c>roleId</c> and <c>role</c> name roles), or null.</summary>
        public string? IdFor(string field)
        {
            var lower = field.ToLowerInvariant();
            var stem = lower.EndsWith("ids", StringComparison.Ordinal) ? lower[..^3] : lower.EndsWith("id", StringComparison.Ordinal) ? lower[..^2] : lower;
            if (stem.Length == 0)
            {
                return null;
            }
            foreach (var name in new[] { stem, stem + "s", stem + "es", stem.EndsWith('y') ? stem[..^1] + "ies" : stem, stem.EndsWith('s') ? stem : stem + "s" })
            {
                if (_byTable.TryGetValue(name, out var ids))
                {
                    return ids[0].ToString();
                }
            }
            return null;
        }
    }
}
