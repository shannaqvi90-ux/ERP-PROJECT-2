using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, company and branch layers: every read the attacker may legitimately make, with only its own,
/// valid parameters (critic p02 round 6, plants B1, B3 and B3b: the company profile report, the
/// branch directory report and the access screen's branch options each read branches past the branch
/// filter, and every gate passed, because the branch attack only ever sent branch Z's own ids and
/// texts, and by the time it reached the reports the attacker had stored Z's texts in records of its
/// own, so the marker check scrubbed them).
///
/// Run before the attack writes anything, so nothing the answers show can be the attacker's own data
/// copied from the victim. Every GET is called as each attacker: with no parameters; with its own
/// company (and own branch) in every documented company or branch parameter; with every record of
/// a report's lookup parameter the attacker can list; with its own company and branch in the list
/// filter of every list whose columns name a company or branch; in every export format and in
/// Arabic; lists followed page by page. Every id those answers show is then sent to every GET whose
/// route takes an id (a user's access, a role, a record's own screen). No answer may contain any of
/// the victim's markers: its ids and the texts no other row of the tenant holds, taken from the
/// database before the attack started.
/// </summary>
public static partial class OwnScopeReads
{
    public sealed record Result(IReadOnlyList<string> Leaks, IReadOnlyList<string> ServerErrors, int Endpoints, int Requests, IReadOnlyList<string> Answered);

    private const int MaxPages = 10;
    private const int MaxIdsPerRoute = 60;

    public static async Task<Result> RunAsync(ErpTestEnvironment env, OpenApiDocument openApi, IReadOnlyList<ApiEndpoint> endpoints,
        IReadOnlyList<(string Name, HttpClient Client)> attackers, Guid company, Guid? branch, CompanySnapshot victim, string victimName)
    {
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var reads = endpoints.Where(e => e.Method == "GET" && !e.Pattern.Contains("{*", StringComparison.Ordinal)).ToList();
        var leaks = new List<string>();
        var errors = new List<string>();
        var answered = new SortedSet<string>(StringComparer.Ordinal);
        var requests = 0;
        var gate = new Lock();

        foreach (var (name, client) in attackers)
        {
            // Ids each answer showed, by the collection (path) that showed them.
            var idsBy = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            async Task<(int Status, string Text, JsonElement? Json)> Get(ApiEndpoint endpoint, string path)
            {
                using var response = await client.GetAsync(path);
                var text = await ResponseText.ReadAsync(response);
                JsonElement? json = null;
                if (response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.Ordinal) == true)
                {
                    try
                    {
                        json = JsonDocument.Parse(text).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                    }
                }
                lock (gate)
                {
                    requests++;
                    if (victim.FindMarker(text) is { } marker)
                    {
                        leaks.Add($"{name} → GET {path} (own parameters only) → {(int)response.StatusCode}: contains {victimName} marker {marker}");
                    }
                    if ((int)response.StatusCode >= 500)
                    {
                        errors.Add($"{name} → GET {path} (own parameters only) → {(int)response.StatusCode}: {text[..Math.Min(200, text.Length)]}");
                    }
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        answered.Add(endpoint.Pattern);
                    }
                }
                return ((int)response.StatusCode, text, json);
            }

            // The reads without route parameters.
            foreach (var endpoint in reads.Where(e => e.RouteParameters.Count == 0))
            {
                var parameters = openApi.Parameters("GET", endpoint.Pattern).Where(p => p.In == "query").ToList();
                var names = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var take = names.Contains("take") ? "take=200" : null;
                var queries = new List<string> { "" };
                foreach (var parameter in parameters)
                {
                    foreach (var value in await OwnValuesAsync(catalog, endpoint, parameter, client, company, branch))
                    {
                        queries.Add($"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}");
                    }
                }
                if (names.Contains("filter") && ListOf(catalog, endpoint) is { } list)
                {
                    foreach (var column in list.Columns.Where(c => c.Filterable && c.Type == Erp.Kernel.Lists.ListColumnType.Reference))
                    {
                        var own = Owner(column.Key) switch { "company" => company.ToString(), "branch" => branch?.ToString(), _ => null };
                        if (own is not null)
                        {
                            queries.Add("filter=" + Uri.EscapeDataString($"{column.Key} eq '{own}'"));
                        }
                    }
                }
                var formats = parameters.FirstOrDefault(p => p.Name == "format")?.Enum ?? [];
                var groupings = parameters.FirstOrDefault(p => p.Name == "groupBy")?.Enum ?? [];
                var languages = parameters.FirstOrDefault(p => p.Name == "language")?.Enum ?? [];
                foreach (var query in queries.Distinct(StringComparer.Ordinal))
                {
                    string Path(string q) => endpoint.Pattern + (string.Join("&", new[] { q, take }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } all ? "?" + all : "");
                    var (status, _, json) = await Get(endpoint, Path(query));
                    if (status != 200)
                    {
                        continue;
                    }
                    Harvest(json, Collection(endpoint.Pattern), idsBy);
                    // Every page of a list.
                    var next = Next(json);
                    for (var page = 1; next is not null && page < MaxPages; page++)
                    {
                        var (pageStatus, _, pageJson) = await Get(endpoint, Path(string.Join("&", new[] { query, "after=" + Uri.EscapeDataString(next) }.Where(s => s.Length > 0))));
                        if (pageStatus != 200) break;
                        Harvest(pageJson, Collection(endpoint.Pattern), idsBy);
                        next = Next(pageJson);
                    }
                    // Every export format, Arabic and every grouping of the same document.
                    var variants = formats.Where(f => f != "json").Select(f => $"format={f}")
                        .Concat(languages.Where(l => l != "en").Select(l => $"language={l}"))
                        .Concat(groupings.Select(g => $"groupBy={Uri.EscapeDataString(g)}"));
                    foreach (var variant in variants)
                    {
                        await Get(endpoint, Path(string.Join("&", new[] { query, variant }.Where(s => s.Length > 0))));
                    }
                }
            }

