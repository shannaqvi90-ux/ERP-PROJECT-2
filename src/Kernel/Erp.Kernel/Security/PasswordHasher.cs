using System.Security.Cryptography;
using System.Text;

namespace Erp.Kernel.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 password hashing (OWASP Password Storage Cheat Sheet: 210,000 iterations
/// for SHA-512). Format: <c>pbkdf2-sha512$iterations$salt$hash</c> (base64), so the cost can be
/// raised later and old hashes still verify and get upgraded on sign-in.
/// </summary>
public static class PasswordHasher
{
    public const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Algorithm = "pbkdf2-sha512";

    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static string Hash(string password, int iterations = Iterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, HashBytes);
        return $"{Algorithm}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time verification. Returns false for any malformed hash.</summary>
    public static bool Verify(string password, string stored, out bool needsRehash)
    {
        needsRehash = false;
        if (password is null || stored is null)
        {
            return false;
        }
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm || !int.TryParse(parts[1], out var iterations) || iterations < 10_000)
        {
            return false;
        }
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsRehash = ok && iterations < Iterations;
        return ok;
    }

    /// <summary>A hash of a random password, verified against when the e-mail is unknown so the
    /// response time does not reveal whether an account exists.</summary>
    public static readonly string Decoy = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
}
