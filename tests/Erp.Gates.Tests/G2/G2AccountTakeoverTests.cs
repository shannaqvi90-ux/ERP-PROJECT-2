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

/// <summary>Its own environment: these tests create users and roles and try to take accounts over.</summary>
public sealed class TakeoverFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    // A module of a later wave is hosted too, so the catalogue holds permissions no identity or
    // tenancy check was written for (see GrantTargets).
    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync(LaterLedgerModule.Settings());

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2, privilege escalation by acting on someone else. Every endpoint that changes a user record
/// (anything but GET on <c>/api/identity/users/{id}…</c>: edit, reset the password, end sessions,
/// clear a sign-in pause) is found from the running app's routing. A caller holding exactly that
/// endpoint's permission (plus reading users and roles) aims it at the Administrator: the answer
/// must be 403 and the Administrator must keep their record, password and sessions, because
/// resetting a stronger account's password is taking it over. The same request aimed at a user
/// with no roles must succeed, which proves the 403 came from the access check. The Administrator
/// alone cannot tell a correct check from one that compares only part of the grants (critic p03
/// round 3, plant P14: a check looking only at identity permissions still refuses the
/// Administrator), so the same request is also aimed at users holding grants the caller lacks in
/// every shape of <see cref="GrantTargets"/>: one permission alone (every one of the catalogue,
/// a later module's included), a whole other module, the caller's own plus one more. Nobody uses these
/// endpoints on themselves (their own password changes only with the current password), and
/// copying a role is creating one: it may not copy permissions the caller lacks.
/// </summary>
public sealed class G2AccountTakeoverTests(TakeoverFixture fixture) : IClassFixture<TakeoverFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private const string UsersPrefix = "/api/identity/users/{";
    private const string RolesPrefix = "/api/identity/roles/{";

    [Fact]
    public async Task Acting_on_a_user_needs_every_permission_that_user_holds()
    {
        using var anonymous = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(Env.Factory.Services)
            .Where(e => e.Method != "GET" && !e.IsAnonymous && e.Pattern.StartsWith(UsersPrefix, StringComparison.Ordinal))
            .ToList();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var adminSession = await admin.GetFromJsonAsync<JsonElement>("/api/auth/session");
        var adminId = adminSession.GetProperty("user").GetProperty("id").GetGuid();
        var catalogue = Env.Factory.Services.GetRequiredService<ModuleCatalog>().PermissionKeys.ToList();
        Assert.Contains(LaterLedgerModule.PermissionKeys[0], catalogue);
        var targets = new TargetRecords(admin, Env);
        var companies = await GateCompanies.OfAsync(admin);
        var administratorRole = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        JsonNode? CompanyValue(string field, JsonNode? current) => GateCompanies.IsCompanyField(field) ? companies.Other(current) : null;
        var problems = new List<string>();
        var checkedEndpoints = 0;
        var fieldVariants = 0;
        var moduleFieldVariants = 0;
        var targetsAimed = 0;
        var companyTargetsAimed = 0;
        var n = 0;
        foreach (var endpoint in endpoints)
        {
            n++;
            var tag = $"{n}{Guid.NewGuid():N}"[..10];
            var permissions = new[] { endpoint.Permission, "identity.users.read", "identity.roles.read" }.Distinct().ToArray();
            var roleId = await CreatedIdAsync(admin, "/api/identity/roles", new { nameEn = $"Takeover {tag}", nameAr = $"استيلاء {tag}", permissions });
            var email = $"takeover.{tag}@{Env.TenantA.EmailDomain}";
            var callerId = await CreatedIdAsync(admin, "/api/identity/users", new { email, displayName = $"Takeover {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
            // The caller works in the companies roles in one company and default companies name.
            await companies.GiveAccessAsync(callerId);
            using var caller = await Env.SignInAsync(email);

            var before = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{adminId}");
            var (status, text) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => adminId.ToString()), await BodyAsync(caller, openApi, endpoint, adminId, tag));
            if (status != (int)HttpStatusCode.Forbidden)
            {
                problems.Add($"{endpoint}: aimed at the Administrator by a user holding only [{string.Join(", ", permissions)}] answered {status} (expected 403): {Short(text)}");
            }
            var after = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{adminId}");
            if (before.GetProperty("version").GetUInt32() != after.GetProperty("version").GetUInt32())
            {
                problems.Add($"{endpoint}: the Administrator's record changed");
            }
            if (!(await admin.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean())
            {
                problems.Add($"{endpoint}: the Administrator's session ended");
                return;
            }
            try
            {
                using var again = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
            }
            catch (InvalidOperationException e)
            {
                problems.Add($"{endpoint}: the Administrator can no longer sign in: {e.Message}");
            }

            // Control: the same request aimed at a user with no roles succeeds.
            var targetEmail = $"target.{tag}@{Env.TenantA.EmailDomain}";
            var targetId = await CreatedIdAsync(admin, "/api/identity/users", new { email = targetEmail, displayName = $"Target {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() });
            await companies.GiveAccessAsync(targetId);
            var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => targetId.ToString()), await BodyAsync(caller, openApi, endpoint, targetId, tag));
            if (controlStatus is < 200 or >= 300)
            {
                problems.Add($"{endpoint}: aimed at a user without roles answered {controlStatus}, so the gate cannot tell the access check from a malformed request: {Short(controlText)}");
            }
            checkedEndpoints++;

            // Users holding grants the caller lacks without holding everything (critic p03 round 3,
            // plant P14): each one alone, a whole other module, the caller's own plus one more.
            foreach (var target in GrantTargets.For(catalogue, permissions))
            {
                var victim = await targets.UserAsync(target.Permissions);
                var victimBefore = await ReadUserAsync(admin, victim);
                if (victimBefore.StartsWith("404 ", StringComparison.Ordinal))
                {
                    problems.Add($"{endpoint}: the user holding {target} is gone (an earlier request removed them)");
                    continue;
                }
                var (targetStatus, targetText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => victim.ToString()), await BodyAsync(caller, openApi, endpoint, victim, $"{tag}g{++targetsAimed}"));
                if (targetStatus != (int)HttpStatusCode.Forbidden)
                {
                    problems.Add($"{endpoint}: aimed at a user holding {target} by a user holding only [{string.Join(", ", permissions)}] answered {targetStatus} (expected 403): {Short(targetText)}");
                }
                var victimAfter = await ReadUserAsync(admin, victim);
                if (victimAfter != victimBefore)
                {
                    problems.Add($"{endpoint}: the user holding {target} changed: before {Short(victimBefore)}; after {Short(victimAfter)}");
                }
            }

            // Users whose grants the caller lacks are held in one company only (critic p03 round 5,
            // finding R1: a check reading only workspace-wide roles lets a clerk act on a company
            // manager). One target per module, its role held in the first company, which the caller
            // works in (so nothing is hidden from them: only what the role grants protects it).
            if (companies.First is { } inCompany)
            {
                foreach (var target in GrantTargets.PerModule(catalogue, permissions))
                {
                    var victim = await targets.UserInCompanyAsync(target.Permissions, inCompany, companies);
                    var victimBefore = await ReadUserAsync(admin, victim);
                    if (victimBefore.StartsWith("404 ", StringComparison.Ordinal))
                    {
                        problems.Add($"{endpoint}: the user holding {target} in one company only is gone (an earlier request removed them)");
                        continue;
                    }
                    var (targetStatus, targetText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => victim.ToString()), await BodyAsync(caller, openApi, endpoint, victim, $"{tag}c{++companyTargetsAimed}"));
                    if (targetStatus != (int)HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint}: aimed at a user holding {target} in one company only by a user holding only [{string.Join(", ", permissions)}] everywhere answered {targetStatus} (expected 403): {Short(targetText)}");
                    }
                    var victimAfter = await ReadUserAsync(admin, victim);
                    if (victimAfter != victimBefore)
                    {
                        problems.Add($"{endpoint}: the user holding {target} in one company only changed: before {Short(victimBefore)}; after {Short(victimAfter)}");
                    }
                }

                // A user holding, in a company the caller does not work in, a role granting nothing
                // the caller lacks: what it grants cannot be seen from the caller's companies, so
                // the user is beyond them. The caller works in the first company alone for this
                // request, and in both again afterwards.
                if (companies.Ids.Count > 1)
                {
                    var elsewhere = await targets.UserInCompanyAsync(["identity.users.read"], companies.Ids[1], companies, fresh: true);
                    var elsewhereBefore = await ReadUserAsync(admin, elsewhere);
                    await companies.LimitAccessAsync(callerId, inCompany);
                    var (hiddenStatus, hiddenText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => elsewhere.ToString()), await BodyAsync(caller, openApi, endpoint, elsewhere, $"{tag}h"));
                    var elsewhereAfter = await ReadUserAsync(admin, elsewhere);
                    // Control: working in the first company alone, the caller still acts on a user
                    // without roles who works there too.
                    var limitedTarget = await CreatedIdAsync(admin, "/api/identity/users", new { email = $"limited.{tag}@{Env.TenantA.EmailDomain}", displayName = $"Limited target {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = Array.Empty<Guid>() });
                    await companies.LimitAccessAsync(limitedTarget, inCompany);
                    var (limitedStatus, limitedText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => limitedTarget.ToString()), await BodyAsync(caller, openApi, endpoint, limitedTarget, $"{tag}hc"));
                    await companies.GiveAccessAsync(callerId);
                    if (limitedStatus is < 200 or >= 300)
                    {
                        problems.Add($"{endpoint}: working in one company, aimed at a user without roles answered {limitedStatus}, so the gate cannot tell the check of hidden roles from a malformed request: {Short(limitedText)}");
                    }
                    companyTargetsAimed++;
                    if (hiddenStatus != (int)HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint}: aimed at a user holding a role in a company the caller does not work in answered {hiddenStatus} (expected 403): {Short(hiddenText)}");
                    }
                    if (elsewhereAfter != elsewhereBefore)
                    {
                        problems.Add($"{endpoint}: the user holding a role in a company the caller does not work in changed: before {Short(elsewhereBefore)}; after {Short(elsewhereAfter)}");
                    }
                }
            }

            // One field at a time (critic p03 round 2, plant P5): each writable property changed
            // alone and left out alone, aimed at the Administrator and at a user holding one other
            // module's grants (a path-specific check narrowed to some modules), with the same single
            // change on the user without roles as the control.
            if (endpoint.HasBody && openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is { } schema)
            {
                var strongTargets = new List<(Guid Id, string Label)> { (adminId, "the Administrator") };
                foreach (var target in GrantTargets.PerModule(catalogue, permissions))
                {
                    strongTargets.Add((await targets.UserAsync(target.Permissions), $"a user holding {target}"));
                }
                var k = 0;
                foreach (var spec in FieldVariants.Specs(openApi, schema))
                {
                    foreach (var (strongId, label) in strongTargets)
                    {
                        k++;
                        var strongBody = FieldVariants.Apply(openApi, schema, spec, await BaseBodyAsync(admin, openApi, endpoint, strongId, $"{tag}a{k}"), $"{tag}a{k}", Env.TenantA.EmailDomain,
                            (field, current) => GrantBearingRecords.Stronger(field, current, administratorRole, catalogue, companies.First), CompanyValue);
                        var weakBody = FieldVariants.Apply(openApi, schema, spec, await BaseBodyAsync(admin, openApi, endpoint, targetId, $"{tag}t{k}"), $"{tag}t{k}", Env.TenantA.EmailDomain,
                            (field, current) => GrantBearingRecords.WithinCaller(field, current, roleId, permissions, companies.First), CompanyValue);
                        if (strongBody is null || weakBody is null)
                        {
                            problems.Add($"{endpoint} {spec}: the gate has no different valid value for this field aimed at {label}; extend FieldVariants rather than leave the field untested");
                            continue;
                        }
                        var strongBefore = await ReadUserAsync(admin, strongId);
                        var (fieldStatus, fieldText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => strongId.ToString()), strongBody);
                        var strongAfter = await ReadUserAsync(admin, strongId);
                        var (fieldControl, fieldControlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => targetId.ToString()), weakBody);
                        var accepted = fieldControl is >= 200 and < 300;
                        if (accepted && fieldStatus != (int)HttpStatusCode.Forbidden)
                        {
                            problems.Add($"{endpoint} {spec}: aimed at {label} by a user holding only [{string.Join(", ", permissions)}] answered {fieldStatus} (expected 403): {Short(fieldText)}");
                        }
                        else if (!accepted && spec.Kind == FieldVariants.Kind.Changed)
                        {
                            problems.Add($"{endpoint} {spec}: aimed at a user without roles answered {fieldControl}, so the gate cannot tell the access check from a malformed request: {Short(fieldControlText)}");
                        }
                        else if (!accepted && fieldStatus is not ((int)HttpStatusCode.Forbidden or (int)HttpStatusCode.BadRequest))
                        {
                            problems.Add($"{endpoint} {spec}: refused on the control ({fieldControl}) but answered {fieldStatus} on {label} (expected 400 or 403): {Short(fieldText)}");
                        }
                        if (strongAfter != strongBefore)
                        {
                            problems.Add($"{endpoint} {spec}: the record of {label} changed: before {Short(strongBefore)}; after {Short(strongAfter)}");
                        }
                        if (strongId == adminId)
                        {
                            fieldVariants++;
                        }
                        else
                        {
                            moduleFieldVariants++;
                        }
                    }
                }
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{checkedEndpoints} endpoints acting on users, {fieldVariants} single-field requests aimed at the Administrator, " +
                                                        $"{targetsAimed} requests aimed at users holding grants the caller lacks, {moduleFieldVariants} single-field requests aimed at them");
        TestContext.Current.TestOutputHelper?.WriteLine($"{companyTargetsAimed} requests aimed at users whose roles are held in one company");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(companyTargetsAimed >= Ratchet.Min("g2.takeoverCompanyTargets"),
            $"{companyTargetsAimed} requests aimed at users whose roles are held in one company; ratchet minimum {Ratchet.Min("g2.takeoverCompanyTargets")}");
        Assert.True(fieldVariants >= Ratchet.Min("g2.takeoverFieldVariantsChecked"),
            $"{fieldVariants} single-field requests aimed at the Administrator; ratchet minimum {Ratchet.Min("g2.takeoverFieldVariantsChecked")}");
        Assert.True(checkedEndpoints >= Ratchet.Min("g2.takeoverEndpointsChecked"),
            $"{checkedEndpoints} endpoints acting on users checked; ratchet minimum {Ratchet.Min("g2.takeoverEndpointsChecked")}");
        Assert.True(targetsAimed >= Ratchet.Min("g2.takeoverPartialTargets"),
            $"{targetsAimed} requests aimed at users holding grants the caller lacks; ratchet minimum {Ratchet.Min("g2.takeoverPartialTargets")}");
        Assert.True(moduleFieldVariants >= Ratchet.Min("g2.takeoverModuleFieldVariants"),
            $"{moduleFieldVariants} single-field requests aimed at users holding another module's grants; ratchet minimum {Ratchet.Min("g2.takeoverModuleFieldVariants")}");
    }

    /// <summary>A user as the administrator reads them, with what they can do and where they start.</summary>
    private static async Task<string> ReadUserAsync(HttpClient admin, Guid id)
    {
        using var record = await admin.GetAsync($"/api/identity/users/{id}");
        using var access = await admin.GetAsync($"/api/identity/users/{id}/access");
        using var workplace = await admin.GetAsync($"/api/identity/users/{id}/default-company");
        return $"{(int)record.StatusCode} {await record.Content.ReadAsStringAsync()} | {(int)access.StatusCode} {await access.Content.ReadAsStringAsync()} | " +
               $"{(int)workplace.StatusCode} {await workplace.Content.ReadAsStringAsync()}";
    }

    [Fact]
    public async Task Nobody_resets_their_own_password_or_ends_their_own_access_through_administration()
    {
        using var anonymous = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(Env.Factory.Services)
            .Where(e => e.Method == "POST" && !e.IsAnonymous && e.Pattern.StartsWith(UsersPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpoints);
        var email = $"self.{Guid.NewGuid():N}"[..20] + $"@{Env.TenantA.EmailDomain}";
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var selfId = await CreatedIdAsync(admin, "/api/identity/users", new { email, displayName = "Self", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { administrator } });
        using var self = await Env.SignInAsync(email);
        var problems = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var (status, text) = await SendAsync(self, endpoint.Method, endpoint.Path(_ => selfId.ToString()), await BodyAsync(self, openApi, endpoint, selfId, "self"));
            if (status != (int)HttpStatusCode.Forbidden)
            {
                problems.Add($"{endpoint}: an administrator aiming it at themselves answered {status} (expected 403): {Short(text)}");
            }
        }
        Assert.True((await self.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("authenticated").GetBoolean(), "The caller's own session ended");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Creating_from_a_role_cannot_grant_more_than_the_caller_holds()
    {
        using var anonymous = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var endpoints = EndpointInventory.From(Env.Factory.Services)
            .Where(e => e.Method == "POST" && !e.IsAnonymous && e.Pattern.StartsWith(RolesPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(endpoints);
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var administrator = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean()).GetProperty("id").GetGuid();
        var catalogue = Env.Factory.Services.GetRequiredService<ModuleCatalog>().PermissionKeys.ToList();
        var targets = new TargetRecords(admin, Env);
        var problems = new List<string>();
        var checkedTargets = 0;
        var n = 0;
        foreach (var endpoint in endpoints)
        {
            var tag = $"{++n}{Guid.NewGuid():N}"[..10];
            var permissions = new[] { endpoint.Permission, "identity.roles.read" }.Distinct().ToArray();
            var callerRole = await CreatedIdAsync(admin, "/api/identity/roles", new { nameEn = $"Copier {tag}", nameAr = $"ناسخ {tag}", permissions });
            var email = $"copier.{tag}@{Env.TenantA.EmailDomain}";
            await CreatedIdAsync(admin, "/api/identity/users", new { email, displayName = $"Copier {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { callerRole } });
            using var caller = await Env.SignInAsync(email);
            var count = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("total").GetInt32();

            var (status, text) = await SendAsync(caller, "POST", endpoint.Path(_ => administrator.ToString()), await BodyAsync(caller, openApi, endpoint, administrator, tag));
            if (status != (int)HttpStatusCode.Forbidden)
            {
                problems.Add($"{endpoint}: on the Administrator role by a user holding only [{string.Join(", ", permissions)}] answered {status} (expected 403): {Short(text)}");
            }
            if ((await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("total").GetInt32() != count)
            {
                problems.Add($"{endpoint}: a role was created");
            }
            var (controlStatus, controlText) = await SendAsync(caller, "POST", endpoint.Path(_ => callerRole.ToString()), await BodyAsync(caller, openApi, endpoint, callerRole, tag + "c"));
            if (controlStatus is < 200 or >= 300)
            {
                problems.Add($"{endpoint}: on the caller's own role answered {controlStatus}: {Short(controlText)}");
            }

            // Roles granting what the caller lacks without granting everything (critic p03 round 3,
            // plant P16: a copy check narrowed to identity permissions still refuses the Administrator).
            var k = 0;
            foreach (var target in GrantTargets.For(catalogue, permissions))
            {
                var source = await targets.RoleAsync(target.Permissions);
                var total = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("total").GetInt32();
                var (targetStatus, targetText) = await SendAsync(caller, "POST", endpoint.Path(_ => source.ToString()), await BodyAsync(caller, openApi, endpoint, source, $"{tag}g{++k}"));
                if (targetStatus != (int)HttpStatusCode.Forbidden)
                {
                    problems.Add($"{endpoint}: on {target} by a user holding only [{string.Join(", ", permissions)}] answered {targetStatus} (expected 403): {Short(targetText)}");
                }
                if ((await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("total").GetInt32() != total)
                {
                    problems.Add($"{endpoint}: on {target}: a role was created");
                }
                checkedTargets++;
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{endpoints.Count} endpoints creating from a role, {checkedTargets} requests on roles granting what the caller lacks");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedTargets >= Ratchet.Min("g2.copyPartialTargets"),
            $"{checkedTargets} requests on roles granting what the caller lacks; ratchet minimum {Ratchet.Min("g2.copyPartialTargets")}");
    }

    [Fact]
    public async Task Acting_on_a_record_that_grants_access_needs_everything_it_grants()
    {
        var result = await GrantBearingRecords.RunAsync(Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Checked.Count} endpoints acting on grant-bearing records checked: {string.Join(", ", result.Checked)}");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        // Roles: edit, delete and copy; users: edit, reset the password, end sessions, clear a pause.
        foreach (var expected in new[] { "PUT /api/identity/roles/{id:guid}", "DELETE /api/identity/roles/{id:guid}", "POST /api/identity/roles/{id:guid}/copy", "PUT /api/identity/users/{id:guid}" })
        {
            Assert.Contains(expected, result.Checked);
        }
        // Each writable field alone (critic p03 round 2, plant P5): an e-mail correction, a role
        // change, a deactivation, a reset without a password each take their own path.
        var variants = result.FieldVariants ?? [];
        TestContext.Current.TestOutputHelper?.WriteLine($"{variants.Count} single-field requests: {string.Join(", ", variants)}");
        foreach (var expected in new[]
                 {
                     "PUT /api/identity/users/{id:guid} [email changed]", "PUT /api/identity/users/{id:guid} [isActive changed]",
                     "PUT /api/identity/users/{id:guid} [roleIds changed]", "PUT /api/identity/users/{id:guid} [language changed]",
                     "PUT /api/identity/users/{id:guid} [displayNameAr changed]", "PUT /api/identity/roles/{id:guid} [permissions changed]",
                     "PUT /api/identity/roles/{id:guid} [nameAr changed]", "POST /api/identity/users/{id:guid}/password [password left out]",
                     "POST /api/identity/users/{id:guid}/password [mustChangePassword changed]",
                 })
        {
            Assert.Contains(expected, variants);
        }
        Assert.True(variants.Count >= Ratchet.Min("g2.grantFieldVariantsChecked"),
            $"{variants.Count} single-field requests on grant-bearing records; ratchet minimum {Ratchet.Min("g2.grantFieldVariantsChecked")}");
        Assert.True(result.Checked.Count >= Ratchet.Min("g2.grantBearingActionsChecked"),
            $"{result.Checked.Count} endpoints acting on grant-bearing records checked; ratchet minimum {Ratchet.Min("g2.grantBearingActionsChecked")}");
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.PartialTargets} requests aimed at records granting what the caller lacks, {result.ModuleFieldVariants} single-field requests aimed at records granting one other module");
        Assert.True(result.PartialTargets >= Ratchet.Min("g2.grantBearingPartialTargets"),
            $"{result.PartialTargets} requests aimed at records granting what the caller lacks; ratchet minimum {Ratchet.Min("g2.grantBearingPartialTargets")}");
        Assert.True(result.ModuleFieldVariants >= Ratchet.Min("g2.grantBearingModuleFieldVariants"),
            $"{result.ModuleFieldVariants} single-field requests aimed at records granting one other module; ratchet minimum {Ratchet.Min("g2.grantBearingModuleFieldVariants")}");
    }

    [Fact]
    public async Task No_endpoint_grants_another_modules_permissions_the_caller_lacks()
    {
        // The escalation check again, in this environment whose catalogue holds a module of a later
        // wave: asking for a ledger permission alone, or a role granting it, is refused like asking
        // for everything.
        var result = await GrantEscalation.RunAsync(Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Checked.Count} grant endpoints, {result.PartialTargets} requests asking for grants the caller lacks");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.PartialTargets >= Ratchet.Min("g2.grantEscalationPartialTargets"),
            $"{result.PartialTargets} requests asking for grants the caller lacks; ratchet minimum {Ratchet.Min("g2.grantEscalationPartialTargets")}");
    }

    /// <summary>A body that passes validation: the record's own GET for an edit (with a changed
    /// name and the account switched off), otherwise fresh names, a valid password, flags on.</summary>
    private static async Task<JsonObject?> BodyAsync(HttpClient caller, OpenApiDocument openApi, ApiEndpoint endpoint, Guid target, string tag)
    {
        if (!endpoint.HasBody || openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
        {
            return null;
        }
        var k = 0;
        var body = openApi.BuildBody(schema, (type, format, name) =>
        {
            k++;
            var lower = name?.ToLowerInvariant() ?? "";
            return type switch
            {
                "string" when format == "uuid" => null,
                "string" when lower.Contains("email") => $"g2.{tag}.{k}@takeover.example",
                "string" when lower.Contains("password") => "Taken-Over-Password-1",
                "string" when lower == "language" => "en",
                "string" when lower.EndsWith("ar", StringComparison.Ordinal) => $"نسخة {tag} {k}",
                "string" => $"G2 {tag} {k}",
                "boolean" => true,
                _ => null,
            };
        }) as JsonObject ?? [];
        foreach (var (field, value) in body.ToList())
        {
            if (value is JsonArray array && array.All(x => x is null))
            {
                body[field] = new JsonArray();
            }
        }
        if (endpoint.Method == "PUT")
        {
            var item = await caller.GetFromJsonAsync<JsonObject>(endpoint.Path(_ => target.ToString())) ?? [];
            foreach (var (field, _) in body.ToList())
            {
                if (item[field] is { } current)
                {
                    body[field] = current.DeepClone();
                }
            }
            body["displayName"] = $"Taken over {tag}";
        }
        return body;
    }

    /// <summary>The base of a single-field request: for an edit, exactly the user's own values (as
    /// the administrator reads them) for the fields the request has; otherwise fresh valid values.</summary>
    private static async Task<JsonObject> BaseBodyAsync(HttpClient admin, OpenApiDocument openApi, ApiEndpoint endpoint, Guid target, string tag)
    {
        var body = await BodyAsync(admin, openApi, endpoint, target, tag) ?? [];
        if (endpoint.Method is "PUT" or "PATCH")
        {
            var item = await admin.GetFromJsonAsync<JsonObject>(endpoint.Path(_ => target.ToString())) ?? [];
            foreach (var (field, _) in body.ToList())
            {
                body[field] = item[field]?.DeepClone();
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

    private static async Task<Guid> CreatedIdAsync(HttpClient admin, string path, object body)
    {
        using var response = await admin.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"POST {path} answered {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static string Short(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
