using System.Collections.Concurrent;
using System.Security.Cryptography;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// Everything the attacking tenant's answers can show, as text: every value of every column of its
/// own rows and of the tables no tenant owns (the seeds), read with the superuser. A "value that
/// exists nowhere" (the differential oracle's control) is drawn until it occurs in none of it, in
/// no letter case, so a control never matches a row, a code prefix or a number of the attacker's
/// own by chance and the oracle never reports a leak that is not there (p10 round 1, p12 rounds 1
/// and 2: a two-letter control hit the attacker's own number prefixes, a six-digit control occurred
/// inside one of 100,000 contact numbers).
/// </summary>
public sealed class ControlCorpus
{
    public static ControlCorpus Empty { get; } = new([]);

    private readonly string[] _haystacks;
    private readonly ConcurrentDictionary<string, string> _perValue = new(StringComparer.Ordinal);
    private int _fallbacks;

    /// <param name="haystacks">Texts the control must not occur in (any case).</param>
    public ControlCorpus(IEnumerable<string> haystacks) =>
        _haystacks = haystacks.Select(h => h.ToLowerInvariant()).ToArray();

    /// <summary>Characters held, for the gate's report.</summary>
    public long Characters => _haystacks.Sum(h => (long)h.Length);

    /// <summary>Controls that had to be longer than their value because every text of the value's
    /// shape occurs in the corpus.</summary>
    public int Fallbacks => _fallbacks;

    public bool Occurs(string candidate)
    {
        var lower = candidate.ToLowerInvariant();
        foreach (var haystack in _haystacks)
        {
            if (haystack.Contains(lower, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A value of the shape of <paramref name="value"/> (every letter and digit replaced at
    /// random, punctuation kept; longer only where every text of that shape occurs) that occurs in
    /// no text of the corpus, in any letter case, nor in <paramref name="answerLower"/> (an answer of
    /// the attacker, lower-cased, named by <paramref name="answerKey"/>). Made once per value (and
    /// answer): the corpus scan is the cost, and the same control for the same value on every endpoint
    /// is as good as a new one.</summary>
    public string ControlFor(string value, string? answerKey = null, string? answerLower = null)
    {
        var control = _perValue.GetOrAdd(value, v => Draw(v, c => Occurs(c)));
        if (answerLower is null || !answerLower.Contains(control.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return control;
        }
        return _perAnswer.GetOrAdd(value + "\u0000" + answerKey,
            _ => Draw(value, c => Occurs(c) || answerLower.Contains(c.ToLowerInvariant(), StringComparison.Ordinal)));
    }

    private readonly ConcurrentDictionary<string, string> _perAnswer = new(StringComparer.Ordinal);

    private string Draw(string value, Func<string, bool> occurs)
    {
        if (Guid.TryParse(value, out _))
        {
            return Guid.NewGuid().ToString();
        }
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var candidate = Shape(value);
            if (!candidate.Equals(value, StringComparison.OrdinalIgnoreCase) && !occurs(candidate))
            {
                return candidate;
            }
        }
        // Every text of this shape (a one or two character value) is in the data: the same shape
        // with more characters of the last one's kind added, until one is not.
        Interlocked.Increment(ref _fallbacks);
        for (var extra = 1; ; extra++)
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var candidate = Shape(value) + Shape(new string(Last(value), extra));
                if (!occurs(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    private static char Last(string value)
    {
        for (var i = value.Length - 1; i >= 0; i--)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                return value[i];
            }
        }
        return 'a';
    }

    /// <summary>Every letter and digit replaced at random, punctuation kept.</summary>
    public static string Shape(string value)
    {
        var random = RandomNumberGenerator.GetBytes(value.Length);
        return new string(value.Select((c, i) => char.IsLetter(c) ? (char)('a' + random[i] % 26) : char.IsDigit(c) ? (char)('0' + random[i] % 10) : c).ToArray());
    }

    /// <summary>The tenant's own text: every column of its rows in the tenant tables, every column of
    /// the tables without a tenant.</summary>
    public static async Task<ControlCorpus> ReadAsync(ErpTestEnvironment env, Guid tenant)
    {
        var haystacks = new List<string>();
        await using var admin = await env.OpenAdminAsync();
        var tenantTables = (await DbCatalog.TenantTablesAsync(admin)).ToHashSet();
        foreach (var table in await DbCatalog.AllTablesAsync(admin))
        {
            var owned = tenantTables.Contains(table);
            foreach (var column in await DbCatalog.ColumnsAsync(admin, table))
            {
                if (column.Type == "bytea")
                {
                    continue;
                }
                var sql = $"SELECT DISTINCT \"{column.Name}\"::text FROM {table.Qualified} WHERE \"{column.Name}\" IS NOT NULL" + (owned ? " AND tenant_id = @t" : "");
                var values = owned
                    ? await DbCatalog.ReadAsync(admin, sql, r => r.GetString(0), ("t", tenant))
                    : await DbCatalog.ReadAsync(admin, sql, r => r.GetString(0));
                var chunk = new System.Text.StringBuilder();
                foreach (var value in values)
                {
                    chunk.Append(value).Append('\u0001');
                    if (chunk.Length > 8_000_000)
                    {
                        haystacks.Add(chunk.ToString());
                        chunk.Clear();
                    }
                }
                if (chunk.Length > 0)
                {
                    haystacks.Add(chunk.ToString());
                }
            }
        }
        return new ControlCorpus(haystacks);
    }
}
