using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 non-interference: what one tenant is answered may not depend on what the other tenant did.
/// The marker-based attack only sees a leak that carries something of tenant B's (an id, a text
/// value, a canary). State shared across requests can leak without one: a count cached by search
/// text hands tenant A tenant B's total (critic p05 round 1, plant L3), a variable captured by an
/// endpoint lambda hands on the previous caller's number, a per-id cache hands on a status code.
/// This check needs no marker. For every GET the app exposes, with route values, query parameters,
/// list groupings and searches drawn from both tenants, it compares two answers to the same
/// request from the same signed-in user over the same database:
/// <list type="bullet">
/// <item>in the shared process, sent right after the other tenant sent exactly the same request
/// (so anything kept per request, per id, per search or per caller holds the other tenant's
/// answer at that moment), and</item>
/// <item>in a fresh app process that only the judged tenant has ever used (its own singletons,
/// caches and endpoint closures, untouched by the other tenant).</item>
/// </list>
/// Both answers (status and body, with times and trace ids normalised) must be identical. A
/// difference is confirmed once more before it is reported; a request whose answer changes from
/// one call to the next in the fresh process is reported as unstable, not judged. Both directions
/// run (judging tenant A after tenant B, and tenant B after tenant A), each with its own fresh
/// process. The check is blind unless enough requests have different true answers in the two
/// tenants, so that count has a ratchet minimum. Static fields are shared by the two hosts (they
/// run in one test process) and are covered by the process-state gate instead.
/// </summary>
public static partial class NonInterference
{
    public sealed record Result(
        IReadOnlyList<string> Findings,
        IReadOnlyList<string> Unstable,
        IReadOnlyList<string> BlindSpots,
        int Requests,
        int Comparisons,
        int Discriminating,
        int Endpoints);

