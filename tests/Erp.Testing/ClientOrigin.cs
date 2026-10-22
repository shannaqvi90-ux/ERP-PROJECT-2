using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Erp.Testing;

/// <summary>
/// Where a test request comes from, as the server sees it (critic p00 round 13, plant P11: a
/// permission check that passed every signed-in request from loopback, "the server's own callers,
/// such as jobs or a reverse proxy's checks on this machine, are trusted", passed every gate,
/// because every test request reached the in-memory test server with no client address at all).
/// <see cref="Peer"/> is the address of the TCP peer (what Kestrel would report: a browser, the
/// compose gateway, a reverse proxy on this machine); <see cref="ForwardedFor"/> an
/// <c>X-Forwarded-For</c> header the peer sends, which the product honours from its configured
/// proxies only. <see cref="Effective"/> is the client address the product should end up with.
/// The test hosts set the peer from <see cref="PeerHeader"/> (<see cref="StartupFilter"/>) and
/// remove that header before the product sees the request; a host outside the tests never reads it.
/// </summary>
/// <param name="Name">Short name findings carry (<c>[cookie@loopback-v6]</c>).</param>
/// <param name="Peer">The TCP peer address, or null for the in-memory server's own (no address).</param>
/// <param name="ForwardedFor">An X-Forwarded-For value the peer sends, or null.</param>
/// <param name="Effective">The client address the product should see after its proxy handling, or null.</param>
public sealed record ClientOrigin(string Name, string? Peer, string? ForwardedFor, string? Effective)
{
    /// <summary>The test-only header that carries <see cref="Peer"/> to the test host.</summary>
    public const string PeerHeader = "X-Erp-Test-Peer-Address";

    /// <summary>Send this origin with <paramref name="request"/> (nothing for the in-memory origin).</summary>
    public HttpRequestMessage Apply(HttpRequestMessage request)
    {
        if (Peer is not null)
        {
            request.Headers.TryAddWithoutValidation(PeerHeader, Peer);
        }
        if (ForwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", ForwardedFor);
        }
        return request;
    }

    /// <summary>Send this origin with every request of <paramref name="client"/>.</summary>
    public HttpClient Apply(HttpClient client)
    {
        if (Peer is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(PeerHeader, Peer);
        }
        if (ForwardedFor is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", ForwardedFor);
        }
        return client;
    }

    /// <summary><see cref="Effective"/> as an address (null for none).</summary>
    public IPAddress? EffectiveAddress => Effective is null ? null : IPAddress.Parse(Effective);

    public override string ToString() => Name;

    /// <summary>
    /// Sets the request's TCP peer from <see cref="PeerHeader"/> and removes the header, first in
    /// the pipeline (before the request recorder and the product's own forwarded-header handling),
    /// so the product sees the request exactly as if it had come from that peer.
    /// </summary>
    internal sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) =>
            {
                if (context.Request.Headers.TryGetValue(PeerHeader, out var peer))
                {
                    context.Request.Headers.Remove(PeerHeader);
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                    context.Connection.RemotePort = 41000;
                }
                return following(context);
            });
            next(app);
        };
    }

    /// <summary>The configured reverse proxy of <see cref="HostSettings"/>.</summary>
    public const string Proxy = "10.0.0.2";

    /// <summary>The network <see cref="HostSettings"/> declares shared (an office NAT).</summary>
    public const string SharedNetwork = "198.51.100.0/24";

    /// <summary>A host that knows a reverse proxy and a shared network, the way a deployment behind
    /// nginx or Caddy and an office NAT is configured (gateway detection stays on, as shipped).</summary>
    public static IReadOnlyDictionary<string, string?> HostSettings { get; } = new Dictionary<string, string?>
    {
        ["Erp:Http:KnownProxies"] = Proxy,
        ["Erp:Auth:SharedAddresses"] = SharedNetwork,
    };

    /// <summary>
    /// The origins the permission gate sends requests from: the in-memory server's own (no
    /// address), ordinary clients (IPv4, IPv6, an office network), loopback in four spellings, the
    /// compose network's gateway and the gateways of this machine's routing table, the configured
    /// proxy without a forwarded address and forwarding an ordinary client or a loopback caller, an
    /// address of the declared shared network, and an ordinary client that claims loopback in its
    /// own X-Forwarded-For (which the product must not believe).
    /// </summary>
    public static IReadOnlyList<ClientOrigin> All { get; } = Build();

    private static List<ClientOrigin> Build()
    {
        var origins = new List<ClientOrigin>
        {
            new("in-memory", null, null, null),
            new("client", "203.0.113.10", null, "203.0.113.10"),
            new("client-v6", "2001:db8::10", null, "2001:db8::10"),
            new("office-lan", "192.168.1.20", null, "192.168.1.20"),
            new("loopback", "127.0.0.1", null, "127.0.0.1"),
            new("loopback-other", "127.0.0.53", null, "127.0.0.53"),
            new("loopback-v6", "::1", null, "::1"),
            new("loopback-mapped", "::ffff:127.0.0.1", null, "::ffff:127.0.0.1"),
            new("compose-gateway", "172.18.0.1", null, "172.18.0.1"),
            new("proxy", Proxy, null, Proxy),
            new("proxy-for-client", Proxy, "203.0.113.20", "203.0.113.20"),
            new("proxy-for-loopback", Proxy, "127.0.0.1", "127.0.0.1"),
            new("shared", "198.51.100.7", null, "198.51.100.7"),
            new("spoofed-loopback", "203.0.113.30", "127.0.0.1", "203.0.113.30"),
        };
        var known = origins.Select(o => o.Peer).ToHashSet(StringComparer.Ordinal);
        var n = 0;
        foreach (var gateway in RoutingTableGateways())
        {
            if (known.Add(gateway.ToString()))
            {
                origins.Add(new ClientOrigin($"machine-gateway{(n++ == 0 ? "" : n.ToString(CultureInfo.InvariantCulture))}", gateway.ToString(), null, gateway.ToString()));
            }
        }
        return origins;
    }

    /// <summary>The IPv4 gateways of this machine's routing table (<c>/proc/net/route</c>, where it
    /// exists): the product treats them as shared, so the gate sends requests from them too.</summary>
    private static IEnumerable<IPAddress> RoutingTableGateways()
    {
        string text;
        try
        {
            text = File.Exists("/proc/net/route") ? File.ReadAllText("/proc/net/route") : "";
        }
        catch (IOException)
        {
            yield break;
        }
        foreach (var line in text.Split('\n').Skip(1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || fields[2].Length != 8 ||
                !int.TryParse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags) || (flags & 0x2) == 0)
            {
                continue;
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(fields[2]);
            }
            catch (FormatException)
            {
                continue;
            }
            Array.Reverse(bytes);
            var address = new IPAddress(bytes);
            if (!address.Equals(IPAddress.Any))
            {
                yield return address;
            }
        }
    }
}
