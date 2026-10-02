using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>Its own environment: the attack writes to tenant A.</summary>
public sealed class G1AttackFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Env = await ErpTestEnvironment.StartGateAsync();
        await GatePreparation.PrepareAsync(Env);
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1, HTTP layer. Signed in as tenant A (cookie and bearer token), as a tenant A user without
/// roles, and anonymously, the attacker calls every endpoint the running app exposes with every
/// kind of tenant B identifier in the route, tenant-switch headers, query string and body. No
/// response may contain any tenant B identifier or canary, no response may be a server error,
/// and no tenant B row may change. Exports, jobs and files are attacked by the probes their
/// modules register.
/// </summary>
public sealed class G1HttpIsolationTests(G1AttackFixture fixture) : IClassFixture<G1AttackFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Tenant_A_cannot_reach_tenant_B_through_any_endpoint()
    {
        var report = await IsolationAttack.RunAsync(Env);

        Assert.True(report.Leaks.Count == 0, $"{report.Leaks.Count} leaks:\n" + string.Join("\n", report.Leaks.Take(50)));
        Assert.True(report.ChangedTables.Count == 0, "Tenant B rows changed during the attack in: " + string.Join(", ", report.ChangedTables));
        Assert.True(report.ServerErrors.Count == 0, $"{report.ServerErrors.Count} server errors:\n" + string.Join("\n", report.ServerErrors.Take(20)));
        Assert.True(report.UncoveredSurfaces.Count == 0, "Endpoint families without an isolation probe: " + string.Join(", ", report.UncoveredSurfaces));
        Assert.True(report.EndpointsAttacked >= Ratchet.Min("g1.endpointsAttacked"), $"{report.EndpointsAttacked} endpoints attacked; ratchet minimum {Ratchet.Min("g1.endpointsAttacked")}");
        Assert.True(report.Requests >= Ratchet.Min("g1.attackRequests"), $"{report.Requests} attack requests; ratchet minimum {Ratchet.Min("g1.attackRequests")}");
        Assert.True(report.ProbesRun >= Ratchet.Min("g1.isolationProbes"), $"{report.ProbesRun} probes; ratchet minimum {Ratchet.Min("g1.isolationProbes")}");
    }
}

/// <summary>
/// The G1 HTTP attack, reusable so the gate's self-tests can prove it catches a planted leak.
/// </summary>
public static class IsolationAttack
{
    private const int VictimIdsPerTable = 5;

    public static async Task<IsolationReport> RunAsync(ErpTestEnvironment Env)
    {
        var a = Env.TenantA;
        var b = Env.TenantB;
        var victim = await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code);
        var own = await TenantSnapshot.TakeAsync(Env, a.Id, null, a.Code);
        Assert.True(victim.Markers.Count > 10, "The victim tenant has too little data to attack.");

