using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, company layer: the users company X shares with company Y (critic p02 round 8, plant PU: with
/// the "works elsewhere" check removed from the tenancy module's default-company change, an
/// administrator limited to one company changed the default working company of a user who also
/// works in a sister company, and every gate passed, because every user the company gates aimed at
/// worked only where the attacker did, or held a role the attacker could not see).
///
/// A user who works in X and in Y is a record both companies share, as the workspace record is
/// shared by every company: whoever works in X alone may not change it. The targets, all of them
/// created here: one who works in X and Y holding a workspace-wide role, one who works in X and Y
/// holding its roles in X and in Y (company roles), and, as the control, one who works in X alone
/// with the same workspace-wide role. Every write of every module whose route takes ids receives
/// the target's id; its body is the endpoint's own read of that target with one field changed at a
/// time (a text, a switch, a choice, a company id, a list losing its last item), or a valid body
/// where there is no read. The tenant's administrator (every company) proves each write: it must
/// succeed and change the target's rows (any tenant table's rows carrying the target's id as
/// <c>id</c> or <c>user_id</c>), and is undone. Then the administrator of company X sends every
/// proven write: each aimed at a target who also works in Y must be refused, those targets' rows
/// must be exactly as they were, and the same writes aimed at the control must succeed (so a
/// refusal is about where the target works, not a malformed request).
/// </summary>
public static partial class SharedUserRecords
{
    public sealed record Target(string Label, Guid Id, bool WorksInY);

    public const string ControlTarget = "the user who works in X alone";
    public const string WorkspaceRoleTarget = "the user who works in X and Y with a workspace-wide role";
    public const string CompanyRolesTarget = "the user who works in X and Y with roles held in X and in Y";

    public sealed record Report(
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> ChangedTargets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ProvenSources,
        IReadOnlyList<string> ControlAccepted,
        int Writes);

    private sealed record Variant(Target Target, ApiEndpoint Endpoint, string Path, string Field, JsonObject? Template, JsonNode? Original, JsonNode? Changed)
    {
        public bool FromRead => Template is not null && Field != "-";
        public string Source => $"{Endpoint} [{Field}]";
    }

    /// <summary>Creates the targets, proves every write on each with <paramref name="control"/>,
    /// then sends them all as <paramref name="attacker"/> (who works in <paramref name="x"/> alone).</summary>
    public static async Task<Report> RunAsync(ErpTestEnvironment env, OpenApiDocument openApi, IReadOnlyList<ApiEndpoint> endpoints,
        HttpClient control, HttpClient attacker, string label, Guid x, Guid y, Guid workspaceRole, Guid companyRole, Guid xBranch1, Guid xBranch2)
    {
        var tenant = env.TenantA;
        var failures = new List<string>();
        var tag = Guid.NewGuid().ToString("N")[..8];
        // The control works in X alone, in two of its branches (so its access has a list to shorten).
        var inX = await CreateUserAsync(control, env, $"in.x.{tag}", "Works in X only", [workspaceRole], []);
        await PutAccessAsync(control, inX, new JsonArray(Access(x, [xBranch1, xBranch2])));
        var bothWorkspace = await CreateUserAsync(control, env, $"in.xy.ws.{tag}", "Works in X and Y, workspace role", [workspaceRole], []);
        await PutAccessAsync(control, bothWorkspace, new JsonArray(Access(x, []), Access(y, [])));
        var bothCompany = await CreateUserAsync(control, env, $"in.xy.cr.{tag}", "Works in X and Y, company roles", [],
            [new JsonObject { ["roleId"] = companyRole, ["companyId"] = x }, new JsonObject { ["roleId"] = companyRole, ["companyId"] = y }]);
        await PutAccessAsync(control, bothCompany, new JsonArray(Access(x, []), Access(y, [])));
        Target[] targets =
        [
            new(ControlTarget, inX, false),
            new(WorkspaceRoleTarget, bothWorkspace, true),
            new(CompanyRolesTarget, bothCompany, true),
        ];
        var fingerprint = await FingerprintSqlAsync(env);

        var writes = endpoints
            .Where(e => e.Method is "POST" or "PUT" or "PATCH" or "DELETE" && !e.IsAnonymous && e.RouteParameters.Count > 0)
            .Where(e => GuidParameter().Matches(e.Pattern).Count == e.RouteParameters.Count)
            .OrderBy(e => e.Method == "DELETE" ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        var reads = endpoints.Where(e => e.Method == "GET").Select(e => e.Pattern).ToHashSet(StringComparer.Ordinal);
        var variants = new List<Variant>();
        var n = 0;
        foreach (var target in targets)
        {
            foreach (var endpoint in writes)
            {
                var path = endpoint.Path(_ => target.Id.ToString());
                var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
                if (endpoint.Method == "DELETE" || schema is null)
                {
                    // Never proven (the control would destroy what it proves) and never aimed at the
                    // control target; judged by the targets' rows.
                    if (target.WorksInY)
                    {
                        variants.Add(new Variant(target, endpoint, path, "-", null, null, null));
                    }
                    continue;
                }
                var read = reads.Contains(endpoint.Pattern) ? await SharedCompanyRecords.ReadJsonAsync(control, path) : null;
                var properties = openApi.Resolve(schema.Value).TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                if (read is null || properties.ValueKind != JsonValueKind.Object)
                {
                    var body = G1WriteOracle.Valid(openApi, schema.Value, env, $"su{n++}", x.ToString());
                    var generic = new Variant(target, endpoint, path, "-", body, null, null);
                    if (await ProveAsync(env, control, fingerprint, generic, failures))
                    {
                        variants.Add(generic);
                    }
                    continue;
                }
                var template = SharedCompanyRecords.Template(openApi, schema.Value, read, env, $"su{n++}", x.ToString());
                foreach (var field in properties.EnumerateObject())
                {
                    if (field.Name == "version" || Change(openApi, field.Value, template[field.Name], x, y) is not { } changed)
                    {
                        continue;
                    }
                    var variant = new Variant(target, endpoint, path, field.Name, template, template[field.Name]?.DeepClone(), changed);
                    if (await ProveAsync(env, control, fingerprint, variant, failures))
                    {
                        variants.Add(variant);
                    }
                }
            }
        }

        var before = new Dictionary<Guid, string>();
        foreach (var target in targets.Where(t => t.WorksInY))
        {
            before[target.Id] = await FingerprintAsync(env, fingerprint, target.Id);
        }
        var sent = 0;
        var accepted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            var (status, text) = await SendAsync(attacker, variant, change: true);
            sent++;
            if (!variant.Target.WorksInY)
            {
                if (status is >= 200 and < 300)
                {
                    accepted.Add(variant.Source);
                }
                continue;
            }
            if (status >= 500)
            {
                failures.Add($"{label} → {variant.Source} aimed at {variant.Target.Label}: server error {status}: {Short(text)}");
            }
            else if (status is >= 200 and < 300 && variant.Endpoint.Method != "DELETE")
            {
                failures.Add($"{label} → {variant.Source} aimed at {variant.Target.Label}, while it works in X alone: answered {status}, expected a refusal");
            }
        }
        var changedTargets = new List<string>();
        foreach (var target in targets.Where(t => t.WorksInY))
        {
            if (await FingerprintAsync(env, fingerprint, target.Id) != before[target.Id])
            {
                changedTargets.Add(target.Label);
            }
        }
        var proven = targets.ToDictionary(t => t.Label,
            t => (IReadOnlyList<string>)variants.Where(v => v.Target == t && v.Endpoint.Method != "DELETE").Select(v => v.Source).Distinct().Order(StringComparer.Ordinal).ToList());
        return new Report(failures, changedTargets, proven, [.. accepted], sent);
    }

    /// <summary>The control sends the write: proven when it succeeds and changes the target's
    /// rows; a write built from the read is then undone with the value first read.</summary>
    private static async Task<bool> ProveAsync(ErpTestEnvironment env, HttpClient control, string fingerprint, Variant variant, List<string> problems)
    {
        var before = await FingerprintAsync(env, fingerprint, variant.Target.Id);
        var (status, _) = await SendAsync(control, variant, change: true);
        if (status is < 200 or >= 300)
        {
            return false;
        }
        var changed = await FingerprintAsync(env, fingerprint, variant.Target.Id) != before;
        if (variant.FromRead)
        {
            var (undo, undoText) = await SendAsync(control, variant, change: false);
            if (undo is < 200 or >= 300)
            {
                problems.Add($"{variant.Source} on {variant.Target.Label}: undoing the administrator's proving write answered {undo}: {Short(undoText)}");
            }
        }
        return changed;
    }

    /// <summary>A body from the read is rebuilt from the sender's own fresh read (its current
    /// version), with the variant's field changed or, to undo, set back to the value first read.</summary>
    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, Variant variant, bool change)
    {
        var body = variant.Template?.DeepClone() as JsonObject;
        if (variant.FromRead && body is not null)
        {
            if (await SharedCompanyRecords.ReadJsonAsync(client, variant.Path) is { } current && current["version"] is { } version)
            {
                body["version"] = version.DeepClone();
            }
            body[variant.Field] = (change ? variant.Changed : variant.Original)?.DeepClone();
        }
        using var request = new HttpRequestMessage(new HttpMethod(variant.Endpoint.Method), variant.Path);
        if (body is not null || variant.Endpoint.HasBody)
        {
            request.Content = new StringContent((body ?? []).ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Another value for one field, or null when it cannot be changed this way: a company
    /// (or other) id becomes company X, or Y where it is X already; a list loses its last item (the
    /// first list, depth first, holding two or more: a user's companies, a company's branches, its
    /// company roles); anything else as for the company's shared records.</summary>
    internal static JsonNode? Change(OpenApiDocument openApi, JsonElement leaf, JsonNode? value, Guid x, Guid y)
    {
        var resolved = openApi.Resolve(leaf);
        foreach (var combinator in new[] { "oneOf", "anyOf" })
        {
            if (resolved.TryGetProperty(combinator, out var options) && options.ValueKind == JsonValueKind.Array &&
                options.EnumerateArray().Where(o => !(o.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "null")).ToList() is [var only])
            {
                leaf = only;
                resolved = openApi.Resolve(only);
            }
        }
        if (resolved.TryGetProperty("format", out var format) && format.GetString() == "uuid")
        {
            return JsonValue.Create(value is JsonValue v && Guid.TryParse(v.ToString(), out var current) && current == x ? y : x);
        }
        if (value is JsonArray array || openApi.TypeOfSchema(leaf) == "array")
        {
            return value is JsonNode node && Shorten(node) is { } shorter ? shorter : null;
        }
        return SharedCompanyRecords.Change(openApi, leaf, value) is null ? null : SharedCompanyRecords.Change(null, default, value);
    }

    /// <summary>The node with the last item of its first list of two or more items removed (depth
    /// first), or null when it holds no such list.</summary>
    private static JsonNode? Shorten(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array when array.Count >= 2:
            {
                var copy = (JsonArray)array.DeepClone();
                copy.RemoveAt(copy.Count - 1);
                return copy;
            }
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item && Shorten(item) is { } shorter)
                    {
                        var copy = (JsonArray)array.DeepClone();
                        copy[i] = shorter;
                        return copy;
                    }
                }
                return null;
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (child is not null && Shorten(child) is { } shorter)
                    {
                        var copy = (JsonObject)obj.DeepClone();
                        copy[name] = shorter;
                        return copy;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>One statement fingerprinting every row of every tenant table (the audit trail left
    /// out) that carries the user's id as its own id or as <c>user_id</c>.</summary>
    private static async Task<string> FingerprintSqlAsync(ErpTestEnvironment env)
    {
        await using var admin = await env.OpenAdminAsync();
        var parts = new List<string>();
        foreach (var table in await DbCatalog.TenantTablesAsync(admin))
        {
            if (table.Schema == "audit") continue;
            var columns = await DbCatalog.ColumnsAsync(admin, table);
            var conditions = new[] { "id", "user_id" }.Where(c => columns.Any(col => col.Name == c && col.Type == "uuid")).Select(c => $"\"{c}\" = @u").ToList();
            if (conditions.Count == 0) continue;
            parts.Add($"'{table.Qualified}:' || (SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t " +
                      $"WHERE tenant_id = @t AND ({string.Join(" OR ", conditions)}))");
        }
        Assert.True(parts.Count >= 3, "the user fingerprint found too few tables carrying a user's id");
        return $"SELECT concat_ws(chr(10), {string.Join(", ", parts)})";
    }

    private static async Task<string> FingerprintAsync(ErpTestEnvironment env, string sql, Guid user)
    {
        await using var admin = await env.OpenAdminAsync();
        return await DbCatalog.ScalarAsync<string>(admin, sql, ("t", env.TenantA.Id), ("u", user));
    }

    private static JsonObject Access(Guid company, Guid[] branches) => new()
    {
        ["companyId"] = company,
        ["allBranches"] = branches.Length == 0,
        ["branchIds"] = new JsonArray(branches.Select(b => (JsonNode)JsonValue.Create(b)).ToArray()),
    };

    private static async Task<Guid> CreateUserAsync(HttpClient admin, ErpTestEnvironment env, string local, string name, Guid[] roles, JsonObject[] companyRoles)
    {
        var body = new JsonObject
        {
            ["email"] = $"{local}@{env.TenantA.EmailDomain}",
            ["displayName"] = name,
            ["language"] = "en",
            ["password"] = ErpTestEnvironment.Password,
            ["mustChangePassword"] = false,
            ["roleIds"] = new JsonArray(roles.Select(r => (JsonNode)JsonValue.Create(r)).ToArray()),
            ["companyRoles"] = new JsonArray(companyRoles.Select(c => (JsonNode)c).ToArray()),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/users") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await admin.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"creating the target {local} answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task PutAccessAsync(HttpClient admin, Guid user, JsonArray companies)
    {
        var version = await CompanyAttack.AccessVersionAsync(admin, user);
        using var response = await admin.PutAsJsonAsync($"/api/tenancy/access/{user}", new JsonObject { ["companies"] = companies, ["version"] = version });
        Assert.True(response.IsSuccessStatusCode, $"giving the target {user} its companies answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";

    [GeneratedRegex(@"\{[A-Za-z_][A-Za-z0-9_]*:guid\}")]
    private static partial Regex GuidParameter();
}
