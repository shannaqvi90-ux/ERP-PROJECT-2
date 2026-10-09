using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Erp.Kernel.Data;

/// <summary>
/// The row version a client sees, kept apart from PostgreSQL's <c>xmin</c> (critic p03 round 6:
/// <c>xmin</c> is the id of the transaction that wrote the row, counted across the whole database,
/// so tenant A could count other tenants' writes between two of its own; tenant B's role got
/// version 944 and tenant A's next record 945).
///
/// The version is a keyed permutation of the 32-bit <c>xmin</c>: an eight-round Feistel network on
/// 16-bit halves whose round functions are tables drawn from AES with the deployment's key. It is a
/// bijection, so EF Core's optimistic concurrency works unchanged (an edit sends back the version
/// it read; the converter turns it back into the <c>xmin</c> the <c>WHERE</c> compares), while
/// versions of different rows, or of one row before and after a write, say nothing about how many
/// transactions came between. The key is <c>ERP_ROW_VERSION_KEY</c> (<c>Erp__RowVersionKey</c>)
/// when set, else random per process (like the device key): versions a client holds then stop
/// matching after a restart, and an edit sent with one answers 409 and reloads.
/// </summary>
public static class RowVersions
{
    private const int Rounds = 8;

    // Round tables, drawn once when the type loads; never written afterwards.
    private static readonly ImmutableArray<ushort> Tables = Draw(Key());

    /// <summary>The version a client sees for <paramref name="xmin"/>.</summary>
    public static uint Hide(uint xmin)
    {
        var left = (ushort)(xmin >> 16);
        var right = (ushort)xmin;
        for (var round = 0; round < Rounds; round++)
        {
            (left, right) = (right, (ushort)(left ^ Tables[(round << 16) | right]));
        }
        return ((uint)left << 16) | right;
    }

    /// <summary>The <c>xmin</c> a version a client sent back stands for.</summary>
    public static uint Reveal(uint version)
    {
        var left = (ushort)(version >> 16);
        var right = (ushort)version;
        for (var round = Rounds - 1; round >= 0; round--)
        {
            (left, right) = ((ushort)(right ^ Tables[(round << 16) | left]), left);
        }
        return ((uint)left << 16) | right;
    }

    private static byte[] Key()
    {
        var configured = Environment.GetEnvironmentVariable("ERP_ROW_VERSION_KEY") ?? Environment.GetEnvironmentVariable("Erp__RowVersionKey");
        return string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : SHA256.HashData(Encoding.UTF8.GetBytes("erp:row-version:" + configured));
    }

    private static ImmutableArray<ushort> Draw(byte[] key)
    {
        // AES in counter mode: block n encrypts the number n; 2^16 blocks of 16 bytes are
        // eight tables of 2^16 two-byte entries.
        var blocks = new byte[(Rounds << 16) * 2];
        for (var n = 0; n < blocks.Length / 16; n++)
        {
            BinaryPrimitives.WriteInt64BigEndian(blocks.AsSpan(n * 16 + 8, 8), n);
        }
        using var aes = Aes.Create();
        aes.Key = key;
        var stream = aes.EncryptEcb(blocks, PaddingMode.None);
        CryptographicOperations.ZeroMemory(key);
        var tables = ImmutableArray.CreateBuilder<ushort>(Rounds << 16);
        for (var i = 0; i < Rounds << 16; i++)
        {
            tables.Add(BinaryPrimitives.ReadUInt16BigEndian(stream.AsSpan(i * 2, 2)));
        }
        return tables.MoveToImmutable();
    }
}
