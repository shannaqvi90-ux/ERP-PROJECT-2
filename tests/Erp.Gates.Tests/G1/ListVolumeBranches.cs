using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Kernel.Lists;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The volume check's searches built from the engine's search branches, not from letters (critic p05
/// round 12, plant L13: the total of a broad initials search, one word of 2 to 5 Latin letters, was
/// kept on disk keyed without the tenant, and all of G1 passed because the volume check searched
/// one-letter words only). The engine runs different code for each shape of search
/// (<see cref="ListBinding{T}"/>: a word of 2 to <see cref="ListSearch.MaxInitials"/> Latin letters
/// also matches initials; a longer word only its trigram patterns; an Arabic word first a regular
/// expression of its spellings; several words one condition each and other relevance scores; a
/// word with digits or other characters escaped patterns), and for each size of answer (ranked up
/// to <see cref="ListSearch.MaxRankedRows"/> rows, the default order past it). For every list at
/// volume, each branch and size gets one search chosen from the tenants' own rows whose true answers
/// differ between the tenants (broad in both, or ranked in both); a required branch without one is
/// reported blind. The chosen searches are judged like every other query (B, A, B, A, first page
/// and deep), and the first short word of each size also with a filter, a sort and a grouping.
/// </summary>
public static partial class ListVolume
{
    /// <summary>Rows per tenant the candidate searches are first estimated on.</summary>
    private const int SampleRows = 3000;

    /// <summary>Candidates whose exact counts are taken per branch before it is reported blind.</summary>
    private const int ExactTries = 12;

    /// <summary>One branch and size of answer of the engine's search.</summary>
    /// <param name="Shape">The branch (see <see cref="SearchBranch"/>).</param>
    /// <param name="Broad">Too broad to rank in both tenants (else ranked in both).</param>
    /// <param name="Required">A list at volume whose rows can take this branch must judge it.</param>
    internal sealed record BranchClass(string Shape, bool Broad, bool Required)
    {
        public string Name => $"{Shape}, {(Broad ? "too broad to rank" : "ranked")}";
    }

    /// <summary>A search chosen for a branch, with its true counts in A and B.</summary>
    internal sealed record BranchChoice(BranchClass Class, string Search, int CountA, int CountB);

    /// <summary>
    /// The branch of the engine a search takes, as <see cref="ListBinding{T}"/> plans it: the words
    /// (split at white space, lower case, distinct); one word of letters is a letter, a short word
    /// (2 to <see cref="ListSearch.MaxInitials"/> letters, tried as initials when the list searches
    /// initials) or a long word, Latin or Arabic; a word with digits or with other characters; or
    /// several words, Latin or with Arabic.
    /// </summary>
    internal static string SearchBranch(string search, bool initials)
    {
        var words = Words(search);
        if (words.Count == 0)
        {
            return "no search";
        }
        if (words.Count == 1)
        {
            var word = words[0];
            if (word.All(char.IsLetter))
            {
                if (ListSearch.HasArabicLetter(word))
                {
                    return word.Length == 1 ? "Arabic letter" : "Arabic word";
                }
                return word.Length switch
                {
                    1 => "one letter",
                    <= ListSearch.MaxInitials => $"{(initials ? "initials" : "short")} word of {word.Length} letters",
                    _ => "long word",
                };
            }
            return word.Any(char.IsDigit) ? "word with digits" : "word with other characters";
        }
        return words.Any(ListSearch.HasArabicLetter) ? "Arabic words" : "words";
    }

    private static List<string> Words(string search) =>
        search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Lower).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// What makes a row match a search, from the row's values as the list answers them: every word
    /// (in any of its spellings) in one of the fields a word like it searches, any case; one word
    /// of 2 to <see cref="ListSearch.MaxInitials"/> Latin letters also matches a value whose initials
    /// it is (<see cref="ListSearch.InitialsPattern"/>, the column the binding names). Null when the
    /// search takes the initials branch but the list reads the initials from a value no column shows.
    /// </summary>
    internal static Func<JsonElement, bool>? SearchMatcher(IListBinding binding, string search)
    {
        var list = binding.Definition;
        var words = Words(search);
        var parts = words.Select(w => (Fields: list.SearchFieldsFor(w), Spellings: ListSearch.Spellings(w).Select(Lower).ToList())).ToList();
        Regex? initials = null;
        string? initialsColumn = null;
        if (binding.SearchesInitials && words is [var one] && one.Length is >= 2 and <= ListSearch.MaxInitials &&
            one.All(char.IsLetter) && !ListSearch.HasArabicLetter(one))
        {
            if (binding.InitialsColumn is not { } column)
            {
                return null;
            }
            initialsColumn = column;
            initials = new Regex(ListSearch.InitialsPattern(one), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return row =>
        {
            if (initials is not null && Text(row, initialsColumn!) is { } value && initials.IsMatch(value))
            {
                return true;
            }
            foreach (var (fields, spellings) in parts)
            {
                var found = false;
                foreach (var field in fields)
                {
                    if (Text(row, field) is { } text)
                    {
                        var lower = Lower(text);
                        if (spellings.Any(s => lower.Contains(s, StringComparison.Ordinal)))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            return true;
        };
    }

    /// <summary>The branches a list's searches are judged on: every short word size, long words,
    /// digits, several words, and, when both tenants' rows hold Arabic text in the fields an Arabic
    /// word searches, Arabic words, each too broad to rank and ranked.</summary>
    internal static IReadOnlyList<BranchClass> BranchClasses(IListBinding binding, bool arabic, bool digits)
    {
        var classes = new List<BranchClass>();
        void Both(string shape, bool required, bool broadRequired = true)
        {
            classes.Add(new BranchClass(shape, true, required && broadRequired));
            classes.Add(new BranchClass(shape, false, required));
        }
        for (var length = 2; length <= ListSearch.MaxInitials; length++)
        {
            Both(SearchBranch(new string('a', length), binding.SearchesInitials), true);
        }
        Both("long word", true);
        Both("word with digits", digits);
        Both("words", true);
        Both("Arabic word", arabic);
        Both("Arabic words", arabic, broadRequired: false);
        Both("word with other characters", false);
        return classes;
    }

    /// <summary>
    /// For each branch class, one search whose true answers differ between the tenants and are both
    /// too broad to rank or both ranked. Candidates come from a sample of each tenant's own rows
    /// (pieces of the words in the search fields, the initials of the initials column, pairs of
    /// words), ordered by their estimated counts (for a broad class the narrowest still clearly too
    /// broad first, for a ranked one the broadest still clearly ranked, initials before pieces of
    /// words); the first whose exact counts over every walked row qualify is chosen.
    /// </summary>
    private static (List<BranchChoice> Chosen, List<string> Blind) ChooseBranchSearches(IListBinding binding, Truth a, Truth b)
    {
        var list = binding.Definition;
        var latinFields = list.SearchFieldsFor("a").ToHashSet(StringComparer.Ordinal);
        var arabicFields = list.SearchFieldsFor("ا").ToHashSet(StringComparer.Ordinal);
        var samples = new[] { Sample(a.Rows), Sample(b.Rows) };
        var sizes = new[] { a.Rows.Count, b.Rows.Count };

        // Row counts in each tenant's sample: pieces of words (Latin letters 2 to 8 long, digits 1
        // to 4, Arabic letters 2 to 5, single other characters), initials, and pairs of words.
        var pieces = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var pairs = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var initialsSeen = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var arabicIn = new bool[2];
        var digitsIn = new bool[2];
        for (var t = 0; t < 2; t++)
        {
            foreach (var row in samples[t])
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var seenPairs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in list.AllSearchFields)
                {
                    if (Text(row, field) is not { } value)
                    {
                        continue;
                    }
                    var text = Lower(value);
                    foreach (var (run, kind) in Runs(text))
                    {
                        if (kind == 'a' && !arabicFields.Contains(field) || kind != 'a' && !latinFields.Contains(field))
                        {
                            continue;
                        }
                        var (shortest, longest) = kind switch { 'l' => (2, 8), 'd' => (1, 4), 'a' => (2, 5), _ => (1, 1) };
                        for (var length = shortest; length <= Math.Min(longest, run.Length); length++)
                        {
                            for (var i = 0; i + length <= run.Length; i++)
                            {
                                seen.Add(run.Substring(i, length));
                            }
                        }
                        arabicIn[t] |= kind == 'a';
                        digitsIn[t] |= kind == 'd';
                    }
                    // Pairs of neighbouring words of one script, whole and as their first two or three letters.
                    var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i + 1 < tokens.Length; i++)
                    {
                        var (x, y) = (tokens[i], tokens[i + 1]);
                        if (!x.All(char.IsLetter) || !y.All(char.IsLetter) || ListSearch.HasArabicLetter(x) != ListSearch.HasArabicLetter(y))
                        {
                            continue;
                        }
                        if (!(ListSearch.HasArabicLetter(x) ? arabicFields : latinFields).Contains(field))
                        {
                            continue;
                        }
                        seenPairs.Add($"{x} {y}");
                        foreach (var cut in new[] { 2, 3 })
                        {
                            if (x.Length > cut && y.Length > cut)
                            {
                                seenPairs.Add($"{x[..cut]} {y[..cut]}");
                            }
                        }
                    }
                }
                foreach (var piece in seen)
                {
                    Count(pieces, piece, t);
                }
                foreach (var pair in seenPairs)
                {
                    Count(pairs, pair, t);
                }
                if (binding.InitialsColumn is { } column && Text(row, column) is { } named &&
                    Regex.Split(Lower(named).Trim(), "[ -]+") is { Length: >= 2 and <= ListSearch.MaxInitials } parts &&
                    parts.All(p => p.Length > 0 && char.IsLetter(p[0]) && !ListSearch.HasArabicLetter(p[..1])))
                {
                    Count(initialsSeen, string.Concat(parts.Select(p => p[0])), t);
                }
            }
        }
        // Broad pairs: two of the commonest short pieces of each script.
        foreach (var arabicPair in new[] { false, true })
        {
            var common = pieces.Where(p => p.Key.Length == 2 && p.Key.All(char.IsLetter) && ListSearch.HasArabicLetter(p.Key) == arabicPair)
                .OrderByDescending(p => Math.Min(p.Value[0], p.Value[1])).ThenBy(p => p.Key, StringComparer.Ordinal).Take(6).Select(p => p.Key).ToList();
            foreach (var x in common)
            {
                foreach (var y in common.Where(y => string.CompareOrdinal(x, y) < 0))
                {
                    pairs.TryAdd($"{x} {y}", [samples[0].Count, samples[1].Count]);
                }
            }
        }

        double Estimate(int[] counts, int t) => samples[t].Count == 0 ? 0 : (double)counts[t] * sizes[t] / samples[t].Count;
        var max = ListSearch.MaxRankedRows;
        var chosen = new List<BranchChoice>();
        var blind = new List<string>();
        foreach (var branch in BranchClasses(binding, arabicIn[0] && arabicIn[1], digitsIn[0] && digitsIn[1]))
        {
            // Pairs are estimated by their matches in the samples (a pair's own count says little).
            var candidates = new List<(string Search, double A, double B, int Rank)>();
            void Consider(IEnumerable<KeyValuePair<string, int[]>> source, int rank, bool matchSamples)
            {
                foreach (var (search, counts) in source)
                {
                    if (SearchBranch(search, binding.SearchesInitials) != branch.Shape)
                    {
                        continue;
                    }
                    var estimated = counts;
                    if (matchSamples)
                    {
                        if (SearchMatcher(binding, search) is not { } matches)
                        {
                            continue;
                        }
                        estimated = [samples[0].Count(matches), samples[1].Count(matches)];
                    }
                    candidates.Add((search, Estimate(estimated, 0), Estimate(estimated, 1), rank));
                }
            }
            if (branch.Shape.Contains("words", StringComparison.Ordinal))
            {
                // Only the commonest pairs are matched against the samples.
                Consider(pairs.OrderByDescending(p => Math.Min(p.Value[0], p.Value[1])).ThenBy(p => p.Key, StringComparer.Ordinal)
                    .Where(p => SearchBranch(p.Key, false) == branch.Shape).Take(40), 0, true);
            }
            else
            {
                Consider(initialsSeen, 0, false);
                Consider(pieces, 1, false);
            }
            var ordered = branch.Broad
                ? candidates.Where(c => c.A > 1.2 * max && c.B > 1.2 * max).OrderBy(c => Math.Min(c.A, c.B)).ThenBy(c => c.Search, StringComparer.Ordinal)
                : candidates.Where(c => c.A >= 1 && c.B >= 1 && c.A < 0.8 * max && c.B < 0.8 * max)
                    .OrderBy(c => c.Rank).ThenByDescending(c => Math.Min(c.A, c.B)).ThenBy(c => c.Search, StringComparer.Ordinal);
            BranchChoice? choice = null;
            foreach (var candidate in ordered.Take(ExactTries))
            {
                if (SearchMatcher(binding, candidate.Search) is not { } matches)
                {
                    continue;
                }
                var countA = a.Rows.Count(matches);
                var countB = b.Rows.Count(matches);
                var fits = branch.Broad ? countA > max && countB > max : countA is > 0 and <= ListSearch.MaxRankedRows && countB is > 0 and <= ListSearch.MaxRankedRows;
                if (fits && countA != countB)
                {
                    choice = new BranchChoice(branch, candidate.Search, countA, countB);
                    break;
                }
            }
            if (choice is not null)
            {
                chosen.Add(choice);
            }
            else if (branch.Required)
            {
                blind.Add($"list '{list.Key}': no search of the branch '{branch.Name}' had true answers that differ between the tenants" +
                          (binding.SearchesInitials && binding.InitialsColumn is null ? " (the list reads initials from a value no column shows, so initials searches cannot be judged)" : ""));
            }
        }
        return (chosen, blind);
    }

    private static void Count(Dictionary<string, int[]> counts, string key, int tenant)
    {
        if (!counts.TryGetValue(key, out var value))
        {
            counts[key] = value = new int[2];
        }
        value[tenant]++;
    }

    /// <summary>Every <see cref="SampleRows"/>-th part of the rows, evenly spread.</summary>
    private static List<JsonElement> Sample(List<JsonElement> rows)
    {
        var stride = Math.Max(1, rows.Count / SampleRows);
        return rows.Where((_, i) => i % stride == 0).ToList();
    }

    /// <summary>The runs of one kind of character in a value: Latin letters ('l'), Arabic letters
    /// ('a'), digits ('d'), and every other character but white space alone ('o').</summary>
    private static IEnumerable<(string Run, char Kind)> Runs(string text)
    {
        static char KindOf(char c) =>
            char.IsDigit(c) ? 'd' : char.IsLetter(c) ? ListSearch.HasArabicLetter(c.ToString()) ? 'a' : 'l'
            // Marks (an Arabic shadda or short vowel) end a run and are no search of their own.
            : char.IsWhiteSpace(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format ? ' ' : 'o';
        var start = 0;
        while (start < text.Length)
        {
            var kind = KindOf(text[start]);
            var end = start + 1;
            while (kind is not ('o' or ' ') && end < text.Length && KindOf(text[end]) == kind)
            {
                end++;
            }
            if (kind != ' ')
            {
                yield return (text[start..end], kind);
            }
            start = end;
        }
    }

    /// <summary>The queries of the chosen branch searches: each alone, and the first short word of
    /// each size of answer also with a filter, a sort and a grouping (each changes which code
    /// serves the count). Pages: the first, the last rows and, for a broad search, past the ranking limit.</summary>
    private static IEnumerable<Query> BranchQueries(IListBinding binding, IReadOnlyList<BranchChoice> chosen)
    {
        var list = binding.Definition;
        var sortKeys = ListAnswers.SortKeys(list);
        var filters = FiltersFor(list);
        var groupable = list.Columns.FirstOrDefault(c => c.Groupable);
        foreach (var choice in chosen)
        {
            var matches = SearchMatcher(binding, choice.Search)!;
            var search = Uri.EscapeDataString(choice.Search);
            yield return new Query("&search=" + search, "search", matches, true, choice.Search, null, choice.Class.Name, Few: true);
        }
        foreach (var broad in new[] { true, false })
        {
            if (chosen.FirstOrDefault(c => c.Class.Broad == broad && c.Class.Shape.EndsWith("word of 2 letters", StringComparison.Ordinal)) is not { } first)
            {
                continue;
            }
            var matches = SearchMatcher(binding, first.Search)!;
            var search = Uri.EscapeDataString(first.Search);
            var name = first.Class.Name;
            if (filters.Count > 0)
            {
                var (filter, filtered) = filters[0];
                yield return new Query($"&search={search}&filter={Uri.EscapeDataString(filter)}", "filtered search", r => matches(r) && filtered(r), true, first.Search, null, name + ", filtered", Few: true);
            }
            if (sortKeys.Count > 0)
            {
                yield return new Query($"&search={search}&sort={Uri.EscapeDataString(sortKeys[0])}", "sorted search", matches, false, first.Search, null, name + ", sorted", Few: true);
            }
            if (groupable is not null)
            {
                yield return new Query($"&search={search}&groupBy={groupable.Key}", "grouped search", matches, false, first.Search, groupable.Key, name + ", grouped", Few: true);
            }
        }
    }

    private static string Describe(BranchChoice choice) =>
        $"{choice.Class.Name}: '{choice.Search}' {choice.CountA.ToString(CultureInfo.InvariantCulture)}/{choice.CountB.ToString(CultureInfo.InvariantCulture)}";
}
