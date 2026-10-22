using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>The G2 environment, a host of it that knows a reverse proxy and a shared network
/// (<see cref="ClientOrigin.HostSettings"/>, the way a deployment behind nginx or Caddy is set up),
/// and, for the self-tests, a planted host of it (<see cref="AddressTrustModule"/>).</summary>
public sealed class G2OriginFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    /// <summary>The real product, configured with a proxy and a shared network.</summary>
    public ErpAppFactory Host { get; private set; } = null!;

    /// <summary>The same, plus the address-trust plant (off until a self-test switches it on).</summary>
    public ErpAppFactory Planted { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        var settings = new Dictionary<string, string?>(Env.Factory.Settings);
        foreach (var (key, value) in ClientOrigin.HostSettings)
        {
            settings[key] = value;
        }
        Host = new ErpAppFactory(settings);
        Planted = new ErpAppFactory(AddressTrustModule.Settings(Env.Factory.Settings));
    }

    public async ValueTask DisposeAsync()
    {
        await Planted.DisposeAsync();
        await Host.DisposeAsync();
        await Env.DisposeAsync();
    }
}

/// <summary>
/// G2 from every client address (critic p00 round 13, the round's biggest gap). Every request of
/// the other G2 matrices reaches the in-memory test server with no client address, so a permission
/// decision that depends on where a request comes from was never tried: plant P11 in the kernel's
/// permission handler passed every signed-in request from loopback, and with it a user with no
/// roles read every user and role of the workspace while every gate passed. Behind nginx or Caddy
/// on the same host every browser request reaches the app from 127.0.0.1, so one such rule would
/// hand every clerk the owner's rights. Here the deny-everything, own-id, grant-exactly-one,
/// anonymous and unknown-token matrices run from every origin of <see cref="ClientOrigin.All"/>
/// (loopback in four spellings, the compose and machine gateways, a configured proxy with and
/// without X-Forwarded-For, a declared shared address, ordinary clients and a client that claims
/// loopback in its own header), over a host configured with that proxy and shared network; the
/// self-tests plant five address-trusting checks and require each to be caught from exactly the
/// origins it trusts.
/// </summary>
public sealed class G2ClientOriginTests(G2OriginFixture fixture) : IClassFixture<G2OriginFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private IReadOnlyList<ApiEndpoint> Permissioned => EndpointInventory.From(fixture.Host.Services).Where(e => !e.IsAnonymous).ToList();

    private static IReadOnlyList<ClientOrigin> Origins => ClientOrigin.All;

    private static void AssertEveryOrigin(IReadOnlySet<string> tried, string what)
    {
        var missing = Origins.Select(o => o.Name).Where(n => !tried.Contains(n)).ToList();
        Assert.True(missing.Count == 0, $"{what}: no request came from {string.Join(", ", missing)}");
        Assert.True(tried.Count >= Ratchet.Min("g2.clientOrigins"), $"{what}: {tried.Count} client origins; ratchet minimum {Ratchet.Min("g2.clientOrigins")}");
    }

    /// <summary>The address the product recorded for the session of <paramref name="token"/>,
    /// read as the superuser (null for none).</summary>
    private async Task<string?> SessionAddressAsync(string token)
    {
        await using var connection = await Env.OpenAdminAsync();
        var rows = await DbCatalog.ReadAsync(connection, "SELECT ip_address::text FROM identity.sessions WHERE token_hash = @h",
            r => r.IsDBNull(0) ? null : r.GetString(0), ("h", SessionTokens.Hash(token)));
        Assert.True(rows.Count == 1, $"{rows.Count} sessions for the token");
        return rows[0] is { } text ? text.Split('/')[0] : null;
    }

    private static string? Normal(ClientOrigin origin) =>
        origin.EffectiveAddress is { IsIPv4MappedToIPv6: true } mapped ? mapped.MapToIPv4().ToString() : origin.EffectiveAddress?.ToString();

    /// <summary>
    /// Each origin reaches the product as the address it stands for: a session signed in from it
    /// records that address (the configured proxy's forwarded client, not the proxy; the spoofing
    /// client's own address, not the loopback it claims; none for the in-memory server's own). A
    /// test client whose origin header the host ignored would make every matrix below a repeat of
    /// the in-memory one; this fails then.
    /// </summary>
    [Fact]
    public async Task Every_client_origin_reaches_the_product_as_the_address_it_stands_for()
    {
        var problems = new List<string>();
        var email = Env.Email(Env.TenantA, "noaccess");
        foreach (var origin in Origins)
        {
            var (token, _) = await Env.SignInForTokenAsync(email, fixture.Host, origin: origin);
            var recorded = await SessionAddressAsync(token);
            if (recorded != Normal(origin))
            {
                problems.Add($"{origin.Name} (peer {origin.Peer ?? "none"}, X-Forwarded-For {origin.ForwardedFor ?? "none"}): the session records {recorded ?? "no address"}, expected {Normal(origin) ?? "no address"}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.Contains(Origins, o => o.Name == "loopback-v6");
        Assert.True(Origins.Count >= Ratchet.Min("g2.clientOrigins"), $"{Origins.Count} client origins; ratchet minimum {Ratchet.Min("g2.clientOrigins")}");
    }

    /// <summary>
    /// A user whose roles grant nothing, signed in from each origin in turn: through every carrier
    /// the session lists no permission and no menu entry and every permissioned endpoint answers
    /// 403 from that origin. Then the requests naming every id of the caller's own, through every
    /// carrier, come from the origins in turn.
    /// </summary>
    [Fact]
    public async Task A_user_whose_roles_grant_nothing_is_denied_everything_from_every_client_address()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var email = $"g2.origins.none.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], "Nothing", "لا شيء");
        var problems = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        foreach (var origin in Origins)
        {
            var result = await CarrierMatrix.DeniedEverythingAsync(Env, email, Permissioned, fixture.Host, ownIds: false, origins: CarrierMatrix.Origins.From(origin));
            problems.AddRange(result.Problems);
            tried.UnionWith(result.OriginsTried);
            requests += result.Requests;
            Assert.Equal(SessionTokenCarriers.All.Select(c => c.Name), result.Carriers);
        }
        var self = await CarrierMatrix.DeniedEverythingAsync(Env, email, Permissioned, fixture.Host, origins: CarrierMatrix.Origins.Rotating(Origins, 0));
        problems.AddRange(self.Problems);
        TestContext.Current.TestOutputHelper?.WriteLine($"{requests} requests from {tried.Count} origins with random ids; {self.SelfRequests} naming the caller's own ids from {self.OriginsTried.Count} origins");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        AssertEveryOrigin(tried, "deny by default");
        AssertEveryOrigin(self.OriginsTried, "deny by default, own ids");
        Assert.Equal(Origins.Count * Permissioned.Count * SessionTokenCarriers.All.Count, requests);
        Assert.Equal(Permissioned.Count, self.SelfEndpoints);
        Assert.True(requests >= Ratchet.Min("g2.clientOriginDenyRequests"), $"{requests} deny requests from client origins; ratchet minimum {Ratchet.Min("g2.clientOriginDenyRequests")}");
        Assert.True(self.SelfRequests >= Ratchet.Min("g2.clientOriginSelfRequests"), $"{self.SelfRequests} own-id requests from client origins; ratchet minimum {Ratchet.Min("g2.clientOriginSelfRequests")}");
    }

    /// <summary>No session, or a token the server never issued: every permissioned endpoint answers
    /// 401 from every origin (without a session, through every carrier with the unknown token).</summary>
    [Fact]
    public async Task Anonymous_callers_and_unknown_tokens_get_401_from_every_client_address()
    {
        var problems = new List<string>();
        var requests = 0;
        foreach (var origin in Origins)
        {
            using var client = origin.Apply(fixture.Host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }));
            client.DefaultRequestHeaders.Add("X-Erp-Request", "1");
            foreach (var endpoint in Permissioned)
            {
                using var response = await client.SendAsync(G2PermissionTests.Request(endpoint));
                requests++;
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    problems.Add($"[anonymous@{origin.Name}] {endpoint}: {(int)response.StatusCode}");
                }
            }
        }
        var tried = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < Origins.Count; offset += Math.Max(1, Origins.Count / 3))
        {
            var unknown = await CarrierMatrix.UnknownTokenAsync(Env, Permissioned, fixture.Host, CarrierMatrix.Origins.Rotating(Origins, offset));
            problems.AddRange(unknown.Problems);
            tried.UnionWith(unknown.OriginsTried);
            requests += unknown.Requests;
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        AssertEveryOrigin(tried, "unknown token");
        Assert.True(requests >= Ratchet.Min("g2.clientOriginUnauthenticatedRequests"),
            $"{requests} unauthenticated requests from client origins; ratchet minimum {Ratchet.Min("g2.clientOriginUnauthenticatedRequests")}");
    }

    /// <summary>
    /// One user per permission, each signed in from the next origin, each request from the next
    /// origin after that (so every permission meets every origin, and every endpoint meets every
    /// origin across the permissions): exactly the endpoints that declare the permission open,
    /// through every carrier. The requests naming the caller's own ids at the module's other
    /// endpoints go through one carrier per permission, the carriers and origins in turn.
    /// </summary>
    [Fact]
    public async Task Granting_exactly_one_permission_opens_exactly_its_endpoints_from_every_client_address()
    {
        var catalog = fixture.Host.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var problems = new List<string>();
        var tried = new HashSet<string>(StringComparer.Ordinal);
        var selfTried = new HashSet<string>(StringComparer.Ordinal);
        var requests = 0;
        var selfRequests = 0;
        var index = 0;
        foreach (var permission in catalog.PermissionKeys.Order(StringComparer.Ordinal))
        {
            var email = $"g2.origins.{permission.Replace('.', '-')}@{Env.TenantA.EmailDomain}";
            await G2PermissionTests.CreateUserHoldingAsync(admin, email, [permission], $"Only {permission} (origins)", $"فقط {permission} (العناوين)");
            var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();
            var selfCarrier = SessionTokenCarriers.All[index % SessionTokenCarriers.All.Count].Name;
            var result = await CarrierMatrix.ExactlyOneAsync(Env, email, permission, expectedMenu, Permissioned, fixture.Host, selfCarriers: [selfCarrier],
                origins: CarrierMatrix.Origins.Rotating(Origins, index));
            problems.AddRange(result.Problems);
            Assert.Equal(SessionTokenCarriers.All.Select(c => c.Name), result.Carriers);
            requests += result.Requests;
            selfRequests += result.SelfRequests;
            tried.UnionWith(result.OriginsTried);
            index++;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{index} permissions, {requests} requests and {selfRequests} naming own ids from {tried.Count} origins");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        AssertEveryOrigin(tried, "grant exactly one");
        Assert.Equal(index * Permissioned.Count * SessionTokenCarriers.All.Count, requests);
        Assert.True(index >= Ratchet.Min("g2.permissions"));
        Assert.True(requests >= Ratchet.Min("g2.clientOriginGrantRequests"), $"{requests} grant requests from client origins; ratchet minimum {Ratchet.Min("g2.clientOriginGrantRequests")}");
        Assert.True(selfRequests >= Ratchet.Min("g2.clientOriginGrantSelfRequests"),
            $"{selfRequests} own-id grant requests from client origins; ratchet minimum {Ratchet.Min("g2.clientOriginGrantSelfRequests")}");
    }

    /// <summary>The origin a finding came from (<c>[cookie@loopback-v6] …</c>).</summary>
    private static string OriginOf(string problem)
    {
        var at = problem.IndexOf('@', StringComparison.Ordinal);
        var end = problem.IndexOf(']', StringComparison.Ordinal);
        return at > 0 && end > at ? problem[(at + 1)..end] : "";
    }

    private static readonly string[] MustCatch =
    [
        "loopback:loopback", "loopback:loopback-other", "loopback:loopback-v6", "loopback:proxy-for-loopback",
        "private:office-lan", "private:compose-gateway", "private:proxy", "private:loopback",
        "shared:shared", "forwarded:proxy-for-client", "forwarded:proxy-for-loopback",
    ];

    /// <summary>
    /// Self-test: permission checks that trust where a request comes from
    /// (<see cref="AddressTrustModule"/>), planted in a host over this environment's database. The
    /// deny-by-default matrix, run from every origin as above, must report each plant from exactly
    /// the origins whose address (after the product's proxy handling) the plant trusts, and from no
    /// other: the critic's loopback rule from 127.0.0.1, 127.0.0.53, ::1 and the proxy forwarding a
    /// loopback caller, never from the client that claims loopback in its own header.
    /// </summary>
    [Theory]
    [InlineData("loopback")]
    [InlineData("private")]
    [InlineData("shared")]
    [InlineData("forwarded")]
    public async Task The_origin_matrices_catch_a_permission_check_that_trusts_where_a_request_comes_from(string mode)
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var email = $"g2.origins.plant.{mode}.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], $"Nothing {mode}", $"لا شيء {mode}");
        var plant = fixture.Planted.Services.GetRequiredService<AddressTrustModule.Switch>();
        var problems = new List<string>();
        var permissioned = EndpointInventory.From(fixture.Planted.Services).Where(e => !e.IsAnonymous).ToList();
        plant.Mode = mode;
        try
        {
            foreach (var origin in Origins)
            {
                var result = await CarrierMatrix.DeniedEverythingAsync(Env, email, permissioned, fixture.Planted, ownIds: false,
                    origins: CarrierMatrix.Origins.From(origin), onlyCarriers: [SessionTokenCarriers.CookieName]);
                problems.AddRange(result.Problems);
            }
        }
        finally
        {
            plant.Mode = "";
        }
        var expected = Origins.Where(o => AddressTrustModule.Trusts(mode, o.EffectiveAddress, o.Peer == ClientOrigin.Proxy && o.ForwardedFor is not null, holdsAny: false))
            .Select(o => o.Name).Order(StringComparer.Ordinal).ToList();
        var reported = problems.Select(OriginOf).Distinct().Order(StringComparer.Ordinal).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"{mode}: {problems.Count} findings from {string.Join(", ", reported)}");
        Assert.True(expected.SequenceEqual(reported), $"plant {mode}: expected findings from [{string.Join(", ", expected)}], got [{string.Join(", ", reported)}]\n" + string.Join("\n", problems.Take(20)));
        foreach (var name in MustCatch.Where(m => m.StartsWith(mode + ":", StringComparison.Ordinal)).Select(m => m[(mode.Length + 1)..]))
        {
            Assert.Contains(name, reported);
        }
        Assert.DoesNotContain("spoofed-loopback", reported);
        Assert.DoesNotContain("in-memory", reported);
        // Each trusted origin answered 200 on every reading endpoint of the users screen, as P11 did.
        foreach (var name in expected)
        {
            Assert.Contains(problems, p => p.StartsWith($"[{SessionTokenCarriers.CookieName}@{name}] GET /api/identity/users:", StringComparison.Ordinal) && p.EndsWith(": 200", StringComparison.Ordinal));
        }
        Assert.True(AddressTrustModule.DenyModes.Length + 1 >= Ratchet.Min("g2.clientOriginPlantsCaught"),
            $"{AddressTrustModule.DenyModes.Length + 1} address plants; ratchet minimum {Ratchet.Min("g2.clientOriginPlantsCaught")}");
    }

    /// <summary>
    /// Self-test: a check that trusts loopback callers who hold any permission at all
    /// (<c>held-loopback</c>). The deny-by-default matrix cannot see it (its caller holds nothing)
    /// and stays clean; the grant-exactly-one matrix with its origins in turn must report it, from
    /// loopback origins only.
    /// </summary>
    [Fact]
    public async Task The_origin_grant_matrix_catches_a_check_that_trusts_loopback_callers_holding_any_permission()
    {
        var catalog = fixture.Planted.Services.GetRequiredService<ModuleCatalog>();
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        const string permission = "identity.users.read";
        var nobody = $"g2.origins.held.none.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        var holder = $"g2.origins.held.one.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, nobody, [], "Nothing held", "لا شيء محتجز");
        await G2PermissionTests.CreateUserHoldingAsync(admin, holder, [permission], "Read users held", "قراءة المستخدمين محتجز");
        var expectedMenu = catalog.Menu.Where(m => m.Permission == permission).Select(m => m.Key).ToList();
        var permissioned = EndpointInventory.From(fixture.Planted.Services).Where(e => !e.IsAnonymous).ToList();
        var plant = fixture.Planted.Services.GetRequiredService<AddressTrustModule.Switch>();
        CarrierMatrix.Result denied, granted;
        plant.Mode = "held-loopback";
        try
        {
            denied = await CarrierMatrix.DeniedEverythingAsync(Env, nobody, permissioned, fixture.Planted, ownIds: false, origins: CarrierMatrix.Origins.Rotating(Origins, 0));
            granted = await CarrierMatrix.ExactlyOneAsync(Env, holder, permission, expectedMenu, permissioned, fixture.Planted, selfCarriers: [],
                origins: CarrierMatrix.Origins.Rotating(Origins, 1));
        }
        finally
        {
            plant.Mode = "";
        }
        Assert.True(denied.Problems.Count == 0, "The deny matrix cannot see this plant, yet reported:\n" + string.Join("\n", denied.Problems));
        var reported = granted.Problems.Select(OriginOf).Distinct().ToList();
        var loopbacks = Origins.Where(o => o.EffectiveAddress is { } a && IPAddress.IsLoopback(a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a)).Select(o => o.Name).ToList();
        Assert.True(granted.Problems.Count > 0, "The grant-exactly-one matrix missed loopback callers holding a permission passing every check");
        Assert.True(reported.All(loopbacks.Contains), $"Findings from origins the plant does not trust: {string.Join(", ", reported.Except(loopbacks))}");
        Assert.All(granted.Problems, p => Assert.EndsWith("(expected 403)", p, StringComparison.Ordinal));
    }
}
