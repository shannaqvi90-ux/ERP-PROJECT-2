using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
/// G1, sign-in throttling. Failed sign-ins pause only the client that failed, for only the address
/// it failed on: knowing an address (one's own tenant's, another tenant's, or one that exists in
/// both) never lets anyone stop that account's owner signing in from their own client, and
/// failures against one tenant's account never touch another tenant's account. A paused client is
/// told so, and until when (critic p00 rounds 7 to 10, routed to p03), with the right password as
/// with a wrong one; and every answer, attempt by attempt (status, code, words, the time left), is
/// the same for an address no account has, one in tenant B only, one in both tenants and a
/// deactivated account, so neither a failure nor a pause tells whether or where an account
/// exists. A failure (before the pause) states no policy: no number in it.
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
        var pausedAnswers = new List<string>();
        for (var i = 0; i < Attempts; i++)
        {
            var (status, body) = await SignInAsync(attacker, shared, $"Wrong-Password-{i:D2}");
            AssertRefusedInTurn(i, status, body);
            if (status == HttpStatusCode.TooManyRequests)
            {
                pausedAnswers.Add(Answer(body));
            }
        }
        // The attacker's client is now paused for both accounts: even the right password is
        // refused, with the same answer as a wrong one.
        var (pausedStatus, pausedBody) = await SignInAsync(attacker, shared, "Password-Of-B-2026");
        Assert.Equal(HttpStatusCode.TooManyRequests, pausedStatus);
        Assert.Single(pausedAnswers.Append(Answer(pausedBody)).Distinct());

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
            var (status, body) = await SignInAsync(attacker, victim, $"Wrong-Password-{i:D2}");
            AssertRefusedInTurn(i, status, body);
        }
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync("198.51.100.22", victim, ErpTestEnvironment.Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(attacker, Env.Email(Env.TenantA, "viewer"), ErpTestEnvironment.Password)).Status);
    }

    [Fact]
    public async Task Failures_and_pauses_answer_alike_whether_or_not_and_wherever_an_account_has_the_address()
    {
        using var adminA = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var adminB = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        foreach (var language in new[] { "en", "ar" })
        {
            var tag = $"{language}{Guid.NewGuid():N}"[..12];
            var shared = $"alike.{tag}@both.example";
            await CreateUserAsync(adminA, shared, "Password-Of-A-2026");
            await CreateUserAsync(adminB, shared, "Password-Of-B-2026");
            var onlyB = $"alike.{tag}@{Env.TenantB.EmailDomain}";
            await CreateUserAsync(adminB, onlyB, "Password-Of-B-2026");
            var deactivated = $"gone.{tag}@{Env.TenantB.EmailDomain}";
            await CreateUserAsync(adminB, deactivated, "Password-Of-B-2026");
            await DeactivateAsync(adminB, deactivated);
            var addresses = new[]
            {
                ("an address no account has", $"nobody.{tag}@nowhere.example", "Password-Of-B-2026"),
                ("an address in tenant B only", onlyB, "Password-Of-B-2026"),
                ("an address in both tenants", shared, "Password-Of-B-2026"),
                ("a deactivated account", deactivated, "Password-Of-B-2026"),
                ("the gate's own user without access", Env.Email(Env.TenantA, "noaccess"), ErpTestEnvironment.Password),
            };
            // Each address from a client of its own, so each starts from nothing.
            var runs = new List<(string Label, List<string> Answers, List<int> Waits)>();
            var n = language == "en" ? 60 : 70;
            foreach (var (label, email, rightPassword) in addresses)
            {
                var client = $"203.0.113.{n++}";
                var answers = new List<string>();
                var waits = new List<int>();
                for (var i = 0; i < Attempts; i++)
                {
                    var (status, body) = await SignInAsync(client, email, $"Wrong-Password-{language}-{i}", language);
                    AssertRefusedInTurn(i, status, body);
                    answers.Add($"{(int)status}|{Answer(body)}");
                    waits.Add(status == HttpStatusCode.TooManyRequests ? body.GetProperty("retryAfterSeconds").GetInt32() : 0);
                }
                // The right password while paused: the same answer.
                var (pausedStatus, pausedBody) = await SignInAsync(client, email, rightPassword, language);
                Assert.Equal(HttpStatusCode.TooManyRequests, pausedStatus);
                answers.Add($"{(int)pausedStatus}|{Answer(pausedBody)}");
                waits.Add(pausedBody.GetProperty("retryAfterSeconds").GetInt32());
                runs.Add((label, answers, waits));
            }
            foreach (var (label, answers, waits) in runs.Skip(1))
            {
                Assert.True(answers.SequenceEqual(runs[0].Answers),
                    $"{language}: {label} answered differently from {runs[0].Label}:\n{string.Join("\n", answers)}\n---\n{string.Join("\n", runs[0].Answers)}");
                // The time left is the same rule for every address (a few seconds apart, as the runs were).
                for (var i = 0; i < waits.Count; i++)
                {
                    Assert.True(Math.Abs(waits[i] - runs[0].Waits[i]) <= 60, $"{language}: {label}, attempt {i + 1}: {waits[i]} s left against {runs[0].Waits[i]} s");
                }
            }
            // A failure before the pause states no policy (no number); the pause says it is one, in
            // the request's language, with the minutes left.
            var failure = runs[0].Answers[0];
            Assert.StartsWith("401|auth.signInFailed|", failure, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"[0-9٠-٩۰-۹]", failure["401|".Length..]);
            var pause = runs[0].Answers[^1];
            Assert.StartsWith("429|auth.signInPaused|", pause, StringComparison.Ordinal);
            Assert.Matches(language == "ar" ? @"\p{IsArabic}" : "paused", pause);
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
            var (status, body) = await SignInAsync(office, email, $"Wrong-Password-{i:D2}");
            AssertRefusedInTurn(i, status, body);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(office, email, "Owner-Password-2026")).Status);
        Assert.Equal(HttpStatusCode.OK, (await SignInWithAsync(owner, office, email, "Owner-Password-2026")).Status);

        // A device earned on the attacker's own account counts nothing on the owner's.
        using var attacker = Env.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SignInWithAsync(attacker, office, Env.Email(Env.TenantA, "viewer"), ErpTestEnvironment.Password)).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInWithAsync(attacker, office, email, "Owner-Password-2026")).Status);

        // A tampered cookie is ignored: still the office address, still paused.
        using var forger = Env.CreateClient(requestHeader: true);
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password = "Owner-Password-2026" }) };
        forged.Headers.Add("X-Forwarded-For", office);
        forged.Headers.Add("Cookie", "erp_device=W3siRSI6IkFBQUEiLCJEIjoiQkJCQiJ9XQ.bm90LWEtc2lnbmF0dXJl");
        using (var response = await forger.SendAsync(forged))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // The owner's own failures still pause the owner's device.
        for (var i = 0; i < Attempts; i++)
        {
            var (status, body) = await SignInWithAsync(owner, office, email, $"Typo-Password-{i:D2}");
            AssertRefusedInTurn(i, status, body);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInWithAsync(owner, office, email, "Owner-Password-2026")).Status);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SignInWithAsync(HttpClient client, string clientAddress, string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-in") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    /// <summary>The failure limit the gate environment runs with (clamped as the sign-in function does).</summary>
    private int Threshold => Math.Clamp(int.Parse(Env.Factory.Services.GetRequiredService<IConfiguration>()["Erp:Auth:LockoutThreshold"] ?? "5",
        System.Globalization.CultureInfo.InvariantCulture), 3, 50);

    /// <summary>The (0-based) <paramref name="attempt"/>-th failure in a row from one client at one
    /// address: refused as a failure until the limit is reached, then (the attempt that reaches it
    /// included) paused, with the time it lifts.</summary>
    private void AssertRefusedInTurn(int attempt, HttpStatusCode status, JsonElement body)
    {
        if (attempt < Threshold - 1)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Equal("auth.signInFailed", body.GetProperty("code").GetString());
            Assert.False(body.TryGetProperty("pausedUntil", out _));
            return;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.Equal("auth.signInPaused", body.GetProperty("code").GetString());
        var until = body.GetProperty("pausedUntil").GetDateTimeOffset();
        Assert.InRange(until, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        Assert.InRange(body.GetProperty("retryAfterSeconds").GetInt32(), 1, 86_400);
    }

    private static async Task DeactivateAsync(HttpClient admin, string email)
    {
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users?search={Uri.EscapeDataString(email)}");
        var id = list.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
        var user = await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/identity/users/{id}") ?? [];
        user["isActive"] = false;
        var response = await admin.PutAsJsonAsync($"/api/identity/users/{id}", user);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
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
