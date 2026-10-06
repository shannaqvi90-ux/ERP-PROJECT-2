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

    /// <summary>The spellings a search word matches: itself without short vowels and tatweel, its
    /// Arabic letter variants, each with a shadda after one letter, and (when typed with marks) the
    /// word as typed; at most <see cref="MaxSpellings"/>, the likeliest first. Latin words match
    /// only themselves.</summary>
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
        var ranked = spellings.Select(s => (Spelling: s, Changes: s.Where((c, i) => c != text[i]).Count(), Shadda: false)).ToList();
        // Names are often stored with a shadda on one letter ("شمّة", "محمّد", "عليّ") and typed
        // without it: an Arabic word also matches each spelling with a shadda after one of its
        // letters (the first excepted), as one more change. The trigram indexes read the shadda as
        // part of the word, so these patterns are served by the index like the others.
        if (HasArabicLetter(text))
        {
            ranked.AddRange(ranked.ToList().SelectMany(r => Enumerable.Range(1, r.Spelling.Length - 1)
                .Where(i => IsArabicLetter(r.Spelling[i]))
                .Select(i => (r.Spelling.Insert(i + 1, Shadda), r.Changes + 1, true))));
        }
        // As typed first, then the spellings that change fewest letters (the likeliest ones, kept
        // when a phrase of several words has to be cut to a bounded number of spellings).
        // Among spellings with as many changes, the letter variants before the added shadda.
        var result = ranked
            .OrderBy(r => r.Changes)
            .ThenBy(r => r.Shadda)
            .ThenBy(r => r.Spelling, StringComparer.Ordinal)
            .Select(r => r.Spelling)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSpellings)
            .ToList();
        // A word typed with short vowels or shadda is also tried exactly as typed, first: a name
        // stored with them ("مُحَمَّد") is then found by typing it as it is written.
        if (text.Length != word.Length)
        {
            result.Insert(0, word);
            if (result.Count > MaxSpellings)
            {
                result.RemoveAt(result.Count - 1);
            }
        }
        return result;
    }

    /// <summary>A search word as a regular expression both PostgreSQL (<c>~*</c>) and .NET read
    /// alike: letters and digits as typed, every other character escaped, each Arabic letter of a
    /// spelling group as a class of the group (<c>[اأإآٱ]</c>); short vowels and tatweel dropped.</summary>
    public static string Pattern(string word)
    {
        var pattern = new StringBuilder();
        // In an Arabic word, every letter may be followed in the stored value by short vowels,
        // shadda or tatweel (written or not), so "محمد" ranks "مُحَمَّد" as it ranks "محمد".
        var marks = HasArabicLetter(word) ? IgnorableClass : "";
        foreach (var c in word)
        {
            if (IsIgnorable(c))
            {
                continue;
            }
            if (GroupOf(c) is { } group)
            {
                pattern.Append('[').Append(group).Append(']').Append(marks);
            }
            else if (char.IsLetterOrDigit(c))
            {
                pattern.Append(c).Append(char.IsLetter(c) ? marks : "");
            }
            else
            {
                pattern.Append('\\').Append(c);
            }
        }
        return pattern.ToString();
    }

    /// <summary>Any run of the marks <see cref="IsIgnorable"/> accepts, as a regular expression both
    /// PostgreSQL and .NET read alike (the characters themselves, no escapes or ranges).</summary>
    private const string IgnorableClass = "[\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0670\u0640]*";

    /// <summary>The word contains a letter of the Arabic script (Arabic, Arabic Supplement and the
    /// presentation forms), so it may occur in a search field marked <see cref="ListTextScript.Arabic"/>.</summary>
    public static bool HasArabicLetter(string word)
    {
        foreach (var c in word)
        {
            if (char.IsLetter(c) && c is (>= '\u0600' and <= '\u06FF') or (>= '\u0750' and <= '\u077F')
                or (>= '\u08A0' and <= '\u08FF') or (>= '\uFB50' and <= '\uFDFF') or (>= '\uFE70' and <= '\uFEFF'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether quick search tries the word on a search field: every word on a field of
    /// any script, only words with an Arabic letter on a field of Arabic script.</summary>
    public static bool Reaches(ListColumn field, string word) =>
        field.Script == ListTextScript.Any || HasArabicLetter(word);

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

    private const string Shadda = "\u0651";

    private static bool IsArabicLetter(char c) => HasArabicLetter(c.ToString());

    /// <summary>Arabic short vowels, shadda, sukun, superscript alef and tatweel.</summary>
    private static bool IsIgnorable(char c) => c is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ';
}