        using var anonymousForDocs = Env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymousForDocs);
        var endpoints = EndpointInventory.From(Env.Factory.Services);

        var attackers = new List<Attacker>
        {
            new("tenant A administrator (cookie)", () => Env.SignInAsync(Env.Email(a, "admin"))),
            new("tenant A administrator (bearer)", () => Env.SignInWithTokenAsync(Env.Email(a, "admin"))),
            new("tenant A user without roles", () => Env.SignInAsync(Env.Email(a, "noaccess"))),
            new("anonymous", () => Task.FromResult(Env.CreateClient())),
        };
        foreach (var attacker in attackers)
        {
            await attacker.ConnectAsync();
        }

        var victimRouteValues = victim.IdsByTable.Values.SelectMany(ids => ids.Take(VictimIdsPerTable))
            .Append(b.Id).Distinct().Select(id => id.ToString())
            .Append(b.Code)
            .ToList();
        var ownRouteValues = own.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty)
            .Select(id => id.ToString()).ToList();

        var leaks = new List<string>();
        var serverErrors = new List<string>();
        var requests = 0;
        var attacked = new HashSet<string>();
        var counter = 0;

        foreach (var endpoint in endpoints)
        {
            var paths = endpoint.RouteParameters.Count == 0
                ? [endpoint.Path(_ => "")]
                : victimRouteValues.Concat(endpoint.HasBody ? ownRouteValues : [])
                    .Select(value => endpoint.Path(_ => value)).Distinct().ToList();
            var bodySchema = endpoint.HasBody ? openApi.RequestSchema(endpoint.Method, endpoint.Pattern) : null;
            var signIn = endpoint.Name == "auth.signIn";

            foreach (var path in paths)
            {
                foreach (var variant in Enum.GetValues<Variant>())
                {
                    foreach (var attacker in attackers)
                    {
                        using var request = BuildRequest(endpoint, path, variant, bodySchema, openApi, victim, b, signIn, ref counter);
                        using var response = await attacker.Client.SendAsync(request);
                        var text = await response.Content.ReadAsStringAsync();
                        requests++;
                        var where = $"{attacker.Name} → {endpoint.Method} {path} [{variant}] → {(int)response.StatusCode}";
                        var marker = victim.FindMarker(text) ?? victim.FindMarker(response.Headers.Location?.ToString() ?? "");
                        if (marker is not null)
                        {
                            leaks.Add($"{where}: response contains tenant B marker {marker}");
                        }
                        if ((int)response.StatusCode >= 500)
                        {
                            serverErrors.Add($"{where}: {text[..Math.Min(300, text.Length)]}");
                        }
                        if (endpoint.Name == "auth.signOut" && attacker.Name != "anonymous")
                        {
                            await attacker.ConnectAsync();
                        }
                    }
                }
            }
            attacked.Add(endpoint.Key);
        }

        // Exports, imports, jobs and files: every surface kind in use needs a probe, and every
        // probe runs with tenant B's identifiers.
        var catalog = Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var probeKinds = new HashSet<SurfaceKind>();
        var probesRun = 0;
        await using (var scope = Env.Factory.Services.CreateAsyncScope())
        {
            foreach (var probeType in catalog.Modules.SelectMany(m => m.IsolationProbes))
            {
                var probe = (IIsolationProbe)scope.ServiceProvider.GetRequiredService(probeType);
                probeKinds.Add(probe.Kind);
                var result = await probe.RunAsync(new IsolationProbeContext(attackers[0].Client, a.Id, b.Id, victim.AllIds.ToList(), victim.Markers), CancellationToken.None);
                requests += result.Attempts;
                probesRun++;
                foreach (var observed in result.Observed)
                {
                    if (victim.FindMarker(observed) is { } marker)
                    {
                        leaks.Add($"probe {probe.Name}: observed tenant B marker {marker}");
                    }
                }
            }
        }
        var uncovered = endpoints.Where(e => e.Surface != SurfaceKind.Data && !probeKinds.Contains(e.Surface))
            .Select(e => $"{e.Key} ({e.Surface})").ToList();

        var after = await TenantSnapshot.TakeAsync(Env, b.Id, b.Canary, b.Code);
        var changed = TenantSnapshot.Differences(victim, after);

        foreach (var attacker in attackers)
        {
            attacker.Client.Dispose();
        }
        return new IsolationReport(leaks, serverErrors, changed, uncovered, attacked.Count, requests, probesRun);
    }

    private enum Variant
    {
        Plain,
        TenantHeaders,
        TenantQuery,
    }

    private static HttpRequestMessage BuildRequest(
        ApiEndpoint endpoint, string path, Variant variant, System.Text.Json.JsonElement? bodySchema, OpenApiDocument openApi,
        TenantSnapshot victim, Erp.Kernel.Seeding.SeedTenant b, bool signIn, ref int counter)
    {
        var victimIds = victim.AllIds.Select(i => i.ToString()).ToList();
        var uri = path;
        if (variant == Variant.TenantQuery)
        {
            var id = victimIds[counter % victimIds.Count];
            uri += $"?tenantId={b.Id}&tenant={b.Code}&tenant_id={b.Id}&companyId={b.Id}&id={id}&userId={id}&search={b.Canary}&workspace={b.Code}";
        }
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), uri);
        if (variant == Variant.TenantHeaders)
        {
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Tenant", b.Code);
            request.Headers.TryAddWithoutValidation("Tenant-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Company-Id", b.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", $"{b.Code}.example");
            request.Headers.TryAddWithoutValidation("Cookie", $"erp_tenant={b.Id}");
        }
        if (endpoint.HasBody || endpoint.Method == "DELETE")
        {
            var n = counter++;
            var body = bodySchema is { } schema
                ? openApi.BuildBody(schema, (type, format, name) => Leaf(type, format, name, victimIds, b, signIn, n)) as JsonObject ?? []
                : [];
            body["tenantId"] = b.Id.ToString();
            body["tenant_id"] = b.Id.ToString();
            body["companyId"] = b.Id.ToString();
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        counter++;
        return request;
    }

    /// <summary>Leaf values: tenant B ids wherever an id is expected; tenant B e-mail and workspace
    /// only for sign-in (elsewhere the attacker would be writing B's strings into its own data).</summary>
    private static JsonNode? Leaf(string type, string? format, string? name, List<string> victimIds, Erp.Kernel.Seeding.SeedTenant b, bool signIn, int n)
    {
        var lower = name?.ToLowerInvariant() ?? "";
        return type switch
        {
            "string" when format == "uuid" => victimIds[n % victimIds.Count],
            "string" when lower.Contains("email") => signIn ? $"admin@{b.EmailDomain}" : $"attack{n}@attack-{n}.example",
            "string" when lower == "workspace" => signIn ? b.Code : "alpha",
            "string" when lower == "password" => "Attack-Password-1",
            "string" when lower == "language" => "en",
            "string" when lower.Contains("permission") => "identity.users.read",
            "string" when format == "date-time" => DateTimeOffset.UtcNow.ToString("O"),
            "string" => $"attack-{n}",
            "integer" => 0,
            "number" => "1",
            "boolean" => true,
            _ => null,
        };
    }

    private sealed class Attacker(string name, Func<Task<HttpClient>> connect)
    {
        public string Name { get; } = name;
        public HttpClient Client { get; private set; } = null!;

        public async Task ConnectAsync()
        {
            Client?.Dispose();
            Client = await connect();
        }
    }
}

public sealed record IsolationReport(
    IReadOnlyList<string> Leaks,
    IReadOnlyList<string> ServerErrors,
    IReadOnlyList<string> ChangedTables,
    IReadOnlyList<string> UncoveredSurfaces,
    int EndpointsAttacked,
    int Requests,
    int ProbesRun);
