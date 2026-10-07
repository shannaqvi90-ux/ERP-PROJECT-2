using System.Collections.Immutable;
using System.Buffers.Binary;
using System.IO.Compression;
using HarfBuzzSharp;

namespace Erp.Modules.Reports.Pdf;

/// <summary>
/// One embedded font of printed documents: its TrueType bytes (decoded from the bundled WOFF),
/// the metrics a PDF font descriptor needs, and a HarfBuzz font that shapes text with it (Arabic
/// joining forms, ligatures, mark placement, kerning). Built once from code-bundled files and
/// never changed (HarfBuzz fonts are made immutable, so concurrent requests may shape with them).
/// </summary>
public sealed class PdfFontFace : IDisposable
{
    private readonly Blob _blob;
    private readonly Face _face;

    internal PdfFontFace(string name, bool bold, byte[] sfnt)
    {
        Name = name;
        Bold = bold;
        Sfnt = sfnt;
        // HarfBuzz reads the font from unmanaged memory owned by the blob (freed when it is disposed).
        var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(sfnt.Length);
        System.Runtime.InteropServices.Marshal.Copy(sfnt, 0, memory, sfnt.Length);
        _blob = new Blob(memory, sfnt.Length, MemoryMode.ReadOnly, () => System.Runtime.InteropServices.Marshal.FreeHGlobal(memory));
        _blob.MakeImmutable();
        _face = new Face(_blob, 0);
        _face.MakeImmutable();
        Font = new Font(_face);
        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(Table("head").AsSpan(18));
        var head = Table("head");
        BoundingBox = [Scale(ReadInt16(head, 36)), Scale(ReadInt16(head, 38)), Scale(ReadInt16(head, 40)), Scale(ReadInt16(head, 42))];
        var hhea = Table("hhea");
        Ascent = Scale(ReadInt16(hhea, 4));
        Descent = Scale(ReadInt16(hhea, 6));
        var os2 = TryTable("OS/2");
        CapHeight = os2 is { Length: >= 90 } && BinaryPrimitives.ReadUInt16BigEndian(os2) >= 2 ? Scale(ReadInt16(os2, 88)) : Ascent;
        GlyphCount = _face.GlyphCount;
        // Compressed once here: every PDF embeds the whole font, and deflating it on every print
        // was most of a document's processor time.
        using var buffer = new MemoryStream();
        using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(sfnt);
        }
        CompressedSfnt = ImmutableArray.Create(buffer.ToArray());
    }

    /// <summary>The TrueType bytes, zlib-compressed (the PDF font file stream), made once and
    /// immutable: the face is shared by every request of every tenant.</summary>
    public ImmutableArray<byte> CompressedSfnt { get; }

    /// <summary>PostScript-style name, unique among the faces (NotoSansArabic-Bold).</summary>
    public string Name { get; }
    public bool Bold { get; }

    /// <summary>The TrueType (sfnt) bytes embedded in PDFs.</summary>
    public byte[] Sfnt { get; }
    public Font Font { get; }
    public int UnitsPerEm { get; }
    public int GlyphCount { get; }

    /// <summary>In thousandths of an em (PDF glyph space).</summary>
    public int Ascent { get; }
    public int Descent { get; }
    public int CapHeight { get; }
    public System.Collections.Immutable.ImmutableArray<int> BoundingBox { get; }

    public bool Covers(int codepoint) => Font.TryGetGlyph((uint)codepoint, out _);

    /// <summary>The glyph's default advance in thousandths of an em.</summary>
    public int Width(uint glyph) => Scale(Font.GetHorizontalGlyphAdvance(glyph));

    public int Scale(int fontUnits) => (int)Math.Round(fontUnits * 1000.0m / UnitsPerEm);

    private byte[] Table(string tag) => TryTable(tag) ?? throw new InvalidDataException($"font {Name} has no {tag} table");

    private byte[]? TryTable(string tag)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(Sfnt.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(Sfnt, record, 4) == tag)
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(Sfnt.AsSpan(record + 8));
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(Sfnt.AsSpan(record + 12));
                return Sfnt.AsSpan(offset, length).ToArray();
            }
        }
        return null;
    }

    private static short ReadInt16(byte[] data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));

    public void Dispose()
    {
        Font.Dispose();
        _face.Dispose();
        _blob.Dispose();
    }
}

/// <summary>
/// The fonts of printed documents: Noto Sans Arabic for Arabic script, Noto Sans (Latin and Latin
/// Extended subsets) for everything else, each regular and bold (SIL OFL-1.1, accepted by the
/// owner for fonts; reviewed in tests/Gates/font-licences.txt). Loaded once from the module's
/// embedded files. A character goes to the first face that has it, Arabic script first to the
/// Arabic face.
/// </summary>
public sealed class PdfFonts : IDisposable
{
    private readonly System.Collections.Immutable.ImmutableArray<PdfFontFace> _regular;
    private readonly System.Collections.Immutable.ImmutableArray<PdfFontFace> _bold;

