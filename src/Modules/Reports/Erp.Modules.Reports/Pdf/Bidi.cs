namespace Erp.Modules.Reports.Pdf;

/// <summary>A run of text at one embedding level, in visual order, as a range of the logical string.</summary>
/// <param name="Start">First character (logical index).</param>
/// <param name="Length">Characters in the run.</param>
/// <param name="RightToLeft">The run is laid out right to left (odd level).</param>
public readonly record struct BidiRun(int Start, int Length, bool RightToLeft);

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX #9) for one paragraph of a printed document, without
/// explicit embeddings: character types, the weak-type rules W1–W7, neutrals N1–N2, implicit
/// levels I1–I2, trailing white space L1 and reordering L2. Mixed Arabic and English (a company
/// name in English inside an Arabic document, an e-mail, an amount with its currency) comes out in
/// the order a reader expects, as browsers lay it out on screen. Mirroring of brackets in
/// right-to-left runs is left to the shaper (HarfBuzz mirrors in right-to-left buffers).
/// </summary>
public static partial class Bidi
{
    private enum T : byte { L, R, AL, EN, ES, ET, AN, CS, NSM, BN, B, S, WS, ON }

    /// <summary>True for characters of the Arabic script blocks.</summary>
    public static bool IsArabicScript(int c) =>
        c is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x0870 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF;

    /// <summary>Characters that only steer the layout and are never drawn (marks, joiners, isolates).</summary>
    public static bool IsInvisibleControl(char c) =>
        c is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u061C' or '\uFEFF' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    /// <summary>The paragraph direction from its first strong character, or null when it has none.</summary>
    public static bool? FirstStrongIsRightToLeft(string text)
    {
        foreach (var c in text)
        {
            switch (TypeOf(c))
            {
                case T.L:
                    return false;
                case T.R or T.AL:
                    return true;
            }
        }
        return null;
    }

