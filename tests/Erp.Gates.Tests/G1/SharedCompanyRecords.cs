using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, branch layer: the records every branch of a company shares (critic p02 round 4, plant P7:
/// with the "needs every branch" refusal removed from the company update, an administrator limited
/// to one warehouse renamed the legal entity, changed its tax registration and deactivated it for
/// every branch, and every gate passed, because the branch attack's victim was only another
/// branch's own rows).
///
/// The shared records are the rows of every table whose entity is <see cref="ICompanyWide"/>, in
/// any module (found in the running app's models, so a later module's company-wide table is
/// attacked without anyone listing it), for the attacker's own company X. Every write endpoint whose
/// route takes ids receives company X's id; a body is built from what the endpoint's own read
/// answers for X (one changed field at a time: each text, code, choice, number and switch), or a
/// valid body where there is no read. Each such write is first sent by the tenant's administrator,
/// who works in every branch: a write it completes, and that changes X's shared rows, is proven
/// valid and is undone. Then, after the rest of the branch attack, the branch-limited administrator
/// sends every proven write (and every delete): each must be refused, and X's shared rows must be
/// exactly as they were.
/// </summary>
public static partial class SharedCompanyRecords
{
    public sealed record Report(
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> ChangedTables,
        IReadOnlyList<string> Sources,
        IReadOnlyList<string> Tables,
        int Writes);

    /// <summary>One write to company X: <c>Field</c> is the changed field ("-" for a delete or a
    /// generated body), <c>FromRead</c> when the body is the endpoint's own read with that field changed.</summary>
    public sealed record Variant(ApiEndpoint Endpoint, string Path, string Field, bool FromRead, JsonObject? Template, bool Proven)
    {
        /// <summary>The administrator's own write changed X's shared rows: the attacker's must be refused.</summary>
        public bool ChangesShared { get; init; }

        public string Source => $"{Endpoint} [{Field}]";

        /// <summary>A write to the workspace's own record (no company id in its route).</summary>
        public bool Workspace { get; init; }
    }

    /// <param name="Tables">Company X's shared tables (<see cref="ICompanyWide"/>), empty when only the
    /// workspace's records are attacked. <see cref="WorkspaceTables"/>: the tables every company
    /// shares (<see cref="IWorkspaceWide"/>).</param>
    public sealed record Prepared(Guid Tenant, Guid Company, IReadOnlyList<TableRef> Tables, IReadOnlyList<Variant> Variants,
        IReadOnlyDictionary<string, string> Before, IReadOnlyList<string> Problems)
    {
        public IReadOnlyList<TableRef> WorkspaceTables { get; init; } = [];
    }

    /// <summary>The tables of every <see cref="ICompanyWide"/> entity of every module.</summary>
    public static async Task<IReadOnlyList<TableRef>> TablesAsync(IServiceProvider services)
    {
        var catalog = services.GetRequiredService<ModuleCatalog>();
        await using var scope = services.CreateAsyncScope();
        return catalog.DbContexts
            .SelectMany(t => ((DbContext)scope.ServiceProvider.GetRequiredService(t)).Model.GetEntityTypes())
            .Where(e => typeof(ICompanyWide).IsAssignableFrom(e.ClrType) && e.GetTableName() is not null)
            .Select(e => new TableRef(e.GetSchema() ?? "public", e.GetTableName()!))
            .Distinct()
            .OrderBy(t => t.Qualified, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The tables of every <see cref="IWorkspaceWide"/> entity of every module: the records
    /// every company of the workspace shares (critic p02 round 6: an administrator limited to one
    /// company, or one branch, renamed the workspace for every company and no gate looked).</summary>
    public static async Task<IReadOnlyList<TableRef>> WorkspaceTablesAsync(IServiceProvider services)
    {
        var catalog = services.GetRequiredService<ModuleCatalog>();
        await using var scope = services.CreateAsyncScope();
        return catalog.DbContexts
            .SelectMany(t => ((DbContext)scope.ServiceProvider.GetRequiredService(t)).Model.GetEntityTypes())
            .Where(e => typeof(IWorkspaceWide).IsAssignableFrom(e.ClrType) && e.GetTableName() is not null)
            .Select(e => new TableRef(e.GetSchema() ?? "public", e.GetTableName()!))
            .Distinct()
            .OrderBy(t => t.Qualified, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A fingerprint of company X's rows in every shared table and of the tenant's rows in
    /// every workspace-wide table (superuser read).</summary>
    public static async Task<Dictionary<string, string>> SnapshotAsync(ErpTestEnvironment env, Guid tenant, Guid company, IReadOnlyList<TableRef> tables,
        IReadOnlyList<TableRef>? workspaceTables = null)
    {
        await using var admin = await env.OpenAdminAsync();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            result[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE tenant_id = @t AND company_id = @c",
                ("t", tenant), ("c", company));
        }
        foreach (var table in workspaceTables ?? [])
        {
            result[table.Qualified] = await DbCatalog.ScalarAsync<string>(admin,
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM {table.Qualified} t WHERE tenant_id = @t",
                ("t", tenant));
        }
        return result;
    }

    private static IReadOnlyList<string> Changed(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        before.Keys.Union(after.Keys).Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k)).Order(StringComparer.Ordinal).ToList();

    /// <summary>Builds every write to company X and proves each with <paramref name="control"/> (an
    /// administrator who works in every branch of X), undoing what it changed; then fingerprints
    /// X's shared rows.</summary>
    /// <param name="companyRecords">False in the company attack: its attacker works in every branch
    /// of company X, so only the records every company shares are attacked.</param>
    public static async Task<Prepared> PrepareAsync(ErpTestEnvironment env, OpenApiDocument openApi, IReadOnlyList<ApiEndpoint> endpoints,
        HttpClient control, Guid tenant, Guid company, bool companyRecords = true)
    {
        var tables = companyRecords ? await TablesAsync(env.Factory.Services) : [];
        var workspaceTables = await WorkspaceTablesAsync(env.Factory.Services);
        var problems = new List<string>();
        var variants = new List<Variant>();
        var reads = endpoints.Where(e => e.Method == "GET").Select(e => e.Pattern).ToHashSet(StringComparer.Ordinal);
        var id = company.ToString();
        var n = 0;
        // The workspace's own record: every write without route parameters that has a read at the
        // same address (a single record, such as the workspace and its settings), its body built from
        // that read with one field changed at a time. Proven when the tenant's administrator's write
        // changes a workspace-wide table (a user's own workplace or preferences change none).
        foreach (var endpoint in endpoints.Where(e => e.Method is "PUT" or "PATCH" && !e.IsAnonymous && e.RouteParameters.Count == 0 && reads.Contains(e.Pattern))
                     .OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema ||
                !openApi.Resolve(schema).TryGetProperty("properties", out var properties) ||
                await ReadJsonAsync(control, endpoint.Pattern) is not { } read)
            {
                continue;
            }
            var template = Template(openApi, schema, read, env, $"sw{n++}", id);
            foreach (var field in properties.EnumerateObject())
            {
                if (field.Name == "version" || Change(openApi, field.Value, template[field.Name]) is null)
                {
                    continue;
                }
                variants.Add(await ProveAsync(env, control, tenant, company, tables, workspaceTables,
                    new Variant(endpoint, endpoint.Pattern, field.Name, true, template, false) { Workspace = true }, problems));
            }
        }
        foreach (var endpoint in endpoints.Where(e => companyRecords && e.Method is "POST" or "PUT" or "PATCH" or "DELETE" && !e.IsAnonymous && e.RouteParameters.Count > 0)
                     .Where(e => GuidParameter().Matches(e.Pattern).Count == e.RouteParameters.Count)
                     .OrderBy(e => e.Method == "DELETE" ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            var path = endpoint.Path(_ => id);
            var schema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            if (endpoint.Method == "DELETE" || schema is null)
            {
                // Never proven (the control would destroy what it proves); judged by the fingerprint.
                variants.Add(new Variant(endpoint, path, "-", false, null, Proven: true));
                continue;
            }
            var properties = openApi.Resolve(schema.Value).TryGetProperty("properties", out var p) ? p : default;
            JsonObject? read = null;
            if (reads.Contains(endpoint.Pattern))
            {
                read = await ReadJsonAsync(control, path);
            }
            if (read is null || properties.ValueKind != JsonValueKind.Object)
            {
                var body = G1WriteOracle.Valid(openApi, schema.Value, env, $"sh{n++}", id);
                variants.Add(await ProveAsync(env, control, tenant, company, tables, workspaceTables, new Variant(endpoint, path, "-", false, body, false), problems));
                continue;
            }
            var template = Template(openApi, schema.Value, read, env, $"sh{n++}", id);
            foreach (var field in properties.EnumerateObject())
            {
                if (field.Name == "version" || Change(openApi, field.Value, template[field.Name]) is null)
                {
                    continue;
                }
                variants.Add(await ProveAsync(env, control, tenant, company, tables, workspaceTables, new Variant(endpoint, path, field.Name, true, template, false), problems));
            }
        }
        var before = await SnapshotAsync(env, tenant, company, tables, workspaceTables);
        return new Prepared(tenant, company, tables, variants, before, problems) { WorkspaceTables = workspaceTables };
    }

    /// <summary>The control sends the write: proven when it succeeds and changes X's shared rows;
    /// a write built from the read is then undone with the values first read.</summary>
    private static async Task<Variant> ProveAsync(ErpTestEnvironment env, HttpClient control, Guid tenant, Guid company, IReadOnlyList<TableRef> tables,
        IReadOnlyList<TableRef> workspaceTables, Variant variant, List<string> problems)
    {
        var before = await SnapshotAsync(env, tenant, company, tables, workspaceTables);
        var (status, _) = await SendAsync(control, variant, change: true);
        if (status is < 200 or >= 300)
        {
            return variant;
        }
        var changed = Changed(before, await SnapshotAsync(env, tenant, company, tables, workspaceTables)).Count > 0;
        if (variant.FromRead)
        {
            var (undo, undoText) = await SendAsync(control, variant, change: false);
            if (undo is < 200 or >= 300)
            {
                problems.Add($"{variant.Source}: undoing the administrator's proving write answered {undo}: {Short(undoText)}");
            }
        }
        return variant with { Proven = changed, ChangesShared = changed };
    }

    /// <summary>After the rest of the branch attack: the branch-limited administrator sends every
    /// proven write and every delete to company X.</summary>
    public static async Task<Report> AttackAsync(ErpTestEnvironment env, OpenApiDocument openApi, Prepared prepared, HttpClient attacker, string label)
    {
        var failures = new List<string>(prepared.Problems);
        var writes = 0;
        foreach (var variant in prepared.Variants.Where(v => v.Proven))
        {
            var (status, text) = await SendAsync(attacker, variant, change: true);
            writes++;
            if (variant.ChangesShared && variant.Workspace && status is >= 200 and < 300)
            {
                failures.Add($"{label} → {variant.Source} on the workspace every company shares, while it works in only some of its companies or branches: answered {status}, expected a refusal");
            }
            else if (variant.ChangesShared && status is >= 200 and < 300)
            {
                failures.Add($"{label} → {variant.Source} on company X, which it works in only one branch of: answered {status}, expected a refusal");
            }
            else if (variant.ChangesShared && status == 403 && ProblemCode(text) == CrossBranchWriteException.WorkspaceCode)
            {
                failures.Add($"{label} → {variant.Source}: refused only by the last layer (the kernel's workspace-wide row guard); the endpoint itself does not check that the caller works in every company and branch");
            }
            else if (variant.ChangesShared && status == 403 && ProblemCode(text) == CrossBranchWriteException.DefaultCode)
            {
                // Refused by the kernel's guard on company-wide rows alone: that guard is the second
                // layer, never the only one; the endpoint must check the caller's branches itself.
                failures.Add($"{label} → {variant.Source}: refused only by the last layer (the kernel's company-wide row guard); the endpoint itself does not check that the caller works in every branch of company X");
            }
            else if (status >= 500)
            {
                failures.Add($"{label} → {variant.Source}: server error {status}: {Short(text)}");
            }
        }
        var after = await SnapshotAsync(env, prepared.Tenant, prepared.Company, prepared.Tables, prepared.WorkspaceTables);
        return new Report(failures, Changed(prepared.Before, after), prepared.Variants.Where(v => v.Proven).Select(v => v.Source).Distinct().Order(StringComparer.Ordinal).ToList(),
            prepared.Tables.Concat(prepared.WorkspaceTables).Select(t => t.Qualified).ToList(), writes);
    }

    /// <summary>Sends the write: a body from the read is rebuilt from a fresh read by the sender
    /// (its current version), with the variant's field changed or, to undo, set back to the value
    /// first read.</summary>
    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, Variant variant, bool change)
    {
        JsonObject? body = variant.Template?.DeepClone() as JsonObject;
        if (variant.FromRead && body is not null)
        {
            if (await ReadJsonAsync(client, variant.Path) is { } current && current["version"] is { } version)
            {
                body["version"] = version.DeepClone();
            }
            if (change)
            {
                body[variant.Field] = Change(null, default, body[variant.Field]);
            }
        }
        using var request = new HttpRequestMessage(new HttpMethod(variant.Endpoint.Method), variant.Path);
        if (body is not null || variant.Endpoint.HasBody)
        {
            request.Content = new StringContent((body ?? []).ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The read's JSON object, or null (not found, refused, or not JSON: an image, a file).</summary>
    private static async Task<JsonObject?> ReadJsonAsync(HttpClient client, string path)
    {
        using var answer = await client.GetAsync(path);
        if (answer.StatusCode != HttpStatusCode.OK || answer.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.Ordinal) != true)
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(await answer.Content.ReadAsStringAsync()) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The request body as the read answers it: every documented field the read carries,
    /// a valid value for the rest.</summary>
    private static JsonObject Template(OpenApiDocument openApi, JsonElement schema, JsonObject read, ErpTestEnvironment env, string tag, string company)
    {
        var body = G1WriteOracle.Valid(openApi, schema, env, tag, company);
        foreach (var (name, _) in body.ToList())
        {
            if (read.ContainsKey(name))
            {
                body[name] = read[name]?.DeepClone();
            }
        }
        return body;
    }

    /// <summary>A different valid-looking value for one field, or null when the field cannot be
    /// changed this way: a switch flips, a number moves by one, a choice takes another choice, a
    /// patterned text (a code, a tax number, a phone) gets another value of the same shape, and a
    /// free text gets a suffix. With a schema, it only says whether the field is changeable.</summary>
    private static JsonNode? Change(OpenApiDocument? openApi, JsonElement leaf, JsonNode? value)
    {
        if (openApi is not null)
        {
            var resolved = openApi.Resolve(leaf);
            var type = openApi.TypeOfSchema(leaf);
            if (resolved.TryGetProperty("format", out var format) && format.GetString() is "uuid" or "date-time" or "date")
            {
                return null;
            }
            if (type is not ("string" or "boolean" or "integer") && !resolved.TryGetProperty("enum", out _))
            {
                return null;
            }
        }
        return value switch
        {
            JsonValue v when v.TryGetValue<bool>(out var b) => JsonValue.Create(!b),
            JsonValue v when v.TryGetValue<int>(out var i) => JsonValue.Create(i > 1 ? i - 1 : i + 1),
            JsonValue v when v.TryGetValue<string>(out var text) && text.Length > 0 => JsonValue.Create(Other(text)),
            _ => null,
        };
    }

    /// <summary>Another value of the same kind: a code-like or numeric text gets the same shape
    /// (letters for letters, digits for digits), a word choice another known choice, a free text a
    /// suffix (kept within the usual lengths).</summary>
    private static string Other(string text)
    {
        if (Choices.FirstOrDefault(c => c.Contains(text, StringComparer.Ordinal)) is { } choices)
        {
            return choices.First(c => c != text);
        }
        if (text.Any(char.IsWhiteSpace) || text.Any(c => c > 127))
        {
            return text.Length > 180 ? text[..180] + " B" : text + " B";
        }
        return CompanyAttack.Reshape(text);
    }

    /// <summary>Word choices (enumerations serialised as camel-case text) whose read value must be
    /// swapped for another member, not reshaped.</summary>
    private static readonly string[][] Choices =
    [
        ["abuDhabi", "dubai", "sharjah", "ajman", "ummAlQuwain", "rasAlKhaimah", "fujairah"],
        ["en", "ar"],
        ["monday", "sunday", "saturday"],
    ];

    private static string? ProblemCode(string text)
    {
        try
        {
            return JsonNode.Parse(text)?["code"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";

    [GeneratedRegex(@"\{[A-Za-z_][A-Za-z0-9_]*:guid\}")]
    private static partial Regex GuidParameter();
}
