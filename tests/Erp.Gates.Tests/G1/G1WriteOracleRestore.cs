using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// What the write oracles leave behind (critic p03 round 7, G3: the text oracle wrote tenant B's
/// seeded addresses into tenant A, under the gate password, and renamed both workspaces; the id
/// oracle that ran next on the same environment signed in as tenant B's administrator and was asked
/// to choose a workspace, so <c>./erp verify</c> failed or passed by test order). Nothing is left
/// out of what the oracles send; what a send changed that another test may rely on is put back
/// afterwards through the product's own edit endpoints, so the audit trail records the undo too:
///
/// <list type="bullet">
/// <item>a record of tenant A's that a send gave a value of tenant B's seed data (an address of
/// one of B's users) gets a fresh value of tenant A's in that field, right after the send;</item>
/// <item>every record of its own with no id in the route (a workspace's settings), read for both
/// tenants before the run, gets its values back after it.</item>
/// </list>
///
/// An undo that the product refuses is a problem of the run, never ignored: a later module whose
/// record cannot be put back says so instead of leaving a shared environment changed.
/// </summary>
public static partial class G1WriteOracle
{
    /// <summary>The records of its own (no id in the route) that the oracles edit, as each client
    /// read them before the run.</summary>
    internal sealed class Singletons
    {
        private readonly List<(HttpClient Client, string Who, ApiEndpoint Edit, JsonObject Before)> _records = [];

        public static async Task<Singletons> TakeAsync(OpenApiDocument openApi, IEnumerable<ApiEndpoint> endpoints, params (HttpClient Client, string Who)[] clients)
        {
            var taken = new Singletons();
            foreach (var edit in endpoints.Where(e => e.Method is "PUT" or "PATCH" && e.RouteParameters.Count == 0 && !e.IsAnonymous)
                         .Where(e => openApi.RequestSchema(e.Method, e.Pattern) is not null))
            {
                foreach (var (client, who) in clients)
                {
                    using var read = await client.GetAsync(edit.Pattern);
                    if (read.IsSuccessStatusCode && await read.Content.ReadFromJsonAsync<JsonNode>() is JsonObject before)
                    {
                        taken._records.Add((client, who, edit, before));
                    }
                }
            }
            return taken;
        }

        /// <summary>Put every record back as it was read; a refusal is returned as a problem.</summary>
        public async Task<List<string>> RestoreAsync(OpenApiDocument openApi, ErpTestEnvironment env)
        {
            var problems = new List<string>();
            foreach (var (client, who, edit, before) in _records)
            {
                var schema = openApi.RequestSchema(edit.Method, edit.Pattern)!.Value;
                using var read = await client.GetAsync(edit.Pattern);
                var now = read.IsSuccessStatusCode ? await read.Content.ReadFromJsonAsync<JsonNode>() as JsonObject : null;
                if (now is null)
                {
                    problems.Add($"{edit} ({who}): the record could not be read back to restore it ({(int)read.StatusCode})");
                    continue;
                }
                if (Same(before, now))
                {
                    continue;
                }
                var body = Valid(openApi, schema, env, "restore", CompanyOf(client));
                foreach (var (name, _) in body.ToList())
                {
                    // The record's original values; its version is the current one.
                    var source = IsVersion(name) ? now : before;
                    if (source[name] is { } value)
                    {
                        body[name] = value.DeepClone();
                    }
                }
                using var put = await client.SendAsync(Json(new HttpMethod(edit.Method), edit.Pattern, body));
                if (!put.IsSuccessStatusCode)
                {
                    problems.Add($"{edit} ({who}): restoring the record after the oracle was refused ({(int)put.StatusCode}): {Short(await put.Content.ReadAsStringAsync())}");
                }
            }
            return problems;
        }

        private static bool IsVersion(string name) => name.Equals("version", StringComparison.OrdinalIgnoreCase) || name.Equals("rowVersion", StringComparison.OrdinalIgnoreCase);

        private static bool Same(JsonObject before, JsonObject now) =>
            before.Where(p => !IsVersion(p.Key) && !p.Key.StartsWith("updated", StringComparison.OrdinalIgnoreCase))
                .All(p => JsonNode.DeepEquals(p.Value, now[p.Key]));
    }

    /// <summary>
    /// Give the field of the record a send created or edited (<paramref name="record"/>, its
    /// address) a fresh value of the caller's own, through the edit endpoint for that address. Used
    /// when tenant A's send carried a value of tenant B's seed data and was accepted.
    /// </summary>
    internal static async Task<string?> UndoValueAsync(HttpClient client, OpenApiDocument openApi, IReadOnlyList<ApiEndpoint> endpoints, ErpTestEnvironment env,
        string record, string field, string fresh)
    {
        var edit = endpoints.FirstOrDefault(e => e.Method is "PUT" or "PATCH" && !e.IsAnonymous && Matches(e, record) && openApi.RequestSchema(e.Method, e.Pattern) is not null);
        if (edit is null)
        {
            return $"{record}: no edit endpoint to give [{field}] a fresh value after the oracle wrote tenant B's seed value there";
        }
        using var read = await client.GetAsync(record);
        var current = read.IsSuccessStatusCode ? await read.Content.ReadFromJsonAsync<JsonNode>() as JsonObject : null;
        if (current is null)
        {
            return $"{record}: could not be read to undo [{field}] ({(int)read.StatusCode})";
        }
        var body = Valid(openApi, openApi.RequestSchema(edit.Method, edit.Pattern)!.Value, env, "undo", CompanyOf(client));
        foreach (var (name, _) in body.ToList())
        {
            if (current[name] is { } value)
            {
                body[name] = value.DeepClone();
            }
        }
        body[field] = fresh;
        using var put = await client.SendAsync(Json(new HttpMethod(edit.Method), record, body));
        return put.IsSuccessStatusCode ? null : $"{edit} at {record}: undoing [{field}] was refused ({(int)put.StatusCode}): {Short(await put.Content.ReadAsStringAsync())}";
    }

    /// <summary>The endpoint's pattern matches the address segment by segment (a route parameter
    /// matches any one segment).</summary>
    private static bool Matches(ApiEndpoint endpoint, string path)
    {
        var pattern = endpoint.Pattern.Trim('/').Split('/');
        var actual = path.Split('?')[0].Trim('/').Split('/');
        return pattern.Length == actual.Length &&
               pattern.Zip(actual).All(p => p.First.StartsWith('{') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The address of the record a send changed: the record an edit was sent to, or the
    /// one a create made (its <c>Location</c>, else its collection and the id it answered).</summary>
    private static string? RecordOf(string sentTo, string method, HttpResponseMessage response, string text, string? collection)
    {
        if (method is "PUT" or "PATCH")
        {
            return sentTo;
        }
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        string? id = null;
        try
        {
            if (JsonNode.Parse(text) is JsonObject answer && answer["id"] is JsonValue value)
            {
                id = value.ToString();
            }
        }
        catch (JsonException)
        {
        }
        if (id is null)
        {
            return response.Headers.Location?.OriginalString;
        }
        return $"{(collection ?? sentTo).TrimEnd('/')}/{id}";
    }
}
