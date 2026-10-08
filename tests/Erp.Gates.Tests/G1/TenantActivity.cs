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
    private readonly MarkerSearch _search;

    /// <param name="notOwn">Text that does not identify this tenant although it now holds it:
    /// the other tenant's values (and anything carrying its canary) that the attack stored here.</param>
    public MarkerSet(TenantSnapshot snapshot, VictimValues values, IEnumerable<string>? notOwn = null, string? otherCanary = null)
    {
        _snapshot = snapshot;
        var excluded = (notOwn ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _markers = values.Markers
            .Where(m => !excluded.Contains(m) && (otherCanary is null || !m.Contains(otherCanary, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _search = new MarkerSearch(_markers);
    }

    public int Count => _snapshot.Markers.Count + _markers.Count;

    public string? Find(string text) => _snapshot.FindMarker(text) ?? _search.Find(text);
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
    private readonly HashSet<string> _routeValues = new(StringComparer.OrdinalIgnoreCase);
    private int _preTouches;
    private readonly Lock _lock = new();
    private MarkerSet? _forbidden;
    private int _requests;
    private int _reverseChecks;
    private int _successfulReads;
    private int _counter;

    /// <summary>Distinguishes this activity's created records from those of any other activity on
    /// the same database (upper-case letters and digits, so it fits code patterns).</summary>
    private readonly string _run = Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 2);

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

    /// <summary>Every value this tenant put into a route parameter (its own ids that answered, and
    /// the records it created). Tenant A's route attack replays each of them on every route, so a
    /// cache keyed by a route id alone (the shape of critic p03 round 1's plants T1 and T2: the
    /// access view of a user cached per user id) is read back with exactly the id tenant B filled
    /// it with, not with some other id of tenant B that was never cached.</summary>
    public IReadOnlyList<string> RouteValues
    {
        get
        {
            lock (_lock) return _routeValues.Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Requests in which this tenant opened a route with the very value tenant A was about
    /// to send in it.</summary>
    public int PreTouches => _preTouches;

    /// <summary>Reasons the activity may have been blind (an actor signed out, nothing answered).</summary>
    public List<string> BlindSpots { get; } = [];

    public static async Task<TenantActivity> StartAsync(ErpTestEnvironment env, SeedTenant tenant, IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi)
    {
        var activity = new TenantActivity(env, tenant, endpoints, openApi);
        activity._actors.Add(new Actor($"tenant {tenant.Code} administrator (cookie)", await env.SignInAsync(env.Email(tenant, "admin"))));
        activity._actors.Add(new Actor($"tenant {tenant.Code} administrator (bearer)", await env.SignInWithTokenAsync(env.Email(tenant, "admin"))));
        activity._actors.Add(new Actor($"tenant {tenant.Code} read-only user (cookie)", await env.SignInAsync(env.Email(tenant, "viewer")), "viewer"));
        // The Arabic side (critic p04 round 4): an administrator whose every request runs in
        // Arabic with Arabic-Indic digits, so whatever the Arabic branch of a handler keeps is
        // this tenant's too when the other tenant's Arabic session comes.
        activity._actors.Add(new Actor($"tenant {tenant.Code} administrator in Arabic (cookie)", await ArabicSession.SignInAsync(env, tenant), ArabicSession.Local));
        return activity;
    }

    /// <summary>The other tenant's markers, which no response to this tenant may contain.</summary>
    public void Watch(MarkerSet other) => _forbidden = other;

    private Actor Admin => _actors[0];

    private Actor Viewer => _actors[2];

    private Actor Arabic => _actors[3];

    /// <summary>This tenant's administrator in Arabic with Arabic-Indic digits (<see cref="ArabicSession"/>).</summary>
    public HttpClient ArabicClient => Arabic.Client;

    /// <summary>Requests this tenant's Arabic session sent.</summary>
    public int ArabicRequests => _arabicRequests;

    private int _arabicRequests;

    /// <summary>Puts the Arabic session back to Arabic with Arabic-Indic digits: a write of its own
    /// preferences (a body variant) may have changed them. Counted as a blind spot when the
    /// session is no longer signed in.</summary>
    public async Task EnsureArabicAsync(string phase)
    {
        if (_baseline is null)
        {
            await ArabicSession.PrepareAsync(Arabic.Client);
        }
        else
        {
            // A write of this tenant's own: framed like every other own write, so it never counts
            // as a change someone else made.
            await _tracking.WaitAsync();
            try
            {
                _changedByOthers.UnionWith(TenantSnapshot.Differences(_baseline, await SnapshotAsync()));
                await ArabicSession.PrepareAsync(Arabic.Client);
                _baseline = await SnapshotAsync();
            }
            finally
            {
                _tracking.Release();
            }
        }
        Interlocked.Increment(ref _requests);
        if (!await ArabicSession.IsArabicAsync(Arabic.Client))
        {
            lock (_lock) BlindSpots.Add($"{phase}: {Arabic.Name} no longer answers in Arabic with Arabic-Indic digits");
        }
    }

    /// <summary>Endpoints that change data and that the activity calls: every write except signing
    /// in and out (the actors stay signed in for the whole attack) and catch-all routes (they only
    /// answer 404).</summary>
    public IReadOnlyList<ApiEndpoint> Writes => _endpoints.Where(IsActivityWrite).ToList();

    private static bool IsActivityWrite(ApiEndpoint e) =>
        e.Method != "GET" && e.Method != "HEAD" && e.Name is not ("auth.signIn" or "auth.signOut") &&
        !e.Pattern.Contains("{*", StringComparison.Ordinal);

    /// <summary>This tenant's administrator, signed in since the activity started (signing in again
    /// later can meet the sign-in throttle the attack set off with this tenant's e-mail). Also handed
    /// to isolation probes so the victim can use a surface (print, export) right before the attacker does.</summary>
    public HttpClient AdminClient => Admin.Client;

    /// <summary>
    /// Every endpoint that changes data, called by this tenant's administrator on its own records
    /// with a body that passes validation: creates first (their new ids become the targets of the
    /// updates and deletes), then updates as an edit-and-save round trip of the record's own GET,
    /// then deletes of records this run created.
    /// </summary>
    public async Task WriteAsync(TenantSnapshot own)
    {
        var writes = Writes;
        WriteEndpoints = writes.Count;
        foreach (var endpoint in writes.Where(e => e.Method == "POST"))
        {
            await WriteVariantsAsync(endpoint, own, "own create");
            await WriteOneAsync(endpoint, own, "own create", variant: FirstValues(endpoint));
        }
        foreach (var endpoint in writes.Where(e => e.Method is "PUT" or "PATCH"))
        {
            await WriteVariantsAsync(endpoint, own, "own save");
            await WriteOneAsync(endpoint, own, "own save", variant: FirstValues(endpoint));
        }
        foreach (var endpoint in writes.Where(e => e.Method == "DELETE"))
        {
            await WriteOneAsync(endpoint, own, "own delete");
        }
    }

    /// <summary>The write once with every variant of its body (<see cref="VariantsOf"/>), so
    /// whatever the code behind each documented value leaves in process-wide state is this
    /// tenant's. The caller writes <see cref="FirstValues"/> after, which puts every enumerated
    /// field back to its first value.</summary>
    private async Task WriteVariantsAsync(ApiEndpoint endpoint, TenantSnapshot own, string phase)
    {
        foreach (var variant in VariantsOf(endpoint))
        {
            await WriteOneAsync(endpoint, own, $"{phase}, {variant}", variant: variant);
        }
    }

    /// <summary>
    /// Every variant of the endpoint's body beyond its default: each documented value of each
    /// enumerated field alone (the other fields as in the default body), and, when the body has
    /// more than one enumerated field, every field at its n-th value together (Arabic language
    /// with Arabic-Indic digits). A body built from documented values only ever sends the first
    /// value, so the code behind every other value (the Arabic side of the shell, critic p04
    /// round 4, plant L1) was never run by either tenant and a leak there passed every gate.
    /// </summary>
    public IReadOnlyList<WriteVariant> VariantsOf(ApiEndpoint endpoint)
    {
        if (!endpoint.HasBody || _openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return [];
        }
        var leaves = _openApi.EnumLeaves(schema);
        var variants = new List<WriteVariant>();
        foreach (var leaf in leaves)
        {
            foreach (var value in leaf.Values)
            {
                variants.Add(new WriteVariant($"{leaf.Name}={value.ToJsonString()}", [(leaf, value)], leaf, value));
            }
        }
        if (leaves.Count > 1)
        {
            for (var i = 0; i < leaves.Max(l => l.Values.Count); i++)
            {
                var settings = leaves.Select(l => (l, l.Values[Math.Min(i, l.Values.Count - 1)])).ToList();
                variants.Add(new WriteVariant(string.Join(" ", settings.Select(s => $"{s.l.Name}={s.Item2.ToJsonString()}")), settings, null, null));
            }
        }
        return variants;
    }

    /// <summary>
    /// The variant with every enumerated field at its first documented value (null when the body
    /// has none): the default body, and also what an edit and save must send to put a record back
    /// after the variants, since its other fields are copied from the record as the variants left it.
    /// </summary>
    public WriteVariant? FirstValues(ApiEndpoint endpoint)
    {
        if (!endpoint.HasBody || _openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return null;
        }
        var leaves = _openApi.EnumLeaves(schema);
        return leaves.Count == 0
            ? null
            : new WriteVariant(string.Join(" ", leaves.Select(l => $"{l.Name}={l.Values[0].ToJsonString()}")), leaves.Select(l => (l, l.Values[0])).ToList(), null, null);
    }

    /// <summary>The variant with every enumerated field at its last documented value (null when the
    /// body has none). For the shell's own preferences that is Arabic with Arabic-Indic digits, so
    /// an Arabic session that writes it stays Arabic.</summary>
    public WriteVariant? LastValues(ApiEndpoint endpoint)
    {
        if (!endpoint.HasBody || _openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return null;
        }
        var leaves = _openApi.EnumLeaves(schema);
        return leaves.Count == 0
            ? null
            : new WriteVariant(string.Join(" ", leaves.Select(l => $"{l.Name}={l.Values[^1].ToJsonString()}")), leaves.Select(l => (l, l.Values[^1])).ToList(), null, null);
    }

    /// <summary>Variants (endpoint key and label) whose settings a body held when it was sent.</summary>
    public IReadOnlySet<string> AppliedVariants
    {
        get
        {
            lock (_lock) return _appliedVariants.ToHashSet(StringComparer.Ordinal);
        }
    }

    private readonly HashSet<string> _appliedVariants = new(StringComparer.Ordinal);

    /// <summary>
    /// One valid write by this tenant on its own records, by the administrator (cookie) or, with
    /// <paramref name="bearer"/>, by the administrator's bearer token: a create; an edit and save
    /// of the record's own GET; or the delete of a record created for it just before (so a delete
    /// can be repeated as often as the attack needs). Returns the status; a write that does not
    /// succeed is recorded in <see cref="UnsuccessfulWrites"/>.
    /// </summary>
    public async Task<int> WriteOneAsync(ApiEndpoint endpoint, TenantSnapshot own, string phase, bool bearer = false, WriteVariant? variant = null, bool arabic = false)
    {
        var via = arabic ? Arabic : null;
        if (_baseline is null)
        {
            return (await WriteCoreAsync(endpoint, own, phase, bearer, variant, via)).Status;
        }
        // Changes since this tenant's last own write were made by someone else.
        await _tracking.WaitAsync();
        try
        {
            var before = await SnapshotAsync();
            _changedByOthers.UnionWith(TenantSnapshot.Differences(_baseline, before));
            var (status, _) = await WriteCoreAsync(endpoint, own, phase, bearer, variant, via);
            _baseline = await SnapshotAsync();
            return status;
        }
        finally
        {
            _tracking.Release();
        }
    }

    private TenantSnapshot? _baseline;
    private readonly SortedSet<string> _changedByOthers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tracking = new(1, 1);

    private Task<TenantSnapshot> SnapshotAsync() => TenantSnapshot.TakeAsync(_env, _tenant.Id, _tenant.Canary, _tenant.Code);

    /// <summary>
    /// From now on this tenant's own writes are told apart from changes anyone else makes to its
    /// rows: each own write is framed by snapshots, so what changed between two own writes (and
    /// since <paramref name="baseline"/>) was changed by someone else. Writes must not run
    /// concurrently with another tenant's writes while tracking (the phases that write run in turn).
    /// </summary>
    public void TrackChangesFrom(TenantSnapshot baseline) => _baseline = baseline;

    /// <summary>Tables of this tenant whose rows someone other than this activity changed since
    /// tracking started.</summary>
    public async Task<IReadOnlyList<string>> ChangedByOthersAsync()
    {
        if (_baseline is null)
        {
            throw new InvalidOperationException("Change tracking was not started.");
        }
        _changedByOthers.UnionWith(TenantSnapshot.Differences(_baseline, await SnapshotAsync()));
        return _changedByOthers.ToList();
    }

    /// <summary>
    /// The same valid write (same record, same body rules, same variant), sent through
    /// <paramref name="client"/>: a client signed in as this tenant's administrator, possibly to
    /// another app process. Returns the status and the answer's body. Used to compare what the
    /// write answers in a process the other tenant also uses with what it answers in a process
    /// only this tenant has used (<see cref="NonInterference"/>).
    /// </summary>
    public async Task<(int Status, string Text)> WriteThroughAsync(HttpClient client, string clientName, ApiEndpoint endpoint, TenantSnapshot own, string phase, WriteVariant? variant,
        string owner = "admin")
    {
        if (_baseline is not null)
        {
            throw new InvalidOperationException("Writes through another client are not framed by change tracking.");
        }
        return await WriteCoreAsync(endpoint, own, phase, bearer: false, variant, new Actor(clientName, client, owner));
    }

    private async Task<(int Status, string Text)> WriteCoreAsync(ApiEndpoint endpoint, TenantSnapshot own, string phase, bool bearer, WriteVariant? variant, Actor? via)
    {
        var actor = via ?? (bearer ? _actors[1] : Admin);
        switch (endpoint.Method)
        {
            case "POST":
            {
                var path = await OwnPathAsync(endpoint, own, forWrite: true, actor);
                var (status, text, location) = await SendAsync(actor, "POST", path, BuildBody(endpoint, own, template: null, variant), $"POST {path} [{phase}]");
                NoteWrite(endpoint, status, text);
                if (status is >= 200 and < 300 && CreatedId(text, location) is { } id)
                {
                    var collection = CreatedKey(endpoint.Pattern.TrimEnd('/'), actor.Owner);
                    lock (_lock)
                    {
                        if (!_created.TryGetValue(collection, out var ids)) _created[collection] = ids = [];
                        ids.Add(id);
                        // A record this tenant created is a route value of its own activity: the
                        // attack replays it (a per-id cache holds exactly such ids).
                        _routeValues.Add(id);
                    }
                }
                return (status, text);
            }
            case "PUT" or "PATCH":
            {
                var path = await OwnPathAsync(endpoint, own, forWrite: true, actor);
                JsonNode? template = null;
                if (_endpoints.Any(e => e.Method == "GET" && e.Pattern == endpoint.Pattern))
                {
                    var (getStatus, getText, _) = await SendAsync(actor, "GET", path, null, $"GET {path} [{phase}, own read before save]");
                    if (getStatus == 200)
                    {
                        try { template = JsonNode.Parse(getText); } catch (JsonException) { }
                    }
                }
                var (status, text, _) = await SendAsync(actor, endpoint.Method, path, BuildBody(endpoint, own, template, variant), $"{endpoint.Method} {path} [{phase}]");
                NoteWrite(endpoint, status, text);
                return (status, text);
            }
            default:
            {
                string path;
                if (endpoint.RouteParameters.Count == 0)
                {
                    path = endpoint.Path(_ => "");
                }
                else
                {
                    var collection = CollectionOf(endpoint.Pattern);
                    var target = await FreshRecordAsync(collection, own, phase, actor);
                    // Nothing could be created to delete: still run the handler on a record that does not exist.
                    path = endpoint.Path(_ => target ?? Guid.NewGuid().ToString());
                    if (target is not null)
                    {
                        lock (_lock)
                        {
                            if (_created.TryGetValue(CreatedKey(collection, actor.Owner), out var ids)) ids.Remove(target);
                            foreach (var key in _routes.Where(r => r.Value == target).Select(r => r.Key).ToList()) _routes.Remove(key);
                        }
                    }
                }
                var (status, text, _) = await SendAsync(actor, endpoint.Method, path, null, $"{endpoint.Method} {path} [{phase}]");
                NoteWrite(endpoint, status, text);
                return (status, text);
            }
        }
    }

    /// <summary>A record of the collection created now by <paramref name="creator"/> (so it is
    /// theirs, even when only its owner may see it), only to be deleted: never one that another
    /// write targets.</summary>
    private async Task<string?> FreshRecordAsync(string collection, TenantSnapshot own, string phase, Actor creator)
    {
        var create = _endpoints.FirstOrDefault(e => e.Method == "POST" && e.RouteParameters.Count == 0 && e.Pattern.TrimEnd('/') == collection);
        if (create is null)
        {
            return null;
        }
        var path = create.Path(_ => "");
        var (status, text, location) = await SendAsync(creator, "POST", path, BuildBody(create, own, template: null), $"POST {path} [{phase}, record to delete]");
        return status is >= 200 and < 300 ? CreatedId(text, location) : null;
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
            var work = new List<(Actor Actor, string Path, string Label)>();
            foreach (var endpoint in reads.Where(e => !e.Pattern.Contains("{*", StringComparison.Ordinal)))
            {
                var basePath = await OwnPathAsync(endpoint, own, forWrite: false);
                foreach (var parameter in _openApi.Parameters("GET", endpoint.Pattern).Where(p => p.In == "query"))
                {
                    var parameterValues = (parameter switch
                    {
                        { Format: "uuid" } => values.IdSample.Select(i => i.ToString()),
                        { Type: "string" } => values.All,
                        _ => [],
                    }).Concat(parameter.Enum ?? []);
                    foreach (var value in parameterValues)
                    {
                        var uri = basePath + "?" + Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(value);
                        work.Add((Admin, uri, $"GET {basePath} [{phase}, own value in query {parameter.Name}]"));
                    }
                    // The Arabic session sends the values the attack's Arabic session sends
                    // (ArabicValuesFor), so what the Arabic branch keeps per value is this tenant's.
                    foreach (var value in ArabicValuesFor(parameter, values))
                    {
                        var uri = basePath + "?" + Uri.EscapeDataString(parameter.Name) + "=" + Uri.EscapeDataString(value);
                        work.Add((Arabic, uri, $"GET {basePath} [{phase}, in Arabic, own value in query {parameter.Name}]"));
                    }
                }
            }
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests },
                async (item, _) => await SendAsync(item.Actor, "GET", item.Path, null, item.Label));
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
            else if (actor == Arabic && !ArabicSession.IsArabicSessionText(text))
            {
                lock (_lock) BlindSpots.Add($"{phase}: {actor.Name} no longer answers in Arabic with Arabic-Indic digits");
            }
        }
    }

    /// <summary>Values an Arabic session sends in one query parameter: sampled ids for a uuid, the
    /// small cross-section (<see cref="VictimValues.Probe"/>) for text, and every published value.
    /// The English sessions send every value; the Arabic ones a cross-section of them, which
    /// reaches every Arabic branch of every handler with the tenant's values at a fraction of the
    /// cost.</summary>
    /// <param name="published">With every value the document publishes for the parameter (a
    /// tenant's own reads); the attack leaves them out, as they are no tenant's values.</param>
    public static IEnumerable<string> ArabicValuesFor(ApiParameter parameter, VictimValues values, bool published = true) => (parameter switch
    {
        { Format: "uuid" } => values.IdSample.Select(i => i.ToString()),
        { Type: "string" } => values.Probe,
        _ => [],
    }).Concat(published ? parameter.Enum ?? [] : []).Distinct(StringComparer.Ordinal);

    /// <summary>The administrator uses the endpoint on this tenant's own records right before or
    /// after tenant A attacks it: a read for a GET, a valid write for an endpoint that changes data
    /// (so whatever a write leaves in process-wide state is tenant B's when tenant A's write runs).</summary>
    public async Task TouchAsync(ApiEndpoint endpoint, TenantSnapshot own, string phase)
    {
        if (endpoint.Method == "GET")
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: false);
            await SendAsync(Admin, "GET", path, null, $"GET {path} [{phase}]");
            await SendAsync(Arabic, "GET", path, null, $"GET {path} [{phase}, in Arabic]");
        }
        else if (IsActivityWrite(endpoint))
        {
            await WriteOneAsync(endpoint, own, phase);
        }
    }

    /// <summary>
    /// Before tenant A attacks a route, tenant B's administrator and read-only user open it (GET)
    /// with every value tenant A is about to put in its route parameters: whatever a handler keeps
    /// per id (a closure, a static or a singleton cache keyed without the tenant) then holds tenant
    /// B's answer for each id tenant A sends, so a leak through it cannot hide behind the choice of
    /// ids. Only GET routes with parameters; writes are left to <see cref="WriteAsync"/>.
    /// </summary>
    public async Task TouchEveryAsync(ApiEndpoint endpoint, IEnumerable<string> values, string phase)
    {
        if (endpoint.Method != "GET" || endpoint.RouteParameters.Count == 0 || endpoint.Pattern.Contains("{*", StringComparison.Ordinal))
        {
            return;
        }
        var work = new List<(Actor Actor, string Path)>();
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = endpoint.Path(_ => value);
            work.Add((Admin, path));
            work.Add((Viewer, path));
            work.Add((Arabic, path));
        }
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests }, async (item, _) =>
        {
            await SendAsync(item.Actor, "GET", item.Path, null, $"GET {item.Path} [{phase}]");
            Interlocked.Increment(ref _preTouches);
        });
    }

    /// <summary>The endpoint's path with this tenant's own ids in its route parameters.</summary>
    public Task<string> OwnPathForAsync(ApiEndpoint endpoint, TenantSnapshot own) => OwnPathAsync(endpoint, own, forWrite: false);

    /// <summary>One GET of this tenant's own path (with its query) by the administrator, or by
    /// every actor; each answer is judged for the other tenant's markers like every other answer.
    /// Returns the administrator's status.</summary>
    public async Task<int> ReadPathAsync(string path, string phase, bool everyActor = false)
    {
        var (status, _, _) = await SendAsync(Admin, "GET", path, null, $"GET {path} [{phase}]");
        if (everyActor)
        {
            foreach (var actor in _actors.Skip(1))
            {
                await SendAsync(actor, "GET", path, null, $"GET {path} [{phase}]");
            }
        }
        return status;
    }

    /// <summary>Records a reason this tenant's activity may have been blind.</summary>
    public void NoteBlindSpot(string reason)
    {
        lock (_lock) BlindSpots.Add(reason);
    }

    /// <summary>A quick round of every GET by the administrator (after tenant A wrote, so a write
    /// that poisoned a process-wide cache is read back by tenant B).</summary>
    public async Task ReadRoundAsync(TenantSnapshot own, string phase)
    {
        foreach (var endpoint in _endpoints.Where(e => e.Method == "GET"))
        {
            var path = await OwnPathAsync(endpoint, own, forWrite: false);
            await SendAsync(Admin, "GET", path, null, $"GET {path} [{phase}]");
            await SendAsync(Arabic, "GET", path, null, $"GET {path} [{phase}, in Arabic]");
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
        var text = await Infrastructure.ResponseText.ReadAsync(response);
        var status = (int)response.StatusCode;
        var location = response.Headers.Location?.ToString() ?? "";
        var headers = ResponseHeaders.Text(response);
        Interlocked.Increment(ref _requests);
        if (actor == Arabic)
        {
            Interlocked.Increment(ref _arabicRequests);
        }
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
            if (forbidden.Find(text) is { } marker)
            {
                lock (_lock) Leaks.Add($"{actor.Name} → {label} → {status}: response to tenant {_tenant.Code} contains the attacking tenant's marker {marker}");
            }
            else if (forbidden.Find(headers) is { } headerMarker)
            {
                var line = headers.Split('\n').FirstOrDefault(h => forbidden.Find(h) is not null) ?? "";
                lock (_lock) Leaks.Add($"{actor.Name} → {label} → {status}: a response header to tenant {_tenant.Code} contains the attacking tenant's marker {headerMarker} ({line})");
            }
        }
        return (status, text, location);
    }

    /// <summary>The path with this tenant's own id in every route parameter: a record this run
    /// created in the same collection, else the first own id (tables named like the route first)
    /// that the administrator can read. Writes never target the actors' own user records (nor the
    /// role-less user the attack signs in).</summary>
    private async Task<string> OwnPathAsync(ApiEndpoint endpoint, TenantSnapshot own, bool forWrite, Actor? actor = null)
    {
        if (endpoint.RouteParameters.Count == 0 || endpoint.Pattern.Contains("{*", StringComparison.Ordinal))
        {
            return endpoint.Path(_ => "");
        }
        // Another user than the administrator (the Arabic administrator) writes the records it
        // created itself, creating one first: some records only their owner may see or change.
        var owner = actor?.Owner ?? "admin";
        var prober = actor ?? Admin;
        if (owner != "admin" && forWrite)
        {
            var ownedKey = CreatedKey(CollectionOf(endpoint.Pattern), owner);
            bool hasOwn;
            lock (_lock) hasOwn = _created.TryGetValue(ownedKey, out var mine) && mine.Count > 0;
            if (!hasOwn && await FreshRecordAsync(CollectionOf(endpoint.Pattern), own, "a record of its own to write", prober) is { } fresh)
            {
                lock (_lock)
                {
                    if (!_created.TryGetValue(ownedKey, out var ids)) _created[ownedKey] = ids = [];
                    ids.Add(fresh);
                    _routeValues.Add(fresh);
                }
            }
        }
        var key = owner == "admin" ? $"{CollectionOf(endpoint.Pattern)}|{forWrite}" : $"{CollectionOf(endpoint.Pattern)}|{forWrite}|{owner}";
        lock (_lock)
        {
            if (_routes.TryGetValue(key, out var known))
            {
                _routeValues.Add(known);
                return endpoint.Path(_ => known);
            }
        }
        var candidates = new List<string>();
        lock (_lock)
        {
            if (_created.TryGetValue(CreatedKey(CollectionOf(endpoint.Pattern), owner), out var created)) candidates.AddRange(created);
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
                lock (_lock)
                {
                    _routes[key] = candidate;
                    _routeValues.Add(candidate);
                }
                return path;
            }
            using var probe = new HttpRequestMessage(HttpMethod.Get, readPath);
            using var response = await prober.Client.SendAsync(probe);
            Interlocked.Increment(ref _requests);
            if (response.IsSuccessStatusCode)
            {
                lock (_lock)
                {
                    _routes[key] = candidate;
                    _routeValues.Add(candidate);
                }
                return path;
            }
        }
        lock (_lock) _routeValues.Add(candidates[0]);
        return endpoint.Path(_ => candidates[0]);
    }

    private List<string>? _actorUserIds;

    private async Task<List<string>> ActorUserIdsAsync()
    {
        if (_actorUserIds is not null) return _actorUserIds;
        await using var admin = await _env.OpenAdminAsync();
        _actorUserIds = (await DbCatalog.ReadAsync(admin,
                "SELECT id FROM identity.users WHERE tenant_id = @t AND lower(email) = ANY(@e)", r => r.GetGuid(0),
                ("t", _tenant.Id), ("e", new[] { _env.Email(_tenant, "admin"), _env.Email(_tenant, "viewer"), _env.Email(_tenant, "noaccess"), _env.Email(_tenant, ArabicSession.Local) })))
            .Select(i => i.ToString()).ToList();
        return _actorUserIds;
    }

    /// <summary>A body that passes validation: fields copied from the record's own GET when there
    /// is one (an edit and save), otherwise fresh values that carry this tenant's canary or e-mail
    /// domain, so anything they leave in process-wide state is recognisable.</summary>
    private JsonNode? BuildBody(ApiEndpoint endpoint, TenantSnapshot own, JsonNode? template, WriteVariant? variant = null)
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
        // Every leaf conforms to its documented constraints (enums, patterns, lengths, ranges), so
        // the write passes validation and its handler runs to the end.
        var body = _openApi.BuildBody(schema, (leaf, type, format, name) => _openApi.Conform(leaf, OwnLeaf(type, format, name, own, n)), useDocumentedValues: true) as JsonObject ?? [];
        if (template is null)
        {
            UniqueDocumentedValues(schema, body, n);
        }
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
        // An action on every row a list's search and filter match, confirmed by the count the list
        // showed (expectedCount; POST /api/identity/users/matching/active): the search carries the
        // tenant's marker and a word nothing holds, and the count is 0, so the handler runs to the
        // end (match, count, rules, a set-based update of no rows) without deactivating the users the
        // rest of the gate signs in as. A generated filter would not parse, so there is none.
        if (body.ContainsKey("expectedCount") && body.ContainsKey("search"))
        {
            body["search"] = $"Activity {_tenant.Canary ?? _tenant.Code} {Guid.NewGuid():N}";
            body["expectedCount"] = 0;
            body.Remove("filter");
        }
        if (variant is not null)
        {
            // After the record's own values: the variant's documented values replace them.
            var applied = true;
            foreach (var (leaf, value) in variant.Settings)
            {
                applied &= OpenApiDocument.SetLeaf(body, leaf.Path, value);
            }
            if (applied)
            {
                lock (_lock) _appliedVariants.Add($"{endpoint.Key}|{variant.Label}");
            }
        }
        return body;
    }

    /// <summary>
    /// A create takes a field's documented example where the field has one (so it passes
    /// validation), and the example is the same in every body: a field that must be unique in the
    /// workspace, such as a company or branch code, would refuse the second create and the handler
    /// would never run to the end. Each create therefore gets its own variant of a patterned
    /// example (the example, this run's tag and the body's number) when the variant still conforms
    /// to the field's pattern and length; other fields keep the example.
    /// </summary>
    private void UniqueDocumentedValues(JsonElement schema, JsonObject body, int n)
    {
        var resolved = _openApi.Resolve(schema);
        if (!resolved.TryGetProperty("properties", out var properties))
        {
            return;
        }
        foreach (var property in properties.EnumerateObject())
        {
            if (body[property.Name] is not JsonValue value || value.GetValueKind() != JsonValueKind.String || !HasPattern(_openApi.Resolve(property.Value)))
            {
                continue;
            }
            var candidate = $"{value.GetValue<string>()}-{_run}-{n}";
            if (_openApi.Conform(property.Value, JsonValue.Create(candidate)) is JsonValue conformed &&
                conformed.GetValueKind() == JsonValueKind.String && conformed.GetValue<string>() == candidate)
            {
                body[property.Name] = candidate;
            }
        }
    }

    private bool HasPattern(JsonElement leaf) =>
        leaf.ValueKind == JsonValueKind.Object &&
        (leaf.TryGetProperty("pattern", out _) ||
         new[] { "oneOf", "anyOf", "allOf" }.Any(c => leaf.TryGetProperty(c, out var options) && options.EnumerateArray().Any(o => HasPattern(_openApi.Resolve(o)))));

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
    internal static string OwnIdFor(string field, TenantSnapshot own)
    {
        var stem = field.EndsWith("ids", StringComparison.Ordinal) ? field[..^3] : field.EndsWith("id", StringComparison.Ordinal) ? field[..^2] : field;
        if (stem.Length > 0)
        {
            foreach (var (table, ids) in own.IdsByTable)
            {
                var tableName = table.Split('.').Last();
                var plural = stem.EndsWith('y') ? stem[..^1] + "ies" : stem + "s";
                if (ids.Count > 0 && (tableName == plural || tableName == stem || tableName == stem + "es"))
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

    /// <param name="Owner">Local part of the e-mail of the user the actor is signed in as: records
    /// only their owner can see (a saved list view) are created and written per owner.</param>
    private sealed record Actor(string Name, HttpClient Client, string Owner = "admin");

    /// <summary>Key of the records an owner created in a collection (the administrator's keep the
    /// bare collection).</summary>
    private static string CreatedKey(string collection, string owner) => owner == "admin" ? collection : $"{owner}|{collection}";
}

/// <summary>One way to fill a write's body beyond its defaults (<see cref="TenantActivity.VariantsOf"/>):
/// the given documented values at the given leaves.</summary>
/// <param name="Leaf">The one enumerated field this variant sets, when it sets only one.</param>
/// <param name="Value">That field's value.</param>
public sealed record WriteVariant(string Label, IReadOnlyList<(EnumLeaf Leaf, JsonNode Value)> Settings, EnumLeaf? Leaf, JsonNode? Value)
{
    public override string ToString() => Label;
}
