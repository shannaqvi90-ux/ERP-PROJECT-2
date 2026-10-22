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
    /// <param name="ownIds">False for the carrier self-test alone (its plant is about carriers, and
    /// the own-id requests have a self-test of their own).</param>
    /// <param name="origins">Where the sign-in and each request come from (<see cref="Origins"/>);
    /// the in-memory server's own, as before, when null.</param>
    /// <param name="onlyCarriers">The carriers to run (every carrier when null; self-tests only).</param>
    public static async Task<Result> DeniedEverythingAsync(ErpTestEnvironment env, string email, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null, bool ownIds = true,
        Origins? origins = null, IReadOnlyList<string>? onlyCarriers = null)
    {
        var random = await DeniedForRandomIdsAsync(env, email, permissioned, factory, origins, onlyCarriers);
        if (!ownIds)
        {
            return random;
        }
        var self = await SelfReferencingAsync(env, email, null, permissioned, onlyCarriers ?? SessionTokenCarriers.All.Select(c => c.Name).ToList(), factory, origins);
        return random with
        {
            Problems = [.. random.Problems, .. self.Problems], SelfRequests = self.Requests, OwnIdsTried = self.OwnIdsTried, SelfEndpoints = self.Endpoints,
            OriginsTried = random.OriginsTried.Union(self.Origins).ToHashSet(StringComparer.Ordinal),
        };
    }

    /// <summary>The finding prefix of a carrier, and of the origin when the run has origins.</summary>
    private static string Tag(string carrier, ClientOrigin? origin) => origin is null ? carrier : $"{carrier}@{origin.Name}";

    private static async Task<Result> DeniedForRandomIdsAsync(ErpTestEnvironment env, string email, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory,
        Origins? origins, IReadOnlyList<string>? onlyCarriers)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        foreach (var carrier in await env.SignInPerCarrierAsync(email, factory, origin: origins?.SignIn))
        {
            using (carrier)
            {
                if (onlyCarriers is not null && !onlyCarriers.Contains(carrier.Carrier))
                {
                    continue;
                }
                carriers.Add(carrier.Carrier);
                using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
                origins?.SignIn?.Apply(sessionRequest);
                using var sessionResponse = await carrier.Client.SendAsync(sessionRequest);
                var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
                var sessionTag = Tag(carrier.Carrier, origins?.SignIn);
                if (session.GetProperty("permissions").GetArrayLength() != 0)
                {
                    problems.Add($"[{sessionTag}] the session lists permissions: {session.GetProperty("permissions")}");
                }
                if (session.GetProperty("menu").GetArrayLength() != 0)
                {
                    problems.Add($"[{sessionTag}] the session shows menu entries: {session.GetProperty("menu")}");
                }
                foreach (var endpoint in permissioned)
                {
                    var origin = origins?.ForRequest(requests);
                    using var request = G2PermissionTests.Request(endpoint);
                    origin?.Apply(request);
                    using var response = await carrier.Client.SendAsync(request);
                    requests++;
                    if (origin is not null)
                    {
                        tried.Add(origin.Name);
                    }
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        problems.Add($"[{Tag(carrier.Carrier, origin)}] {endpoint}: {(int)response.StatusCode}");
                    }
                }
            }
        }
        return new Result(problems, carriers, requests) { OriginsTried = tried };
    }

    /// <summary>A user holding exactly <paramref name="permission"/>: through every carrier, the
    /// session lists exactly it and its menu entries, and exactly the endpoints that declare it
    /// open. Then, through each carrier in <paramref name="selfCarriers"/> (every carrier when
    /// null; none when empty, for the carrier self-test alone), with a session of its own: every endpoint of the permission's module that declares
    /// another permission answers 403 when the request names any id of the caller's own
    /// (<see cref="OwnIds"/>), so holding one permission never opens another action on "my own"
    /// records.</summary>
    /// <param name="origins">Where the sign-in and each request come from (<see cref="Origins"/>);
    /// the in-memory server's own, as before, when null.</param>
    public static async Task<Result> ExactlyOneAsync(ErpTestEnvironment env, string email, string permission, IReadOnlyList<string> expectedMenu,
        IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null, IReadOnlyList<string>? selfCarriers = null, Origins? origins = null)
    {
        var random = await ExactlyOneForRandomIdsAsync(env, email, permission, expectedMenu, permissioned, factory, origins);
        if (selfCarriers is [])
        {
            return random;
        }
        var module = OwnIds.ModuleOf(permission);
        var others = permissioned.Where(e => e.Permission != permission && OwnIds.ModuleOf(e.Permission) == module).ToList();
        var self = await SelfReferencingAsync(env, email, permission, others, selfCarriers ?? SessionTokenCarriers.All.Select(c => c.Name).ToList(), factory, origins);
        return random with
        {
            Problems = [.. random.Problems, .. self.Problems], SelfRequests = self.Requests, OwnIdsTried = self.OwnIdsTried, SelfEndpoints = self.Endpoints,
            OriginsTried = random.OriginsTried.Union(self.Origins).ToHashSet(StringComparer.Ordinal),
        };
    }

    /// <summary>Every endpoint given answers 403 to requests naming the caller's own ids, through
    /// each carrier named, each carrier with a session of its own (an action that ends the session
    /// under a planted bypass then spoils only that carrier's later requests).</summary>
    private static async Task<SelfResult> SelfReferencingAsync(ErpTestEnvironment env, string email, string? holding, IReadOnlyList<ApiEndpoint> endpoints,
        IReadOnlyList<string> carriers, ErpAppFactory? factory, Origins? origins)
    {
        var problems = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        var ownIds = 0;
        foreach (var carrier in carriers)
        {
            var (token, userId) = await env.SignInForTokenAsync(email, factory, origin: origins?.SignIn);
            var own = await OwnIds.OfAsync(env, userId, token);
            ownIds = Math.Max(ownIds, own.Count);
            using var client = env.ClientCarrying(carrier, token, factory);
            foreach (var (endpoint, id, build) in OwnIds.Requests(endpoints, own))
            {
                var origin = origins?.ForRequest(requests);
                using var request = build();
                origin?.Apply(request);
                using var response = await client.SendAsync(request);
                requests++;
                if (origin is not null)
                {
                    tried.Add(origin.Name);
                }
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    problems.Add($"[{Tag(carrier, origin)}] {(holding is null ? "" : holding + ": ")}{endpoint} naming {id.Source} answered {(int)response.StatusCode} (expected 403) {body[..Math.Min(160, body.Length)]}");
                }
            }
        }
        return new SelfResult(problems, requests, ownIds, endpoints.Count, tried);
    }

    private sealed record SelfResult(IReadOnlyList<string> Problems, int Requests, int OwnIdsTried, int Endpoints, IReadOnlySet<string> Origins);

    private static async Task<Result> ExactlyOneForRandomIdsAsync(ErpTestEnvironment env, string email, string permission, IReadOnlyList<string> expectedMenu,
        IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory, Origins? origins)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        foreach (var carrier in await env.SignInPerCarrierAsync(email, factory, origin: origins?.SignIn))
        {
            using (carrier)
            {
                carriers.Add(carrier.Carrier);
                using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
                origins?.SignIn?.Apply(sessionRequest);
                using var sessionResponse = await carrier.Client.SendAsync(sessionRequest);
                var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
                var sessionTag = Tag(carrier.Carrier, origins?.SignIn);
                var granted = session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
                if (granted is not [var only] || only != permission)
                {
                    problems.Add($"[{sessionTag}] {permission}: session lists [{string.Join(", ", granted)}]");
                }
                var menu = session.GetProperty("menu").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToList();
                if (!menu.SequenceEqual(expectedMenu))
                {
                    problems.Add($"[{sessionTag}] {permission}: menu shows [{string.Join(", ", menu)}], expected [{string.Join(", ", expectedMenu)}]");
                }
                foreach (var endpoint in permissioned)
                {
                    var origin = origins?.ForRequest(requests);
                    using var request = G2PermissionTests.Request(endpoint);
                    origin?.Apply(request);
                    using var response = await carrier.Client.SendAsync(request);
                    requests++;
                    if (origin is not null)
                    {
                        tried.Add(origin.Name);
                    }
                    var opened = response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized);
                    var shouldOpen = endpoint.Permission == permission;
                    if (opened != shouldOpen)
                    {
                        problems.Add($"[{Tag(carrier.Carrier, origin)}] {permission}: {endpoint} answered {(int)response.StatusCode} (expected {(shouldOpen ? "allowed" : "403")})");
                    }
                }
            }
        }
        return new Result(problems, carriers, requests) { OriginsTried = tried };
    }

    /// <summary>A token the server never issued, sent through every carrier: every permissioned
    /// endpoint answers 401, and so does the session endpoint.</summary>
    /// <param name="origins">Where each request comes from (<see cref="Origins"/>); the in-memory
    /// server's own, as before, when null.</param>
    public static async Task<Result> UnknownTokenAsync(ErpTestEnvironment env, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null, Origins? origins = null)
    {
        var problems = new List<string>();
        var carriers = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        var token = SessionTokens.Generate();
        foreach (var carrier in SessionTokenCarriers.All)
        {
            carriers.Add(carrier.Name);
            using var client = env.ClientCarrying(carrier.Name, token, factory);
            // The session endpoint is anonymous: it answers, as nobody.
            using (var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session"))
            {
                origins?.SignIn?.Apply(sessionRequest);
                using var session = await client.SendAsync(sessionRequest);
                if (await SignedInAsync(session) is { } who)
                {
                    problems.Add($"[{Tag(carrier.Name, origins?.SignIn)}] GET /api/auth/session with an unknown token: {(int)session.StatusCode} {who}");
                }
            }
            foreach (var endpoint in permissioned)
            {
                var origin = origins?.ForRequest(requests);
                using var request = G2PermissionTests.Request(endpoint);
                origin?.Apply(request);
                using var response = await client.SendAsync(request);
                requests++;
                if (origin is not null)
                {
                    tried.Add(origin.Name);
                }
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    problems.Add($"[{Tag(carrier.Name, origin)}] {endpoint} with an unknown token: {(int)response.StatusCode}");
                }
            }
        }
        return new Result(problems, carriers, requests) { OriginsTried = tried };
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

        /// <summary>The client origins requests came from (empty for a run without origins).</summary>
        public IReadOnlySet<string> OriginsTried { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Where a matrix's sign-in and each of its requests come from (critic p00 round 13, plant
    /// P11). <see cref="SignIn"/> is the origin of the sign-in and the session check;
    /// <see cref="ForRequest"/> gives the origin of the request with that running number.
    /// </summary>
    public sealed record Origins(ClientOrigin? SignIn, Func<int, ClientOrigin> ForRequest)
    {
        /// <summary>Everything from <paramref name="origin"/>.</summary>
        public static Origins From(ClientOrigin origin) => new(origin, _ => origin);

        /// <summary>The sign-in from origin <paramref name="offset"/> of <paramref name="all"/>, and
        /// request k from origin <paramref name="offset"/> + k, in turn: every origin gets requests,
        /// and consecutive requests (one endpoint's own ids, or neighbouring endpoints) come from
        /// different origins.</summary>
        public static Origins Rotating(IReadOnlyList<ClientOrigin> all, int offset) =>
            new(all[offset % all.Count], k => all[(offset + k) % all.Count]);
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
