using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Seeding;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Strings that identify one tenant's data: every id and canary of its snapshot plus its
/// long, tenant-only text values. Used to judge responses sent to the other tenant.</summary>
public sealed class MarkerSet
{
    private readonly TenantSnapshot _snapshot;
    private readonly IReadOnlyList<string> _markers;

    /// <param name="notOwn">Text that does not identify this tenant although it now holds it:
    /// the other tenant's values (and anything carrying its canary) that the attack stored here.</param>
    public MarkerSet(TenantSnapshot snapshot, VictimValues values, IEnumerable<string>? notOwn = null, string? otherCanary = null)
    {
        _snapshot = snapshot;
        var excluded = (notOwn ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _markers = values.Markers
            .Where(m => !excluded.Contains(m) && (otherCanary is null || !m.Contains(otherCanary, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public int Count => _snapshot.Markers.Count + _markers.Count;

    public string? Find(string text) =>
        _snapshot.FindMarker(text) ?? _markers.FirstOrDefault(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Tenant B using the product while tenant A attacks it. In production every tenant shares one
/// process, so data can cross tenants through process-wide state (static fields, singletons,
/// in-memory or output caches, anything keyed without the tenant) even when every query is
/// tenant-bound. An attack that only ever signs in tenant A never puts tenant B's data into that
/// state and cannot see such a leak. Tenant B's administrator (cookie and bearer token) and its
/// read-only user therefore call every endpoint the app exposes before the attack (every write
/// with valid bodies on B's own records, every read with B's own ids and values in every
/// documented parameter), touch each endpoint again right before and after tenant A attacks it,
/// keep reading in the background, concurrently, while tenant A's parameter and body attacks
/// run, and read everything once more at the end. Tenant A's responses are judged for tenant B's
/// markers by the attack; every response tenant B receives is judged here for tenant A's markers.
/// </summary>
public sealed class TenantActivity
{
    private readonly ErpTestEnvironment _env;
    private readonly SeedTenant _tenant;
    private readonly IReadOnlyList<ApiEndpoint> _endpoints;
    private readonly OpenApiDocument _openApi;
    private readonly List<Actor> _actors = [];
    private readonly Dictionary<string, List<string>> _created = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private MarkerSet? _forbidden;
    private int _requests;
    private int _reverseChecks;
    private int _successfulReads;
    private int _counter;

    private TenantActivity(ErpTestEnvironment env, SeedTenant tenant, IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi)
    {
        _env = env;
        _tenant = tenant;
        _endpoints = endpoints;
        _openApi = openApi;
    }

    /// <summary>Responses to this tenant that contained the other tenant's markers.</summary>
    public List<string> Leaks { get; } = [];

    /// <summary>Requests this tenant sent.</summary>
    public int Requests => _requests;

    /// <summary>Writes this tenant made to its own records that succeeded (2xx).</summary>
    public int SuccessfulWrites { get; private set; }

    /// <summary>Endpoints that change data this tenant called with its own records.</summary>
    public int WriteEndpoints { get; private set; }

    /// <summary>Responses to this tenant judged for the other tenant's markers.</summary>
    public int ReverseChecks => _reverseChecks;

    /// <summary>Requests sent by the background reader while tenant A attacked.</summary>
    public int ConcurrentRequests { get; private set; }

    /// <summary>Reasons the activity may have been blind (an actor signed out, nothing answered).</summary>
    public List<string> BlindSpots { get; } = [];

    public static async Task<TenantActivity> StartAsync(ErpTestEnvironment env, SeedTenant tenant, IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi)
    {
        var activity = new TenantActivity(env, tenant, endpoints, openApi);
        activity._actors.Add(new Actor($"tenant {tenant.Code} administrator (cookie)", await env.SignInAsync(env.Email(tenant, "admin"))));
        activity._actors.Add(new Actor($"tenant {tenant.Code} administrator (bearer)", await env.SignInWithTokenAsync(env.Email(tenant, "admin"))));
        activity._actors.Add(new Actor($"tenant {tenant.Code} read-only user (cookie)", await env.SignInAsync(env.Email(tenant, "viewer"))));
        return activity;
    }

    /// <summary>The other tenant's markers, which no response to this tenant may contain.</summary>
    public void Watch(MarkerSet other) => _forbidden = other;

    private Actor Admin => _actors[0];

    /// <summary>
    /// Every endpoint that changes data, called by this tenant's administrator on its own records
    /// with a body that passes validation: creates first (their new ids become the targets of the
    /// updates and deletes), then updates as an edit-and-save round trip of the record's own GET,
    /// then deletes of what this run created. Signing in and out is left out (the actors stay
    /// signed in for the whole attack).
    /// </summary>
    public async Task WriteAsync(TenantSnapshot own)
    {
        // Catch-all routes only answer 404; signing in and out would end the actors' sessions.
        var writes = _endpoints.Where(e => e.Method != "GET" && e.Method != "HEAD" && e.Name is not ("auth.signIn" or "auth.signOut") &&
                                           !e.Pattern.Contains("{*", StringComparison.Ordinal)).ToList();
        WriteEndpoints = writes.Count;
        foreach (var endpoint in writes.Where(e => e.Method == "POST"))
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: true);
            var body = BuildBody(endpoint, own, template: null);
            var (status, text, location) = await SendAsync(Admin, endpoint.Method, path, body, $"{endpoint.Method} {path} [own create]");
            NoteWrite(endpoint, status, text);
            if (status is >= 200 and < 300 && CreatedId(text, location) is { } id)
            {
                var collection = endpoint.Pattern.TrimEnd('/');
                lock (_lock)
                {
                    if (!_created.TryGetValue(collection, out var ids)) _created[collection] = ids = [];
                    ids.Add(id);
                }
            }
        }
        foreach (var endpoint in writes.Where(e => e.Method is "PUT" or "PATCH"))
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: true);
            JsonNode? template = null;
            if (_endpoints.Any(e => e.Method == "GET" && e.Pattern == endpoint.Pattern))
            {
                var (getStatus, getText, _) = await SendAsync(Admin, "GET", path, null, $"GET {path} [own read before save]");
                if (getStatus == 200)
                {
                    try { template = JsonNode.Parse(getText); } catch (JsonException) { }
                }
            }
            var (status, text, _) = await SendAsync(Admin, endpoint.Method, path, BuildBody(endpoint, own, template), $"{endpoint.Method} {path} [own save]");
            NoteWrite(endpoint, status, text);
        }
        foreach (var endpoint in writes.Where(e => e.Method == "DELETE"))
        {
            var collection = CollectionOf(endpoint.Pattern);
            List<string> targets;
            lock (_lock) targets = _created.TryGetValue(collection, out var ids) && ids.Count > 0 ? [ids[^1]] : [];
            var path = endpoint.RouteParameters.Count == 0 ? endpoint.Path(_ => "") : targets.Count > 0 ? endpoint.Path(_ => targets[0]) : null;
            if (path is null)
            {
                // Nothing this run created to delete: still run the handler on a record that does not exist.
                path = endpoint.Path(_ => Guid.NewGuid().ToString());
            }
            var (status, text, _) = await SendAsync(Admin, "DELETE", path, null, $"DELETE {path} [own delete]");
            NoteWrite(endpoint, status, text);
        }
    }

    /// <summary>Writes whose handler did not succeed for this tenant's own records (the write
    /// then never reached the code that could fill a cache).</summary>
    public List<string> UnsuccessfulWrites { get; } = [];

    private void NoteWrite(ApiEndpoint endpoint, int status, string text)
    {
        if (status is < 200 or >= 300)
        {
            lock (_lock) UnsuccessfulWrites.Add($"{endpoint.Key} → {status} {text[..Math.Min(200, text.Length)]}");
        }
    }

    /// <summary>Every GET, by every actor, on this tenant's own records; with <paramref name="values"/>
    /// also every one of this tenant's values in every documented query parameter (administrator),
    /// so a cache keyed by a search term without the tenant holds this tenant's answers.</summary>
    public async Task ReadAsync(TenantSnapshot own, VictimValues? values, string phase)
    {
        var reads = _endpoints.Where(e => e.Method == "GET").ToList();
        var successBefore = _successfulReads;
        foreach (var endpoint in reads)
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: false);
            foreach (var actor in _actors)
            {
                await SendAsync(actor, "GET", path, null, $"GET {path} [{phase}]");
            }
        }
        if (values is not null)
        {
            var work = new List<(string Path, string Label)>();
            foreach (var endpoint in reads.Where(e => !e.Pattern.Contains("{*", StringComparison.Ordinal)))
            {
                var basePath = await OwnPathAsync(endpoint, own, forWrite: false);
                foreach (var parameter in _openApi.Parameters("GET", endpoint.Pattern).Where(p => p.In == "query"))
                {
                    var parameterValues = parameter switch
                    {
                        { Format: "uuid" } => values.IdSample.Select(i => i.ToString()),
                        { Type: "string" } => values.All,
                        _ => [],
                    };
                    foreach (var value in parameterValues)
                    {
                        var uri = basePath + "?" + Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(value);
                        work.Add((uri, $"GET {basePath} [{phase}, own value in query {parameter.Name}]"));
                    }
                }
            }
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (item, _) => await SendAsync(Admin, "GET", item.Path, null, item.Label));
        }
        if (_successfulReads == successBefore)
        {
            lock (_lock) BlindSpots.Add($"{phase}: no read by tenant {_tenant.Code} succeeded");
        }
        foreach (var actor in _actors)
        {
            var (status, text, _) = await SendAsync(actor, "GET", "/api/auth/session", null, $"GET /api/auth/session [{phase}, still signed in]");
            if (status != 200 || !text.Contains("\"authenticated\":true", StringComparison.Ordinal))
            {
                lock (_lock) BlindSpots.Add($"{phase}: {actor.Name} is no longer signed in ({status})");
            }
        }
    }

