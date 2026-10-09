using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Erp.Modules.Identity.Auth.Passkeys;

/// <summary>COSE algorithm identifiers the product accepts (IANA COSE Algorithms registry).</summary>
internal static class CoseAlgorithms
{
    /// <summary>ECDSA with SHA-256 on P-256.</summary>
    public const int Es256 = -7;

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-256.</summary>
    public const int Rs256 = -257;

    public static readonly IReadOnlyList<int> Accepted = [Es256, Rs256];
}

/// <summary>The authenticator data WebAuthn signs (W3C Web Authentication, section 6.1).</summary>
internal sealed record AuthenticatorData(
    byte[] RpIdHash,
    byte Flags,
    uint SignCount,
    byte[]? CredentialId,
    byte[]? PublicKey,
    int? Algorithm)
{
    public const byte UserPresent = 0x01;
    public const byte UserVerified = 0x04;
    public const byte BackupEligibleFlag = 0x08;
    public const byte BackedUpFlag = 0x10;
    public const byte AttestedCredentialData = 0x40;
    public const byte ExtensionData = 0x80;

    public bool Has(byte flag) => (Flags & flag) == flag;
}

/// <summary>
/// The checks WebAuthn asks of a relying party (W3C Web Authentication, sections 7.1 and 7.2),
/// written out for the two ceremonies the product uses: registering a passkey and signing in with
/// one. The product asks for no attestation ("none"): it trusts the person's own device as the
/// person, never a device maker's certificate, so attestation statements are not read.
/// </summary>
internal static class WebAuthn
{
    public const string CreationType = "webauthn.create";
    public const string AssertionType = "webauthn.get";

