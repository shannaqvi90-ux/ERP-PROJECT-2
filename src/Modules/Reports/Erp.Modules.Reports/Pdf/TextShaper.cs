using System.Text;
using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace Erp.Modules.Reports.Pdf;

/// <summary>A shaped glyph: its id in the face, advance and offsets in thousandths of an em.</summary>
public readonly record struct ShapedGlyph(uint Glyph, int Advance, int OffsetX, int OffsetY);

/// <summary>Glyphs of one face in visual order, the characters they draw (logical order) and
/// which glyph draws which characters (for copy and search in the PDF).</summary>
public sealed record GlyphRun(PdfFontFace Face, IReadOnlyList<ShapedGlyph> Glyphs, string Text, bool RightToLeft, IReadOnlyList<string> GlyphText)
{
    public int Advance => Glyphs.Sum(g => g.Advance);
}

/// <summary>One line of text, shaped and ordered for drawing left to right.</summary>
public sealed record ShapedLine(IReadOnlyList<GlyphRun> Runs, decimal Size)
{
    /// <summary>Width in points.</summary>
    public decimal Width => Runs.Sum(r => r.Advance) * Size / 1000.0m;

    public bool IsEmpty => Runs.Count == 0;
}

/// <summary>
/// Lays a string out for the PDF: bidirectional ordering (<see cref="Bidi"/>), a face per
/// character (<see cref="PdfFonts"/>), HarfBuzz shaping per run (joining forms, lam-alef, marks,
/// kerning), and line breaking at spaces within a width. A paragraph's direction is that of its
/// first strong character (an English name stays left to right inside an Arabic document), else
/// the document's.
/// One shaper lays out one document (it is not shared between requests or threads): a line it
/// has shaped once is kept for the rest of that document, since line breaking asks for the same
/// text many times (decision p03-identity-gate-processor-time).
/// </summary>
public sealed class TextShaper(PdfFonts fonts)
{
    private readonly Dictionary<(string Text, decimal Size, int Scale, bool Bold, bool Rtl), ShapedLine> _shaped = new();

    public ShapedLine Shape(string text, decimal size, bool bold, bool documentRightToLeft)
    {
        var key = (text, size, size.Scale, bold, documentRightToLeft);
        if (!_shaped.TryGetValue(key, out var line))
        {
            line = ShapeUncached(text, size, bold, documentRightToLeft);
            _shaped[key] = line;
        }
        return line;
    }

    private ShapedLine ShapeUncached(string text, decimal size, bool bold, bool documentRightToLeft)
    {
        text = Clean(text);
        var rtl = Bidi.FirstStrongIsRightToLeft(text) ?? documentRightToLeft;
        var runs = new List<GlyphRun>();
        foreach (var run in Bidi.VisualRuns(text, rtl))
        {
            var segments = Segments(text, run.Start, run.Length, bold);
            if (run.RightToLeft)
            {
                segments.Reverse();
            }
            foreach (var (face, start, length) in segments)
            {
                var shaped = ShapeRun(face, text, start, length, run.RightToLeft);
                if (shaped.Glyphs.Count > 0)
                {
                    runs.Add(shaped);
                }
            }
        }
        return new ShapedLine(runs, size);
    }