            // The reads whose route takes ids: every id the attacker's own answers showed, those of
            // the collection the route belongs to first.
            var everyId = idsBy.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var endpoint in reads.Where(e => e.RouteParameters.Count == 1 && GuidRoute().IsMatch(e.Pattern)))
            {
                var own = idsBy.GetValueOrDefault(Collection(endpoint.Pattern)) ?? [];
                foreach (var id in own.Concat(everyId).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxIdsPerRoute).ToList())
                {
                    var (status, _, json) = await Get(endpoint, endpoint.Path(_ => id));
                    if (status == 200)
                    {
                        Harvest(json, Collection(endpoint.Pattern), idsBy);
                    }
                }
            }
        }
        return new Result(leaks, errors, reads.Count, requests, [.. answered]);
    }

    /// <summary>The attacker's own valid values for one query parameter: its company or branch in a
    /// parameter that names one, every record of the lookup list a report parameter draws from.</summary>
    private static async Task<IReadOnlyList<string>> OwnValuesAsync(ModuleCatalog catalog, ApiEndpoint endpoint, ApiParameter parameter, HttpClient client, Guid company, Guid? branch)
    {
        if (parameter.Format != "uuid" && Owner(parameter.Name) is null)
        {
            return [];
        }
        var values = new List<string>();
        switch (Owner(parameter.Name))
        {
            case "company": values.Add(company.ToString()); break;
            case "branch" when branch is { } b: values.Add(b.ToString()); break;
        }
        if (ReportOf(catalog, endpoint)?.Parameters.FirstOrDefault(p => p.Key == parameter.Name) is { Lookup: { } lookup } &&
            catalog.FindList(lookup) is { } list)
        {
            using var answer = await client.GetAsync($"{list.Endpoint}?take=50");
            if (answer.IsSuccessStatusCode)
            {
                var page = JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement;
                if (page.TryGetProperty("items", out var items))
                {
                    values.AddRange(items.EnumerateArray().Where(i => i.TryGetProperty("id", out _)).Select(i => i.GetProperty("id").GetString()!));
                }
            }
        }
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"company" or "branch" when the parameter or column names one (company, companyId, branchId…).</summary>
    private static string? Owner(string name) =>
        name.Equals("company", StringComparison.OrdinalIgnoreCase) || name.Equals("companyId", StringComparison.OrdinalIgnoreCase) ? "company"
        : name.Equals("branch", StringComparison.OrdinalIgnoreCase) || name.Equals("branchId", StringComparison.OrdinalIgnoreCase) ? "branch"
        : null;

    private static ReportDefinition? ReportOf(ModuleCatalog catalog, ApiEndpoint endpoint) =>
        endpoint.Pattern.StartsWith("/api/reports/run/", StringComparison.Ordinal) ? catalog.FindReport(endpoint.Pattern["/api/reports/run/".Length..])?.Definition : null;

    /// <summary>The list an endpoint serves or prints.</summary>
    private static Erp.Kernel.Lists.ListDefinition? ListOf(ModuleCatalog catalog, ApiEndpoint endpoint) =>
        endpoint.Pattern.StartsWith("/api/reports/lists/", StringComparison.Ordinal)
            ? catalog.FindList(endpoint.Pattern["/api/reports/lists/".Length..])
            : catalog.Lists.FirstOrDefault(l => l.Endpoint == endpoint.Pattern);

    /// <summary>The path up to the first route parameter: the collection a record route belongs to.</summary>
    private static string Collection(string pattern) => pattern.IndexOf("/{", StringComparison.Ordinal) is var at and >= 0 ? pattern[..at] : pattern;

    private static string? Next(JsonElement? json) =>
        json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;

    /// <summary>Every id (any property holding a GUID) an answer shows.</summary>
    private static void Harvest(JsonElement? json, string collection, Dictionary<string, HashSet<string>> into)
    {
        if (json is not { } root)
        {
            return;
        }
        if (!into.TryGetValue(collection, out var ids))
        {
            into[collection] = ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var i in e.EnumerateArray()) Walk(i);
                    break;
                case JsonValueKind.String when Guid.TryParse(e.GetString(), out _):
                    ids.Add(e.GetString()!);
                    break;
            }
        }
        Walk(root);
    }

    [GeneratedRegex(@"^[^{]*\{[A-Za-z_][A-Za-z0-9_]*:guid\}[^{]*$")]
    private static partial Regex GuidRoute();
}
