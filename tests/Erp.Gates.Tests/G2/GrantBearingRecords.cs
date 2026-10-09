using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, acting on a record that grants access. A record is grant-bearing when its collection is
/// created with a grant field (<c>roleIds</c>, <c>permissions</c>): a role, a user, anything a
/// later module adds that hands out access. Every endpoint that acts on one such record (anything
/// but GET under <c>collection/{id}</c>: edit, delete, copy, reset, end sessions, clear a pause) is
/// found from the running app's routing and OpenAPI document, not from a list. A caller holding
/// exactly that endpoint's permission (plus reading roles and users) aims it at a record granting
/// everything (the Administrator role for users, every permission in the catalogue for roles):
/// the answer must be 403 and the record must read back exactly as before (still there after a
/// delete). The same request aimed at a record granting only what the caller holds must succeed,
/// which proves the 403 came from the grant check. Without this, a user who may only delete roles
/// removes roles granting what they do not hold (critic p03 round 1, plant P2), which is how a
/// junior administrator strips the finance team of its access.
/// </summary>
public static class GrantBearingRecords
{
    /// <param name="FieldVariants">Every single-field request sent, as "endpoint [field changed]"
    /// or "endpoint [field left out]" (see <see cref="G2.FieldVariants"/>).</param>
    /// <param name="PartialTargets">Requests aimed at records granting what the caller lacks
    /// without granting everything (<see cref="GrantTargets"/>).</param>
    /// <param name="ModuleFieldVariants">Single-field requests aimed at records granting one other module.</param>
    /// <param name="CompanyCallerTargets">Requests by callers whose grants come from a role in one
    /// company, aimed at records granting what they hold there, held elsewhere (see <see cref="CompanyCallers"/>).</param>
    /// <param name="CompanyCallerChecked">Endpoints those callers were aimed with.</param>
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Checked, IReadOnlyList<string>? FieldVariants = null,
        int PartialTargets = 0, int ModuleFieldVariants = 0, int CompanyCallerTargets = 0, IReadOnlyList<string>? CompanyCallerChecked = null);

    public static async Task<Result> RunAsync(ErpTestEnvironment env)
    {
        var problems = new List<string>();
        var checkedEndpoints = new List<string>();
        var fieldVariants = new List<string>();
        var partialTargets = 0;
        var moduleFieldVariants = 0;
        var companyCallerTargets = 0;
        var companyCallerChecked = new List<string>();
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var everything = catalog.PermissionKeys.Order(StringComparer.Ordinal).ToList();
        using var admin = await env.SignInAsync(env.Email(env.TenantA, "admin"));
        var administratorRole = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var targetRoles = new TargetRecords(admin, env);
        var companies = await GateCompanies.OfAsync(admin);
        JsonNode? CompanyValue(string field, JsonNode? current) => GateCompanies.IsCompanyField(field) ? companies.Other(current) : null;

        var endpoints = EndpointInventory.From(env.Factory.Services);
        var families = endpoints
            .Where(e => e.Method == "POST" && !e.IsAnonymous && e.RouteParameters.Count == 0)
            .Select(e => (Create: e, Schema: openApi.RequestSchema(e.Method, e.Pattern)))
            .Where(x => x.Schema is { } s && openApi.Resolve(s).TryGetProperty("properties", out var p) && GrantEscalation.GrantFields.Any(f => p.TryGetProperty(f, out _)))
            .Select(x => (x.Create, Schema: x.Schema!.Value))
            .ToList();

        var n = 0;
        foreach (var (create, createSchema) in families)
        {
            // Records of this family granting what a caller lacks, created once per grant set.
            var familyTargets = new Dictionary<string, Guid>(StringComparer.Ordinal);
            async Task<Guid> TargetAsync(GrantTargets.Target target)
            {
                if (familyTargets.TryGetValue(target.Key, out var existing))
                {
                    return existing;
                }
                var body = GrantEscalation.ValidBody(openApi, createSchema, env, $"t{familyTargets.Count}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant()}");
                GrantEscalation.SetGrants(body, body.ContainsKey("roleIds") ? [await targetRoles.RoleAsync(target.Permissions)] : [], target.Permissions);
                return familyTargets[target.Key] = await CreatedIdAsync(admin, create.Pattern, body);
            }
            var prefix = create.Pattern.TrimEnd('/') + "/{";
            var acting = endpoints.Where(e => e.Method is not ("GET" or "HEAD" or "OPTIONS") && !e.IsAnonymous && e.Pattern.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var endpoint in acting)
            {
                n++;
                var tag = $"{n}{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant()}";
                var callerPermissions = new[] { endpoint.Permission, "identity.roles.read", "identity.users.read" }.Distinct().ToArray();
                var callerRole = await CreatedIdAsync(admin, "/api/identity/roles",
                    new JsonObject { ["nameEn"] = $"G2 holder {tag}", ["nameAr"] = $"حامل {tag}", ["permissions"] = new JsonArray(callerPermissions.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()) });
                var email = $"g2.holder.{tag}@{env.TenantA.EmailDomain}";
                var callerId = await CreatedIdAsync(admin, "/api/identity/users",
                    new JsonObject { ["email"] = email, ["displayName"] = $"G2 holder {tag}", ["language"] = "en", ["password"] = ErpTestEnvironment.Password, ["mustChangePassword"] = false, ["roleIds"] = new JsonArray(JsonValue.Create(callerRole)) });
                // The caller works in the companies roles in one company and default companies name.
                await companies.GiveAccessAsync(callerId);
                using var caller = await env.SignInAsync(email);

                // A record granting everything.
                var strongBody = GrantEscalation.ValidBody(openApi, createSchema, env, $"{tag}s");
                GrantEscalation.SetGrants(strongBody, [administratorRole], everything);
                var strong = await CreatedIdAsync(admin, create.Pattern, strongBody);
                var strongPath = endpoint.Path(_ => strong.ToString());
                var itemPath = create.Pattern.TrimEnd('/') + "/" + strong;
                var before = await ReadAsync(admin, itemPath);

                var (status, text) = await SendAsync(caller, endpoint.Method, strongPath, await BodyAsync(admin, openApi, endpoint, itemPath, $"{tag}x"));
                if (status != (int)HttpStatusCode.Forbidden)
                {
                    problems.Add($"{endpoint}: aimed at a record granting everything by a user holding only [{string.Join(", ", callerPermissions)}] answered {status} (expected 403): {Short(text)}");
                }
                var after = await ReadAsync(admin, itemPath);
                if (after != before)
                {
                    problems.Add($"{endpoint}: the record granting everything changed: before {Short(before)}; after {Short(after)}");
                }

                // Control: a record granting only what the caller holds.
                var weakBody = GrantEscalation.ValidBody(openApi, createSchema, env, $"{tag}w");
                GrantEscalation.SetGrants(weakBody, [], [endpoint.Permission]);
                var weak = await CreatedIdAsync(admin, create.Pattern, weakBody);
                await companies.GiveAccessAsync(weak);
                var weakItem = create.Pattern.TrimEnd('/') + "/" + weak;
                var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => weak.ToString()), await BodyAsync(admin, openApi, endpoint, weakItem, $"{tag}c"));
                if (controlStatus is < 200 or >= 300)
                {
                    problems.Add($"{endpoint}: aimed at a record granting only what the caller holds answered {controlStatus}, so the gate cannot tell a grant check from a malformed request: {Short(controlText)}");
                }
                checkedEndpoints.Add(endpoint.Key);

                // Records granting what the caller lacks without granting everything.
                var g = 0;
                foreach (var target in GrantTargets.For(everything, callerPermissions))
                {
                    var victim = await TargetAsync(target);
                    var victimItem = create.Pattern.TrimEnd('/') + "/" + victim;
                    var victimBefore = await ReadAsync(admin, victimItem);
                    var (targetStatus, targetText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => victim.ToString()), await BodyAsync(admin, openApi, endpoint, victimItem, $"{tag}g{++g}"));
                    if (targetStatus != (int)HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint}: aimed at {target} by a user holding only [{string.Join(", ", callerPermissions)}] answered {targetStatus} (expected 403): {Short(targetText)}");
                    }
                    var victimAfter = await ReadAsync(admin, victimItem);
                    if (victimAfter != victimBefore)
                    {
                        problems.Add($"{endpoint}: {target} changed: before {Short(victimBefore)}; after {Short(victimAfter)}");
                    }
                    partialTargets++;
                }

                // A caller whose grants all come from a role held in company X (critic p03 round 7,
                // plant Pf: a role edit that took grants held in ONE company as held everywhere let a
                // role manager of one company rewrite a role staff of every company hold, and passed
                // every gate, because every caller above holds workspace-wide roles). A record that
                // grants access directly (a role: its permissions) is defined for the whole workspace
                // and may be held in any company, so a caller who holds what it grants in X alone may
                // not change, delete or copy it while it is held in every company or in Y: 403, the
                // record unchanged, its permissions emptied of one alone too. The control, a record
                // granting nothing held in X, is accepted.
                if (createSchema.TryGetProperty("properties", out var createProperties) && createProperties.TryGetProperty("permissions", out _))
                {
                    var (aimed, found) = await CompanyCallerAsync(env, admin, openApi, companies, create, endpoint, everything, callerPermissions, tag);
                    companyCallerTargets += aimed;
                    problems.AddRange(found);
                    if (aimed > 0)
                    {
                        companyCallerChecked.Add(endpoint.Key);
                    }
                }

                // One field at a time: the same endpoint, each writable property changed alone and
                // left out alone, aimed at the record granting everything and at a record granting
                // one other module (a path-specific check narrowed to some modules) and, as the
                // control, at the record granting only what the caller holds.
                if (endpoint.HasBody && openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is { } schema)
                {
                    var moduleTargets = new List<(Guid Id, string Label)>();
                    foreach (var target in GrantTargets.PerModule(everything, callerPermissions))
                    {
                        moduleTargets.Add((await TargetAsync(target), target.Label));
                    }
                    var m = 0;
                    foreach (var spec in FieldVariants.Specs(openApi, schema))
                    {
                        foreach (var (moduleTarget, label) in moduleTargets)
                        {
                            m++;
                            var targetItem = create.Pattern.TrimEnd('/') + "/" + moduleTarget;
                            var targetVariant = FieldVariants.Apply(openApi, schema, spec, await BaseBodyAsync(admin, openApi, endpoint, targetItem, $"{tag}m{m}"), $"{tag}m{m}", env.TenantA.EmailDomain,
                                (field, current) => Stronger(field, current, administratorRole, everything, companies.First), CompanyValue);
                            if (targetVariant is null)
                            {
                                problems.Add($"{endpoint} {spec}: the gate has no different valid value for this field aimed at {label}; extend FieldVariants rather than leave the field untested");
                                continue;
                            }
                            var targetBefore = await ReadAsync(admin, targetItem);
                            var (variantStatus, variantText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => moduleTarget.ToString()), targetVariant);
                            var targetAfter = await ReadAsync(admin, targetItem);
                            if (variantStatus is >= 200 and < 300)
                            {
                                problems.Add($"{endpoint} {spec}: aimed at {label} by a user holding only [{string.Join(", ", callerPermissions)}] answered {variantStatus} (expected 403): {Short(variantText)}");
                            }
                            if (targetAfter != targetBefore)
                            {
                                problems.Add($"{endpoint} {spec}: {label} changed: before {Short(targetBefore)}; after {Short(targetAfter)}");
                            }
                            moduleFieldVariants++;
                        }
                    }

                    var k = 0;
                    foreach (var spec in FieldVariants.Specs(openApi, schema))
                    {
                        k++;
                        var strongVariant = FieldVariants.Apply(openApi, schema, spec, await BaseBodyAsync(admin, openApi, endpoint, itemPath, $"{tag}s{k}"), $"{tag}s{k}", env.TenantA.EmailDomain,
                            (field, current) => Stronger(field, current, administratorRole, everything, companies.First), CompanyValue);
                        var weakVariant = FieldVariants.Apply(openApi, schema, spec, await BaseBodyAsync(admin, openApi, endpoint, weakItem, $"{tag}w{k}"), $"{tag}w{k}", env.TenantA.EmailDomain,
                            (field, current) => WithinCaller(field, current, callerRole, callerPermissions, companies.First), CompanyValue);
                        if (strongVariant is null || weakVariant is null)
                        {
                            problems.Add($"{endpoint} {spec}: the gate has no different valid value for this field; extend FieldVariants rather than leave the field untested");
                            continue;
                        }
                        var strongBefore = await ReadAsync(admin, itemPath);
                        var (variantStatus, variantText) = await SendAsync(caller, endpoint.Method, strongPath, strongVariant);
                        var strongAfter = await ReadAsync(admin, itemPath);
                        var (variantControl, variantControlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => weak.ToString()), weakVariant);
                        var controlAccepted = variantControl is >= 200 and < 300;
                        if (controlAccepted && variantStatus != (int)HttpStatusCode.Forbidden)
                        {
                            problems.Add($"{endpoint} {spec}: aimed at a record granting everything by a user holding only [{string.Join(", ", callerPermissions)}] answered {variantStatus} (expected 403): {Short(variantText)}");
                        }
                        else if (!controlAccepted && spec.Kind == FieldVariants.Kind.Changed)
                        {
                            problems.Add($"{endpoint} {spec}: aimed at a record granting only what the caller holds answered {variantControl}, so the gate cannot tell a grant check from a malformed request: {Short(variantControlText)}");
                        }
                        else if (!controlAccepted && variantStatus is not ((int)HttpStatusCode.Forbidden or (int)HttpStatusCode.BadRequest))
                        {
                            problems.Add($"{endpoint} {spec}: refused on the control ({variantControl}) but answered {variantStatus} on the record granting everything (expected 400 or 403): {Short(variantText)}");
                        }
                        if (strongAfter != strongBefore)
                        {
                            problems.Add($"{endpoint} {spec}: the record granting everything changed: before {Short(strongBefore)}; after {Short(strongAfter)}");
                        }
                        fieldVariants.Add($"{endpoint} {spec}");
                    }
                }
            }
        }
        return new Result(problems, checkedEndpoints, fieldVariants, partialTargets, moduleFieldVariants, companyCallerTargets, companyCallerChecked);
    }

    /// <summary>
    /// <paramref name="endpoint"/> by a caller holding, through one role in company X and nothing
    /// workspace-wide, the endpoint's permission, reading roles and users, and one target per
    /// module's grants (see <see cref="CompanyCallers"/>). For every such target a fresh record of the
    /// family granting it is held by a user across the workspace, and another by a user in company
    /// Y alone: the request (with one text field changed, for an edit), and for an edit the same
    /// request with the last permission taken away, must answer 403 and leave the record exactly as
    /// it was. The control, a record granting nothing held by a user in X, must be accepted, which
    /// proves the caller's request reaches the grant check. Answers the requests aimed and the
    /// problems found (none without two companies).
    /// </summary>
    private static async Task<(int Aimed, List<string> Problems)> CompanyCallerAsync(ErpTestEnvironment env, HttpClient admin, OpenApiDocument openApi, GateCompanies companies,
        ApiEndpoint create, ApiEndpoint endpoint, IReadOnlyList<string> everything, IReadOnlyList<string> callerPermissions, string tag)
    {
        var problems = new List<string>();
        var moduleTargets = GrantTargets.PerModule(everything, callerPermissions);
        if (await CompanyCallers.CreateAsync(admin, env, companies, $"g2.grantco.{tag}", CompanyCallers.HeldInX(callerPermissions, moduleTargets)) is not { } companyCaller)
        {
            return (0, problems);
        }
        using var inOne = await CompanyCallers.SignInAsync(env, companyCaller, endpoint.Permission);
        var who = $"a user holding [{string.Join(", ", companyCaller.Permissions)}] only through a role in one company";
        var createSchema = openApi.RequestSchema(create.Method, create.Pattern)!.Value;
        var editSchema = endpoint.HasBody && endpoint.Method is "PUT" or "PATCH" ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
        var stripsPermissions = editSchema is { } es && openApi.Resolve(es).TryGetProperty("properties", out var editProperties) && editProperties.TryGetProperty("permissions", out _);
        var n = 0;

        // A fresh record of the family granting exactly these permissions, and its holder.
        async Task<Guid> HeldAsync(IReadOnlyList<string> permissions, Guid? inCompany, string suffix)
        {
            var body = GrantEscalation.ValidBody(openApi, createSchema, env, $"{tag}{suffix}");
            GrantEscalation.SetGrants(body, [], permissions);
            var record = await CreatedIdAsync(admin, create.Pattern, body);
            var holder = new JsonObject
            {
                ["email"] = $"g2.grantco.holder.{tag}{suffix}@{env.TenantA.EmailDomain}",
                ["displayName"] = $"G2 holder {tag}{suffix}",
                ["language"] = "en",
                ["roleIds"] = inCompany is null ? new JsonArray(JsonValue.Create(record)) : new JsonArray(),
                ["companyRoles"] = inCompany is { } c ? new JsonArray(GateCompanies.CompanyRole(record, c)) : new JsonArray(),
            };
            await companies.GiveAccessAsync(await CreatedIdAsync(admin, "/api/identity/users", holder));
            return record;
        }

        var aimed = 0;
        foreach (var target in moduleTargets)
        {
            foreach (var (place, company) in new (string, Guid?)[]
                     {
                         ("held across the workspace", null),
                         ("held in one company only, the other one than where the caller holds it", companyCaller.Y),
                     })
            {
                n++;
                var record = await HeldAsync(target.Permissions, company, $"yr{n}");
                var item = create.Pattern.TrimEnd('/') + "/" + record;
                var label = $"{target}, {place},";
                var requests = new List<(string Spec, JsonObject? Body)> { ("", await BodyAsync(admin, openApi, endpoint, item, $"{tag}yk{n}")) };
                if (stripsPermissions && await BaseBodyAsync(admin, openApi, endpoint, item, $"{tag}yp{n}") is { } stripped && stripped["permissions"] is JsonArray granted && granted.Count > 0)
                {
                    stripped["permissions"] = new JsonArray(granted.Take(granted.Count - 1).Select(x => x!.DeepClone()).ToArray());
                    requests.Add((" [permissions changed]", stripped));
                }
                foreach (var (spec, body) in requests)
                {
                    var before = await ReadAsync(admin, item);
                    var (status, text) = await SendAsync(inOne, endpoint.Method, endpoint.Path(_ => record.ToString()), body);
                    if (status != (int)HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint}{spec}: aimed at {label} by {who} answered {status} (expected 403): {Short(text)}");
                    }
                    var after = await ReadAsync(admin, item);
                    if (after != before)
                    {
                        problems.Add($"{endpoint}{spec}: {label} changed when {who} aimed at it: before {Short(before)}; after {Short(after)}");
                    }
                    aimed++;
                }
            }
        }

        // Control: a record granting nothing, held in X.
        var control = await HeldAsync([], companyCaller.X, "yc");
        var controlItem = create.Pattern.TrimEnd('/') + "/" + control;
        var (controlStatus, controlText) = await SendAsync(inOne, endpoint.Method, endpoint.Path(_ => control.ToString()), await BodyAsync(admin, openApi, endpoint, controlItem, $"{tag}ykc"));
        if (controlStatus is < 200 or >= 300)
        {
            problems.Add($"{endpoint}: aimed at a record granting nothing, held in the one company where the caller holds its grants, by {who}, answered {controlStatus}, " +
                         $"so the gate cannot tell the per-company check from a malformed request: {Short(controlText)}");
        }
        return (aimed, problems);
    }

    /// <summary>A changed grant that stays within what the caller holds: the caller's own role
    /// added or taken away, one of the caller's permissions added or the last one taken away.</summary>
    internal static JsonArray WithinCaller(string field, JsonArray current, Guid callerRole, IReadOnlyList<string> callerPermissions, Guid? company = null)
    {
        if (field == "companyRoles")
        {
            // The caller's own role, in a company the caller works in, added or taken away.
            if (company is not { } c)
            {
                return current;
            }
            var mine = current.FirstOrDefault(x => x is JsonObject o && o["roleId"]?.GetValue<Guid>() == callerRole && o["companyId"]?.GetValue<Guid>() == c);
            return mine is not null
                ? new JsonArray(current.Where(x => !ReferenceEquals(x, mine)).Select(x => x!.DeepClone()).ToArray())
                : new JsonArray([.. current.Select(x => x!.DeepClone()), GateCompanies.CompanyRole(callerRole, c)]);
        }
        var values = current.Select(x => x!.ToString()).ToList();
        if (field == "roleIds")
        {
            var role = callerRole.ToString();
            return new JsonArray((values.Contains(role, StringComparer.OrdinalIgnoreCase) ? values.Where(v => !v.Equals(role, StringComparison.OrdinalIgnoreCase)) : values.Append(role))
                .Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        }
        var missing = callerPermissions.FirstOrDefault(p => !values.Contains(p, StringComparer.Ordinal));
        var changed = missing is not null ? values.Append(missing) : values.Take(values.Count - 1);
        return new JsonArray(changed.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    }

    /// <summary>A changed grant beyond the caller: the last grant taken away, or, when there is
    /// none, the Administrator role (in every company, or in <paramref name="company"/> for roles in
    /// one company) or every permission.</summary>
    internal static JsonArray? Stronger(string field, JsonArray current, Guid administratorRole, IReadOnlyList<string> everything, Guid? company)
    {
        if (current.Count > 0)
        {
            return new JsonArray(current.Take(current.Count - 1).Select(x => x!.DeepClone()).ToArray());
        }
        return field switch
        {
            "roleIds" => new JsonArray(JsonValue.Create(administratorRole)),
            "companyRoles" => company is { } c ? new JsonArray(GateCompanies.CompanyRole(administratorRole, c)) : null,
            _ => new JsonArray(everything.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
        };
    }

    /// <summary>The record as the administrator reads it (status and body), or its status when it
    /// has no GET.</summary>
    private static async Task<string> ReadAsync(HttpClient admin, string path)
    {
        using var response = await admin.GetAsync(path);
        return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";
    }

    /// <summary>A body that passes validation: for an edit, the record's own fields with its first
    /// plain text field changed (a real change, grants untouched); otherwise fresh names, a valid
    /// password and language, flags on.</summary>
    private static Task<JsonObject?> BodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, string itemPath, string tag) =>
        BodyAsync(admin, openApi, endpoint, itemPath, tag, changeOneText: true);

    /// <summary>The base of a single-field request: for an edit, exactly the record's own values;
    /// otherwise fresh valid values.</summary>
    private static async Task<JsonObject> BaseBodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, string itemPath, string tag) =>
        await BodyAsync(admin, openApi, endpoint, itemPath, tag, changeOneText: false) ?? [];

    private static async Task<JsonObject?> BodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, string itemPath, string tag, bool changeOneText)
    {
        if (!endpoint.HasBody || openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return null;
        }
        var body = GrantEscalation.ValidBody(openApi, schema, env: null, tag);
        foreach (var (field, value) in body.ToList())
        {
            if (value is JsonArray array && array.All(x => x is null))
            {
                body[field] = new JsonArray();
            }
        }
        if (endpoint.Method is "PUT" or "PATCH")
        {
            // The resource the edit replaces: its own GET when it has one (a user's default
            // company), else the record's.
            var path = endpoint.Path(_ => itemPath[(itemPath.LastIndexOf('/') + 1)..]);
            using var own = await admin.GetAsync(path);
            using var response = own.IsSuccessStatusCode ? null : await admin.GetAsync(itemPath);
            var source = own.IsSuccessStatusCode ? own : response!;
            if (source.IsSuccessStatusCode && JsonNode.Parse(await source.Content.ReadAsStringAsync()) is JsonObject item)
            {
                var changed = false;
                foreach (var (field, generated) in body.ToList())
                {
                    if (item[field] is not { } current)
                    {
                        continue;
                    }
                    var plainText = current is JsonValue v && v.GetValueKind() == JsonValueKind.String && generated is JsonValue g && g.GetValueKind() == JsonValueKind.String &&
                                    !field.Contains("email", StringComparison.OrdinalIgnoreCase) && !field.Equals("language", StringComparison.OrdinalIgnoreCase) &&
                                    !field.Equals("numerals", StringComparison.OrdinalIgnoreCase) && !Guid.TryParse(v.GetValue<string>(), out _);
                    if (changeOneText && plainText && !changed)
                    {
                        body[field] = field.EndsWith("Ar", StringComparison.Ordinal) ? $"تعديل {tag}" : $"Edited {tag}";
                        changed = true;
                        continue;
                    }
                    body[field] = current.DeepClone();
                }
            }
        }
        return body;
    }

    private static async Task<(int Status, string Text)> SendAsync(HttpClient client, string method, string path, JsonObject? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, JsonObject body)
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

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