    /// <summary>The runs of a paragraph in visual order (left to right on the page).</summary>
    public static IReadOnlyList<BidiRun> VisualRuns(string text, bool rightToLeft)
    {
        if (text.Length == 0)
        {
            return [];
        }
        var levels = Levels(text, rightToLeft);
        // L2: reverse every sequence at or above each odd level, highest level first.
        var order = Enumerable.Range(0, text.Length).ToArray();
        var max = levels.Max();
        var minOdd = levels.Where(l => l % 2 == 1).DefaultIfEmpty((byte)(max + 1)).Min();
        for (var level = max; level >= minOdd && level > 0; level--)
        {
            for (var i = 0; i < order.Length;)
            {
                if (levels[order[i]] < level)
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < order.Length && levels[order[i]] >= level)
                {
                    i++;
                }
                Array.Reverse(order, start, i - start);
            }
        }
        // Group the visual sequence into runs of one level over consecutive logical characters.
        var runs = new List<BidiRun>();
        var at = 0;
        while (at < order.Length)
        {
            var level = levels[order[at]];
            var rtl = level % 2 == 1;
            var first = order[at];
            var length = 1;
            while (at + length < order.Length && levels[order[at + length]] == level
                   && order[at + length] == (rtl ? first - length : first + length))
            {
                length++;
            }
            runs.Add(new BidiRun(rtl ? first - length + 1 : first, length, rtl));
            at += length;
        }
        return runs;
    }

    /// <summary>The resolved embedding level of every character.</summary>
    public static byte[] Levels(string text, bool rightToLeft)
    {
        var n = text.Length;
        var paragraph = rightToLeft ? (byte)1 : (byte)0;
        var sos = rightToLeft ? T.R : T.L;
        var types = new T[n];
        for (var i = 0; i < n; i++)
        {
            types[i] = TypeOf(text[i]);
        }
        // A telephone number ("+971 2 555 7810", "04-555 7810") reads left to right as one unit
        // in any paragraph, as the screen shows it: the algorithm alone would order its digit
        // groups right to left in an Arabic line ("7810 555 2 971+").
        foreach (System.Text.RegularExpressions.Match match in PhoneRegex().Matches(text))
        {
            if (match.Value.Count(char.IsDigit) >= 7)
            {
                for (var i = match.Index; i < match.Index + match.Length; i++)
                {
                    types[i] = T.L;
                }
            }
        }
        var original = (T[])types.Clone();

        // X9 (simplified): boundary neutrals take the type of the character before them.
        // W1: a non-spacing mark takes the type of the character before it.
        for (var i = 0; i < n; i++)
        {
            if (types[i] is T.NSM or T.BN)
            {
                types[i] = i == 0 ? sos : types[i - 1];
            }
        }
        // W2: European digits after Arabic letters are Arabic numbers.
        var lastStrong = sos;
        for (var i = 0; i < n; i++)
        {
            if (types[i] is T.L or T.R or T.AL)
            {
                lastStrong = types[i];
            }
            else if (types[i] == T.EN && lastStrong == T.AL)
            {
                types[i] = T.AN;
            }
        }
        // W3: Arabic letters are right to left.
        for (var i = 0; i < n; i++)
        {
            if (types[i] == T.AL)
            {
                types[i] = T.R;
            }
        }
        // W4: one separator between two numbers of the same kind joins them.
        for (var i = 1; i < n - 1; i++)
        {
            if (types[i] == T.ES && types[i - 1] == T.EN && types[i + 1] == T.EN)
            {
                types[i] = T.EN;
            }
            else if (types[i] == T.CS && types[i - 1] == T.EN && types[i + 1] == T.EN)
            {
                types[i] = T.EN;
            }
            else if (types[i] == T.CS && types[i - 1] == T.AN && types[i + 1] == T.AN)
            {
                types[i] = T.AN;
            }
        }
        // W5: terminators next to European numbers are part of them.
        for (var i = 0; i < n; i++)
        {
            if (types[i] != T.ET)
            {
                continue;
            }
            var end = i;
            while (end < n && types[end] == T.ET)
            {
                end++;
            }
            var touches = (i > 0 && types[i - 1] == T.EN) || (end < n && types[end] == T.EN);
            for (var j = i; j < end; j++)
            {
                if (touches)
                {
                    types[j] = T.EN;
                }
            }
            i = end - 1;
        }
        // W6: remaining separators and terminators are neutral.
        for (var i = 0; i < n; i++)
        {
            if (types[i] is T.ES or T.ET or T.CS)
            {
                types[i] = T.ON;
            }
        }
        // W7: European numbers after left-to-right text are left to right.
        lastStrong = sos;
        for (var i = 0; i < n; i++)
        {
            if (types[i] is T.L or T.R)
            {
                lastStrong = types[i];
            }
            else if (types[i] == T.EN && lastStrong == T.L)
            {
                types[i] = T.L;
            }
        }
        // N1, N2: neutrals between two strong types of one direction take it (numbers count as
        // right to left), otherwise the paragraph's direction.
        for (var i = 0; i < n; i++)
        {
            if (types[i] is not (T.ON or T.WS or T.S or T.B))
            {
                continue;
            }
            var end = i;
            while (end < n && types[end] is T.ON or T.WS or T.S or T.B)
            {
                end++;
            }
            var before = i == 0 ? sos : Strong(types[i - 1]);
            var after = end == n ? sos : Strong(types[end]);
            var resolved = before == after ? before : sos;
            for (var j = i; j < end; j++)
            {
                types[j] = resolved;
            }
            i = end - 1;
        }
        // I1, I2: implicit levels.
        var levels = new byte[n];
        for (var i = 0; i < n; i++)
        {
            levels[i] = (paragraph % 2, types[i]) switch
            {
                (0, T.R) => (byte)(paragraph + 1),
                (0, T.AN or T.EN) => (byte)(paragraph + 2),
                (1, T.L or T.EN or T.AN) => (byte)(paragraph + 1),
                _ => paragraph,
            };
        }
        // L1: white space at the end of the line goes back to the paragraph level.
        for (var i = n - 1; i >= 0 && original[i] is T.WS or T.BN or T.S or T.B; i--)
        {
            levels[i] = paragraph;
        }
        return levels;
    }

    /// <summary>Digit groups joined by spaces, hyphens or brackets, with an optional leading plus:
    /// not dates (slashes, colons) nor amounts (commas, points).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\p{L}\p{N}])\+?\(?\d[\d \-()]*[ \-][\d \-()]*\d(?![\p{L}\p{N}])")]
    private static partial System.Text.RegularExpressions.Regex PhoneRegex();

    private static T Strong(T type) => type is T.EN or T.AN ? T.R : type;

    private static T TypeOf(char c)
    {
        switch (c)
        {
            case >= '0' and <= '9':
                return T.EN;
            case '+' or '-' or '\u2212':
                return T.ES;
            case '#' or '$' or '%' or '\u00A2' or '\u00A3' or '\u00A4' or '\u00A5' or '\u00B0' or '\u00B1' or '\u066A' or '\u20AC':
                return T.ET;
            case ',' or '.' or '/' or ':' or '\u00A0' or '\u060C' or '\u202F':
                return T.CS;
            case '\n' or '\r' or '\u2029':
                return T.B;
            case '\t':
                return T.S;
            case ' ' or (>= '\u2000' and <= '\u200A') or '\u2028':
                return T.WS;
            case '\u200E':
                return T.L;
            case '\u200F':
                return T.R;
            case '\u061C':
                return T.AL;
            case '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or (>= '\u2060' and <= '\u2064') or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'):
                return T.BN;
            case >= '\u0660' and <= '\u0669' or '\u066B' or '\u066C':
                return T.AN;
            case >= '\u06F0' and <= '\u06F9':
                return T.EN;
            case >= '\u0300' and <= '\u036F' or >= '\u0610' and <= '\u061A' or >= '\u064B' and <= '\u065F' or '\u0670'
                or >= '\u06D6' and <= '\u06DC' or >= '\u06DF' and <= '\u06E4' or '\u06E7' or '\u06E8' or >= '\u06EA' and <= '\u06ED':
                return T.NSM;
            case >= '\u0590' and <= '\u05FF' or >= '\u07C0' and <= '\u085F' or >= '\uFB1D' and <= '\uFB4F':
                return T.R;
        }
        if (IsArabicScript(c) || c is >= '\u0780' and <= '\u07BF')
        {
            return T.AL;
        }
        if (char.IsLetter(c) || char.IsDigit(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LetterNumber)
        {
            return T.L;
        }
        return char.IsSurrogate(c) ? T.L : T.ON;
    }
}
