using System.Net;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2 sends every permissioned endpoint's path in a varied-case form to a caller who must be refused
/// (backlog row 13, critic p00 round 16, plant P16). ASP.NET Core routing ignores case, so
/// <c>/API/identity/users</c> is the same endpoint as <c>/api/identity/users</c>; a permission
/// check, a middleware or a policy that tests the path by a case-sensitive comparison would let the
/// varied form through unchecked. One request per endpoint, rotating through three forms
/// (all upper case, capitalised segments, alternating case), cookie session only.
/// </summary>
public sealed class G2PathCaseTests(G2Fixture fixture) : IClassFixture<G2Fixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    /// <summary>The path in the varied-case form <paramref name="form"/> (0: upper case, 1: capitalised
    /// segments, 2: alternating case). Route values are GUIDs, whose case routing does not read.</summary>
    internal static string Vary(string pathAndQuery, int form)
    {
        var query = pathAndQuery.IndexOf('?');
        var path = query < 0 ? pathAndQuery : pathAndQuery[..query];
        var tail = query < 0 ? "" : pathAndQuery[query..];
        var varied = (form % 3) switch
        {
            0 => path.ToUpperInvariant(),
            1 => string.Join('/', path.Split('/').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..])),
            _ => new string(path.Select((c, i) => i % 2 == 0 ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray()),
        };
        return varied + tail;
    }

    /// <summary>Every permissioned endpoint in a varied-case form, as the cookie session of
    /// <paramref name="email"/> (a user whose roles grant nothing): each must answer 403.</summary>
    internal static async Task<(List<string> Problems, int Requests, int Forms)> DeniedAsync(
        ErpTestEnvironment env, string email, IReadOnlyList<ApiEndpoint> permissioned, ErpAppFactory? factory = null)
    {
        var problems = new List<string>();
        var requests = 0;
        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var carrier in await env.SignInPerCarrierAsync(email, factory))
        {
            using (carrier)
            {
                if (carrier.Carrier != SessionTokenCarriers.CookieName)
                {
                    continue;
                }
                foreach (var endpoint in permissioned)
                {
                    using var request = G2PermissionTests.Request(endpoint);
                    var original = request.RequestUri!.OriginalString;
                    request.RequestUri = new Uri(Vary(original, requests), UriKind.RelativeOrAbsolute);
                    forms.Add(request.RequestUri.OriginalString[..Math.Min(5, request.RequestUri.OriginalString.Length)]);
                    using var response = await carrier.Client.SendTimedAsync(request, "G2 varied case");
                    requests++;
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        problems.Add($"{endpoint} as {request.RequestUri.OriginalString}: {(int)response.StatusCode}");
                    }
                }
            }
        }
        return (problems, requests, forms.Count);
    }

    [Fact]
    public async Task A_user_whose_roles_grant_nothing_is_denied_every_endpoint_in_varied_case()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var email = $"g2.pathcase.none.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], "Nothing", "لا شيء");
        var permissioned = EndpointInventory.From(Env.Factory.Services).Where(e => !e.IsAnonymous).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (problems, requests, forms) = await DeniedAsync(Env, email, permissioned);
        TestContext.Current.TestOutputHelper?.WriteLine($"{requests} varied-case requests in {sw.Elapsed.TotalSeconds:0.0} s wall clock");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
        Assert.Equal(permissioned.Count, requests);
        Assert.True(forms >= 3, $"{forms} different prefixes sent");
        Assert.True(requests >= Ratchet.Min("g2.pathCaseDenyRequests"), $"{requests} varied-case deny requests; ratchet minimum {Ratchet.Min("g2.pathCaseDenyRequests")}");
    }

    [Fact]
    public void The_varied_forms_differ_from_the_lower_case_path_and_keep_the_query()
    {
        const string path = "/api/identity/users/0f0e0d0c?take=5&Sort=name";
        var forms = new[] { 0, 1, 2 }.Select(f => Vary(path, f)).ToList();
        Assert.Equal(["/API/IDENTITY/USERS/0F0E0D0C?take=5&Sort=name", "/Api/Identity/Users/0f0e0d0c?take=5&Sort=name", "/ApI/IdEnTiTy/uSeRs/0F0E0D0C?take=5&Sort=name"], forms);
        Assert.All(forms, f => Assert.False(f.StartsWith("/api/", StringComparison.Ordinal)));
    }

    /// <summary>Self-test (plant P16): a permission check that passes any signed-in request whose path
    /// does not start with the lower-case <c>/api/</c>. The lower-case deny matrices stay clean with
    /// it on (that is how it passed every G2 test); the varied-case deny must report every endpoint.</summary>
    [Fact]
    public async Task The_varied_case_deny_catches_a_permission_check_that_reads_the_path_case_sensitively()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var email = $"g2.pathcase.plant.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        await G2PermissionTests.CreateUserHoldingAsync(admin, email, [], "Nothing plant", "لا شيء");
        await using var planted = new ErpAppFactory(PathCaseModule.Settings(Env.Factory.Settings));
        var plant = planted.Services.GetRequiredService<PathCaseModule.Switch>();
        var permissioned = EndpointInventory.From(planted.Services).Where(e => !e.IsAnonymous).ToList();
        var (clean, _, _) = await DeniedAsync(Env, email, permissioned, planted);
        Assert.True(clean.Count == 0, "Without the plant on, the varied-case deny reported:\n" + string.Join("\n", clean.Take(10)));
        plant.On = true;
        try
        {
            // The lower-case deny cannot see it: the plant leaves /api/ paths to the real check.
            var lower = await CarrierMatrix.DeniedEverythingAsync(Env, email, permissioned, planted, ownIds: false, onlyCarriers: [SessionTokenCarriers.CookieName]);
            Assert.True(lower.Problems.Count == 0, "The lower-case deny matrix reported:\n" + string.Join("\n", lower.Problems.Take(10)));
            var (problems, requests, _) = await DeniedAsync(Env, email, permissioned, planted);
            TestContext.Current.TestOutputHelper?.WriteLine($"plant P16: {problems.Count} of {requests} varied-case requests passed");
            Assert.Equal(permissioned.Count, problems.Count);
            Assert.All(problems, p => Assert.DoesNotContain(": 403", p, StringComparison.Ordinal));
        }
        finally
        {
            plant.On = false;
        }
        Assert.True(1 >= Ratchet.Min("g2.pathCasePlantsCaught"), $"1 path-case plant; ratchet minimum {Ratchet.Min("g2.pathCasePlantsCaught")}");
    }
}