    public static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url (padding optional); null for anything else or above <paramref name="maxBytes"/>.</summary>
    public static byte[]? FromBase64Url(string? text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || text.Length > (maxBytes + 2) / 3 * 4 + 2)
        {
            return null;
        }
        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '='))
            {
                return null;
            }
        }
        var standard = text.TrimEnd('=').Replace('-', '+').Replace('_', '/');
        if (standard.Length % 4 == 1)
        {
            return null;
        }
        try
        {
            var bytes = Convert.FromBase64String(standard.PadRight(standard.Length + (4 - standard.Length % 4) % 4, '='));
            return bytes.Length <= maxBytes ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The client data a browser signed over: its type, the challenge it answered and the page
    /// origin, checked against what the server expects. <paramref name="challenge"/> receives the
    /// challenge bytes for the caller to verify (they are the server's own, signed).
    /// </summary>
    public static bool TryReadClientData(byte[] clientDataJson, string expectedType, Func<string, bool> originAllowed, out byte[] challenge)
    {
        challenge = [];
        try
        {
            using var document = JsonDocument.Parse(clientDataJson, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != expectedType ||
                !root.TryGetProperty("challenge", out var given) || given.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("origin", out var origin) || origin.ValueKind != JsonValueKind.String ||
                !originAllowed(origin.GetString()!))
            {
                return false;
            }
            // A page framed by another site (crossOrigin) never signs in to this one.
            if (root.TryGetProperty("crossOrigin", out var cross) && cross.ValueKind == JsonValueKind.True)
            {
                return false;
            }
            if (FromBase64Url(given.GetString(), 128) is not { } bytes)
            {
                return false;
            }
            challenge = bytes;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses authenticator data; with attested credential data (registration) also the
    /// credential id and its COSE public key, turned into a SubjectPublicKeyInfo.</summary>
    public static AuthenticatorData? ReadAuthenticatorData(byte[] data)
    {
        if (data.Length < 37)
        {
            return null;
        }
        var flags = data[32];
        var signCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(33, 4));
        var rpIdHash = data[..32];
        if ((flags & AuthenticatorData.AttestedCredentialData) == 0)
        {
            // An assertion: authenticator data with no credential (extensions may follow).
            return (flags & AuthenticatorData.ExtensionData) == 0 && data.Length != 37
                ? null
                : new AuthenticatorData(rpIdHash, flags, signCount, null, null, null);
        }
        // aaguid (16 bytes), credential id length (2), credential id, COSE key.
        if (data.Length < 37 + 16 + 2)
        {
            return null;
        }
        var idLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(53, 2));
        if (idLength is < 16 or > 1023 || data.Length < 55 + idLength + 1)
        {
            return null;
        }
        var credentialId = data.AsSpan(55, idLength).ToArray();
        if (!Cbor.TryRead(data, 55 + idLength, out var key, out var end) || key is not Dictionary<object, object?> cose)
        {
            return null;
        }
        if (end != data.Length && (flags & AuthenticatorData.ExtensionData) == 0)
        {
            return null;
        }
        if (PublicKeyOf(cose) is not { } publicKey)
        {
            return null;
        }
        return new AuthenticatorData(rpIdHash, flags, signCount, credentialId, publicKey.Spki, publicKey.Algorithm);
    }

    /// <summary>A COSE key (RFC 9053) of an accepted algorithm as a DER SubjectPublicKeyInfo:
    /// EC2 on P-256 for ES256, RSA of at least 2048 bits for RS256.</summary>
    public static (byte[] Spki, int Algorithm)? PublicKeyOf(Dictionary<object, object?> cose)
    {
        if (cose.GetValueOrDefault(1L) is not long kty || cose.GetValueOrDefault(3L) is not long alg)
        {
            return null;
        }
        try
        {
            if (kty == 2 && alg == CoseAlgorithms.Es256)
            {
                if (cose.GetValueOrDefault(-1L) is not long crv || crv != 1 ||
                    cose.GetValueOrDefault(-2L) is not byte[] { Length: 32 } x || cose.GetValueOrDefault(-3L) is not byte[] { Length: 32 } y)
                {
                    return null;
                }
                using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
                return (ecdsa.ExportSubjectPublicKeyInfo(), CoseAlgorithms.Es256);
            }
            if (kty == 3 && alg == CoseAlgorithms.Rs256)
            {
                if (cose.GetValueOrDefault(-1L) is not byte[] { Length: >= 256 and <= 1024 } n || cose.GetValueOrDefault(-2L) is not byte[] { Length: > 0 and <= 8 } e)
                {
                    return null;
                }
                using var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters { Modulus = n, Exponent = e });
                return rsa.KeySize >= 2048 ? (rsa.ExportSubjectPublicKeyInfo(), CoseAlgorithms.Rs256) : null;
            }
        }
        catch (CryptographicException)
        {
            // Not a point on the curve, or not a usable RSA key.
        }
        return null;
    }

    /// <summary>The authenticator data is for this relying party, a person was present and was
    /// verified on the device (a fingerprint, face or device PIN: a passkey replaces the password,
    /// so it must be as strong as one).</summary>
    public static bool IsForRelyingParty(AuthenticatorData data, string rpId) =>
        CryptographicOperations.FixedTimeEquals(data.RpIdHash, SHA256.HashData(Encoding.UTF8.GetBytes(rpId))) &&
        data.Has(AuthenticatorData.UserPresent) && data.Has(AuthenticatorData.UserVerified);

    /// <summary>The device's signature over the authenticator data and the hash of the client
    /// data, checked with the stored public key.</summary>
    public static bool VerifySignature(byte[] spki, int algorithm, byte[] authenticatorData, byte[] clientDataJson, byte[] signature)
    {
        var signed = new byte[authenticatorData.Length + 32];
        authenticatorData.CopyTo(signed, 0);
        SHA256.HashData(clientDataJson).CopyTo(signed, authenticatorData.Length);
        try
        {
            switch (algorithm)
            {
                case CoseAlgorithms.Es256:
                {
                    using var ecdsa = ECDsa.Create();
                    ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
                    return ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                }
                case CoseAlgorithms.Rs256:
                {
                    using var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(spki, out _);
                    return rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                default:
                    return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
