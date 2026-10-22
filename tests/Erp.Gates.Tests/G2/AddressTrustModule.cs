using System.Net;
using System.Net.Sockets;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plant (critic p00 round 13, P11): permission checks that trust where a request comes
/// from. Hosted only by the G2 client-address self-test's planted host, over the G2 environment's
/// database; the mode is switched between runs through <see cref="Switch"/> (one host serves every
/// mode). Modes:
/// <list type="bullet">
/// <item><c>loopback</c>: any signed-in request from loopback passes ("jobs and the reverse proxy
/// on this machine are trusted", the critic's plant);</item>
/// <item><c>private</c>: any signed-in request from loopback or a private network (the office LAN,
/// the compose network and its gateway, the proxy) passes ("the intranet is trusted");</item>
/// <item><c>shared</c>: any signed-in request from an address the deployment declares shared
/// passes ("the office NAT is the office");</item>
/// <item><c>forwarded</c>: any signed-in request the configured proxy forwarded passes ("the proxy
/// already checked it");</item>
/// <item><c>held-loopback</c>: a signed-in request from loopback passes when the caller holds any
/// permission at all (only the grant-exactly-one matrix can see this one).</item>
/// </list>
/// </summary>
public sealed class AddressTrustModule : ErpModule
{
    public static readonly string[] DenyModes = ["loopback", "private", "shared", "forwarded"];

    public override string Name => "addresstrust";

    public override void Register(ModuleBuilder module)
    {
        module.Services.AddSingleton<Switch>();
        module.Services.AddSingleton<IAuthorizationHandler, AddressTrustHandler>();
    }

    public static Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?> baseSettings)
    {
        var settings = new Dictionary<string, string?>(baseSettings)
        {
            ["Erp:Testing:ExtraModules"] = typeof(AddressTrustModule).AssemblyQualifiedName,
        };
        foreach (var (key, value) in ClientOrigin.HostSettings)
        {
            settings[key] = value;
        }
        return settings;
    }

    /// <summary>The plant's current mode (empty: no plant).</summary>
    public sealed class Switch
    {
        public volatile string Mode = "";
    }

    /// <summary>Whether <paramref name="mode"/> trusts a request whose client address is
    /// <paramref name="address"/>, that the configured proxy forwarded or not, from a caller who
    /// holds a permission or not. The handler applies it to what the request carries; the self-test
    /// to what each origin should reach the product as.</summary>
    public static bool Trusts(string mode, IPAddress? address, bool forwarded, bool holdsAny)
    {
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }
        return mode switch
        {
            "loopback" => address is not null && IPAddress.IsLoopback(address),
            "private" => address is not null && (IPAddress.IsLoopback(address) || IsPrivate(address)),
            "shared" => address is not null && IPNetwork.Parse(ClientOrigin.SharedNetwork).Contains(address),
            "forwarded" => forwarded,
            "held-loopback" => holdsAny && address is not null && IPAddress.IsLoopback(address),
            _ => false,
        };
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
        }
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private sealed class AddressTrustHandler(Switch plant) : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true && context.Resource is HttpContext http &&
                Trusts(plant.Mode, http.Connection.RemoteIpAddress,
                    // The product's forwarded-header handling keeps the proxy's own address here when it applied X-Forwarded-For.
                    http.Request.Headers.ContainsKey("X-Original-For"),
                    context.User.FindAll(ErpClaims.Permission).Any()))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