    /// <summary>The administrator reads the endpoint (GET only) on this tenant's own records,
    /// right before or after tenant A attacks it.</summary>
    public async Task TouchAsync(ApiEndpoint endpoint, TenantSnapshot own, string phase)
    {
        if (endpoint.Method != "GET")
        {
            return;
        }
        var path = await OwnPathAsync(endpoint, own, forWrite: false);
        await SendAsync(Admin, "GET", path, null, $"GET {path} [{phase}]");
    }

    /// <summary>A quick round of every GET by the administrator (after tenant A wrote, so a write
    /// that poisoned a process-wide cache is read back by tenant B).</summary>
    public async Task ReadRoundAsync(TenantSnapshot own, string phase)
    {
        foreach (var endpoint in _endpoints.Where(e => e.Method == "GET"))
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: false);
            await SendAsync(Admin, "GET", path, null, $"GET {path} [{phase}]");
        }
    }

    /// <summary>Reads every GET, by every actor, over and over until cancelled: tenant B and
    /// tenant A in the process at the same moment.</summary>
    public async Task RunConcurrentlyAsync(TenantSnapshot own, CancellationToken cancellationToken)
    {
        var reads = _endpoints.Where(e => e.Method == "GET").ToList();
        var sent = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var endpoint in reads)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var path = await OwnPathAsync(endpoint, own, forWrite: false);
                foreach (var actor in _actors)
                {
                    await SendAsync(actor, "GET", path, null, $"GET {path} [concurrent with the attack]");
                    sent++;
                }
            }
        }
        ConcurrentRequests += sent;
        if (sent == 0)
        {
            lock (_lock) BlindSpots.Add("the concurrent reader sent nothing while tenant A attacked");
        }
    }

    private async Task<(int Status, string Text, string Location)> SendAsync(Actor actor, string method, string path, JsonNode? body, string label)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await actor.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var status = (int)response.StatusCode;
        var location = response.Headers.Location?.ToString() ?? "";
        Interlocked.Increment(ref _requests);
        if (method == "GET" && status is >= 200 and < 300)
        {
            Interlocked.Increment(ref _successfulReads);
        }
        if (method != "GET" && status is >= 200 and < 300)
        {
            lock (_lock) SuccessfulWrites++;
        }
        if (_forbidden is { } forbidden)
        {
            Interlocked.Increment(ref _reverseChecks);
            if ((forbidden.Find(text) ?? forbidden.Find(location)) is { } marker)
            {
                lock (_lock) Leaks.Add($"{actor.Name} → {label} → {status}: response to tenant {_tenant.Code} contains the attacking tenant's marker {marker}");
            }
        }
        return (status, text, location);
    }

    /// <summary>The path with this tenant's own id in every route parameter: a record this run
    /// created in the same collection, else the first own id (tables named like the route first)
    /// that the administrator can read. Writes never target the actors' own user records.</summary>
    private async Task<string> OwnPathAsync(ApiEndpoint endpoint, TenantSnapshot own, bool forWrite)
    {
        if (endpoint.RouteParameters.Count == 0 || endpoint.Pattern.Contains("{*", StringComparison.Ordinal))
        {
            return endpoint.Path(_ => "");
        }
        var key = $"{CollectionOf(endpoint.Pattern)}|{forWrite}";
        lock (_lock)
        {
            if (_routes.TryGetValue(key, out var known)) return endpoint.Path(_ => known);
        }
        var candidates = new List<string>();
        lock (_lock)
        {
            if (_created.TryGetValue(CollectionOf(endpoint.Pattern), out var created)) candidates.AddRange(created);
        }
        var segment = Singular(CollectionOf(endpoint.Pattern).Split('/').Last());
        var excluded = forWrite ? await ActorUserIdsAsync() : [];
        candidates.AddRange(own.IdsByTable
            .OrderByDescending(t => t.Key.Contains(segment, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => t.Value.Take(5)).Select(i => i.ToString()).Where(i => !excluded.Contains(i)));
        candidates.Add(own.TenantId.ToString());
        foreach (var candidate in candidates.Distinct())
        {
            var path = endpoint.Path(_ => candidate);
            var readPath = _endpoints.Any(e => e.Method == "GET" && e.Pattern == endpoint.Pattern) ? path : null;
            if (readPath is null)
            {
                lock (_lock) _routes[key] = candidate;
                return path;
            }
            using var probe = new HttpRequestMessage(HttpMethod.Get, readPath);
            using var response = await Admin.Client.SendAsync(probe);
            Interlocked.Increment(ref _requests);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock) _routes[key] = candidate;
                return path;
            }
        }
        return endpoint.Path(_ => candidates[0]);
    }

    private List<string>? _actorUserIds;

    private async Task<List<string>> ActorUserIdsAsync()
    {
        if (_actorUserIds is not null) return _actorUserIds;
        await using var admin = await _env.OpenAdminAsync();
        _actorUserIds = (await DbCatalog.ReadAsync(admin,
                "SELECT id FROM identity.users WHERE tenant_id = @t AND lower(email) = ANY(@e)", r => r.GetGuid(0),
                ("t", _tenant.Id), ("e", new[] { _env.Email(_tenant, "admin"), _env.Email(_tenant, "viewer") })))
            .Select(i => i.ToString()).ToList();
        return _actorUserIds;
    }

    /// <summary>A body that passes validation: fields copied from the record's own GET when there
    /// is one (an edit and save), otherwise fresh values that carry this tenant's canary or e-mail
    /// domain, so anything they leave in process-wide state is recognisable.</summary>
    private JsonNode? BuildBody(ApiEndpoint endpoint, TenantSnapshot own, JsonNode? template)
    {
        if (!endpoint.HasBody)
        {
            return null;
        }
        if (_openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return new JsonObject();
        }
        var n = Interlocked.Increment(ref _counter);
        var body = _openApi.BuildBody(schema, (type, format, name) => OwnLeaf(type, format, name, own, n)) as JsonObject ?? [];
        if (template is JsonObject source)
        {
            foreach (var (field, _) in body.ToList())
            {
                var match = source.FirstOrDefault(p => string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase));
                if (match.Key is not null)
                {
                    body[field] = match.Value?.DeepClone();
                }
            }
        }
        return body;
    }

    private JsonNode? OwnLeaf(string type, string? format, string? name, TenantSnapshot own, int n)
    {
        var lower = name?.ToLowerInvariant() ?? "";
        var marker = _tenant.Canary ?? _tenant.Code;
        return type switch
        {
            "string" when format == "uuid" => OwnIdFor(lower, own),
            "string" when lower.Contains("email") => $"activity{n}@{_tenant.EmailDomain}",
            "string" when lower == "workspace" => _tenant.Code,
            "string" when lower == "password" => "Activity-Password-2026!",
            "string" when lower == "language" => "en",
            "string" when lower == "numerals" => "latn",
            "string" when lower.Contains("permission") => "identity.users.read",
            "string" when format == "date-time" => DateTimeOffset.UtcNow.ToString("O"),
            "string" when lower.EndsWith("ar", StringComparison.Ordinal) => $"نشاط {marker} {n}",
            "string" => $"Activity {marker} {n}",
            "integer" => 0,
            "number" => "1",
            "boolean" => true,
            _ => null,
        };
    }

    /// <summary>An own id for a field such as <c>roleIds</c>: the first id of the table named
    /// like the field (roles), else the tenant id.</summary>
    private static string OwnIdFor(string field, TenantSnapshot own)
    {
        var stem = field.EndsWith("ids", StringComparison.Ordinal) ? field[..^3] : field.EndsWith("id", StringComparison.Ordinal) ? field[..^2] : field;
        if (stem.Length > 0)
        {
            foreach (var (table, ids) in own.IdsByTable)
            {
                var tableName = table.Split('.').Last();
                if (ids.Count > 0 && (tableName == stem + "s" || tableName == stem || tableName == stem + "es"))
                {
                    return ids[0].ToString();
                }
            }
        }
        return own.TenantId.ToString();
    }

    private static string? CreatedId(string text, string location)
    {
        try
        {
            if (JsonNode.Parse(text) is JsonObject obj && obj["id"]?.GetValue<string>() is { } id && Guid.TryParse(id, out _))
            {
                return id;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
        }
        var last = location.TrimEnd('/').Split('/').LastOrDefault();
        return Guid.TryParse(last, out _) ? last : null;
    }

    /// <summary>The collection a route belongs to: the pattern up to its first route parameter.</summary>
    private static string CollectionOf(string pattern)
    {
        var index = pattern.IndexOf('{', StringComparison.Ordinal);
        return (index < 0 ? pattern : pattern[..index]).TrimEnd('/');
    }

    private static string Singular(string segment) =>
        segment.EndsWith("ies", StringComparison.Ordinal) ? segment[..^3] + "y" : segment.EndsWith('s') ? segment[..^1] : segment;

    public void Dispose()
    {
        foreach (var actor in _actors)
        {
            actor.Client.Dispose();
        }
    }

    private sealed record Actor(string Name, HttpClient Client);
}