    public PdfFonts()
    {
        var assembly = typeof(PdfFonts).Assembly;
        PdfFontFace Load(string file, string name, bool bold)
        {
            using var stream = assembly.GetManifestResourceStream($"Erp.Modules.Reports.Fonts.{file}.woff")
                               ?? throw new InvalidOperationException($"font {file} is not embedded");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return new PdfFontFace(name, bold, Woff.ToSfnt(buffer.ToArray()));
        }
        _regular =
        [
            Load("noto-sans-arabic-arabic-400-normal", "NotoSansArabic-Regular", false),
            Load("noto-sans-latin-400-normal", "NotoSans-Regular", false),
            Load("noto-sans-latin-ext-400-normal", "NotoSansLatinExt-Regular", false),
        ];
        _bold =
        [
            Load("noto-sans-arabic-arabic-700-normal", "NotoSansArabic-Bold", true),
            Load("noto-sans-latin-700-normal", "NotoSans-Bold", true),
            Load("noto-sans-latin-ext-700-normal", "NotoSansLatinExt-Bold", true),
        ];
    }

    public IReadOnlyList<PdfFontFace> Faces => [.. _regular, .. _bold];

    /// <summary>The face that draws the character: Arabic script in the Arabic face, anything
    /// else in the first face that has it (null when none does).</summary>
    public PdfFontFace? For(int codepoint, bool bold, PdfFontFace? preferred)
    {
        var faces = bold ? _bold : _regular;
        if (preferred is not null && preferred.Bold == bold && preferred.Covers(codepoint))
        {
            return preferred;
        }
        if (Bidi.IsArabicScript(codepoint) && faces[0].Covers(codepoint))
        {
            return faces[0];
        }
        foreach (var face in faces.Skip(1).Append(faces[0]))
        {
            if (face.Covers(codepoint))
            {
                return face;
            }
        }
        return null;
    }

    /// <summary>The face for text no face has (drawn as the font's missing-glyph box).</summary>
    public PdfFontFace Fallback(bool bold) => (bold ? _bold : _regular)[1];

    public void Dispose()
    {
        foreach (var face in Faces)
        {
            face.Dispose();
        }
    }
}

/// <summary>WOFF 1.0 to TrueType: each table is zlib-compressed (or stored) in the WOFF file.</summary>
public static class Woff
{
    public static byte[] ToSfnt(byte[] woff)
    {
        if (woff.Length < 44 || BinaryPrimitives.ReadUInt32BigEndian(woff) != 0x774F4646)
        {
            throw new InvalidDataException("not a WOFF 1.0m file");
        }
        var flavor = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));
        var tables = new List<(uint Tag, uint Checksum, byte[] Data)>();
        for (var i = 0; i < count; i++)
        {
            var entry = woff.AsSpan(44 + i * 20);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var compressed = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var original = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            var checksum = BinaryPrimitives.ReadUInt32BigEndian(entry[16..]);
            byte[] data;
            if (compressed == original)
            {
                data = woff.AsSpan(offset, original).ToArray();
            }
            else
            {
                using var z = new ZLibStream(new MemoryStream(woff, offset, compressed), CompressionMode.Decompress);
                using var buffer = new MemoryStream(original);
                z.CopyTo(buffer);
                data = buffer.ToArray();
                if (data.Length != original)
                {
                    throw new InvalidDataException("WOFF table has the wrong length");
                }
            }
            tables.Add((tag, checksum, data));
        }
        var output = new MemoryStream();
        var word = new byte[4];
        void U32(uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(word, value);
            output.Write(word);
        }
        void U16(int value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(word, (ushort)value);
            output.Write(word, 0, 2);
        }
        var entrySelector = 0;
        while (1 << (entrySelector + 1) <= count)
        {
            entrySelector++;
        }
        U32(flavor);
        U16(count);
        U16((1 << entrySelector) * 16);
        U16(entrySelector);
        U16(count * 16 - (1 << entrySelector) * 16);
        var position = 12 + 16 * count;
        foreach (var (tag, checksum, data) in tables)
        {
            U32(tag);
            U32(checksum);
            U32((uint)position);
            U32((uint)data.Length);
            position += (data.Length + 3) & ~3;
        }
        foreach (var (_, _, data) in tables)
        {
            output.Write(data);
            while (output.Length % 4 != 0)
            {
                output.WriteByte(0);
            }
        }
        return output.ToArray();
    }
}
