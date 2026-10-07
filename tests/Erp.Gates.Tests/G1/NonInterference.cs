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
        int Endpoints,
        int WriteComparisons = 0,
        int WriteEndpoints = 0,
        int WriteVariants = 0,
        int ArabicComparisons = 0,
        int ArabicAnswers = 0,
        int ArabicWriteComparisons = 0);

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
        var englishA = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (endpoint, uri) in uris)
        {
            var judgedA = await JudgeAsync(state, uri, "tenant A", sharedA, freshA, "tenant B", sharedB);
            var judgedB = await JudgeAsync(state, uri, "tenant B", sharedB, freshB, "tenant A", sharedA);
            englishA[uri] = judgedA;
            state.Endpoints.Add(endpoint.Key);
            if (judgedA is { } answerA && judgedB is { } answerB && answerA != answerB)
            {
                state.Discriminating++;
            }
        }

        // The Arabic side (critic p04 round 4): the request language is the signed-in user's, so
        // the comparisons above only ever ran the English branch of every handler. Every request
        // once more from each tenant's Arabic administrator (Arabic, Arabic-Indic digits), in the
        // shared process right after the other tenant's Arabic administrator sent it, against a
        // fresh process only the judged tenant has used. Answers in Arabic (more Arabic letters
        // than the English administrator's answer to the same request) prove the Arabic branch ran.
        var blind = new List<string>();
        using var arabicA = await ArabicSession.SignInWithTokenAsync(env, a);
        using var arabicB = await ArabicSession.SignInWithTokenAsync(env, b);
        using var freshArabicA = FreshClient(freshProcessA, arabicA);
        using var freshArabicB = FreshClient(freshProcessB, arabicB);
        foreach (var (endpoint, uri) in uris)
        {
            var before = state.Comparisons;
            var judgedA = await JudgeAsync(state, uri, "tenant A in Arabic", arabicA, freshArabicA, "tenant B in Arabic", arabicB);
            await JudgeAsync(state, uri, "tenant B in Arabic", arabicB, freshArabicB, "tenant A in Arabic", arabicA);
            state.ArabicComparisons += state.Comparisons - before;
            if (judgedA is { } arabic && englishA.GetValueOrDefault(uri) is { } english && ArabicSession.ArabicLetters(arabic) > ArabicSession.ArabicLetters(english))
            {
                state.ArabicAnswers++;
            }
        }

        // Writes: what a write answers may not depend on the other tenant's same write either,
        // for every documented value of every enumerated field of its body, and in Arabic.
        await CompareWritesAsync(env, state, allEndpoints, openApi, sharedA, sharedB, freshA, freshB, arabicA, arabicB, freshArabicA, freshArabicB);

        foreach (var (name, client) in new[] { ("tenant A's Arabic administrator", arabicA), ("tenant B's Arabic administrator", arabicB),
                     ("tenant A's Arabic administrator (fresh process)", freshArabicA), ("tenant B's Arabic administrator (fresh process)", freshArabicB) })
        {
            if (!await ArabicSession.IsArabicAsync(client))
            {
                blind.Add($"{name} no longer answers in Arabic with Arabic-Indic digits");
            }
        }
        if (state.ArabicComparisons == 0)
        {
            blind.Add("no request was compared in Arabic");
        }
        if (state.ArabicAnswers == 0)
        {
            blind.Add("no answer to an Arabic session was more Arabic than the English one, so the Arabic branch may never have run");
        }
        if (state.ArabicWriteComparisons == 0)
        {
            blind.Add("no write was compared in Arabic");
        }
        if (state.WriteComparisons == 0)
        {
            blind.Add("no write was compared");
        }
        else if (state.UnstableWrites * 4 > state.WriteComparisons)
        {
            blind.Add($"{state.UnstableWrites} of {state.WriteComparisons} writes answered differently from one call to the next in a fresh process");
        }
        if (state.Comparisons == 0)
        {
            blind.Add("no request was compared");
        }
        else if ((state.Unstable.Count - state.UnstableWrites) * 4 > state.Comparisons)
        {
            blind.Add($"{state.Unstable.Count - state.UnstableWrites} of {state.Comparisons} requests answered differently from one call to the next in a fresh process");
        }
        if (writes == 0)
        {
            blind.Add("neither tenant made a successful write before the comparison");
        }
        if (state.Discriminating == 0)
        {
            blind.Add("no request had different true answers in the two tenants, so no interference could show");
        }
        return new Result(state.Findings, state.Unstable, blind, state.Requests, state.Comparisons, state.Discriminating, state.Endpoints.Count,
            state.WriteComparisons, state.WriteEndpoints.Count, state.WriteVariants.Count, state.ArabicComparisons, state.ArabicAnswers, state.ArabicWriteComparisons);
    }

    /// <summary>
    /// Every write that acts on an existing record (PUT and PATCH, and POST on a record's route:
    /// creates and deletes answer a new record each time), with its default body and with every
    /// variant (<see cref="TenantActivity.VariantsOf"/>: each documented value of each enumerated
    /// field, so the Arabic side of a language or digits field is judged as well as the English
    /// side), compared as for reads: the judged tenant's answer in the shared process right after
    /// the other tenant made the same write with the same value, against its answer in a fresh
    /// process only it has used, written twice there so a difference between two such writes
    /// counts as unstable rather than as a finding. Both directions. Versions (a row's
    /// concurrency token, which every save changes) are left out of the comparison along with
    /// times and trace ids.
    /// </summary>
    private static async Task CompareWritesAsync(ErpTestEnvironment env, State state, IReadOnlyList<ApiEndpoint> allEndpoints, OpenApiDocument openApi,
        HttpClient sharedA, HttpClient sharedB, HttpClient freshA, HttpClient freshB,
        HttpClient arabicA, HttpClient arabicB, HttpClient freshArabicA, HttpClient freshArabicB)
    {
        var a = env.TenantA;
        var b = env.TenantB;
        var activityA = await TenantActivity.StartAsync(env, a, allEndpoints, openApi);
        var activityB = await TenantActivity.StartAsync(env, b, allEndpoints, openApi);
        try
        {
            var ownA = await TenantSnapshot.TakeAsync(env, a.Id, a.Canary, a.Code);
            var ownB = await TenantSnapshot.TakeAsync(env, b.Id, b.Canary, b.Code);
            var sides = new[]
            {
                new WriteSide("tenant A", activityA, ownA, sharedA, freshA),
                new WriteSide("tenant B", activityB, ownB, sharedB, freshB),
            };
            var arabicSides = new[]
            {
                new WriteSide("tenant A in Arabic", activityA, ownA, arabicA, freshArabicA),
                new WriteSide("tenant B in Arabic", activityB, ownB, arabicB, freshArabicB),
            };
            foreach (var endpoint in activityA.Writes.Where(ComparableWrite))
            {
                // Every variant first, every enumerated field at its first value last (it puts both
                // tenants' records back; without enumerated fields, the default body).
                foreach (var variant in activityA.VariantsOf(endpoint).Append(activityA.FirstValues(endpoint)))
                {
                    foreach (var (judged, other) in new[] { (sides[0], sides[1]), (sides[1], sides[0]) })
                    {
                        await JudgeWriteAsync(state, endpoint, variant, judged, other);
                    }
                    state.WriteEndpoints.Add(endpoint.Key);
                    if (variant is not null)
                    {
                        state.WriteVariants.Add($"{endpoint.Key}|{variant.Label}");
                    }
                }
                // The same write by both Arabic administrators, every enumerated field at its last
                // documented value (for the shell's own preferences: Arabic with Arabic-Indic
                // digits, which keeps the sessions Arabic).
                var last = activityA.LastValues(endpoint);
                foreach (var (judged, other) in new[] { (arabicSides[0], arabicSides[1]), (arabicSides[1], arabicSides[0]) })
                {
                    var before = state.WriteComparisons;
                    await JudgeWriteAsync(state, endpoint, last, judged, other);
                    state.ArabicWriteComparisons += state.WriteComparisons - before;
                }
                await ArabicSession.PrepareAsync(arabicA);
                await ArabicSession.PrepareAsync(arabicB);
                await ArabicSession.PrepareAsync(freshArabicA);
                await ArabicSession.PrepareAsync(freshArabicB);
            }
        }
        finally
        {
            activityA.Dispose();
            activityB.Dispose();
        }
    }

    private sealed record WriteSide(string Name, TenantActivity Activity, TenantSnapshot Own, HttpClient Shared, HttpClient Fresh);

    /// <summary>Writes whose true answer is the same each time the same caller makes them: every
    /// write on an existing record (creates and deletes answer a new record each time).</summary>
    internal static bool ComparableWrite(ApiEndpoint endpoint) =>
        endpoint.Method is "PUT" or "PATCH" || (endpoint.Method == "POST" && endpoint.RouteParameters.Count > 0);

    private static async Task JudgeWriteAsync(State state, ApiEndpoint endpoint, WriteVariant? variant, WriteSide judged, WriteSide other)
    {
        var label = $"{endpoint.Method} {endpoint.Pattern}{(variant is null ? "" : $" ({variant})")}";
        async Task<string> WriteAsync(WriteSide side, bool fresh)
        {
            state.Requests++;
            var (status, text) = await side.Activity.WriteThroughAsync(fresh ? side.Fresh : side.Shared, $"{side.Name} ({(fresh ? "fresh process" : "shared process")})",
                endpoint, side.Own, $"write comparison, {label}", variant);
            return $"{status} {NormalizeWrite(text)}";
        }

        await WriteAsync(judged, fresh: true); // the fresh process's state holds the judged tenant's own write
        var truth = await WriteAsync(judged, fresh: true);
        await WriteAsync(other, fresh: false);
        var answer = await WriteAsync(judged, fresh: false);
        state.WriteComparisons++;
        if (answer == truth)
        {
            return;
        }
        var truthAgain = await WriteAsync(judged, fresh: true);
        if (truthAgain != truth)
        {
            state.UnstableWrites++;
            state.Unstable.Add($"{judged.Name}: {label} answers differently from one write to the next ({Short(truth)} / {Short(truthAgain)})");
            return;
        }
        await WriteAsync(other, fresh: false);
        var answerAgain = await WriteAsync(judged, fresh: false);
        if (answerAgain == truth)
        {
            state.UnstableWrites++;
            state.Unstable.Add($"{judged.Name}: {label} differed once in the shared process ({Short(answer)}), then matched");
            return;
        }
        state.Findings.Add($"{judged.Name}: {label} is answered {Short(answerAgain)} in the shared process right after {other.Name} made the same write, " +
                           $"but {Short(truth)} by a fresh process only {judged.Name} has used");
    }

    /// <summary><see cref="Normalize"/>, and every <c>version</c> field (a row's concurrency
    /// token, which each save changes) left out.</summary>
    internal static string NormalizeWrite(string text)
    {
        try
        {
            return Scrub(DropVersions(JsonNode.Parse(text)))?.ToJsonString() ?? "";
        }
        catch (JsonException)
        {
            return TimeRegex().Replace(text, "<time>");
        }
    }

    private static JsonNode? DropVersions(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(k => string.Equals(k, "version", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    obj.Remove(key);
                }
                foreach (var (_, child) in obj.ToList())
                {
                    DropVersions(child);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    DropVersions(item);
                }
                break;
        }
        return node;
    }

    private sealed class State
    {
        public List<string> Findings { get; } = [];
        public List<string> Unstable { get; } = [];
        public HashSet<string> Endpoints { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WriteEndpoints { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WriteVariants { get; } = new(StringComparer.Ordinal);
        public int Requests;
        public int Comparisons;
        public int Discriminating;
        public int WriteComparisons;
        public int UnstableWrites;
        public int ArabicComparisons;
        public int ArabicAnswers;
        public int ArabicWriteComparisons;
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
        foreach (var language in signedIn.DefaultRequestHeaders.AcceptLanguage)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(language);
        }
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
                // Every value the document publishes for the parameter as well (each language, each
                // digit system): free text alone is refused by an enumerated parameter's validation.
                values = values.Concat(parameter.Enum ?? []);
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
