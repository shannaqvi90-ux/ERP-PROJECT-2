using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Erp.Modules.Reports.Pdf;

/// <summary>
/// A minimal PDF 1.7 writer for printed documents: pages with Flate-compressed content streams,
/// embedded TrueType fonts (Type0 / CIDFontType2, Identity-H) cut down to the glyphs used, with
/// only their widths, a ToUnicode map so text can be searched and copied, replacement text (<c>/ActualText</c>)
/// in logical order on right-to-left runs, the document language, and a right-to-left reading
/// direction for Arabic documents.
/// <para>Text is addressed by character id, not glyph id: every distinct pair of a glyph and the
/// characters it stands for gets its own id, and a <c>/CIDToGIDMap</c> maps ids to glyphs. Arabic
/// letters share glyphs (س and ش, ر and ز, medial ب and ن differ only by dots drawn as marks), so a
/// map from glyph to text would give every such letter the text of whichever came first, and copy,
/// search and screen readers would read الإشم for الاسم (critic p06 round 1).</para>
/// </summary>
public sealed class PdfWriter
{
    private readonly List<PdfPage> _pages = [];
    private readonly Dictionary<PdfFontFace, FontUse> _fonts = [];
    private readonly string _title;
    private readonly string _language;
    private readonly bool _rightToLeft;
    private readonly Stream _output;
    private readonly List<long> _offsets = [0];
    private long _position;
    private bool _started;
    private int _catalog;
    private int _pagesId;
    private int _info;

    /// <summary>A writer that sends each page's drawing to <paramref name="output"/> as soon as the
    /// next page starts (<see cref="AddPage"/>), so a document of thousands of pages never holds
    /// more than one page's drawing operations in memory; fonts, page objects and the cross-reference
    /// table follow in <see cref="Finish"/>. The output need not be seekable.</summary>
    public PdfWriter(string title, string language, bool rightToLeft, Stream output)
    {
        _title = title;
        _language = language;
        _rightToLeft = rightToLeft;
        _output = output;
    }

    /// <summary>A writer that keeps the document in memory; <see cref="Write"/> returns its bytes.</summary>
    public PdfWriter(string title, string language, bool rightToLeft) : this(title, language, rightToLeft, new MemoryStream())
    {
    }

    /// <summary>A page. The page before it is finished: its drawing is compressed and written out,
    /// and whatever is drawn on it later (a footer that needs the page count) goes into a second,
    /// small content stream of its own.</summary>
    public PdfPage AddPage(decimal width, decimal height)
    {
        if (_pages.Count > 0)
        {
            Flush(_pages[^1]);
        }
        var page = new PdfPage(this, width, height);
        _pages.Add(page);
        return page;
    }

    public IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>What a glyph that draws no character of its own (a dot, a mark) extracts as.</summary>
    internal const string MarkText = "\u200B";

    /// <summary>The font resource for a run and, per glyph, the character id that draws it.</summary>
    internal (string Name, ushort[] Cids) FontResource(PdfFontFace face, GlyphRun run)
    {
        if (!_fonts.TryGetValue(face, out var use))
        {
            use = new FontUse($"F{_fonts.Count + 1}");
            _fonts[face] = use;
        }
        var cids = new ushort[run.Glyphs.Count];
        for (var i = 0; i < run.Glyphs.Count; i++)
        {
            var glyph = run.Glyphs[i].Glyph;
            // A mark or dot drawn on a letter stands for no character of its own: a zero-width
            // space for extractors that read glyphs, an artifact for those that read structure.
            var text = run.GlyphText[i].Length > 0 ? run.GlyphText[i] : MarkText;
            cids[i] = use.Cid(glyph, text, face.Width(glyph));
        }
        return (use.Name, cids);
    }

    private void Raw(string text) => Bytes(Encoding.Latin1.GetBytes(text));

    private void Bytes(ReadOnlySpan<byte> data)
    {
        _output.Write(data);
        _position += data.Length;
    }

    private int Reserve()
    {
        _offsets.Add(-1);
        return _offsets.Count - 1;
    }

    private void Begin(int id)
    {
        _offsets[id] = _position;
        Raw($"{id} 0 obj\n");
    }

    private void Object(int id, string body)
    {
        Begin(id);
        Raw(body);
        Raw("\nendobj\n");
    }

    private void Stream(int id, byte[] data, string extra = "")
    {
        var compressed = Deflate(data);
        Begin(id);
        Raw($"<< /Length {compressed.Length} /Filter /FlateDecode{extra} >>\nstream\n");
        Bytes(compressed);
        Raw("\nendstream\nendobj\n");
    }

