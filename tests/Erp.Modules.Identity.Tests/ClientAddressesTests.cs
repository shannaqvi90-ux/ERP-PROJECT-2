using System.Net;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Tests;

/// <summary>Telling one client's address from one many clients share (critic p03 round 11): the
/// container gateway from the routing table, loopback, configured proxies and shared addresses.
/// No database.</summary>
public sealed class ClientAddressesTests
{
    // A container on a compose network (172.21.0.0/16, gateway 172.21.0.1) with IPv6 (gateway
    // fd00:21::1), as /proc/net/route and /proc/net/ipv6_route print them.
    private const string Ipv4Routes = """
        Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask		MTU	Window	IRTT
        eth0	00000000	010015AC	0003	0	0	0	00000000	0	0	0
        eth0	000015AC	00000000	0001	0	0	0	0000FFFF	0	0	0
        """;

    private const string Ipv6Routes = """
        00000000000000000000000000000000 00 00000000000000000000000000000000 00 fd000021000000000000000000000001 00000400 00000001 00000000 00000003     eth0
        fd000021000000000000000000000000 40 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000001 00000000 00000001     eth0
        """;

    [Fact]
    public void The_gateways_are_read_from_the_routing_tables()
    {
        Assert.Equal([IPAddress.Parse("172.21.0.1"), IPAddress.Parse("fd00:21::1")], ClientAddresses.Gateways(Ipv4Routes, Ipv6Routes));
        Assert.Empty(ClientAddresses.Gateways(null, null));
        Assert.Empty(ClientAddresses.Gateways("Iface\tDestination\tGateway\nbroken line\neth0\t00000000\tZZZZZZZZ\t0003\n", "short line\n"));
    }

    [Fact]
    public void Each_address_is_named_for_what_it_is()
    {
        var addresses = Create(new Dictionary<string, string?>
        {
            ["Erp:Http:KnownProxies"] = "10.9.8.7",
            ["Erp:Http:KnownNetworks"] = "10.20.0.0/16",
            ["Erp:Auth:SharedAddresses"] = "198.51.100.9, 2001:db8:5::/48",
            ["Erp:Auth:DetectGateways"] = "false",
        });
        Assert.Equal(AddressKinds.Local, addresses.KindOf("127.0.0.1"));
        Assert.Equal(AddressKinds.Local, addresses.KindOf("::1"));
        Assert.Equal(AddressKinds.Proxy, addresses.KindOf("10.9.8.7"));
        Assert.Equal(AddressKinds.Proxy, addresses.KindOf("::ffff:10.9.8.7"));
        Assert.Equal(AddressKinds.Proxy, addresses.KindOf("10.20.3.4"));
        Assert.Equal(AddressKinds.Proxy, addresses.KindOf("198.51.100.9"));
        Assert.Equal(AddressKinds.Proxy, addresses.KindOf("2001:db8:5:1::7"));
        Assert.Equal(AddressKinds.Client, addresses.KindOf("198.51.100.10"));
        Assert.Equal(AddressKinds.Client, addresses.KindOf("2001:db8:6::7"));
        Assert.Null(addresses.KindOf((string?)null));
        Assert.Null(addresses.KindOf("not an address"));
        Assert.True(addresses.IsShared(IPAddress.Loopback));
        Assert.False(addresses.IsShared(IPAddress.Parse("203.0.113.5")));
        Assert.False(addresses.IsShared(null));

        // The paused-clients view: a device, an IPv6 /64 source, an address.
        Assert.Equal(AddressKinds.Device, UserEndpoints.SourceKind("device:AbCdEf123456", addresses));
        Assert.Equal(AddressKinds.Proxy, UserEndpoints.SourceKind("2001:db8:5:1::/64", addresses));
        Assert.Equal(AddressKinds.Client, UserEndpoints.SourceKind("203.0.113.5", addresses));
        Assert.Equal(AddressKinds.Client, UserEndpoints.SourceKind("unknown", addresses));
        Assert.Equal("AbCdEf", UserEndpoints.DeviceTag("device:AbCdEf123456"));
        Assert.Null(UserEndpoints.DeviceTag("203.0.113.5"));
    }

    [Fact]
    public void A_shared_address_that_is_not_an_address_stops_the_start()
    {
        Assert.Throws<InvalidOperationException>(() => Create(new Dictionary<string, string?> { ["Erp:Auth:SharedAddresses"] = "office" }));
        Assert.Throws<InvalidOperationException>(() => Create(new Dictionary<string, string?> { ["Erp:Auth:SharedAddresses"] = "10.0.0.0/99" }));
    }

    private static ClientAddresses Create(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new AuthOptions();
        configuration.GetSection("Erp:Auth").Bind(options);
        return new ClientAddresses(Options.Create(options), configuration, NullLogger<ClientAddresses>.Instance);
    }
}
