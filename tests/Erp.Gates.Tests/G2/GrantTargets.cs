using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, who is "stronger" than the caller. Aiming an access check only at a record that grants
/// everything (the Administrator, a role holding the whole catalogue) cannot tell a correct check
/// from one that compares only part of the grants: a check that looks only at identity
/// permissions still refuses the Administrator, because the Administrator also holds identity
/// permissions the caller lacks (critic p03 round 3, plants P14 and P16: a helpdesk clerk took
/// over a workspace manager who held only tenancy permissions). So every acting-on-a-record and
/// granting probe is also aimed at targets whose grants the caller does not hold in every shape a
/// narrowed comparison could miss:
/// <list type="bullet">
/// <item><b>one permission alone</b>, for every permission of the catalogue the caller lacks (a
/// check narrowed to any module, resource, action or prefix misses at least one of them);</item>
/// <item><b>a whole module</b>, for every module of the catalogue: every permission of that module
/// the caller lacks, disjoint from the caller's own grants (the shape of a finance manager or a
/// purchasing approver next to an identity clerk);</item>
/// <item><b>partial overlap</b>, for every module: the caller's own permissions plus one of that
/// module's they lack (a check that is satisfied by any overlap, or by the larger count).</item>
/// </list>
/// The catalogue is the running app's, which in the gates' own environments includes a module of a
/// later wave (<see cref="LaterLedgerModule"/>), the way <c>ModulePermissionsTests</c> hosts one.
/// </summary>
public static class GrantTargets
{
    public enum Shape { Single, Module, Overlap }

    public sealed record Target(Shape Kind, string Label, IReadOnlyList<string> Permissions)
    {
        public string Key => string.Join(",", Permissions.Order(StringComparer.Ordinal));

        public override string ToString() => Label;
    }

    public static string ModuleOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];

    /// <summary>Every target for a caller holding <paramref name="caller"/>; each one grants at
    /// least one permission the caller lacks.</summary>
    public static IReadOnlyList<Target> For(IEnumerable<string> catalogue, IReadOnlyCollection<string> caller)
    {
        var all = catalogue.Distinct().Order(StringComparer.Ordinal).ToList();
        var lacking = all.Where(p => !caller.Contains(p)).ToList();
        var targets = new List<Target>();
        foreach (var p in lacking)
        {
            targets.Add(new Target(Shape.Single, $"a record granting only {p}", [p]));
        }
        foreach (var module in all.Select(ModuleOf).Distinct().Order(StringComparer.Ordinal))
        {
            var missing = lacking.Where(p => ModuleOf(p) == module).ToList();
            if (missing.Count == 0)
            {
                continue;
            }
            if (missing.Count > 1)
            {
                targets.Add(new Target(Shape.Module, $"a record granting every {module} permission the caller lacks (none the caller holds)", missing));
            }
            targets.Add(new Target(Shape.Overlap, $"a record granting the caller's own permissions plus {missing[0]}",
                [.. caller.Where(all.Contains), missing[0]]));
        }
        return targets;
    }

    /// <summary>One target per module the caller lacks a permission of: what single-field
    /// requests are aimed at besides the record granting everything.</summary>
    public static IReadOnlyList<Target> PerModule(IEnumerable<string> catalogue, IReadOnlyCollection<string> caller) =>
        For(catalogue, caller).Where(t => t.Kind != Shape.Overlap)
            .GroupBy(t => ModuleOf(t.Permissions[0]))
            .Select(g => g.FirstOrDefault(t => t.Kind == Shape.Module) ?? g.First())
            .ToList();
}

/// <summary>Roles and users made by the administrator for <see cref="GrantTargets"/>, created once
/// per permission set and reused (a correct product never changes them; a faulty one is reported
/// the first time).</summary>
public sealed class TargetRecords(HttpClient admin, ErpTestEnvironment env)
{
    private readonly Dictionary<string, Guid> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _users = new(StringComparer.Ordinal);
    private int _n;

