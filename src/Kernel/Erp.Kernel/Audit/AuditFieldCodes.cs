using System.Security.Cryptography;
using System.Text;

namespace Erp.Kernel.Audit;

/// <summary>
/// The code of a recorded field name in <c>audit.entries.field_list</c>: the first ten hex digits of the
/// name's SHA-256, exactly as <c>audit.field_list()</c> writes them. The text is an index key only (a
/// filter by field narrows by it and then tests the field array itself), so its strength does not matter;
/// it is a code because field names share their letters and a trigram index of the names themselves is
/// no narrower than the names' common letters.
/// </summary>
public static class AuditFieldCodes
{
    /// <summary>The code of <paramref name="field"/>.</summary>
    public static string Of(string field) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(field)))[..10].ToLowerInvariant();
}
