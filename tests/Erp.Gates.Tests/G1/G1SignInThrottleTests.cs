using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment, behind a configured proxy so each request can name its client
/// address (X-Forwarded-For) the way a real deployment sees it.</summary>
public sealed class ThrottleFixture : IAsyncLifetime
{
    public const string Proxy = "192.0.2.1";

    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?> { ["Erp:Http:KnownProxies"] = Proxy });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, sign-in throttling. Failed sign-ins pause only the client that failed, for only the account
/// it failed on: knowing an address (one's own tenant's, another tenant's, or one that exists in
/// both) never lets anyone stop that account's owner signing in from their own client, and
/// failures against one tenant's account never touch another tenant's account. A paused client
/// gets exactly the answer a wrong password gets, and no answer states the policy.
/// </summary>
public sealed class G1SignInThrottleTests(ThrottleFixture fixture) : IClassFixture<ThrottleFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private const int Attempts = 12;

    [Fact]
    public async Task Failed_sign_ins_from_one_client_never_lock_the_account_out_for_anyone_else()
    {
        var shared = $"shared.{Guid.NewGuid():N}@both.example";
        using var adminA = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var adminB = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        await CreateUserAsync(adminA, shared, "Password-Of-A-2026");
        await CreateUserAsync(adminB, shared, "Password-Of-B-2026");

        const string attacker = "203.0.113.7";
        var wrongAnswers = new List<string>();
        for (var i = 0; i < Attempts; i++)
        {
            var (status, body) = await SignInAsync(attacker, shared, $"Wrong-Password-{i:D2}");
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            wrongAnswers.Add(Answer(body));
        }
        // The attacker's client is now paused for both accounts: even the right password fails,
        // with the same answer as a wrong one.
        var (pausedStatus, pausedBody) = await SignInAsync(attacker, shared, "Password-Of-B-2026");
        Assert.Equal(HttpStatusCode.Unauthorized, pausedStatus);
        Assert.Single(wrongAnswers.Append(Answer(pausedBody)).Distinct());

        // The owners, from their own clients, are not affected in either tenant.
        var (okB, bodyB) = await SignInAsync("198.51.100.20", shared, "Password-Of-B-2026");
        Assert.Equal(HttpStatusCode.OK, okB);
        Assert.Equal(Env.TenantB.Code, bodyB.GetProperty("tenant").GetProperty("code").GetString());
        var (okA, bodyA) = await SignInAsync("198.51.100.21", shared, "Password-Of-A-2026");
        Assert.Equal(HttpStatusCode.OK, okA);
        Assert.Equal(Env.TenantA.Code, bodyA.GetProperty("tenant").GetProperty("code").GetString());

        // An address that exists only in tenant B, attacked from a client in the same office as a
        // tenant A user: tenant A's users and B's owner elsewhere still sign in.
        var victim = Env.Email(Env.TenantB, "viewer");
        for (var i = 0; i < Attempts; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(attacker, victim, $"Wrong-Password-{i:D2}")).Status);
        }
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync("198.51.100.22", victim, ErpTestEnvironment.Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(attacker, Env.Email(Env.TenantA, "viewer"), ErpTestEnvironment.Password)).Status);
    }

    [Fact]
    public async Task Sign_in_failures_give_one_answer_that_states_no_policy()
    {
        var email = Env.Email(Env.TenantA, "noaccess");
        const string client = "203.0.113.50";
        foreach (var language in new[] { "en", "ar" })
        {
            var answers = new List<string>();
            answers.Add(Answer((await SignInAsync(client, $"nobody.{language}@nowhere.example", "Wrong-Password-1", language)).Body));
            for (var i = 0; i < Attempts; i++)
            {
                answers.Add(Answer((await SignInAsync(client, email, $"Wrong-Password-{language}-{i}", language)).Body));
            }
            answers.Add(Answer((await SignInAsync(client, email, ErpTestEnvironment.Password, language)).Body));
            Assert.Single(answers.Distinct());
            Assert.DoesNotMatch(@"[0-9٠-٩۰-۹]", answers[0]);
        }
    }

    [Fact]
    public async Task A_browser_that_signed_in_before_is_not_paused_by_failures_from_its_network_address()
    {
        // Critic p03 rounds 1-3: behind Docker every client arrives from the compose gateway, so one
        // person's failures paused the account for everyone. The owner's browser, which signed in
        // to the account before, now counts as a client of its own; everyone else behind the same
        // address (the attacker included) is still paused, and a device earned on one's own account
        // or a tampered cookie gives no fresh count on someone else's.
        var email = $"office.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}";
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        await CreateUserAsync(admin, email, "Owner-Password-2026");
        const string office = "203.0.113.50";
        using var owner = Env.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SignInWithAsync(owner, office, email, "Owner-Password-2026")).Status);

        for (var i = 0; i < Attempts; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(office, email, $"Wrong-Password-{i:D2}")).Status);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(office, email, "Owner-Password-2026")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignInWithAsync(owner, office, email, "Owner-Password-2026")).Status);

        // A device earned on the attacker's own account counts nothing on the owner's.
        using var attacker = Env.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SignInWithAsync(attacker, office, Env.Email(Env.TenantA, "viewer"), ErpTestEnvironment.Password)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInWithAsync(attacker, office, email, "Owner-Password-2026")).Status);

        // A tampered cookie is ignored: still the office address, still paused.
        using var forger = Env.CreateClient(requestHeader: true);
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password = "Owner-Password-2026" }) };
        forged.Headers.Add("X-Forwarded-For", office);
        forged.Headers.Add("Cookie", "erp_device=W3siRSI6IkFBQUEiLCJEIjoiQkJCQiJ9XQ.bm90LWEtc2lnbmF0dXJl");
        using (var response = await forger.SendAsync(forged))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // The owner's own failures still pause the owner's device.
        for (var i = 0; i < Attempts; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInWithAsync(owner, office, email, $"Typo-Password-{i:D2}")).Status);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInWithAsync(owner, office, email, "Owner-Password-2026")).Status);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SignInWithAsync(HttpClient client, string clientAddress, string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static string Answer(JsonElement body) =>
        $"{body.GetProperty("code").GetString()}|{body.GetProperty("title").GetString()}";

    private async Task<(HttpStatusCode Status, JsonElement Body)> SignInAsync(string clientAddress, string email, string password, string language = "en")
    {
        using var client = Env.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        request.Headers.AcceptLanguage.ParseAdd(language);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task CreateUserAsync(HttpClient admin, string email, string password)
    {
        var response = await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Shared person", language = "en", password, mustChangePassword = false, roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