    private void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        Raw("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");
        _catalog = Reserve();
        _pagesId = Reserve();
        _info = Reserve();
    }

    /// <summary>Writes out what has been drawn on a page since its last flush, as a content stream of its own.</summary>
    internal void Flush(PdfPage page)
    {
        Start();
        if (page.Content.Length == 0)
        {
            return;
        }
        var id = Reserve();
        Stream(id, Encoding.Latin1.GetBytes(page.Content.ToString()));
        page.Content.Clear();
        page.ContentIds.Add(id);
    }

    /// <summary>The whole document in memory (tests, small documents).</summary>
    public byte[] Write(DateTimeOffset createdAt)
    {
        if (_output is not MemoryStream memory)
        {
            throw new InvalidOperationException("Write returns the bytes of a writer that writes to memory; call Finish for another output.");
        }
        Finish(createdAt);
        return memory.ToArray();
    }

    /// <summary>Writes the rest of the document: what is still drawn on any page, the fonts cut down
    /// to the glyphs drawn, the page objects, the catalogue and the cross-reference table.</summary>
    public void Finish(DateTimeOffset createdAt)
    {
        Start();
        foreach (var page in _pages)
        {
            Flush(page);
        }
        var fontIds = _fonts.ToDictionary(f => f.Key, _ => Reserve());
        var fontResources = string.Join(" ", _fonts.Select(f => $"/{f.Value.Name} {fontIds[f.Key]} 0 R"));
        var pageIds = new List<int>(_pages.Count);
        foreach (var page in _pages)
        {
            var pageId = Reserve();
            pageIds.Add(pageId);
            var contents = page.ContentIds.Count == 1 ? $"{page.ContentIds[0]} 0 R" : $"[{string.Join(" ", page.ContentIds.Select(c => $"{c} 0 R"))}]";
            Object(pageId, $"<< /Type /Page /Parent {_pagesId} 0 R /MediaBox [0 0 {N(page.Width)} {N(page.Height)}] " +
                           $"/Resources << /Font << {fontResources} >> /ProcSet [/PDF /Text] >> /Contents {contents} >>");
        }
        foreach (var (face, use) in _fonts)
        {
            var descendant = Reserve();
            var descriptor = Reserve();
            var file = Reserve();
            var toUnicode = Reserve();
            var cidToGid = Reserve();
            var widths = new StringBuilder();
            for (var cid = 1; cid < use.Glyphs.Count; cid++)
            {
                widths.Append(cid).Append(" [").Append(use.Widths[cid]).Append("] ");
            }
            // Only the glyphs this document draws are embedded (a subset, named with its tag).
            var subset = FontSubset.Of(face.Sfnt, use.Glyphs);
            var baseFont = $"{FontSubset.Tag(face.Name, use.Glyphs)}+{face.Name}";
            Object(fontIds[face], $"<< /Type /Font /Subtype /Type0 /BaseFont /{baseFont} /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>");
            Object(descendant, $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{baseFont} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                               $"/FontDescriptor {descriptor} 0 R /DW 0 /W [{widths}] /CIDToGIDMap {cidToGid} 0 R >>");
            var map = new byte[use.Glyphs.Count * 2];
            for (var cid = 0; cid < use.Glyphs.Count; cid++)
            {
                map[cid * 2] = (byte)(use.Glyphs[cid] >> 8);
                map[cid * 2 + 1] = (byte)(use.Glyphs[cid] & 0xFF);
            }
            Stream(cidToGid, map);
            var box = face.BoundingBox;
            Object(descriptor, $"<< /Type /FontDescriptor /FontName /{baseFont} /Flags 32 /FontBBox [{box[0]} {box[1]} {box[2]} {box[3]}] /ItalicAngle 0 " +
                               $"/Ascent {face.Ascent} /Descent {face.Descent} /CapHeight {face.CapHeight} /StemV {(face.Bold ? 120 : 80)} /FontWeight {(face.Bold ? 700 : 400)} /FontFile2 {file} 0 R >>");
            Stream(file, subset, $" /Length1 {subset.Length}");
            Stream(toUnicode, Encoding.Latin1.GetBytes(ToUnicodeMap(use.Texts)));
        }
        Object(_pagesId, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(p => $"{p} 0 R"))}] /Count {pageIds.Count} >>");
        Object(_catalog, $"<< /Type /Catalog /Pages {_pagesId} 0 R /Lang {Text(_language)} /ViewerPreferences << /DisplayDocTitle true{(_rightToLeft ? " /Direction /R2L" : "")} >> >>");
        Object(_info, $"<< /Title {Text(_title)} /Producer (ERP platform reports) /CreationDate {Text(PdfDate(createdAt))} >>");

        var xref = _position;
        var table = new StringBuilder($"xref\n0 {_offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in _offsets.Skip(1))
        {
            table.Append($"{offset:D10} 00000 n \n");
        }
        table.Append($"trailer\n<< /Size {_offsets.Count} /Root {_catalog} 0 R /Info {_info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(table.ToString());
        _output.Flush();
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

    private static string ToUnicodeMap(IReadOnlyList<string> texts)
    {
        var text = texts.Select((t, cid) => (Cid: cid, Text: t)).Skip(1).ToList();
        var builder = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n" +
            "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in text.Chunk(100))
        {
            builder.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (cid, chars) in chunk)
            {
                builder.Append('<').Append(cid.ToString("X4", CultureInfo.InvariantCulture)).Append("> <")
                    .Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(chars))).Append(">\n");
            }
            builder.Append("endbfchar\n");
        }
        builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return builder.ToString();
    }

    /// <summary>A font as one document uses it: character ids handed out in order of first use
    /// (id 0 is the font's .notdef glyph), each with its glyph, width and text.</summary>
    private sealed class FontUse(string name)
    {
        private readonly Dictionary<(uint Glyph, string Text), ushort> _ids = [];

        public string Name { get; } = name;
        public List<uint> Glyphs { get; } = [0];
        public List<int> Widths { get; } = [0];
        public List<string> Texts { get; } = [""];

        public ushort Cid(uint glyph, string text, int width)
        {
            if (!_ids.TryGetValue((glyph, text), out var cid))
            {
                if (Glyphs.Count > ushort.MaxValue)
                {
                    throw new InvalidOperationException("A document uses more than 65,535 distinct glyph and text pairs of one font.");
                }
                cid = (ushort)Glyphs.Count;
                _ids[(glyph, text)] = cid;
                Glyphs.Add(glyph);
                Widths.Add(width);
                Texts.Add(text);
            }
            return cid;
        }
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

    /// <summary>The content streams already written out for this page, in drawing order.</summary>
    internal List<int> ContentIds { get; } = [];

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
        // Each run's left edge, then the runs in reading order: a right-to-left line is written from
        // its right end, so a reader that takes text in the order it was written (pdf.js) gets the
        // pieces of a line, Arabic and the English inside it, in the order they are read.
        var starts = new List<decimal>(line.Runs.Count);
        var left = x;
        foreach (var run in line.Runs)
        {
            starts.Add(left);
            left += run.Advance * line.Size / 1000.0m;
        }
        var order = Enumerable.Range(0, line.Runs.Count);
        foreach (var index in line.RightToLeft ? order.Reverse() : order)
        {
            var run = line.Runs[index];
            var pen = starts[index];
            var (font, cids) = _writer.FontResource(run.Face, run);
            var scale = line.Size / 1000.0m;
            if (run.RightToLeft)
            {
                Content.Append($"/Span << /ActualText {PdfWriter.Text(run.Text)} >> BDC\n");
            }
            Content.Append($"BT {PdfWriter.N(gray)} g /{font} {PdfWriter.N(line.Size)} Tf\n");
            // The run's letters in one TJ string from one starting point, so a reader's text
            // extraction (pdf.js, PDFium, Poppler) takes the run as one piece and puts a
            // right-to-left run back in reading order as a whole (critic p06 round 2: Arabic came out
            // in fragments wherever a letter carried dots or a mark, each drawn on its own). Every
            // glyph's horizontal offset and the shaper's advance go in as TJ adjustments. Glyphs
            // drawn off the baseline, and marks (dots, vowels: no text of their own), follow in a
            // second pass, each at its own place.
            var placed = new List<(int Index, decimal X)>();
            var segment = new StringBuilder();
            var start = pen;
            decimal pending = 0; // thousandths of the font size to move right before the next glyph
            for (var i = 0; i < run.Glyphs.Count; i++)
            {
                var glyph = run.Glyphs[i];
                var mark = run.GlyphText[i].Length == 0;
                if (glyph.OffsetY != 0 || mark)
                {
                    placed.Add((i, pen + glyph.OffsetX * scale));
                    pending += glyph.Advance;
                    pen += glyph.Advance * scale;
                    continue;
                }
                pending += glyph.OffsetX;
                if (pending != 0)
                {
                    segment.Append(' ').Append((-pending).ToString(CultureInfo.InvariantCulture)).Append(' ');
                }
                AppendHex(segment.Append('<'), cids[i]).Append('>');
                // The font's own width moved the pen; the shaper's advance (less the offset) is where it goes.
                pending = glyph.Advance - glyph.OffsetX - run.Face.Width(glyph.Glyph);
                pen += glyph.Advance * scale;
            }
            if (segment.Length > 0)
            {
                Content.Append($"1 0 0 1 {PdfWriter.N(start)} {PdfWriter.N(y)} Tm [{segment}] TJ\n");
            }
            foreach (var (i, at) in placed)
            {
                var glyph = run.Glyphs[i];
                var mark = run.GlyphText[i].Length == 0;
                Content.Append(mark ? "/Artifact BMC " : "")
                    .Append($"1 0 0 1 {PdfWriter.N(at)} {PdfWriter.N(y + glyph.OffsetY * scale)} Tm <{cids[i]:X4}> Tj")
                    .Append(mark ? " EMC\n" : "\n");
            }
            Content.Append("ET\n");
            if (run.RightToLeft)
            {
                Content.Append("EMC\n");
            }
        }
    }

    /// <summary>Four upper-case hexadecimal digits of a character id, written straight into the
    /// content (no string per glyph: a long document draws millions of glyphs).</summary>
    private static StringBuilder AppendHex(StringBuilder builder, ushort value)
    {
        const string digits = "0123456789ABCDEF";
        return builder.Append(digits[value >> 12]).Append(digits[(value >> 8) & 0xF]).Append(digits[(value >> 4) & 0xF]).Append(digits[value & 0xF]);
    }
}
