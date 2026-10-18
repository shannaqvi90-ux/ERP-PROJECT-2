using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth;

/// <summary>What a network address seen by the server is: one client's, or one many clients share.</summary>
public static class AddressKinds
{
    /// <summary>An address of one client (or of one network that is its own, such as a home router).</summary>
    public const string Client = "client";

    /// <summary>The gateway of a network this server is on: behind a container's published port
    /// every client arrives from it (the shipped compose deployment without a reverse proxy).</summary>
    public const string Gateway = "gateway";

    /// <summary>A configured proxy that did not forward the client's address, or an address the
    /// deployment declares shared (<see cref="AuthOptions.SharedAddresses"/>, an office NAT).</summary>
    public const string Proxy = "proxy";

    /// <summary>This server's own machine (loopback): a proxy or a development server on it.</summary>
    public const string Local = "local";

    /// <summary>A browser that signed in to the account before, counted as itself
    /// (<see cref="TrustedDevices"/>), not as its address.</summary>
    public const string Device = "device";
}

/// <summary>
/// Tells the address of one client from an address many clients share (critic p03 round 11). In
/// the one-command deployment the application sees every request from the compose network's
/// gateway, so the sign-in history and the paused clients could not tell an attacker's script
/// from the account's owner. Shared are: the gateways in this server's own routing table (read
/// once at start from <c>/proc/net/route</c> and <c>/proc/net/ipv6_route</c>, where they exist),
/// loopback, the configured proxies (<c>Erp:Http:KnownProxies</c>, <c>Erp:Http:KnownNetworks</c>)
/// when one reaches the application without a forwarded client address, and the addresses the
/// deployment declares shared (<c>Erp:Auth:SharedAddresses</c>, comma-separated addresses or
/// CIDR networks). Fixed at start, the same for every tenant, never written afterwards.
/// </summary>
internal sealed class ClientAddresses
{
    private readonly ImmutableArray<IPAddress> gateways;
    private readonly ImmutableArray<IPAddress> proxies;
    private readonly ImmutableArray<IPNetwork> proxyNetworks;

    public ClientAddresses(IOptions<AuthOptions> options, IConfiguration configuration, ILogger<ClientAddresses> logger)
    {
        var auth = options.Value;
        gateways = auth.DetectGateways
            ? [.. Gateways(Read("/proc/net/route"), Read("/proc/net/ipv6_route"))]
            : [];
        var shared = Items(auth.SharedAddresses).ToList();
        proxies = [.. Items(configuration["Erp:Http:KnownProxies"]).Select(IPAddress.Parse).Select(Normal)];
        proxyNetworks =
        [
            .. Items(configuration["Erp:Http:KnownNetworks"]).Select(n => IPNetwork.Parse(n)),
            .. shared.Select(n => n.Contains('/', StringComparison.Ordinal)
                ? IPNetwork.TryParse(n, out var network) ? network : throw new InvalidOperationException($"Erp:Auth:SharedAddresses: '{n}' is not a CIDR network.")
                : IPAddress.TryParse(n, out var address) ? new IPNetwork(Normal(address), address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32)
                : throw new InvalidOperationException($"Erp:Auth:SharedAddresses: '{n}' is not an IP address.")),
        ];
        if (gateways.Length > 0)
        {
            logger.LogInformation("Sign-in treats these gateway addresses as shared by many clients: {Gateways}", string.Join(", ", gateways));
        }
    }

    /// <summary>The kind of <paramref name="address"/> (<see cref="AddressKinds"/>); null when there is none.</summary>
    public string? KindOf(string? address) =>
        IPAddress.TryParse(address, out var parsed) ? KindOf(parsed) : null;

    public string KindOf(IPAddress address)
    {
        address = Normal(address);
        if (IPAddress.IsLoopback(address))
        {
            return AddressKinds.Local;
        }
        if (gateways.Contains(address))
        {
            return AddressKinds.Gateway;
        }
        if (proxies.Contains(address) || proxyNetworks.Any(n => n.Contains(address)))
        {
            return AddressKinds.Proxy;
        }
        return AddressKinds.Client;
    }

    /// <summary>Many clients may share this address (any kind but <see cref="AddressKinds.Client"/>).</summary>
    public bool IsShared(IPAddress? address) => address is not null && KindOf(address) != AddressKinds.Client;

    /// <summary>The gateways in Linux routing tables (<c>/proc/net/route</c>: the gateway as
    /// eight hex digits in the host's (little-endian) byte order, with the RTF_GATEWAY flag;
    /// <c>/proc/net/ipv6_route</c>: the next hop as 32 hex digits in network order).</summary>
    internal static IEnumerable<IPAddress> Gateways(string? ipv4Routes, string? ipv6Routes)
    {
        const int gatewayFlag = 0x2;
        var found = new List<IPAddress>();
        foreach (var line in (ipv4Routes ?? "").Split('\n').Skip(1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || fields[2].Length != 8 ||
                !int.TryParse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags) || (flags & gatewayFlag) == 0)
            {
                continue;
            }
            try
            {
                var bytes = Convert.FromHexString(fields[2]);
                Array.Reverse(bytes);
                var address = new IPAddress(bytes);
                if (!address.Equals(IPAddress.Any))
                {
                    found.Add(address);
                }
            }
            catch (FormatException)
            {
            }
        }
        foreach (var line in (ipv6Routes ?? "").Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9 || fields[4].Length != 32 ||
                !int.TryParse(fields[8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags) || (flags & gatewayFlag) == 0)
            {
                continue;
            }
            try
            {
                var address = new IPAddress(Convert.FromHexString(fields[4]));
                if (!address.Equals(IPAddress.IPv6Any))
                {
                    found.Add(address);
                }
            }
            catch (FormatException)
            {
            }
        }
        return found.Distinct();
    }

    private static IPAddress Normal(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IEnumerable<string> Items(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
