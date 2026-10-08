using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Auth.Passkeys;

/// <summary>
/// Challenges for the two passkey ceremonies, kept by nobody: a challenge carries its own purpose,
/// issue time and binding, signed with the deployment's key, so the server needs no table of
/// outstanding challenges and the sign-in screen can be handed one with the anonymous session
/// answer it already asks for. A challenge is good for <see cref="AuthOptions.PasskeyChallengeSeconds"/>;
/// a registration challenge works only for the session it was issued to. A signed answer can be
/// sent once only: each passkey keeps the issue time of the last challenge it answered and refuses
/// any answer to that one or an older one (<see cref="Passkey.LastChallengeAt"/>).
/// </summary>
internal sealed class PasskeyChallenges(TrustedDevices devices, IOptions<AuthOptions> options, TimeProvider time)
{
    public enum Purpose : byte
    {
        Register = 1,
        SignIn = 2,
    }

    private const byte Version = 1;
    private const int PayloadLength = 1 + 1 + 8 + 16 + 16;
    private const int MacLength = 16;
    public const int Length = PayloadLength + MacLength;

    private readonly byte[] _key = devices.DeriveKey("passkey-challenge");

    /// <summary>A new challenge for <paramref name="purpose"/>, bound to <paramref name="binding"/>
    /// (the session that registers; none for sign-in).</summary>
    public byte[] Issue(Purpose purpose, Guid? binding = null)
    {
        var challenge = new byte[Length];
        challenge[0] = Version;
        challenge[1] = (byte)purpose;
        BinaryPrimitives.WriteInt64BigEndian(challenge.AsSpan(2, 8), time.GetUtcNow().ToUnixTimeMilliseconds());
        RandomNumberGenerator.Fill(challenge.AsSpan(10, 16));
        (binding ?? Guid.Empty).TryWriteBytes(challenge.AsSpan(26, 16), bigEndian: true, out _);
        Mac(challenge.AsSpan(0, PayloadLength)).CopyTo(challenge.AsSpan(PayloadLength));
        return challenge;
    }

    /// <summary>Whether the bytes are a challenge this deployment issued for this purpose and
    /// binding, still within its lifetime; <paramref name="issuedAt"/> is when it was issued.</summary>
    public bool Verify(byte[] challenge, Purpose purpose, Guid? binding, out DateTimeOffset issuedAt)
    {
        issuedAt = default;
        if (challenge.Length != Length || challenge[0] != Version || challenge[1] != (byte)purpose ||
            !CryptographicOperations.FixedTimeEquals(Mac(challenge.AsSpan(0, PayloadLength)), challenge.AsSpan(PayloadLength)))
        {
            return false;
        }
        var expected = new byte[16];
        (binding ?? Guid.Empty).TryWriteBytes(expected, bigEndian: true, out _);
        if (!CryptographicOperations.FixedTimeEquals(expected, challenge.AsSpan(26, 16)))
        {
            return false;
        }
        issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64BigEndian(challenge.AsSpan(2, 8)));
        var age = time.GetUtcNow() - issuedAt;
        // A little clock skew between app instances is allowed; a challenge from the future is not.
        return age >= TimeSpan.FromSeconds(-30) && age <= TimeSpan.FromSeconds(Math.Clamp(options.Value.PasskeyChallengeSeconds, 30, 3600));
    }

    /// <summary>How long the browser should wait for the person, in milliseconds.</summary>
    public int TimeoutMilliseconds => Math.Clamp(options.Value.PasskeyChallengeSeconds, 30, 3600) * 1000;

    /// <summary>The relying party id for this request: the configured one, else the request's host name.</summary>
    public string RpId(HttpContext http) =>
        string.IsNullOrWhiteSpace(options.Value.PasskeyRpId) ? http.Request.Host.Host.ToLowerInvariant() : options.Value.PasskeyRpId.Trim().ToLowerInvariant();

    /// <summary>Whether a passkey answer may come from this page origin: a configured one, else
    /// exactly the request's own origin.</summary>
    public bool OriginAllowed(string origin, HttpContext http)
    {
        var configured = options.Value.PasskeyOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')).ToList();
        return configured.Count > 0
            ? configured.Contains(origin, StringComparer.OrdinalIgnoreCase)
            : string.Equals(origin, $"{http.Request.Scheme}://{http.Request.Host.Value}", StringComparison.OrdinalIgnoreCase);
    }

    private byte[] Mac(ReadOnlySpan<byte> payload) => HMACSHA256.HashData(_key, payload)[..MacLength];
}
