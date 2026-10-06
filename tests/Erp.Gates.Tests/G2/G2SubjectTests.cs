using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: the check creates users and roles and signs them in and out.</summary>
public sealed class SubjectFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>G2, object level: an action on "me" acts on the caller only (see <see cref="SubjectInjection"/>).</summary>
public sealed class G2SubjectTests(SubjectFixture fixture) : IClassFixture<SubjectFixture>
{
    [Fact]
    public async Task An_action_on_the_caller_never_acts_on_another_user_the_request_names()
    {
        var result = await SubjectInjection.RunAsync(fixture.Env);
        TestContext.Current.TestOutputHelper?.WriteLine($"{result.Checked.Count} endpoints, {result.Injections} requests naming another user: {string.Join(", ", result.Checked)}");
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        // The endpoints that act on the caller by name must be among those checked.
        foreach (var expected in new[] { "PUT /api/identity/me/preferences", "PUT /api/tenancy/workplace", "POST /api/auth/sign-out", "POST /api/auth/sign-in" })
        {
            Assert.Contains(expected, result.Checked);
        }
        Assert.Equal(SubjectInjection.Endpoints(fixture.Env).Select(e => e.Key).Order(StringComparer.Ordinal), result.Checked.Order(StringComparer.Ordinal));
        Assert.True(result.Checked.Count >= Ratchet.Min("g2.subjectEndpointsChecked"),
            $"{result.Checked.Count} endpoints acting on the caller checked; ratchet minimum {Ratchet.Min("g2.subjectEndpointsChecked")}");
        Assert.True(result.Injections >= Ratchet.Min("g2.subjectInjections"),
            $"{result.Injections} requests naming another user; ratchet minimum {Ratchet.Min("g2.subjectInjections")}");
    }
}
