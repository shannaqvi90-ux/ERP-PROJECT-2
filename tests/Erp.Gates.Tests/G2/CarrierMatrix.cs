using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// The two permission matrices of G2, run once per session carrier the kernel reads
/// (<see cref="SessionTokenCarriers.All"/>: the browser's cookie and an API client's Bearer header,
/// and any carrier added later). A permission check that behaves differently for one carrier (critic
/// p00 round 9, plant P3: any signed-in Bearer caller passed every permission check) shows up as
/// that carrier's findings, each prefixed with the carrier's name.
/// </summary>
public static class CarrierMatrix
{
    /// <summary>A user whose roles grant nothing: through every carrier, the session lists no
    /// permission and no menu entry, and every permissioned endpoint answers 403, both with random
    /// ids and with every id of the caller's own (<see cref="OwnIds"/>: their user id, session,
    /// tenant, companies, branches and the records that name them), each through a session of its
    /// own per carrier.</summary>
    public static async Task<Result> DeniedEverythingAsync(ErpTestEnvironment env, string email, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null)
    {
        var random = await DeniedForRandomIdsAsync(env, email, permissioned, factory);
        var self = await SelfReferencingAsync(env, email, null, permissioned, SessionTokenCarriers.All.Select(c => c.Name).ToList(), factory);
        return random with { Problems = [.. random.Problems, .. self.Problems], SelfRequests = self.Requests, OwnIdsTried = self.OwnIdsTried, SelfEndpoints = self.Endpoints };
    }

