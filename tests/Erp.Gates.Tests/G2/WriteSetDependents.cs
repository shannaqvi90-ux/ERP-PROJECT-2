using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// The rows that hang on a record, owned by other permissions (critic p02 round 14, plant PB3:
/// closing a company also closed every branch of it, for a caller holding
/// <c>tenancy.companies.update</c> but not <c>tenancy.branches.update</c>, and every gate passed: the
/// write-set gate made each record fresh, and a fresh company has no branches, so a write that
/// cascades from a record into its dependent rows never ran while the gate watched).
///
/// The write-set gate sends every request that edits, deletes or acts on a record twice: once at a
/// fresh record, once at a record the administrator first gave, through the product's own
/// endpoints:
/// <list type="number">
/// <item>a child in every collection whose create names the record by reference, a field named
/// after the record at any depth of the create's body (<c>companyId</c> for a company,
/// <c>roleId</c> or <c>roleIds</c> for a role): a company gets a branch and a user holding a role in
/// it, a role gets a user holding it everywhere and in one company. Other ids in the same item are
/// filled from the run (the working company, a role granting nothing);</item>
/// <item>every list of references on the record itself filled, through the record's own edit: a
/// user holds a role everywhere and one in a company;</item>
/// <item>the rows that say who works where: a bystander user (holding only the workplace switch)
/// given the company limited to the record's branch and working there, for a company (its new
/// branch) and for a branch; the company a user starts in, for a user.</item>
/// </list>
/// A dependent the gate cannot make is a finding (a blind spot), never skipped.
/// </summary>
internal sealed class WriteSetDependents(ErpTestEnvironment env, OpenApiDocument openApi, HttpClient admin, IReadOnlyList<ApiEndpoint> endpoints, string company)
    : IDisposable
{
    private string? emptyRole;
    private Guid bystander;
    private string bystanderEmail = "";
    private HttpClient? bystanderClient;
    private int n;

    /// <summary>Dependent rows made so far (children, filled reference lists, access rows and
    /// workplaces).</summary>
    public int Made { get; private set; }

    /// <summary>Records given dependents, by collection.</summary>
    public SortedSet<string> Collections { get; } = new(StringComparer.Ordinal);

    /// <summary>The singular of a collection's last segment: <c>companies</c> → <c>company</c>.</summary>
    public static string Stem(string collection)
    {
        var last = collection.TrimEnd('/')[(collection.TrimEnd('/').LastIndexOf('/') + 1)..];
        return last switch
        {
            _ when last.EndsWith("ies", StringComparison.Ordinal) => last[..^3] + "y",
            _ when last.EndsWith("ches", StringComparison.Ordinal) || last.EndsWith("shes", StringComparison.Ordinal) ||
                   last.EndsWith("sses", StringComparison.Ordinal) || last.EndsWith("xes", StringComparison.Ordinal) => last[..^2],
            _ when last.EndsWith('s') => last[..^1],
            _ => last,
        };
    }

    /// <summary>The plural a collection of <paramref name="stem"/> records is named by
    /// (<c>company</c> → <c>companies</c>, <c>branch</c> → <c>branches</c>).</summary>
    public static string Plural(string stem) =>
        stem.EndsWith('y') && stem.Length > 1 && !"aeiou".Contains(stem[^2]) ? stem[..^1] + "ies" :
        stem.EndsWith("ch", StringComparison.Ordinal) || stem.EndsWith("sh", StringComparison.Ordinal) || stem.EndsWith('s') || stem.EndsWith('x') ? stem + "es" :
        stem + "s";

    /// <summary>Whether a record of <paramref name="collection"/> gets any dependent (the second
    /// sending of a request is skipped only where it would repeat the first).</summary>
    public bool HasDependents(string collection)
    {
        var stem = Stem(collection);
        if (stem is "company" or "branch" or "user")
        {
            return true;
        }
        var children = endpoints.Any(e => e.Method == "POST" && e.RouteParameters.Count == 0 && e.Pattern != collection &&
                                          openApi.RequestSchema("POST", e.Pattern) is { } schema &&
                                          openApi.Leaves(schema).Any(l => l.Format == "uuid" && (l.Name.Equals(stem + "Id", StringComparison.OrdinalIgnoreCase) ||
                                                                                                  l.Name.Equals(stem + "Ids", StringComparison.OrdinalIgnoreCase))));
        var lists = endpoints.Any(e => e.Method == "PUT" && e.RouteParameters.Count == 1 && e.Pattern.StartsWith(collection + "/{", StringComparison.Ordinal) &&
                                       e.Pattern.IndexOf('/', collection.Length + 1) < 0 && openApi.RequestSchema("PUT", e.Pattern) is { } schema &&
                                       openApi.Leaves(schema).Any(l => l.Format == "uuid" && l.Path.Contains(OpenApiDocument.ArrayItems) && KnownName(l.Name)));
        return children || lists;
    }

    private static bool KnownName(string name) =>
        name is "roleId" or "roleIds" or "companyId" or "companyIds";

    /// <summary>Gives the record <paramref name="id"/> of <paramref name="collection"/> its
    /// dependents; the problem when one could not be made.</summary>
    public async Task<string?> AddAsync(string collection, string id, string tag)
    {
        if (await EnsureAsync() is { } setUp)
        {
            return setUp;
        }
        var stem = Stem(collection);
        var children = new List<(string Collection, string Id)>();

        // 1. A child in every collection whose create names the record.
        foreach (var create in endpoints.Where(e => e.Method == "POST" && e.RouteParameters.Count == 0 && e.Pattern != collection))
        {
            if (openApi.RequestSchema("POST", create.Pattern) is not { } schema)
            {
                continue;
            }
            var leaves = openApi.Leaves(schema);
            var refs = leaves.Where(l => l.Format == "uuid" && (l.Name.Equals(stem + "Id", StringComparison.OrdinalIgnoreCase) ||
                                                                l.Name.Equals(stem + "Ids", StringComparison.OrdinalIgnoreCase))).ToList();
            if (refs.Count == 0)
            {
                continue;
            }
            var body = G1WriteOracle.Valid(openApi, schema, env, $"{tag}d{++n}", company);
            foreach (var leaf in refs)
            {
                OpenApiDocument.EnsurePath(body, body, leaf.Path);
                OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(id));
            }
            // The other ids of an item the reference sits in (a role in one company: its company).
            foreach (var leaf in leaves.Where(l => l.Format == "uuid" && l.InArrayObject && !refs.Contains(l) && Get(body, l.Path) is null &&
                                                   refs.Any(r => SameItem(r.Path, l.Path))))
            {
                if (Known(leaf.Name) is { } value)
                {
                    OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(value));
                }
            }
            await Ceremonies.CompleteAsync(admin, "POST", create.Pattern, body);
            using var made = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Post, create.Pattern, body));
            var text = await made.Content.ReadAsStringAsync();
            if (made.StatusCode != HttpStatusCode.Created)
            {
                return $"giving the record {collection}/{id} a dependent through POST {create.Pattern} ({string.Join(", ", refs.Select(r => r.Display))}) " +
                       $"answered {(int)made.StatusCode}: {Short(text)}";
            }
            children.Add((create.Pattern, JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!));
            Made++;
        }

        // 2. Every list of references on the record filled, through its own edit.
        if (endpoints.FirstOrDefault(e => e.Method == "PUT" && e.RouteParameters.Count == 1 && e.Pattern.StartsWith(collection + "/{", StringComparison.Ordinal) &&
                                          e.Pattern.IndexOf('/', collection.Length + 1) < 0) is { } edit &&
            openApi.RequestSchema("PUT", edit.Pattern) is { } editSchema)
        {
            var lists = openApi.Leaves(editSchema)
                .Where(l => l.Format == "uuid" && l.Path.Contains(OpenApiDocument.ArrayItems) && Known(l.Name) is not null)
                .ToList();
            if (lists.Count > 0)
            {
                if (await FilledAsync(edit.Path(_ => id), $"{collection}/{id}", editSchema, lists) is { } problem)
                {
                    return problem;
                }
                Made += lists.Count;
            }
        }

        // 3. Who works where.
        switch (stem)
        {
            case "company":
                if (children.FirstOrDefault(c => Stem(c.Collection) == "branch") is { Id: { } branch })
                {
                    if (await WorkThereAsync(id, branch) is { } problem)
                    {
                        return problem;
                    }
                }
                else
                {
                    return $"the record {collection}/{id} got no branch: no create names a company by companyId, so the gate cannot put anyone to work in one of its branches";
                }
                break;
            case "branch":
            {
                var read = await admin.GetFromJsonAsync<JsonObject>($"{collection}/{id}");
                if (read?["companyId"]?.GetValue<string>() is not { } of)
                {
                    return $"reading the branch {collection}/{id} gave no companyId";
                }
                if (await WorkThereAsync(of, id) is { } problem)
                {
                    return problem;
                }
                break;
            }
            case "user":
                if (endpoints.FirstOrDefault(e => e.Method == "PUT" && e.Pattern.StartsWith(collection + "/{", StringComparison.Ordinal) &&
                                                  e.Pattern.EndsWith("/default-company", StringComparison.Ordinal)) is { } starts)
                {
                    var path = starts.Path(_ => id);
                    var current = await admin.GetFromJsonAsync<JsonObject>(path);
                    using var set = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Put, path,
                        new JsonObject { ["companyId"] = company, ["version"] = current?["version"]?.DeepClone() }));
                    if (!set.IsSuccessStatusCode)
                    {
                        return $"setting the company the user {id} starts in (PUT {path}) answered {(int)set.StatusCode}: {Short(await set.Content.ReadAsStringAsync())}";
                    }
                    Made++;
                }
                break;
        }
        Collections.Add(collection);
        return null;
    }

    /// <summary>The run's value for an id named <paramref name="name"/>: a role granting nothing,
    /// the working company.</summary>
    private string? Known(string name) =>
        name.Equals("roleId", StringComparison.OrdinalIgnoreCase) || name.Equals("roleIds", StringComparison.OrdinalIgnoreCase) ? emptyRole :
        name.Equals("companyId", StringComparison.OrdinalIgnoreCase) || name.Equals("companyIds", StringComparison.OrdinalIgnoreCase) ? company :
        null;

    /// <summary>The record as read, with every listed reference given one item, saved through its own edit.</summary>
    private async Task<string?> FilledAsync(string path, string readPath, JsonElement schema, IReadOnlyList<BodyLeaf> lists)
    {
        using var read = await admin.GetAsync(readPath);
        if (!read.IsSuccessStatusCode || await read.Content.ReadFromJsonAsync<JsonNode>() is not JsonObject record)
        {
            return $"reading {readPath} to fill its reference lists answered {(int)read.StatusCode}";
        }
        var body = new JsonObject();
        if (openApi.Resolve(schema).TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (record[property.Name] is { } value)
                {
                    body[property.Name] = value.DeepClone();
                }
            }
        }
        foreach (var leaf in lists)
        {
            var top = leaf.Path[0];
            body[top] = new JsonArray();
            OpenApiDocument.EnsurePath(body, null, leaf.Path);
        }
        foreach (var leaf in openApi.Leaves(schema).Where(l => l.Format == "uuid" && l.Path.Contains(OpenApiDocument.ArrayItems) && Known(l.Name) is not null))
        {
            OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(Known(leaf.Name)!));
        }
        using var saved = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Put, path, body));
        return saved.IsSuccessStatusCode
            ? null
            : $"filling the reference lists of {readPath} ({string.Join(", ", lists.Select(l => l.Display))}) through PUT {path} answered {(int)saved.StatusCode}: {Short(await saved.Content.ReadAsStringAsync())}";
    }

    /// <summary>The bystander is given <paramref name="companyId"/> limited to
    /// <paramref name="branchId"/> (besides what it already had) and switches to work there.</summary>
    private async Task<string?> WorkThereAsync(string companyId, string branchId)
    {
        var access = await admin.GetFromJsonAsync<JsonObject>($"/api/tenancy/access/{bystander}") ?? [];
        var companies = (access["companies"] as JsonArray ?? []).Select(c => c!.DeepClone()).OfType<JsonObject>().ToList();
        var entry = companies.FirstOrDefault(c => c["companyId"]?.GetValue<string>() == companyId);
        if (entry is null)
        {
            entry = new JsonObject { ["companyId"] = companyId, ["allBranches"] = false, ["branchIds"] = new JsonArray() };
            companies.Add(entry);
        }
        if (entry["allBranches"]?.GetValue<bool>() != true && entry["branchIds"] is JsonArray branches && branches.All(b => b?.GetValue<string>() != branchId))
        {
            branches.Add(branchId);
        }
        using (var given = await admin.SendAsync(G1WriteOracle.Json(HttpMethod.Put, $"/api/tenancy/access/{bystander}",
                   new JsonObject { ["companies"] = new JsonArray([.. companies]), ["version"] = access["version"]?.DeepClone() })))
        {
            if (!given.IsSuccessStatusCode)
            {
                return $"giving the bystander company {companyId} limited to branch {branchId} answered {(int)given.StatusCode}: {Short(await given.Content.ReadAsStringAsync())}";
            }
        }
        for (var attempt = 0; ; attempt++)
        {
            bystanderClient ??= await env.SignInAsync(bystanderEmail);
            using var switched = await bystanderClient.SendAsync(G1WriteOracle.Json(HttpMethod.Put, "/api/tenancy/workplace",
                new JsonObject { ["companyId"] = companyId, ["branchId"] = branchId }));
            if (switched.IsSuccessStatusCode)
            {
                Made += 2;
                return null;
            }
            if (switched.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // A session ended by another request of the run (sessions revoked): sign in again.
                bystanderClient.Dispose();
                bystanderClient = null;
                continue;
            }
            return $"the bystander switching to work in company {companyId}, branch {branchId} answered {(int)switched.StatusCode}: {Short(await switched.Content.ReadAsStringAsync())}";
        }
    }

    /// <summary>
    /// The requests that change a record's lists of references (a user's roles everywhere and in
    /// one company): each top-level list whose items are ids the run knows (or items holding only
    /// such ids) sent emptied, and sent holding one item of the run's values. A body copies the
    /// record's own lists, so without these an edit never adds or removes an item, and what giving
    /// or taking one writes is never judged.
    /// </summary>
    public IEnumerable<(string Name, Action<JsonObject> Change)> ListChanges(JsonElement schema)
    {
        foreach (var group in openApi.Leaves(schema).Where(l => l.Path.Count >= 2 && l.Path[1] == OpenApiDocument.ArrayItems).GroupBy(l => l.Path[0]))
        {
            var leaves = group.ToList();
            if (leaves.Any(l => l.Format != "uuid" || !KnownName(l.Name)))
            {
                continue;
            }
            var name = group.Key;
            yield return ($"{name} emptied", body => body[name] = new JsonArray());
            yield return ($"{name} given one", body =>
            {
                body[name] = new JsonArray();
                OpenApiDocument.EnsurePath(body, null, leaves[0].Path);
                foreach (var leaf in leaves)
                {
                    OpenApiDocument.SetLeaf(body, leaf.Path, JsonValue.Create(Known(leaf.Name)!));
                }
            });
        }
    }

    /// <summary>The run's role granting nothing, and the bystander: a user holding only the
    /// workplace switch, working nowhere yet.</summary>
    public async Task<string?> EnsureAsync()
    {
        if (emptyRole is not null)
        {
            return null;
        }
        var tag = Guid.NewGuid().ToString("N")[..8];
        var empty = await CreatedAsync("/api/identity/roles", new { nameEn = $"Dependents {tag}", nameAr = $"التوابع {tag}", permissions = Array.Empty<string>() });
        var switching = await CreatedAsync("/api/identity/roles", new { nameEn = $"Bystander {tag}", nameAr = $"المتفرج {tag}", permissions = new[] { "tenancy.workplace.switch" } });
        if ((empty.Problem ?? switching.Problem) is { } problem)
        {
            return problem;
        }
        bystanderEmail = $"g2.bystander.{tag}@{env.TenantA.EmailDomain}";
        var user = await CreatedAsync("/api/identity/users",
            new { email = bystanderEmail, displayName = $"Bystander {tag}", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { switching.Id } });
        if (user.Problem is { } userProblem)
        {
            return userProblem;
        }
        bystander = Guid.Parse(user.Id!);
        emptyRole = empty.Id;
        return null;
    }

    private async Task<(string? Id, string? Problem)> CreatedAsync(string path, object body)
    {
        using var response = await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.Created
            ? (JsonDocument.Parse(text).RootElement.GetProperty("id").GetString(), null)
            : (null, $"making the write-set dependents' {path} record answered {(int)response.StatusCode}: {Short(text)}");
    }

    /// <summary>Two leaf paths in the same item of the same array (<c>companyRoles[].roleId</c>,
    /// <c>companyRoles[].companyId</c>).</summary>
    private static bool SameItem(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var cut = a.ToList().LastIndexOf(OpenApiDocument.ArrayItems);
        return cut >= 0 && b.Count == a.Count && a.Take(cut + 1).SequenceEqual(b.Take(cut + 1));
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

    private static string Short(string text) => text.Length <= 240 ? text : text[..240] + "…";

    public void Dispose() => bystanderClient?.Dispose();
}
