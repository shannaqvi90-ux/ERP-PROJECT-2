using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Testing;

/// <summary>
/// A passkey kept in software, as a test's stand-in for a person's device: it answers the
/// product's WebAuthn options the way an authenticator does (ES256 on P-256, attestation "none",
/// a person present and verified), so registration and sign-in run through the real endpoints and
/// checks. Every value a test wants to tamper with (origin, relying party, flags, counter, user
/// handle, signature) can be changed per answer.
/// </summary>
public sealed class SoftwarePasskey : IDisposable
{
    public const string DefaultOrigin = "http://localhost";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint _counter;

    public SoftwarePasskey(bool counting = false)
    {
        CredentialId = RandomNumberGenerator.GetBytes(32);
        Counting = counting;
    }

    public byte[] CredentialId { get; }

    /// <summary>Whether the device keeps a signature counter (a security key does; a synced passkey sends 0).</summary>
    public bool Counting { get; }

    /// <summary>The user handle the product gave at registration (base64url).</summary>
    public string? UserHandle { get; private set; }

    public string Origin { get; set; } = DefaultOrigin;

    public static string Base64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var standard = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight(standard.Length + (4 - standard.Length % 4) % 4, '='));
    }

    /// <summary>Asks for creation options as the signed-in <paramref name="client"/> and registers
    /// this passkey under <paramref name="name"/>; returns the product's answer.</summary>
    public async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string name = "Test device", Func<JsonObject, JsonObject>? tamper = null)
    {
        using var optionsResponse = await client.PostAsync("/api/identity/me/passkeys/options", null);
        if (!optionsResponse.IsSuccessStatusCode)
        {
            return new HttpResponseMessage(optionsResponse.StatusCode) { Content = new StringContent(await optionsResponse.Content.ReadAsStringAsync()) };
        }
        var options = await optionsResponse.Content.ReadFromJsonAsync<JsonObject>() ?? [];
        var body = Registration(options, name);
        return await client.PostAsJsonAsync("/api/identity/me/passkeys", tamper is null ? body : tamper(body));
    }

    /// <summary>The registration body answering <paramref name="options"/> (creation options).</summary>
    public JsonObject Registration(JsonObject options, string name, string? rpId = null, byte flags = 0x45)
    {
        UserHandle = options["user"]!["id"]!.GetValue<string>();
        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());
        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId ?? options["rp"]!["id"]!.GetValue<string>())));
        authData.Add(flags);
        authData.AddRange(BigEndian(_counter));
        authData.AddRange(new byte[16]);
        authData.Add((byte)(CredentialId.Length >> 8));
        authData.Add((byte)CredentialId.Length);
        authData.AddRange(CredentialId);
        authData.AddRange(CoseKey());
        var attestation = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes([.. authData])));
        return new JsonObject
        {
            ["name"] = name,
            ["clientDataJson"] = Base64Url(clientData),
            ["attestationObject"] = Base64Url(attestation),
            ["transports"] = new JsonArray("internal", "hybrid"),
        };
    }

    /// <summary>The device's answer to a sign-in <paramref name="challenge"/> (base64url) for
    /// <paramref name="rpId"/>, as the sign-in request's <c>passkey</c> field.</summary>
    public JsonObject Assertion(string challenge, string rpId = "localhost", byte flags = 0x05, string? userHandle = null, string? origin = null,
        string type = "webauthn.get", byte[]? credentialId = null, uint? counter = null)
    {
        if (Counting)
        {
            _counter++;
        }
        var clientData = ClientData(type, challenge, origin);
        var authData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(authData, 0);
        authData[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(authData.AsSpan(33), counter ?? _counter);
        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return new JsonObject
        {
            ["credentialId"] = Base64Url(credentialId ?? CredentialId),
            ["clientDataJson"] = Base64Url(clientData),
            ["authenticatorData"] = Base64Url(authData),
            ["signature"] = Base64Url(signature),
            ["userHandle"] = userHandle ?? UserHandle ?? throw new InvalidOperationException("Register the passkey first."),
        };
    }

    /// <summary>A fresh sign-in challenge from the anonymous session answer.</summary>
    public static async Task<string> ChallengeAsync(HttpClient anonymous)
    {
        var session = await anonymous.GetFromJsonAsync<JsonObject>("/api/auth/session");
        return session!["passkey"]!["challenge"]!.GetValue<string>();
    }

    /// <summary>Signs <paramref name="anonymous"/> in with this passkey (a fresh challenge, a
    /// truthful answer); returns the product's answer.</summary>
    public async Task<HttpResponseMessage> SignInAsync(HttpClient anonymous, bool issueToken = false) =>
        await anonymous.PostAsJsonAsync("/api/auth/sign-in", new JsonObject
        {
            ["passkey"] = Assertion(await ChallengeAsync(anonymous)),
            ["issueToken"] = issueToken,
        });

    private byte[] ClientData(string type, string challenge, string? origin = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = type,
            ["challenge"] = challenge,
            ["origin"] = origin ?? Origin,
            ["crossOrigin"] = false,
        });

    private byte[] CoseKey()
    {
        var p = _key.ExportParameters(false);
        return Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),
            (Cbor.Int(3), Cbor.Int(-7)),
            (Cbor.Int(-1), Cbor.Int(1)),
            (Cbor.Int(-2), Cbor.Bytes(p.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(p.Q.Y!)));
    }

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    public void Dispose() => _key.Dispose();

    /// <summary>Just enough CBOR writing for an attestation object and a COSE key.</summary>
    public static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            return [.. Head(3, (ulong)utf8.Length), .. utf8];
        }

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(e => e.Key.Concat(e.Value))];

        private static byte[] Head(int major, ulong value)
        {
            var type = (byte)(major << 5);
            return value switch
            {
                < 24 => [(byte)(type | (byte)value)],
                <= byte.MaxValue => [(byte)(type | 24), (byte)value],
                <= ushort.MaxValue => [(byte)(type | 25), (byte)(value >> 8), (byte)value],
                _ => [(byte)(type | 26), (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value],
            };
        }
    }
}