    private static readonly string[] SharedTexts = ["a", "al", "e", "1", "co", "ad"];

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        var a = env.TenantA;
        var b = env.TenantB;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Method == "GET" && e.Surface == SurfaceKind.Data && e.Pattern.StartsWith("/api/", StringComparison.Ordinal))
            .ToList();

        // Both tenants first make every write the app offers on their own records in the shared
        // process, so whatever a write leaves behind (a counter, a "recently changed" list, a
        // cache filled on save) is there when the other tenant reads.
        var allEndpoints = EndpointInventory.From(env.Factory.Services);
        var writes = 0;
        foreach (var tenant in new[] { b, a })
        {
            var writer = await TenantActivity.StartAsync(env, tenant, allEndpoints, openApi);
            try
            {
                await writer.WriteAsync(await TenantSnapshot.TakeAsync(env, tenant.Id, tenant.Canary, tenant.Code));
                writes += writer.SuccessfulWrites;
            }
            finally
            {
                writer.Dispose();
            }
        }

        var ownA = await TenantSnapshot.TakeAsync(env, a.Id, a.Canary, a.Code);
        var ownB = await TenantSnapshot.TakeAsync(env, b.Id, b.Canary, b.Code);
        var textsA = (await VictimValues.ReadAsync(env, ownA, b.Id)).Strings;
        var textsB = (await VictimValues.ReadAsync(env, ownB, a.Id)).Strings;

        using var sharedA = await env.SignInWithTokenAsync(env.Email(a, "admin"));
        using var sharedB = await env.SignInWithTokenAsync(env.Email(b, "admin"));
        await using var freshProcessA = env.StartFreshProcess();
        await using var freshProcessB = env.StartFreshProcess();
        using var freshA = FreshClient(freshProcessA, sharedA);
        using var freshB = FreshClient(freshProcessB, sharedB);

        var uris = RequestsFor(endpoints, openApi, catalog, ownA, ownB, textsA, textsB, a.Code, b.Code);
        var state = new State();
        foreach (var (endpoint, uri) in uris)
        {
            var judgedA = await JudgeAsync(state, uri, "tenant A", sharedA, freshA, "tenant B", sharedB);
            var judgedB = await JudgeAsync(state, uri, "tenant B", sharedB, freshB, "tenant A", sharedA);
            state.Endpoints.Add(endpoint.Key);
            if (judgedA is { } answerA && judgedB is { } answerB && answerA != answerB)
            {
                state.Discriminating++;
            }
        }

        var blind = new List<string>();
        if (state.Comparisons == 0)
        {
            blind.Add("no request was compared");
        }
        else if (state.Unstable.Count * 4 > state.Comparisons)
        {
            blind.Add($"{state.Unstable.Count} of {state.Comparisons} requests answered differently from one call to the next in a fresh process");
        }
        if (writes == 0)
        {
            blind.Add("neither tenant made a successful write before the comparison");
        }
        if (state.Discriminating == 0)
        {
            blind.Add("no request had different true answers in the two tenants, so no interference could show");
        }
        return new Result(state.Findings, state.Unstable, blind, state.Requests, state.Comparisons, state.Discriminating, state.Endpoints.Count);
    }

    private sealed class State
    {
        public List<string> Findings { get; } = [];
        public List<string> Unstable { get; } = [];
        public HashSet<string> Endpoints { get; } = new(StringComparer.Ordinal);
        public int Requests;
        public int Comparisons;
        public int Discriminating;
    }

    /// <summary>One comparison. Returns the judged tenant's true (fresh-process) answer when it is
    /// stable, otherwise null.</summary>
    private static async Task<string?> JudgeAsync(State state, string uri, string judged, HttpClient shared, HttpClient fresh, string other, HttpClient otherShared)
    {
        await GetAsync(state, fresh, uri); // warms whatever the fresh process keeps, with the judged tenant's own answer
        var truth = await GetAsync(state, fresh, uri);
        await GetAsync(state, otherShared, uri);
        var answer = await GetAsync(state, shared, uri);
        state.Comparisons++;
        if (answer == truth)
        {
            return truth;
        }
        var truthAgain = await GetAsync(state, fresh, uri);
        if (truthAgain != truth)
        {
            state.Unstable.Add($"{judged}: GET {uri} answers differently from one call to the next ({Short(truth)} / {Short(truthAgain)})");
            return null;
        }
        await GetAsync(state, otherShared, uri);
        var answerAgain = await GetAsync(state, shared, uri);
        if (answerAgain == truth)
        {
            state.Unstable.Add($"{judged}: GET {uri} differed once in the shared process ({Short(answer)}), then matched");
            return truth;
        }
        state.Findings.Add($"{judged}: GET {uri} is answered {Short(answerAgain)} in the shared process right after {other} sent the same request, " +
                           $"but {Short(truth)} by a fresh process only {judged} has used");
        return truth;
    }

    private static async Task<string> GetAsync(State state, HttpClient client, string uri)
    {
        state.Requests++;
        using var response = await client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode} {Normalize(body)}";
    }

    private static HttpClient FreshClient(ErpAppFactory process, HttpClient signedIn)
    {
        var client = process.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    /// <summary>Every GET with values from both tenants: route values (each tenant's ids and
    /// code), each documented query parameter alone (texts both tenants hold, each tenant's own
    /// texts and ids, flags and numbers), and for registered lists every grouping and sort.</summary>
    internal static List<(ApiEndpoint Endpoint, string Uri)> RequestsFor(IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi, ModuleCatalog catalog,
        TenantSnapshot ownA, TenantSnapshot ownB, IReadOnlyList<string> textsA, IReadOnlyList<string> textsB, string codeA, string codeB)
    {
        var idsA = ownA.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty).Append(ownA.TenantId).Select(i => i.ToString()).ToList();
        var idsB = ownB.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty).Append(ownB.TenantId).Select(i => i.ToString()).ToList();
        var texts = SharedTexts.Concat(textsA.Take(2)).Concat(textsB.Take(2)).Distinct(StringComparer.Ordinal).ToList();
        var result = new List<(ApiEndpoint, string)>();
        foreach (var endpoint in endpoints)
        {
            var parameters = openApi.Parameters(endpoint.Method, endpoint.Pattern);
            var routes = endpoint.RouteParameters.Count == 0
                ? [endpoint.Path(_ => "")]
                : idsA.Concat(idsB).Append(codeA).Append(codeB).Select(v => endpoint.Path(_ => v)).Distinct(StringComparer.Ordinal).ToList();
            foreach (var route in routes)
            {
                result.Add((endpoint, route));
            }
            var basePath = routes[0];
            foreach (var parameter in parameters.Where(p => p.In == "query"))
            {
                IEnumerable<string> values = (parameter.Type, parameter.Format) switch
                {
                    ("boolean", _) => ["true", "false"],
                    ("integer" or "number", _) => ["1", "50"],
                    (_, "uuid") => [idsA[0], idsB[0]],
                    _ => texts,
                };
                foreach (var value in values)
                {
                    result.Add((endpoint, $"{basePath}?{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}"));
                }
            }
            if (catalog.Lists.FirstOrDefault(l => l.Endpoint == endpoint.Pattern) is { } list)
            {
                foreach (var column in list.Columns.Where(c => c.Groupable))
                {
                    result.Add((endpoint, $"{basePath}?take=50&groupBy={Uri.EscapeDataString(column.Key)}"));
                    result.Add((endpoint, $"{basePath}?take=50&search=a&groupBy={Uri.EscapeDataString(column.Key)}"));
                }
                foreach (var column in list.Columns.Where(c => c.Sortable))
                {
                    result.Add((endpoint, $"{basePath}?take=5&sort=-{Uri.EscapeDataString(column.Key)}"));
                }
            }
        }
        return result.DistinctBy(r => r.Item2, StringComparer.Ordinal).ToList();
    }

    /// <summary>The body with trace ids removed and every date-time replaced, so two answers that
    /// differ only by when they were made compare equal.</summary>
    internal static string Normalize(string text)
    {
        try
        {
            return Scrub(JsonNode.Parse(text))?.ToJsonString() ?? "";
        }
        catch (JsonException)
        {
            return TimeRegex().Replace(text, "<time>");
        }
    }

    private static JsonNode? Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("traceId");
                foreach (var (key, child) in obj.ToList())
                {
                    obj[key] = Scrub(child?.DeepClone());
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Scrub(array[i]?.DeepClone());
                }
                return array;
            case JsonValue leaf when leaf.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(TimeRegex().Replace(leaf.GetValue<string>(), "<time>"));
            default:
                return node?.DeepClone();
        }
    }

    private static string Short(string text) => text.Length <= 160 ? text : text[..160] + "…";

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex TimeRegex();
}
