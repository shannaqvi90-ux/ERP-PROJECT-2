using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Erp.Modules.Reports.Pdf;

/// <summary>
/// A minimal PDF 1.7 writer for printed documents: pages with Flate-compressed content streams,
/// embedded TrueType fonts addressed by glyph id (Type0 / CIDFontType2, Identity-H) with only the
/// widths of the glyphs used, a ToUnicode map so text can be searched and copied, replacement text
/// (<c>/ActualText</c>) in logical order on right-to-left runs, the document language, and a
/// right-to-left reading direction for Arabic documents.
/// </summary>
public sealed class PdfWriter(string title, string language, bool rightToLeft)
{
    private readonly List<PdfPage> _pages = [];
    private readonly Dictionary<PdfFontFace, FontUse> _fonts = [];

    public PdfPage AddPage(decimal width, decimal height)
    {
        var page = new PdfPage(this, width, height);
        _pages.Add(page);
        return page;
    }

    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>What a glyph that draws no character of its own (a dot, a mark) extracts as.</summary>
    internal const string MarkText = "\u200B";

    internal string FontResource(PdfFontFace face, GlyphRun run)
    {
        if (!_fonts.TryGetValue(face, out var use))
        {
            use = new FontUse($"F{_fonts.Count + 1}");
            _fonts[face] = use;
        }
        for (var i = 0; i < run.Glyphs.Count; i++)
        {
            var glyph = run.Glyphs[i].Glyph;
            use.Widths[glyph] = face.Width(glyph);
            if (run.GlyphText[i].Length > 0)
            {
                // A letter's glyph maps to its characters (replacing a placeholder given while it was only seen as a mark).
                if (!use.Text.TryGetValue(glyph, out var known) || known == MarkText)
                {
                    use.Text[glyph] = run.GlyphText[i];
                }
            }
            else
            {
                // A mark or dot drawn on a letter stands for no character of its own: a zero-width
                // space for extractors that read glyphs, an artifact for those that read structure.
                use.Text.TryAdd(glyph, MarkText);
            }
        }
        return use.Name;
    }

    public byte[] Write(DateTimeOffset createdAt)
    {
        var output = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Raw(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        int Reserve()
        {
            offsets.Add(-1);
            return offsets.Count - 1;
        }
        void Begin(int id)
        {
            offsets[id] = output.Position;
            Raw($"{id} 0 obj\n");
        }
        void Object(int id, string body)
        {
            Begin(id);
            Raw(body);
            Raw("\nendobj\n");
        }
        void Stream(int id, byte[] data, string extra = "")
        {
            var compressed = Deflate(data);
            Begin(id);
            Raw($"<< /Length {compressed.Length} /Filter /FlateDecode{extra} >>\nstream\n");
            output.Write(compressed);
            Raw("\nendstream\nendobj\n");
        }

        Raw("%PDF-1.7m\n%\u00E2\u00E3\u00CF\u00D3\n");
        var catalog = Reserve();
        var pages = Reserve();
        var info = Reserve();
        var fontIds = _fonts.ToDictionary(f => f.Key, _ => Reserve());
        var pageIds = new List<int>();
        foreach (var page in _pages)
        {
            var contentId = Reserve();
            var pageId = Reserve();
            pageIds.Add(pageId);
            Stream(contentId, Encoding.Latin1.GetBytes(page.Content.ToString()));
            var fonts = string.Join(" ", _fonts.Select(f => $"/{f.Value.Name} {fontIds[f.Key]} 0 R"));
            Object(pageId, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {N(page.Width)} {N(page.Height)}] " +
                           $"/Resources << /Font << {fonts} >> /ProcSet [/PDF /Text] >> /Contents {contentId} 0 R >>");
        }
        foreach (var (face, use) in _fonts)
        {
            var descendant = Reserve();
            var descriptor = Reserve();
            var file = Reserve();
            var toUnicode = Reserve();
            var widths = new StringBuilder();
            foreach (var (glyph, width) in use.Widths.OrderBy(w => w.Key))
            {
                widths.Append(glyph).Append(" [").Append(width).Append("] ");
            }
            Object(fontIds[face], $"<< /Type /Font /Subtype /Type0 /BaseFont /{face.Name} /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>");
            Object(descendant, $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{face.Name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                               $"/FontDescriptor {descriptor} 0 R /DW 0 /W [{widths}] /CIDToGIDMap /Identity >>");
            var box = face.BoundingBox;
            Object(descriptor, $"<< /Type /FontDescriptor /FontName /{face.Name} /Flags 32 /FontBBox [{box[0]} {box[1]} {box[2]} {box[3]}] /ItalicAngle 0 " +
                               $"/Ascent {face.Ascent} /Descent {face.Descent} /CapHeight {face.CapHeight} /StemV {(face.Bold ? 120 : 80)} /FontWeight {(face.Bold ? 700 : 400)} /FontFile2 {file} 0 R >>");
            Stream(file, face.Sfnt, $" /Length1 {face.Sfnt.Length}");
            Stream(toUnicode, Encoding.Latin1.GetBytes(ToUnicodeMap(use.Text)));
        }
        Object(pages, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(p => $"{p} 0 R"))}] /Count {pageIds.Count} >>");
        Object(catalog, $"<< /Type /Catalog /Pages {pages} 0 R /Lang {Text(language)} /ViewerPreferences << /DisplayDocTitle true{(rightToLeft ? " /Direction /R2L" : "")} >> >>");
        Object(info, $"<< /Title {Text(title)} /Producer (ERP platform reports) /CreationDate {Text(PdfDate(createdAt))} >>");