    /// <summary>The text broken into lines no wider than <paramref name="width"/> points, at spaces
    /// (inside a word only when the word alone is wider). Explicit line breaks are kept. At most
    /// <paramref name="maxLines"/> lines: the rest of the text is not laid out at all, so the work
    /// is bounded by the lines kept, whatever the text's length (a cell holding thousands of
    /// characters without a space once kept a report request busy for good).</summary>
    public IReadOnlyList<ShapedLine> Wrap(string text, decimal size, bool bold, bool documentRightToLeft, decimal width, int maxLines = 50)
    {
        var lines = new List<ShapedLine>();
        foreach (var paragraph in (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (lines.Count >= maxLines)
            {
                break;
            }
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (lines.Count >= maxLines)
                {
                    break;
                }
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Shape(candidate, size, bold, documentRightToLeft).Width <= width || current.Length == 0 && Fits(word, size, bold, documentRightToLeft, width))
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                {
                    lines.Add(Shape(current, size, bold, documentRightToLeft));
                    current = "";
                }
                // A word wider than the line is cut where it no longer fits.
                var rest = word;
                while (rest.Length > 0 && lines.Count < maxLines)
                {
                    var cut = FittingPrefix(rest, size, bold, documentRightToLeft, width);
                    if (cut == rest.Length)
                    {
                        break;
                    }
                    lines.Add(Shape(rest[..cut], size, bold, documentRightToLeft));
                    rest = rest[cut..];
                }
                current = rest;
            }
            if (lines.Count < maxLines)
            {
                lines.Add(Shape(current, size, bold, documentRightToLeft));
            }
        }
        return lines;
    }

    /// <summary>The length of the longest start of <paramref name="text"/> that fits the width (the
    /// whole text when it fits): doubling the length until it no longer fits, then halving between,
    /// so only starts about a line long are ever shaped, however long the text. At least one
    /// character (a character wider than the line still makes progress), never half of a
    /// surrogate pair.</summary>
    private int FittingPrefix(string text, decimal size, bool bold, bool rtl, decimal width)
    {
        var probe = 1;
        while (probe < text.Length && Fits(text[..probe], size, bold, rtl, width))
        {
            probe *= 2;
        }
        if (probe >= text.Length && Fits(text, size, bold, rtl, width))
        {
            return text.Length;
        }
        int low = Math.Max(1, probe / 2), high = Math.Min(probe, text.Length) - 1, best = 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Fits(text[..middle], size, bold, rtl, width))
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        if (best < text.Length && char.IsHighSurrogate(text[best - 1]))
        {
            best = best > 1 ? best - 1 : best + 1;
        }
        return Math.Min(best, text.Length);
    }

    private bool Fits(string text, decimal size, bool bold, bool rtl, decimal width) => Shape(text, size, bold, rtl).Width <= width;

    /// <summary>Consecutive characters of one face (logical order) within a run. Spaces, digits
    /// and punctuation stay with the face of the text around them when that face has them.</summary>
    private List<(PdfFontFace Face, int Start, int Length)> Segments(string text, int start, int length, bool bold)
    {
        var segments = new List<(PdfFontFace Face, int Start, int Length)>();
        PdfFontFace? current = null;
        var segmentStart = start;
        for (var i = start; i < start + length; i++)
        {
            int codepoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < start + length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
            }
            var face = fonts.For(codepoint, bold, current) ?? current ?? fonts.Fallback(bold);
            if (current is null)
            {
                current = face;
            }
            else if (face != current)
            {
                segments.Add((current, segmentStart, i - segmentStart));
                current = face;
                segmentStart = i;
            }
            if (codepoint > 0xFFFF)
            {
                i++;
            }
        }
        if (current is not null)
        {
            segments.Add((current, segmentStart, start + length - segmentStart));
        }
        return segments;
    }

    private static GlyphRun ShapeRun(PdfFontFace face, string text, int start, int length, bool rtl)
    {
        var slice = text.Substring(start, length);
        using var buffer = new Buffer();
        buffer.AddUtf16(slice);
        buffer.Direction = rtl ? Direction.RightToLeft : Direction.LeftToRight;
        buffer.Script = HasArabicScript(slice) ? Script.Arabic : Script.Latin;
        buffer.Language = new Language(rtl ? "ar" : "en");
        buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
        face.Font.Shape(buffer);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var glyphs = new List<ShapedGlyph>(infos.Length);
        var glyphText = new List<string>(infos.Length);
        // Clusters in glyph (visual) order; each cluster's characters span to the next cluster start.
        var clusterStarts = infos.Select(i => (int)i.Cluster).Distinct().Order().ToList();
        // Each cluster's end, looked up once (searching the list per glyph made a long run quadratic).
        var clusterEnd = new Dictionary<int, int>(clusterStarts.Count);
        for (var c = 0; c < clusterStarts.Count; c++)
        {
            clusterEnd[clusterStarts[c]] = c + 1 < clusterStarts.Count ? clusterStarts[c + 1] : slice.Length;
        }
        // The characters of a cluster belong to its widest glyph (the letter); the other glyphs of
        // the cluster are marks and dots drawn on it (Noto Sans Arabic draws dots as separate glyphs).
        var owner = new Dictionary<int, int>();
        for (var g = 0; g < infos.Length; g++)
        {
            var cluster = (int)infos[g].Cluster;
            if (!owner.TryGetValue(cluster, out var current) || positions[g].XAdvance > positions[current].XAdvance)
            {
                owner[cluster] = g;
            }
        }
        for (var g = 0; g < infos.Length; g++)
        {
            var cluster = (int)infos[g].Cluster;
            var chars = slice[cluster..clusterEnd[cluster]];
            glyphs.Add(new ShapedGlyph(infos[g].Codepoint, face.Scale(positions[g].XAdvance), face.Scale(positions[g].XOffset), face.Scale(positions[g].YOffset)));
            glyphText.Add(owner[cluster] == g ? StripControls(chars) : "");
        }
        return new GlyphRun(face, glyphs, StripControls(slice), rtl, glyphText);
    }

    private static bool HasArabicScript(string text)
    {
        foreach (var c in text)
        {
            if (Bidi.IsArabicScript(c))
            {
                return true;
            }
        }
        return false;
    }

    private static string StripControls(string text)
    {
        if (!text.Any(Bidi.IsInvisibleControl))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Where(c => !Bidi.IsInvisibleControl(c)))
        {
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>Tabs as spaces, other control characters dropped (a PDF line draws text only).</summary>
    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c == '\t')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
