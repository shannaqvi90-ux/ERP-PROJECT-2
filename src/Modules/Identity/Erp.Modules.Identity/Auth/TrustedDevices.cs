using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth;

/// <summary>
/// Devices that have signed in to an account before (the device-cookie defence against lockout
/// abuse in OWASP's authentication guidance). After a successful sign-in the browser keeps a signed
/// cookie naming that account (by a hash of its address) and a random device id. On the next
/// attempt at that account from that browser, failures are counted under the device instead of
/// the network address, so someone behind the same address (an office NAT, a container gateway,
/// a shared proxy) who keeps failing pauses only their own attempts, never the account owner's
/// device. The cookie works only for the account it was earned on: signing in to one's own account
/// gives no fresh count on anyone else's. It carries no address and grants nothing else; a cookie
/// that does not verify is ignored.
/// </summary>
internal sealed class TrustedDevices(IOptions<AuthOptions> options)
{
    public const string CookieName = "erp_device";

    /// <summary>Accounts one browser remembers (the oldest is forgotten first).</summary>
    public const int MaxAccounts = 10;

    public const string SourcePrefix = "device:";

    // Random per process when no key is configured (devices are then forgotten on restart); the
    // same for every tenant and never written after start-up.
    private static readonly ImmutableArray<byte> ProcessKey = ImmutableArray.Create(RandomNumberGenerator.GetBytes(32));

    private sealed record Entry(string E, string D);

    private byte[] Key =>
        string.IsNullOrWhiteSpace(options.Value.DeviceKey) ? ProcessKey.ToArray() : SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.DeviceKey));

    /// <summary>The failure-count source of a device that has signed in to this address before, or null.</summary>
    public string? SourceFor(HttpContext http, string email)
    {
        var account = AccountOf(email);
        return Read(http).FirstOrDefault(e => e.E == account) is { } entry ? SourcePrefix + entry.D : null;
    }

    /// <summary>Remember this browser for the account just signed in to; answers its failure-count
    /// source (the same as <see cref="SourceFor"/> answers from now on). <paramref name="deviceId"/>:
    /// the id the browser already had as a device let in (<see cref="PendingId"/>), kept so its
    /// history continues under one device.</summary>
    public string Remember(HttpContext http, string email, bool secure, string? deviceId = null)
    {
        var account = AccountOf(email);
        var entries = Read(http).Where(e => e.E != account).TakeLast(MaxAccounts - 1).ToList();
        var known = Read(http).FirstOrDefault(e => e.E == account) ?? new Entry(account, deviceId ?? Base64Url(RandomNumberGenerator.GetBytes(9)));
        entries.Add(known);
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(entries));
        http.Response.Cookies.Append(CookieName, $"{payload}.{Sign(payload)}", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(options.Value.DeviceDays),
            IsEssential = true,
        });
        return SourcePrefix + known.D;
    }

    /// <summary>The id of this browser as a device not yet known to any account it signs in to
    /// (the <c>erp_pending</c> cookie, signed like the device cookie), or null.</summary>
    public string? PendingId(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(PendingCookieName, out var cookie) || cookie is not { Length: > 0 and <= 128 })
        {
            return null;
        }
        var dot = cookie.IndexOf('.');
        if (dot <= 0 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign("pending:" + cookie[..dot])), Encoding.ASCII.GetBytes(cookie[(dot + 1)..])))
        {
            return null;
        }
        return cookie[..dot];
    }

    /// <summary>This browser's pending device id, given now when it has none (a day-long cookie):
    /// the sign-in screen of a browser paused on a shared address shows the device code made from
    /// it, and the user or an administrator lets exactly that browser in with the code.</summary>
    public string IssuePending(HttpContext http, bool secure)
    {
        if (PendingId(http) is { } existing)
        {
            return existing;
        }
        var id = Base64Url(RandomNumberGenerator.GetBytes(9));
        http.Response.Cookies.Append(PendingCookieName, $"{id}.{Sign("pending:" + id)}", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(1),
            IsEssential = true,
        });
        return id;
    }

    /// <summary>The device code of browser <paramref name="deviceId"/> for the account with this
    /// address (<see cref="DeviceApprovals"/>): eight symbols, keyed by this deployment's key, so
    /// nobody can make a browser whose code is one they choose, and a code says nothing of the
    /// browser or the account.</summary>
    public string ApprovalCode(string email, string deviceId) =>
        DeviceApprovals.CodeFrom(HMACSHA256.HashData(DeriveKey("device-approval"), Encoding.UTF8.GetBytes(AccountOf(email) + "\n" + deviceId)));

    public const string PendingCookieName = "erp_pending";

    private List<Entry> Read(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var cookie) || cookie is not { Length: > 0 and <= 2048 })
        {
            return [];
        }
        var dot = cookie.IndexOf('.');
        if (dot <= 0 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(cookie[..dot])), Encoding.ASCII.GetBytes(cookie[(dot + 1)..])))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<Entry>>(FromBase64Url(cookie[..dot])) is { } entries
                ? entries.Where(e => e is { E.Length: > 0, D.Length: > 0 }).TakeLast(MaxAccounts).ToList()
                : [];
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            return [];
        }
    }

    private string Sign(string payload) => Base64Url(HMACSHA256.HashData(Key, Encoding.ASCII.GetBytes(payload)));

    /// <summary>A key of the deployment's own for another purpose (the passkey challenges), derived
    /// from the same configured key (or this process's random one), never the cookie key itself.</summary>
    public byte[] DeriveKey(string purpose) => HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes("erp:" + purpose));

    private static string AccountOf(string email) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()))[..12]);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
