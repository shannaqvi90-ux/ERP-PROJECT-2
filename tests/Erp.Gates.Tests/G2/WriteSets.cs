using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, what a permission lets a request write (critic p02 round 10, plant P1b: PUT
/// /api/tenancy/workplace, which declares only tenancy.workplace.switch, also switched on the
/// company's Arabic-legal-name requirement, a legal setting that needs tenancy.companies.update; every
/// gate checked which permission an endpoint declares, none what the endpoint writes).
///
/// Every permission has a reviewed write set (tests/Gates/permission-write-sets.txt): the tables,
/// actions and columns a request it authorises may change. For every endpoint that changes data,
/// a user holding exactly the endpoint's permission (and working in every company and branch, so
/// no company or branch condition hides a write) sends it requests that pass validation and run to
/// the end: a create, an edit of a record the administrator made for it (fresh values, the record's
/// own version and references), a delete of such a record, and the same request with every
/// true-or-false field turned the other way and every documented choice of every choice field. The
/// audit trail records every row the request changed, with its columns, whoever wrote it (the
/// trigger runs on every audited table whatever the write path): any change outside the write set
/// of the endpoint's permission (and the reviewed writes any signed-in request makes, permission
/// <c>*</c>) is a finding. An endpoint the run never gets an accepted answer from is a finding unless
/// it is reviewed in tests/Gates/write-set-unexercised.txt with the reason; a write-set line no
/// request used is a finding too, so every set stays as narrow as what the product does.
/// </summary>
public static class WriteSets
{
    public const string ReviewedFile = "tests/Gates/permission-write-sets.txt";
    public const string UnexercisedFile = "tests/Gates/write-set-unexercised.txt";

    /// <summary>A reviewed line: <paramref name="Permission"/> (or <c>*</c>) may make
    /// <paramref name="Actions"/> on <paramref name="Table"/>, changing <paramref name="Columns"/>
    /// (null: any column).</summary>
    public sealed record Line(string Permission, string Table, IReadOnlySet<string> Actions, IReadOnlySet<string>? Columns, string Reason)
    {
        public string Key => $"{Permission} {Table}";

        public bool Covers(string permission, Write write) =>
            (Permission == "*" || Permission == permission) && Table == write.Table && Actions.Contains(write.Action) &&
            (Columns is null || write.Columns.All(Columns.Contains));
    }

    /// <summary>One audited row change: the table (schema.table), the action and the changed columns.</summary>
    public sealed record Write(long Id, string Table, string Action, IReadOnlyList<string> Columns)
    {
        public override string ToString() => $"{Action} {Table} ({string.Join(", ", Columns)})";
    }

    /// <param name="Exercised">Endpoints that answered 2xx to at least one request, with the
    /// permissions the caller held.</param>
    /// <param name="Requests">Requests sent by callers holding the endpoint's permission.</param>
    /// <param name="Writes">Audited row changes judged.</param>
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Exercised, IReadOnlyList<string> Unexercised, int Requests, int Writes,
        IReadOnlyList<string> Observed);

    public static List<Line> Load(string relativePath = ReviewedFile)
    {
        var lines = new List<Line>();
        foreach (var (entry, reason) in Repo.ReadReviewedList(relativePath))
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
            {
                throw new InvalidOperationException($"{relativePath}: '{entry}' is not '<permission> <schema.table> <actions> <columns>'");
            }
            var actions = parts[2].Split(',').ToHashSet(StringComparer.Ordinal);
            if (actions.Any(a => a is not ("insert" or "update" or "delete")))
            {
                throw new InvalidOperationException($"{relativePath}: '{entry}': actions are insert, update and delete");
            }
            lines.Add(new Line(parts[0], parts[1], actions, parts[3] == "*" ? null : parts[3].Split(',').ToHashSet(StringComparer.Ordinal), reason));
        }
        return lines;
    }

    /// <summary>The writes of one request outside the write set of <paramref name="permission"/>.</summary>
    public static IEnumerable<Write> Outside(string permission, IEnumerable<Write> writes, IReadOnlyList<Line> lines) =>
        writes.Where(w => !lines.Any(l => l.Covers(permission, w)));

    /// <summary>Problems of the reviewed files themselves: a line naming no catalogue permission or
    /// no reason, the same table twice for one permission.</summary>
    public static List<string> FileProblems(IReadOnlyList<Line> lines, IReadOnlyCollection<string> catalogue)
    {
        var problems = new List<string>();
        foreach (var line in lines)
        {
            if (line.Permission != "*" && !catalogue.Contains(line.Permission))
            {
                problems.Add($"{ReviewedFile}: '{line.Key}' names a permission the catalogue does not have");
            }
            if (string.IsNullOrWhiteSpace(line.Reason))
            {
                problems.Add($"{ReviewedFile}: '{line.Key}' needs a reason after #");
            }
        }
        foreach (var twice in lines.GroupBy(l => l.Key).Where(g => g.Count() > 1))
        {
            problems.Add($"{ReviewedFile}: '{twice.Key}' is listed {twice.Count()} times; one line per permission and table");
        }
        return problems;
    }

    public static async Task<Result> RunAsync(ErpTestEnvironment env, Func<ApiEndpoint, bool>? only = null, IReadOnlyList<Line>? reviewed = null)
    {
        var lines = reviewed ?? Load();
        var unexercisedReviewed = Repo.ReadReviewedList(UnexercisedFile).ToDictionary(e => e.Entry, e => e.Reason, StringComparer.Ordinal);
        var problems = new List<string>();
        var exercised = new List<string>();
        var unexercised = new List<string>();
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        var writes = 0;

        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        problems.AddRange(FileProblems(lines, catalog.PermissionKeys.ToList()));

        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        await G1WriteOracle.UseWorkingCompanyAsync(admin);
        var company = G1WriteOracle.CompanyOf(admin);
        var endpoints = EndpointInventory.From(env.Factory.Services).Where(e => !e.IsAnonymous).ToList();
        var writing = endpoints
            .Where(e => e.Method is "POST" or "PUT" or "PATCH" or "DELETE" && !e.ReadOnlyOperation && (only is null || only(e)))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        // Permissions that read (every permission a GET declares): a second caller holds them too,
        // for an endpoint whose handler also needs to see the record (a shared view needs its
        // list's own permission as well as sharing). Its writes are still judged against the write
        // set of the endpoint's own permission alone.
        var readPermissions = endpoints.Where(e => e.Method == "GET").Select(e => e.Permission).Distinct().Order(StringComparer.Ordinal).ToArray();

        var callers = new Dictionary<string, Caller>(StringComparer.Ordinal);
        try
        {
            var n = 0;
            foreach (var endpoint in writing)
            {
                var accepted = false;
                var answers = new List<string>();
                foreach (var withReads in new[] { false, true })
                {
                    var held = withReads ? [endpoint.Permission, .. readPermissions.Where(p => p != endpoint.Permission)] : new[] { endpoint.Permission };
                    var key = string.Join(",", held);
                    if (!callers.TryGetValue(key, out var caller))
                    {
                        n++;
                        caller = await Caller.CreateAsync(env, admin, held, $"{n}{Guid.NewGuid():N}"[..10]);
                        callers[key] = caller;
                    }
                    foreach (var variant in await VariantsAsync(env, openApi, admin, caller, endpoint, company, endpoints))
                    {
                        var (status, text, before) = await variant.SendAsync();
                        requests++;
                        var changes = await WritesSinceAsync(env, before);
                        writes += changes.Count;
                        var label = $"{endpoint} [{variant.Name}] as a user holding exactly [{string.Join(", ", held)}]";
                        foreach (var write in changes)
                        {
                            foreach (var line in lines.Where(l => l.Covers(endpoint.Permission, write)))
                            {
                                observed.Add(line.Key);
                            }
                        }
                        foreach (var outside in Outside(endpoint.Permission, changes, lines))
                        {
                            problems.Add($"{label} wrote {outside}, outside the write set of {endpoint.Permission} ({ReviewedFile}): " +
                                         $"a request this permission authorises changes what it does not cover (answered {status})");
                        }
                        if (status is >= 200 and < 300)
                        {
                            accepted = true;
                        }
                        else
                        {
                            answers.Add($"{variant.Name}: {status} {Short(text)}");
                        }
                    }
                    if (accepted)
                    {
                        exercised.Add($"{endpoint.Key} [{(withReads ? "with read permissions" : "alone")}]");
                        break;
                    }
                }
                if (!accepted)
                {
                    if (unexercisedReviewed.ContainsKey(endpoint.Key))
                    {
                        unexercised.Add(endpoint.Key);
                    }
                    else
                    {
                        problems.Add($"{endpoint}: no request passed (so its writes were never judged against the write set of {endpoint.Permission}): " +
                                     $"{string.Join(" | ", answers.Take(4))}; make the gate reach it, or review it in {UnexercisedFile} with the reason");
                    }
                }
                else if (unexercisedReviewed.ContainsKey(endpoint.Key))
                {
                    problems.Add($"{UnexercisedFile}: {endpoint.Key} is listed as unreachable but answered 2xx; remove the line");
                }
            }
        }
        finally
        {
            foreach (var caller in callers.Values)
            {
                caller.Dispose();
            }
        }

        if (only is null)
        {
            var keys = writing.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
            problems.AddRange(unexercisedReviewed.Keys.Where(k => !keys.Contains(k)).Select(k => $"{UnexercisedFile}: {k} is no endpoint that changes data; remove the line"));
            problems.AddRange(lines.Where(l => !observed.Contains(l.Key))
                .Select(l => $"{ReviewedFile}: '{l.Key}' was used by no request of the run; a write set lists only what its requests write (remove the line, or make the gate reach the write)"));
        }
        return new Result(problems, exercised, unexercised, requests, writes, observed.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>A request ready to send as the caller, after any set-up the administrator did.</summary>
    /// <param name="SendAsync">Prepares the request, marks the end of the audit trail, sends it:
    /// the answer and the mark (the request's own writes are the audited changes after it).</param>
    private sealed record Variant(string Name, Func<Task<(int Status, string Text, long Before)>> SendAsync);

    /// <summary>The requests sent to one endpoint: the valid one, then one per true-or-false field
    /// turned the other way and one per other documented choice of each choice field. Each is built
    /// afresh (a new record to edit or delete, its current version) right before it is sent.</summary>
    private static async Task<IReadOnlyList<Variant>> VariantsAsync(ErpTestEnvironment env, OpenApiDocument openApi, HttpClient admin, Caller caller,
        ApiEndpoint endpoint, string company, IReadOnlyList<ApiEndpoint> endpoints)
    {
        var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
        var changes = new List<(string Name, Action<JsonObject> Change)> { ("valid", _ => { }) };
        if (schema is { } s)
        {
            foreach (var leaf in openApi.Leaves(s).Where(l => l.Type == "boolean"))
            {
                changes.Add(($"{leaf.Display} turned", body =>
                {
                    var now = Get(body, leaf.Path) is JsonValue v && v.TryGetValue<bool>(out var b) && b;
                    OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(!now));
                }));
            }
            foreach (var leaf in openApi.EnumLeaves(s))
            {
                foreach (var value in leaf.Values.Skip(1))
                {
                    changes.Add(($"{leaf.Name}={value.ToJsonString()}", body => OpenApiDocument.SetLeaf(body, leaf.Path, value.DeepClone())));
                }
            }
            // Every number at another valid value too (critic p02 round 11, plant P1d: the switch
            // also set the company's fiscal year start month, 1 to 12; the valid body sends 1, the
            // month every company already had, so nothing reached the audit trail and the write
            // went unseen). Together with the valid body's value (the smallest the field takes),
            // the largest it takes is a value that differs from what is stored, whatever that is.
            foreach (var leaf in openApi.Leaves(s).Where(l => l.Type is "integer" or "number" &&
                                                            !l.Name.EndsWith("version", StringComparison.OrdinalIgnoreCase) &&
                                                            !l.Name.Equals("expectedCount", StringComparison.Ordinal)))
            {
                var bounds = openApi.Resolve(leaf.Schema);
                var low = Bound(bounds, "minimum") ?? 1m;
                var other = Bound(bounds, "maximum") is { } x && x > low && x <= int.MaxValue ? x : low + 1;
                changes.Add(($"{leaf.Display}={other}", body => OpenApiDocument.SetLeaf(body, leaf.Path,
                    leaf.Type == "integer" ? JsonValue.Create((long)other) : JsonValue.Create(other))));
            }
        }
        var n = 0;
        return changes.Select(c => new Variant(c.Name, async () =>
        {
            n++;
            var tag = $"ws{n}{Guid.NewGuid():N}"[..12];
            var prepared = await PrepareAsync(env, openApi, admin, caller, endpoint, schema, company, endpoints, tag);
            if (prepared.Problem is { } problem)
            {
                return (0, problem, await LastAuditIdAsync(env));
            }
            if (prepared.Body is { } body)
            {
                c.Change(body);
            }
            var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), prepared.Path);
            if (prepared.Body is { } content)
            {
                request.Content = new StringContent(content.ToJsonString(), Encoding.UTF8, "application/json");
            }
            var before = await LastAuditIdAsync(env);
            using (request)
            using (var response = await caller.Client.SendAsync(request))
            {
                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), before);
            }
        })).ToList();
    }

    /// <summary>A documented bound of a number (written as a number or as a string).</summary>
    private static decimal? Bound(JsonElement schema, string name) =>
        schema.TryGetProperty(name, out var value) switch
        {
            false => null,
            true when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) => number,
            true when value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    private sealed record Prepared(string Path, JsonObject? Body, string? Problem = null);

    /// <summary>The path and body of one request: a create as is; an edit, delete or action on a
    /// record made first (by the caller when the caller's own permission creates it, as for a
    /// saved view or a passkey; otherwise by the administrator), carrying fresh values with the
    /// record's own version and references.</summary>
    private static async Task<Prepared> PrepareAsync(ErpTestEnvironment env, OpenApiDocument openApi, HttpClient admin, Caller caller, ApiEndpoint endpoint,
        JsonElement? schema, string company, IReadOnlyList<ApiEndpoint> endpoints, string tag)
    {
        var body = schema is { } s ? G1WriteOracle.Valid(openApi, s, env, tag, company) : null;
        if (endpoint.RouteParameters.Count == 0 && body is not null && body.ContainsKey("expectedCount") && body.ContainsKey("search"))
        {
            // An action on "all that match" a list's search (activate every user found): a record
            // made first through the list's collection, found by its own e-mail, exactly one match.
            var list = endpoint.Pattern[..endpoint.Pattern.IndexOf("/matching", StringComparison.Ordinal)];
            var made = await MakeAsync(env, openApi, admin, caller, endpoints, list, company, tag);
            if (made.Problem is { } problem)
            {
                return new Prepared("", null, problem);
            }
            body["search"] = made.Body?["email"]?.DeepClone() ?? made.Body?["code"]?.DeepClone();
            body["filter"] = null;
            body["expectedCount"] = 1;
            return new Prepared(endpoint.Pattern, body);
        }
        if (endpoint.RouteParameters.Count == 0)
        {
            if (endpoint.Method is "PUT" or "PATCH" && body is not null)
            {
                // A record of its own with no id in the route (the workspace's settings, the
                // caller's preferences): its version as read.
                await CarryVersionAsync(caller.Client, endpoint.Pattern, body);
                await CarryVersionAsync(admin, endpoint.Pattern, body);
            }
            if (body is not null)
            {
                await Ceremonies.CompleteAsync(caller.Client, endpoint.Method, endpoint.Pattern, body);
            }
            return new Prepared(endpoint.Pattern, body);
        }

        // The record the route names: made through the POST of the collection the first route
        // parameter sits in, or of the collection named like it (a user's id names users).
        var parameter = endpoint.RouteParameters[0];
        var cut = endpoint.Pattern.IndexOf("/{" + parameter, StringComparison.Ordinal);
        var collection = endpoint.Pattern[..cut];
        var create = endpoints.FirstOrDefault(e => e.Method == "POST" && e.Pattern == collection) ?? NamedCollection(endpoints, parameter);
        if (create is null || openApi.RequestSchema("POST", create.Pattern) is null)
        {
            return new Prepared("", null, $"no POST creates the record {endpoint.Pattern} names by {{{parameter}}}");
        }
        var record0 = await MakeAsync(env, openApi, admin, caller, endpoints, create.Pattern, company, tag);
        if (record0.Problem is { } failed)
        {
            return new Prepared("", null, failed);
        }
        var id = record0.Id!;
        var maker = record0.Maker!;
        var path = endpoint.Path(_ => id);
        var recordPath = $"{create.Pattern}/{id}";
        // A delete of something under the record that the same path puts there first (a logo).
        if (endpoint.Method == "DELETE" && endpoint.Pattern != $"{create.Pattern}/{{{parameter}}}" &&
            endpoints.FirstOrDefault(e => e.Method == "PUT" && e.Pattern == endpoint.Pattern) is { } put &&
            openApi.RequestSchema("PUT", put.Pattern) is { } putSchema)
        {
            var putBody = G1WriteOracle.Valid(openApi, putSchema, env, tag + "p", company);
            using var placed = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Put, path, putBody));
        }
        if (body is not null)
        {
            // The record's own references (ids) and version; every other value fresh, so the edit
            // changes the record rather than saving it as it is.
            var readPath = endpoints.Any(e => e.Method == "GET" && e.Pattern == endpoint.Pattern) ? path : recordPath;
            using var read = await (maker == caller.Client ? caller.Client : admin).GetAsync(readPath);
            if (read.IsSuccessStatusCode && await read.Content.ReadFromJsonAsync<JsonNode>() is JsonObject record)
            {
                foreach (var (name, value) in body.ToList())
                {
                    if (record[name] is { } own && (name == "version" || value is null || IsReference(name)))
                    {
                        body[name] = own.DeepClone();
                    }
                }
            }
            await Ceremonies.CompleteAsync(caller.Client, endpoint.Method, path, body);
        }
        return new Prepared(path, body);
    }

    private sealed record Made(string? Id, HttpClient? Maker, JsonObject? Body, string? Problem = null);

    /// <summary>A fresh record of <paramref name="collection"/>: made by the caller when the
    /// caller's own permissions create it (a saved view, a passkey), otherwise by the administrator.
    /// A company made is given to the caller (every branch), and a user made works in every company,
    /// as people the screens make do, so no company condition refuses the request first.</summary>
    private static async Task<Made> MakeAsync(ErpTestEnvironment env, OpenApiDocument openApi, HttpClient admin, Caller caller,
        IReadOnlyList<ApiEndpoint> endpoints, string collection, string company, string tag)
    {
        if (endpoints.FirstOrDefault(e => e.Method == "POST" && e.Pattern == collection) is not { } create ||
            openApi.RequestSchema("POST", create.Pattern) is not { } createSchema)
        {
            return new Made(null, null, null, $"no POST {collection} makes the record");
        }
        var maker = caller.Holds(create.Permission) ? caller.Client : admin;
        var createBody = G1WriteOracle.Valid(openApi, createSchema, env, tag + "t", company);
        await Ceremonies.CompleteAsync(maker, "POST", create.Pattern, createBody);
        string id;
        using (var made = await maker.SendAsync(G1WriteOracle.Json(HttpMethod.Post, create.Pattern, createBody)))
        {
            var text = await made.Content.ReadAsStringAsync();
            if (made.StatusCode != HttpStatusCode.Created)
            {
                return new Made(null, null, null, $"creating the record through POST {create.Pattern} answered {(int)made.StatusCode}: {Short(text)}");
            }
            id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;
        }
        if (collection == "/api/tenancy/companies")
        {
            await Caller.GiveEveryCompanyAsync(admin, caller.UserId);
        }
        else if (collection == "/api/identity/users")
        {
            // The caller too: companies other requests of the run made since the caller was given
            // every company would otherwise be ones the user works in and the caller does not.
            await Caller.GiveEveryCompanyAsync(admin, caller.UserId);
            await Caller.GiveEveryCompanyAsync(admin, Guid.Parse(id));
        }
        return new Made(id, maker, createBody);
    }

    private static bool IsReference(string name) => name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("Ids", StringComparison.Ordinal);

    /// <summary>The collection a route parameter named like a record (<c>userId</c>) is created in.</summary>
    private static ApiEndpoint? NamedCollection(IReadOnlyList<ApiEndpoint> endpoints, string parameter)
    {
        var stem = parameter.EndsWith("Id", StringComparison.Ordinal) ? parameter[..^2] : parameter;
        return endpoints.FirstOrDefault(e => e.Method == "POST" && e.Pattern.EndsWith("/" + stem + "s", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CarryVersionAsync(HttpClient client, string path, JsonObject body)
    {
        if (!body.ContainsKey("version"))
        {
            return;
        }
        using var read = await client.GetAsync(path);
        if (read.IsSuccessStatusCode && await read.Content.ReadFromJsonAsync<JsonNode>() is JsonObject record && record["version"] is { } version)
        {
            body["version"] = version.DeepClone();
        }
    }

    private static JsonNode? Get(JsonNode? body, IReadOnlyList<string> path)
    {
        var node = body;
        foreach (var part in path)
        {
            node = part == OpenApiDocument.ArrayItems ? (node as JsonArray)?.FirstOrDefault() : (node as JsonObject)?[part];
        }
        return node;
    }

    private static async Task<long> LastAuditIdAsync(ErpTestEnvironment env)
    {
        await using var owner = await env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<long>(owner, "SELECT COALESCE(max(id), 0) FROM audit.entries");
    }

    /// <summary>Every audited row change after <paramref name="after"/>, whoever made it.</summary>
    public static async Task<List<Write>> WritesSinceAsync(ErpTestEnvironment env, long after)
    {
        await using var owner = await env.OpenAdminAsync();
        return await DbCatalog.ReadAsync(owner,
            "SELECT id, table_schema || '.' || table_name, action, ARRAY(SELECT jsonb_object_keys(changes) ORDER BY 1) FROM audit.entries WHERE id > @after ORDER BY id",
            r => new Write(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetFieldValue<string[]>(3)), ("after", after));
    }

    private static string Short(string text) => text.Length <= 240 ? text : text[..240] + "…";

    /// <summary>A user holding exactly <see cref="Held"/> through one role, working in every
    /// company and every branch, signed in.</summary>
    private sealed class Caller : IDisposable
    {
        private Caller(HttpClient client, IReadOnlyList<string> held, Guid userId)
        {
            Client = client;
            Held = held;
            UserId = userId;
        }

        public Guid UserId { get; }

        public HttpClient Client { get; }

        public IReadOnlyList<string> Held { get; }

        public string Permission => Held[0];

        public bool Holds(string permission) => Held.Contains(permission);

        public static async Task<Caller> CreateAsync(ErpTestEnvironment env, HttpClient admin, IReadOnlyList<string> held, string tag)
        {
            var role = await CreatedAsync(admin, "/api/identity/roles", new { nameEn = $"Write set {tag}", nameAr = $"مجموعة الكتابة {tag}", permissions = held });
            var email = $"g2.writes.{tag}@{env.TenantA.EmailDomain}";
            var user = await CreatedAsync(admin, "/api/identity/users",
                new { email, displayName = $"Write set {tag}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { role } });
            // Every company and every branch of each: no company or branch condition hides a write.
            await GiveEveryCompanyAsync(admin, user);
            return new Caller(await env.SignInAsync(email), held, user);
        }

        /// <summary>The administrator lets <paramref name="user"/> work in every company, every branch.</summary>
        public static async Task GiveEveryCompanyAsync(HttpClient admin, Guid user)
        {
            var access = await admin.GetFromJsonAsync<JsonObject>($"/api/tenancy/access/{user}") ?? [];
            var companies = (access["options"] as JsonArray ?? []).Select(o => (JsonNode)new JsonObject
            {
                ["companyId"] = o!["id"]!.DeepClone(), ["allBranches"] = true, ["branchIds"] = new JsonArray(),
            }).ToArray();
            if (companies.Length == 0)
            {
                return;
            }
            var body = new JsonObject { ["companies"] = new JsonArray(companies), ["version"] = access["version"]?.DeepClone() };
            using var given = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/tenancy/access/{user}", body));
            if (!given.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"giving user {user} every company answered {(int)given.StatusCode}: {await given.Content.ReadAsStringAsync()}");
            }
        }

        private static async Task<Guid> CreatedAsync(HttpClient admin, string path, object body)
        {
            using var response = await admin.PostAsJsonAsync(path, body);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw new InvalidOperationException($"POST {path} as administrator answered {(int)response.StatusCode}: {text}");
            }
            return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
        }

        public void Dispose() => Client.Dispose();
    }
}