    /// <summary>A role granting exactly <paramref name="permissions"/>.</summary>
    public async Task<Guid> RoleAsync(IReadOnlyList<string> permissions)
    {
        var key = string.Join(",", permissions.Distinct().Order(StringComparer.Ordinal));
        if (_roles.TryGetValue(key, out var id))
        {
            return id;
        }
        var tag = Tag();
        id = await CreatedIdAsync("/api/identity/roles", new JsonObject
        {
            ["nameEn"] = $"G2 target {tag}",
            ["nameAr"] = $"هدف {tag}",
            ["permissions"] = new JsonArray(permissions.Distinct().Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
        });
        _roles[key] = id;
        return id;
    }

    /// <summary>An invited user (never signed in) holding exactly a role granting <paramref name="permissions"/>.</summary>
    public async Task<Guid> UserAsync(IReadOnlyList<string> permissions)
    {
        var key = string.Join(",", permissions.Distinct().Order(StringComparer.Ordinal));
        if (_users.TryGetValue(key, out var id))
        {
            return id;
        }
        var role = await RoleAsync(permissions);
        var tag = Tag();
        id = await CreatedIdAsync("/api/identity/users", new JsonObject
        {
            ["email"] = $"g2.target.{tag}@{env.TenantA.EmailDomain}",
            ["displayName"] = $"G2 target {tag}",
            ["language"] = "en",
            ["roleIds"] = new JsonArray(JsonValue.Create(role)),
        });
        await GivePasskeyAsync(id);
        _users[key] = id;
        return id;
    }

    private readonly Dictionary<string, Guid> _companyUsers = new(StringComparer.Ordinal);

    /// <summary>An invited user holding a role granting exactly <paramref name="permissions"/> in
    /// <paramref name="companyId"/> only, and no role in the whole workspace (critic p03 round 5,
    /// finding R1: a company manager whose roles are all per company), with access to
    /// <paramref name="companies"/>' companies. With <paramref name="fresh"/>, a new user every time, never handed out again.</summary>
    public async Task<Guid> UserInCompanyAsync(IReadOnlyList<string> permissions, Guid companyId, GateCompanies companies, bool fresh = false)
    {
        var key = string.Join(",", permissions.Distinct().Order(StringComparer.Ordinal)) + "@" + companyId;
        if (!fresh && _companyUsers.TryGetValue(key, out var id))
        {
            return id;
        }
        var role = await RoleAsync(permissions);
        var tag = Tag();
        id = await CreatedIdAsync("/api/identity/users", new JsonObject
        {
            ["email"] = $"g2.incompany.{tag}@{env.TenantA.EmailDomain}",
            ["displayName"] = $"G2 company target {tag}",
            ["language"] = "en",
            ["roleIds"] = new JsonArray(),
            ["companyRoles"] = new JsonArray(GateCompanies.CompanyRole(role, companyId)),
        });
        await companies.GiveAccessAsync(id);
        await GivePasskeyAsync(id);
        if (!fresh)
        {
            // A fresh user may be changed or removed by its request; later lookups get the shared one.
            _companyUsers[key] = id;
        }
        return id;
    }

    /// <summary>Gives the user a passkey (written straight into the database: an invited user
    /// cannot sign in to add one), so a request that removes another user's way to sign in shows in
    /// the user as the administrator reads them (critic p03 round 8: a lost device kept signing in;
    /// a refused request must not quietly take a stronger user's passkeys either).</summary>
    public async Task GivePasskeyAsync(Guid userId)
    {
        await using var connection = await env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "INSERT INTO identity.passkeys (id, tenant_id, user_id, credential_id, public_key, algorithm, name, sign_count, backup_eligible, backed_up, transports) " +
            "SELECT @id, u.tenant_id, u.id, @credential, @key, -7, @name, 0, false, false, ARRAY['internal'] FROM identity.users u WHERE u.id = @user", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("credential", Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray());
        command.Parameters.AddWithValue("key", Guid.NewGuid().ToByteArray());
        command.Parameters.AddWithValue("name", $"G2 key {Tag()}");
        command.Parameters.AddWithValue("user", userId);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException($"no user {userId} to give a passkey");
        }
    }

    private string Tag() => $"{++_n}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8].ToLowerInvariant()}";

    private async Task<Guid> CreatedIdAsync(string path, JsonObject body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await admin.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} as administrator answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }
}