        var xref = output.Position;
        Raw($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            Raw($"{offset:D10} 00000 n \n");
        }
        Raw($"trailer\n<< /Size {offsets.Count} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>A text string: UTF-16BE with a byte-order mark, as a hex string.</summary>
    public static string Text(string value) => "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value)) + ">";

    public static string N(decimal value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string PdfDate(DateTimeOffset at) => "D:" + at.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }
        return buffer.ToArray();
    }

    private static string ToUnicodeMap(IReadOnlyDictionary<uint, string> text)
    {
        var builder = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n" +
            "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in text.OrderBy(t => t.Key).Chunk(100))
        {
            builder.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (glyph, chars) in chunk)
            {
                builder.Append('<').Append(glyph.ToString("X4", CultureInfo.InvariantCulture)).Append("> <")
                    .Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(chars))).Append(">\n");
            }
            builder.Append("endbfchar\n");
        }
        builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return builder.ToString();
    }

    private sealed class FontUse(string name)
    {
        public string Name { get; } = name;
        public SortedDictionary<uint, int> Widths { get; } = [];
        public Dictionary<uint, string> Text { get; } = [];
    }
}

/// <summary>One page: drawing operations in PDF user space (points, origin bottom left).</summary>
public sealed class PdfPage
{
    private readonly PdfWriter _writer;

    internal PdfPage(PdfWriter writer, decimal width, decimal height)
    {
        _writer = writer;
        Width = width;
        Height = height;
    }

    public decimal Width { get; }
    public decimal Height { get; }
    internal StringBuilder Content { get; } = new();

    public void FillRectangle(decimal x, decimal y, decimal width, decimal height, decimal gray)
    {
        Content.Append($"q {PdfWriter.N(gray)} g {PdfWriter.N(x)} {PdfWriter.N(y)} {PdfWriter.N(width)} {PdfWriter.N(height)} re f Q\n");
    }

    public void Line(decimal x1, decimal y1, decimal x2, decimal y2, decimal gray, decimal thickness)
    {
        Content.Append($"q {PdfWriter.N(gray)} G {PdfWriter.N(thickness)} w {PdfWriter.N(x1)} {PdfWriter.N(y1)} m {PdfWriter.N(x2)} {PdfWriter.N(y2)} l S Q\n");
    }

    /// <summary>Draws a shaped line with its left edge at <paramref name="x"/> and baseline at <paramref name="y"/>.</summary>
    public void Text(ShapedLine line, decimal x, decimal y, decimal gray = 0)
    {
        var pen = x;
        foreach (var run in line.Runs)
        {
            var font = _writer.FontResource(run.Face, run);
            var scale = line.Size / 1000.0m;
            if (run.RightToLeft)
            {
                Content.Append($"/Span << /ActualText {PdfWriter.Text(run.Text)} >> BDC\n");
            }
            Content.Append($"BT {PdfWriter.N(gray)} g /{font} {PdfWriter.N(line.Size)} Tf\n");
            var segment = new StringBuilder();
            var segmentX = pen;
            void Flush()
            {
                if (segment.Length > 0)
                {
                    Content.Append($"1 0 0 1 {PdfWriter.N(segmentX)} {PdfWriter.N(y)} Tm [{segment}] TJ\n");
                    segment.Clear();
                }
            }
            for (var i = 0; i < run.Glyphs.Count; i++)
            {
                var glyph = run.Glyphs[i];
                var mark = run.GlyphText[i].Length == 0;
                if (glyph.OffsetX != 0 || glyph.OffsetY != 0 || mark)
                {
                    Flush();
                    Content.Append(mark ? "/Artifact BMC " : "")
                        .Append($"1 0 0 1 {PdfWriter.N(pen + glyph.OffsetX * scale)} {PdfWriter.N(y + glyph.OffsetY * scale)} Tm <{glyph.Glyph:X4}> Tj")
                        .Append(mark ? " EMC\n" : "\n");
                    pen += glyph.Advance * scale;
                    segmentX = pen;
                    continue;
                }
                if (segment.Length == 0)
                {
                    segmentX = pen;
                }
                segment.Append('<').Append(glyph.Glyph.ToString("X4", CultureInfo.InvariantCulture)).Append('>');
                var adjust = run.Face.Width(glyph.Glyph) - glyph.Advance;
                if (adjust != 0)
                {
                    segment.Append(' ').Append(adjust.ToString(CultureInfo.InvariantCulture)).Append(' ');
                }
                pen += glyph.Advance * scale;
            }
            Flush();
            Content.Append("ET\n");
            if (run.RightToLeft)
            {
                Content.Append("EMC\n");
            }
        }
    }
}
