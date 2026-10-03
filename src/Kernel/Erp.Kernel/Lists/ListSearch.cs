using System.Text;

namespace Erp.Kernel.Lists;

/// <summary>
/// How quick search reads what people type. Every word must occur in one of the list's search
/// fields (any case). Arabic words also match the spellings people use interchangeably: the alef
/// forms (ا أ إ آ ٱ), final yeh and alef maqsura (ي ى ئ), teh marbuta and heh (ة ه), waw and waw
/// with hamza (و ؤ); short vowels and tatweel typed in the search are ignored. A list searched
/// without an explicit sort is ordered by relevance (see <see cref="RelevanceKey"/>).
/// </summary>
public static class ListSearch
{
    /// <summary>The sort key of relevance order inside cursors (never a column key: column keys
    /// cannot start with '~').</summary>
    public const string RelevanceKey = "~relevance";

    /// <summary>Most matching rows a search ranks; a broader search (a letter or two) keeps the
    /// list's default order, since ranking every row would cost more than it tells.</summary>
    public const int MaxRankedRows = 10_000;

    /// <summary>Most spellings one search word expands to for matching; positions beyond it keep
    /// the letter as typed (the first and last letters, where spelling varies most, first).</summary>
    public const int MaxSpellings = 32;

    // Groups of letters typed for one another. A typed letter matches every letter of its group.
    private static readonly System.Collections.Immutable.ImmutableArray<string> Groups = ["اأإآٱ", "يىئ", "هة", "وؤ"];

    /// <summary>Score parts of relevance (see <see cref="ListBinding{T}"/>): a word at the start of
    /// a field, a word at the start of a word inside a field, the whole search equal to a field, the
    /// whole search at the start of a field, the words in the typed order at word starts.</summary>
    internal const int FieldStartScore = 4;
    internal const int WordStartScore = 2;
    internal const int ExactScore = 16;
    internal const int PhraseStartScore = 8;
    internal const int InOrderScore = 4;

    /// <summary>Relevance is score × this, minus the length of the first search field (capped), so
    /// that among equally good matches the shorter (closer) value comes first.</summary>
    internal const int LengthSlots = 1024;

    /// <summary>Characters that start a word inside a field (a name's parts, an e-mail's parts).</summary>
    internal static readonly System.Collections.Immutable.ImmutableArray<string> WordSeparators = [" ", "."];

    /// <summary>The spellings a search word matches: itself without short vowels and tatweel, and
    /// its Arabic letter variants (at most <see cref="MaxSpellings"/>). Latin words match only
    /// themselves.</summary>
    public static IReadOnlyList<string> Spellings(string word)
    {
        var plain = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            if (!IsIgnorable(c))
            {
                plain.Append(c);
            }
        }
        var text = plain.ToString();
        if (text.Length == 0)
        {
            return [word];
        }
        var positions = new List<(int Index, string Group)>();
        for (var i = 0; i < text.Length; i++)
        {
            if (GroupOf(text[i]) is { } group)
            {
                positions.Add((i, group));
            }
        }
        // Expand the first and last letters first, then the others in order, while the number of
        // spellings stays within bounds.
        var ordered = positions
            .OrderBy(p => p.Index == 0 || p.Index == text.Length - 1 ? 0 : 1)
            .ThenBy(p => p.Index)
            .ToList();
        var chosen = new List<(int Index, string Group)>();
        var count = 1;
        foreach (var position in ordered)
        {
            if (count * position.Group.Length > MaxSpellings)
            {
                continue;
            }
            count *= position.Group.Length;
            chosen.Add(position);
        }
        var spellings = new List<string> { text };
        foreach (var (index, group) in chosen)
        {
            spellings = spellings
                .SelectMany(s => group.Select(letter =>
                {
                    var chars = s.ToCharArray();
                    chars[index] = letter;
                    return new string(chars);
                }))
                .ToList();
        }
        // As typed first, then the spellings that change fewest letters (the likeliest ones, kept
        // when a phrase of several words has to be cut to a bounded number of spellings).
        return spellings.Distinct(StringComparer.Ordinal)
            .OrderBy(s => s.Where((c, i) => c != text[i]).Count())
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A search word as a regular expression both PostgreSQL (<c>~*</c>) and .NET read
    /// alike: letters and digits as typed, every other character escaped, each Arabic letter of a
    /// spelling group as a class of the group (<c>[اأإآٱ]</c>); short vowels and tatweel dropped.</summary>
    public static string Pattern(string word)
    {
        var pattern = new StringBuilder();
        foreach (var c in word)
        {
            if (IsIgnorable(c))
            {
                continue;
            }
            if (GroupOf(c) is { } group)
            {
                pattern.Append('[').Append(group).Append(']');
            }
            else if (char.IsLetterOrDigit(c))
            {
                pattern.Append(c);
            }
            else
            {
                pattern.Append('\\').Append(c);
            }
        }
        return pattern.ToString();
    }

    private static string? GroupOf(char c)
    {
        foreach (var group in Groups)
        {
            if (group.Contains(c, StringComparison.Ordinal))
            {
                return group;
            }
        }
        return null;
    }

    /// <summary>Arabic short vowels, shadda, sukun, superscript alef and tatweel.</summary>
    private static bool IsIgnorable(char c) => c is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ';
}
