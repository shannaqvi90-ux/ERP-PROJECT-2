using System.Security.Cryptography;
using System.Text;

namespace Erp.Modules.Identity.Auth;

/// <summary>
/// Letting one new device sign in from an address many clients share (critic p03 round 12).
/// In the shipped deployment every browser arrives from the compose gateway, so after anyone's
/// failures there a person on a new laptop, a fresh browser or cleared cookies was paused with
/// their right password, and the only way in was a new set-up code, which throws the password
/// away. Now the paused sign-in screen of such a browser shows a device code (made from the
/// browser's own pending id and the account's address, <see cref="TrustedDevices.ApprovalCode"/>).
/// The person, signed in on another device (My account), or an administrator (the user's Sign-in
/// history) enters it; for <see cref="AuthOptions.DeviceApprovalMinutes"/> that one browser is then
/// a client of its own on that one account, apart from the shared address: its right password signs
/// in, its first sign-in makes it a known device (<see cref="TrustedDevices"/>) and uses the
/// approval up, and its own failures pause it after the same threshold as any client. Nothing else
/// changes: everyone else behind the address stays paused, and the code is useless to any other
/// browser, because a browser's code is keyed by this deployment's key.
/// </summary>
public static class DeviceApprovals
{
    /// <summary>The set-up codes' unambiguous symbols (no 0/O, 1/I/L).</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int Length = 8;

    /// <summary>The code made from keyed bytes, shown as two groups of four (K7QM-3XRA).</summary>
    public static string CodeFrom(ReadOnlySpan<byte> keyed)
    {
        Span<char> code = stackalloc char[Length + 1];
        for (int i = 0, k = 0; i < code.Length; i++)
        {
            code[i] = i == 4 ? '-' : Alphabet[keyed[k++] % Alphabet.Length];
        }
        return new string(code);
    }

    /// <summary>The code as typed, in its stored form (eight symbols, upper case, no separator), or
    /// null when it is not a device code. Spaces and hyphens are ignored, letters may be lower case.</summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Length > 32)
        {
            return null;
        }
        var symbols = new StringBuilder(Length);
        foreach (var c in typed)
        {
            if (c is '-' or ' ' or ' ')
            {
                continue;
            }
            var upper = char.ToUpperInvariant(c);
            if (!Alphabet.Contains(upper, StringComparison.Ordinal) || symbols.Length == Length)
            {
                return null;
            }
            symbols.Append(upper);
        }
        return symbols.Length == Length ? symbols.ToString() : null;
    }

    /// <summary>What is kept of a code (<see cref="User.DeviceApprovalHash"/>), from a code in any
    /// accepted form; null when it is not a device code.</summary>
    public static byte[]? Hash(string? code) =>
        Normalize(code) is { } normal ? SHA256.HashData(Encoding.UTF8.GetBytes("erp-device-approval\n" + normal)) : null;

    /// <summary>How long an approval holds, clamped as documented.</summary>
    public static TimeSpan Lifetime(AuthOptions options) => TimeSpan.FromMinutes(Math.Clamp(options.DeviceApprovalMinutes, 5, 1440));
}