    private static async Task<Result> DeniedForRandomIdsAsync(ErpTestEnvironment env, string email, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var requests = 0;
        foreach (var carrier in await env.SignInPerCarrierAsync(email, factory))
        {
            using (carrier)
            {
                carriers.Add(carrier.Carrier);
                var session = await carrier.Client.GetFromJsonAsync<JsonElement>("/api/auth/session");
                if (session.GetProperty("permissions").GetArrayLength() != 0)
                {
                    problems.Add($"[{carrier.Carrier}] the session lists permissions: {session.GetProperty("permissions")}");
                }
                if (session.GetProperty("menu").GetArrayLength() != 0)
                {
                    problems.Add($"[{carrier.Carrier}] the session shows menu entries: {session.GetProperty("menu")}");
                }
                foreach (var endpoint in permissioned)
                {
                    using var response = await carrier.Client.SendAsync(G2PermissionTests.Request(endpoint));
                    requests++;
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        problems.Add($"[{carrier.Carrier}] {endpoint}: {(int)response.StatusCode}");
                    }
                }
            }
        }
        return new Result(problems, carriers, requests);
    }

    /// <summary>A user holding exactly <paramref name="permission"/>: through every carrier, the
    /// session lists exactly it and its menu entries, and exactly the endpoints that declare it
    /// open. Then, through each carrier in <paramref name="selfCarriers"/> (every carrier when
    /// null), with a session of its own: every endpoint of the permission's module that declares
    /// another permission answers 403 when the request names any id of the caller's own
    /// (<see cref="OwnIds"/>), so holding one permission never opens another action on "my own"
    /// records.</summary>
    public static async Task<Result> ExactlyOneAsync(ErpTestEnvironment env, string email, string permission, IReadOnlyList<string> expectedMenu,
        IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null, IReadOnlyList<string>? selfCarriers = null)
    {
        var random = await ExactlyOneForRandomIdsAsync(env, email, permission, expectedMenu, permissioned, factory);
        var module = OwnIds.ModuleOf(permission);
        var others = permissioned.Where(e => e.Permission != permission && OwnIds.ModuleOf(e.Permission) == module).ToList();
        var self = await SelfReferencingAsync(env, email, permission, others, selfCarriers ?? SessionTokenCarriers.All.Select(c => c.Name).ToList(), factory);
        return random with { Problems = [.. random.Problems, .. self.Problems], SelfRequests = self.Requests, OwnIdsTried = self.OwnIdsTried, SelfEndpoints = self.Endpoints };
    }

    /// <summary>Every endpoint given answers 403 to requests naming the caller's own ids, through
    /// each carrier named, each carrier with a session of its own (an action that ends the session
    /// under a planted bypass then spoils only that carrier's later requests).</summary>
    private static async Task<SelfResult> SelfReferencingAsync(ErpTestEnvironment env, string email, string? holding, IReadOnlyList<ApiEndpoint> endpoints,
        IReadOnlyList<string> carriers, ErpAppFactory? factory)
    {
        var problems = new List<string>();
        var requests = 0;
        var ownIds = 0;
        foreach (var carrier in carriers)
        {
            var (token, userId) = await env.SignInForTokenAsync(email, factory);
            var own = await OwnIds.OfAsync(env, userId, token);
            ownIds = Math.Max(ownIds, own.Count);
            using var client = env.ClientCarrying(carrier, token, factory);
            foreach (var (endpoint, id, build) in OwnIds.Requests(endpoints, own))
            {
                using var request = build();
                using var response = await client.SendAsync(request);
                requests++;
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    problems.Add($"[{carrier}] {(holding is null ? "" : holding + ": ")}{endpoint} naming {id.Source} answered {(int)response.StatusCode} (expected 403) {body[..Math.Min(160, body.Length)]}");
                }
            }
        }
        return new SelfResult(problems, requests, ownIds, endpoints.Count);
    }

    private sealed record SelfResult(IReadOnlyList<string> Problems, int Requests, int OwnIdsTried, int Endpoints);

    private static async Task<Result> ExactlyOneForRandomIdsAsync(ErpTestEnvironment env, string email, string permission, IReadOnlyList<string> expectedMenu,
        IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var requests = 0;
        foreach (var carrier in await env.SignInPerCarrierAsync(email, factory))
        {
            using (carrier)
            {
                carriers.Add(carrier.Carrier);
                var session = await carrier.Client.GetFromJsonAsync<JsonElement>("/api/auth/session");
                var granted = session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
                if (granted is not [var only] || only != permission)
                {
                    problems.Add($"[{carrier.Carrier}] {permission}: session lists [{string.Join(", ", granted)}]");
                }
                var menu = session.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToList();
                if (!menu.SequenceEqual(expectedMenu))
                {
                    problems.Add($"[{carrier.Carrier}] {permission}: menu shows [{string.Join(", ", menu)}], expected [{string.Join(", ", expectedMenu)}]");
                }
                foreach (var endpoint in permissioned)
                {
                    using var response = await carrier.Client.SendAsync(G2PermissionTests.Request(endpoint));
                    requests++;
                    var opened = response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
                    var shouldOpen = endpoint.Permission == permission;
                    if (opened != shouldOpen)
                    {
                        problems.Add($"[{carrier.Carrier}] {permission}: {endpoint} answered {(int)response.StatusCode} (expected {(shouldOpen ? "allowed" : "403")})");
                    }
                }
            }
        }
        return new Result(problems, carriers, requests);
    }

    /// <summary>A token the server never issued, sent through every carrier: every permissioned
    /// endpoint answers 401, and so does the session endpoint.</summary>
    public static async Task<Result> UnknownTokenAsync(ErpTestEnvironment env, IReadOnlyList<ApiEndpoint> permissioned)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var requests = 0;
        var token = SessionTokens.Generate();
        foreach (var carrier in SessionTokenCarriers.All)
        {
            carriers.Add(carrier.Name);
            using var client = env.ClientCarrying(carrier.Name, token);
            // The session endpoint is anonymous: it answers, as nobody.
            using (var session = await client.GetAsync("/api/auth/session"))
            {
                if (await SignedInAsync(session) is { } who)
                {
                    problems.Add($"[{carrier.Name}] GET /api/auth/session with an unknown token: {(int)session.StatusCode} {who}");
                }
            }
            foreach (var endpoint in permissioned)
            {
                using var response = await client.SendAsync(G2PermissionTests.Request(endpoint));
                requests++;
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    problems.Add($"[{carrier.Name}] {endpoint} with an unknown token: {(int)response.StatusCode}");
                }
            }
        }
        return new Result(problems, carriers, requests);
    }

    /// <summary>Who an answer of <c>GET /api/auth/session</c> says is signed in, or null for
    /// nobody (401, or 200 with <c>authenticated: false</c> and no user).</summary>
    public static async Task<string?> SignedInAsync(HttpResponseMessage session)
    {
        if (session.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
        if (session.StatusCode != HttpStatusCode.OK)
        {
            return $"answered {(int)session.StatusCode}";
        }
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        var user = body.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u.GetProperty("email").GetString() : null;
        return body.GetProperty("authenticated").GetBoolean() || user is not null ? $"signed in as {user ?? "someone"}" : null;
    }

    /// <param name="Requests">Requests with random ids (one per endpoint and carrier).</param>
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Carriers, int Requests)
    {
        /// <summary>Requests that named one of the caller's own ids.</summary>
        public int SelfRequests { get; init; }

        /// <summary>The most own ids found for one session of the caller.</summary>
        public int OwnIdsTried { get; init; }

        /// <summary>Endpoints the requests naming own ids went to.</summary>
        public int SelfEndpoints { get; init; }
    }
}

/// <summary>
/// Self-test plant (critic p00 round 9, P3): any signed-in caller that sends an
/// <c>Authorization: Bearer</c> header passes every permission check, while cookie sessions are
/// still checked. Hosted only by the G2 self-test's planted host, over the G2 environment's
/// database; the carrier matrices must report it for the Bearer carrier and only for it.
/// </summary>
public sealed class BearerBypassModule : ErpModule
{
    public override string Name => "bearerbypass";

    public override void Register(ModuleBuilder module) =>
        module.Services.AddSingleton<IAuthorizationHandler, BearerBypassHandler>();

    public static Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?> baseSettings)
    {
        var settings = new Dictionary<string, string?>(baseSettings)
        {
            ["Erp:Testing:ExtraModules"] = typeof(BearerBypassModule).AssemblyQualifiedName,
        };
        return settings;
    }

    private sealed class BearerBypassHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true && context.Resource is HttpContext http &&
                http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
