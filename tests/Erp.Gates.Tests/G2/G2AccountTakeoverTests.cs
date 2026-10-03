using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: these tests create users and roles and try to take accounts over.</summary>
public sealed class TakeoverFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2, privilege escalation by acting on someone else. Every endpoint that changes a user record
/// (anything but GET on <c>/api/identity/users/{id}…</c>: edit, reset the password, end sessions,
/// clear a sign-in pause) is found from the running app's routing. A caller holding exactly that
/// endpoint's permission (plus reading users and roles) aims it at the Administrator: the answer
/// must be 403 and the Administrator must keep their record, password and sessions, because
/// resetting a stronger account's password is taking it over. The same request aimed at a user
/// with no roles must succeed, which proves the 403 came from the access check. Nobody uses these
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
        var problems = new List<string>();
        var checkedEndpoints = 0;
        var n = 0;
        foreach (var endpoint in endpoints)
        {
            n++;
            var tag = $"{n}{Guid.NewGuid():N}"[..10];
            var permissions = new[] { endpoint.Permission, "identity.users.read", "identity.roles.read" }.Distinct().ToArray();
            var roleId = await CreatedIdAsync(admin, "/api/identity/roles", new { nameEn = $"Takeover {tag}", nameAr = $"استيلاء {tag}", permissions });
            var email = $"takeover.{tag}@{Env.TenantA.EmailDomain}";
            await CreatedIdAsync(admin, "/api/identity/users", new { email, displayName = $"Takeover {tag}", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
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
            var (controlStatus, controlText) = await SendAsync(caller, endpoint.Method, endpoint.Path(_ => targetId.ToString()), await BodyAsync(caller, openApi, endpoint, targetId, tag));
            if (controlStatus is < 200 or >= 300)
            {
                problems.Add($"{endpoint}: aimed at a user without roles answered {controlStatus}, so the gate cannot tell the access check from a malformed request: {Short(controlText)}");
            }
            checkedEndpoints++;
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedEndpoints >= Ratchet.Min("g2.takeoverEndpointsChecked"),
            $"{checkedEndpoints} endpoints acting on users checked; ratchet minimum {Ratchet.Min("g2.takeoverEndpointsChecked")}");
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
        var problems = new List<string>();
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
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Acting_on_a_record_that_grants_access_needs_everything_it_grants()
    {
        var result = await GrantBearingRecords.RunAsync(Env);
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        // Roles: edit, delete and copy; users: edit, reset the password, end sessions, clear a pause.
        foreach (var expected in new[] { "PUT /api/identity/roles/{id:guid}", "DELETE /api/identity/roles/{id:guid}", "POST /api/identity/roles/{id:guid}/copy", "PUT /api/identity/users/{id:guid}" })
        {
            Assert.Contains(expected, result.Checked);
        }
        Assert.True(result.Checked.Count >= Ratchet.Min("g2.grantBearingActionsChecked"),
            $"{result.Checked.Count} endpoints acting on grant-bearing records checked; ratchet minimum {Ratchet.Min("g2.grantBearingActionsChecked")}");
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
