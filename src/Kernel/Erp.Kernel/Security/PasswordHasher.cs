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

    /// <summary>
    /// The public part of a stored hash: <c>algorithm$iterations$salt</c>. Sign-in hands it to the
    /// application instead of the hash, so the application can prove a password
    /// (<see cref="Prove"/>) without ever reading a stored hash.
    /// </summary>
    public static string? ChallengeOf(string stored)
    {
        var parts = stored?.Split('$');
        return parts is { Length: 4 } && parts[0] == Algorithm ? string.Join('$', parts[..3]) : null;
    }

    /// <summary>
    /// The stored-hash form this password would have under the challenge's algorithm, cost and
    /// salt; it equals the stored hash exactly when the password is right. Returns null for a
    /// malformed challenge, after the same amount of work, so timing does not tell them apart.
    /// </summary>
    public static string? Prove(string password, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        var parts = challenge?.Split('$');
        byte[]? salt = null;
        var iterations = 0;
        if (parts is { Length: 3 } && parts[0] == Algorithm && int.TryParse(parts[1], out iterations) && iterations is >= 10_000 and <= 10_000_000)
        {
            try
            {
                salt = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                salt = null;
            }
        }
        if (salt is null || salt.Length == 0)
        {
            Verify(password, Decoy, out _);
            return null;
        }
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, HashBytes);
        return $"{challenge}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True when a challenge's cost is below today's, so the hash should be renewed at
    /// the next successful sign-in.</summary>
    public static bool IsOutdated(string challenge)
    {
        var parts = challenge?.Split('$');
        return parts is { Length: >= 2 } && int.TryParse(parts[1], out var iterations) && iterations < Iterations;
    }

    /// <summary>A hash of a random password, verified against when the e-mail is unknown so the
    /// response time does not reveal whether an account exists.</summary>
    public static readonly string Decoy = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
}
