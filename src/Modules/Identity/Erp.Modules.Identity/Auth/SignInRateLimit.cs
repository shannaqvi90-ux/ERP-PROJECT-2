using System.Net;
using System.Text.Json;
using Erp.Kernel.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth;

/// <summary>
/// The sign-in rate limit's client behind an address many clients share (critic p03 round 12:
/// behind the shipped compose gateway every browser had the gateway's address, so 30 anonymous
/// requests a minute stopped every sign-in of every workspace). From an address of one client
/// nothing changes: the budget is the address's. From a shared address (<see cref="ClientAddresses"/>)
/// each account typed is a client of its own (the address and the e-mail address, normalised as
/// sign-in does), and each passkey user handle likewise, so a script hammering one address or
/// many unknown ones uses up only those addresses' budgets, never another person's. Guessing at
/// one account from the shared address stays within one client's budget, as before; everyone
/// behind the address together stays within the shared ceiling (Erp:RateLimits:SharedAddressClients
/// clients' budget). The body is read with the same serializer options the endpoint binds with, so
/// the partition names exactly the account the endpoint will try, and left readable for it.
/// </summary>
internal static class SignInRateLimit
{
    /// <summary>Bodies larger than this are not read here (the endpoint refuses them anyway); they
    /// count against the shared address's own budget.</summary>
    private const int MaxBody = 32 * 1024;

    public static async ValueTask<RateLimitClient?> ClientAsync(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }
        if (!http.RequestServices.GetRequiredService<ClientAddresses>().IsShared(address))
        {
            return null;
        }
        var source = SignInService.SourceOf(address);
        var who = await WhoAsync(http);
        return new RateLimitClient(who is null ? source : source + "|" + who, source);
    }

    /// <summary>"account:" and the normalised e-mail address, "passkey:" and the user handle, or
    /// null when the body names neither.</summary>
    private static async Task<string?> WhoAsync(HttpContext http)
    {
        var request = http.Request;
        if (request.ContentLength is > MaxBody)
        {
            return null;
        }
        request.EnableBuffering(MaxBody);
        try
        {
            var buffer = new byte[MaxBody + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await request.Body.ReadAsync(buffer.AsMemory(read), http.RequestAborted)) > 0)
            {
                read += n;
            }
            if (read > MaxBody)
            {
                return null;
            }
            var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            var body = JsonSerializer.Deserialize<SignInRequest>(buffer.AsSpan(0, read), options);
            if (!string.IsNullOrWhiteSpace(body?.Email))
            {
                return "account:" + body.Email.Trim().ToLowerInvariant();
            }
            if (body?.Passkey?.UserHandle is { Length: > 0 and <= 100 } handle)
            {
                return "passkey:" + handle;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
