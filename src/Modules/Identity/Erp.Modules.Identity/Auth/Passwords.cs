using System.Security.Cryptography;
using Erp.Kernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Auth;

/// <summary>
/// Setting passwords and one-time set-up codes. The hash is written with an update the
/// application role is allowed (it may write <c>password_hash</c>, never read it).
/// </summary>
internal static class Passwords
{
    /// <summary>Unambiguous characters (no 0/O, 1/I/L): 32 symbols, 5 bits each.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public sealed record Refusal(string Code, object?[] Args);

    /// <summary>The validation code for an unacceptable new password, or null.</summary>
    public static Refusal? Problem(string password)
    {
        if (password.Length < PasswordHasher.MinLength)
        {
            return new Refusal("passwordTooShort", [PasswordHasher.MinLength]);
        }
        if (password.Length > PasswordHasher.MaxLength)
        {
            return new Refusal("passwordTooLong", [PasswordHasher.MaxLength]);
        }
        if (password.Distinct().Count() < 4)
        {
            return new Refusal("passwordTooSimple", []);
        }
        return null;
    }

    /// <summary>A one-time set-up code: 12 random characters in three groups (about 59 bits),
    /// for example <c>K7QM-3XRA-PZ9D</c>. Guessing is limited by the sign-in pause and the expiry.</summary>
    public static string NewSetupCode()
    {
        Span<char> code = stackalloc char[14];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = i is 4 or 9 ? '-' : CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return new string(code);
    }

    /// <summary>Insert the credential of a new user.</summary>
    public static void Add(IdentityDbContext db, Guid userId, string password, bool mustChange, DateTimeOffset? expiresAt, DateTimeOffset now, Guid? by) =>
        db.Credentials.Add(new UserCredential
        {
            Id = userId,
            PasswordHash = PasswordHasher.Hash(password),
            MustChange = mustChange,
            ExpiresAt = expiresAt,
            ChangedAt = now,
            ChangedBy = by,
        });

    /// <summary>Replace a user's password (or set-up code).</summary>
    public static async Task SetAsync(IdentityDbContext db, Guid userId, string password, bool mustChange, DateTimeOffset? expiresAt,
        DateTimeOffset now, Guid? by, CancellationToken cancellationToken)
    {
        var hash = PasswordHasher.Hash(password);
        var updated = await db.Credentials.Where(c => c.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PasswordHash, hash)
            .SetProperty(c => c.MustChange, mustChange)
            .SetProperty(c => c.ExpiresAt, expiresAt)
            .SetProperty(c => c.ChangedAt, now)
            .SetProperty(c => c.ChangedBy, by), cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException("The user has no credential row.");
        }
    }
}
