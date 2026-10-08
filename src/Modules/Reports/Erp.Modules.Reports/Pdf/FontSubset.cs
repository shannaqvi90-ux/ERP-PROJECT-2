using System.Buffers.Binary;

namespace Erp.Modules.Reports.Pdf;

/// <summary>
/// A TrueType font cut down to the glyphs one document draws, for embedding (PDF 1.7, 9.6.4 and
/// 9.9: a subset font, its name prefixed with a six-letter tag). Glyph ids stay as they are, so the
/// document's <c>/CIDToGIDMap</c> and widths need no change: every glyph the document does not use
/// keeps its place but loses its outline (an empty entry in <c>loca</c>), and the tables only a
/// shaper reads (GSUB, GPOS, GDEF: text is shaped before it is written) and the glyph names of
/// <c>post</c> are left out. Composite glyphs keep the glyphs they are built from. Glyph 0
/// (.notdef, drawn for a character no font has) is always kept.
/// <para>A whole Noto Sans Arabic face is 289 KB; a page of Arabic text uses a few dozen of its
/// 1,340 glyphs, so an Arabic document went from about 210 KB to a few tens of KB.</para>
/// </summary>
public static class FontSubset
{
    /// <summary>Tables a PDF reader never needs from an embedded font that is drawn by glyph id.</summary>
    private static readonly System.Collections.Frozen.FrozenSet<string> Dropped =
        System.Collections.Frozen.FrozenSet.Create(StringComparer.Ordinal, "GDEF", "GPOS", "GSUB", "STAT", "DSIG", "BASE", "JSTF", "MATH", "MERG", "meta");

    private sealed record Table(string Tag, int Offset, int Length);

    /// <summary>The font with only the outlines of <paramref name="glyphs"/> (and what they are built
    /// from, and glyph 0); every glyph id keeps its meaning.</summary>
    public static byte[] Of(byte[] sfnt, IEnumerable<uint> glyphs)
    {
        var tables = Directory(sfnt);
        if (!tables.TryGetValue("glyf", out var glyf) || !tables.TryGetValue("loca", out var loca)
            || !tables.TryGetValue("head", out var head) || !tables.TryGetValue("maxp", out var maxp))
        {
            throw new InvalidDataException("not a TrueType outline font (glyf, loca, head and maxp are needed to cut it down)");
        }
        var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(maxp.Offset + 4));
        var longOffsets = BinaryPrimitives.ReadInt16BigEndian(sfnt.AsSpan(head.Offset + 50)) == 1;
        var starts = new int[glyphCount + 1];
        for (var g = 0; g <= glyphCount; g++)
        {
            starts[g] = longOffsets
                ? (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(loca.Offset + g * 4))
                : BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(loca.Offset + g * 2)) * 2;
        }
        ReadOnlySpan<byte> Outline(int g) => sfnt.AsSpan(glyf.Offset + starts[g], Math.Max(0, starts[g + 1] - starts[g]));

        // The glyphs to keep: those drawn, glyph 0, and every component of a composite, recursively.
        var keep = new bool[glyphCount];
        var pending = new Stack<int>(glyphs.Where(g => g < glyphCount).Select(g => (int)g).Append(0));
        while (pending.TryPop(out var g))
        {
            if (keep[g])
            {
                continue;
            }
            keep[g] = true;
            foreach (var component in Components(Outline(g)))
            {
                if (component < glyphCount && !keep[component])
                {
                    pending.Push(component);
                }
            }
        }

        // glyf with only the kept outlines (each on a four-byte boundary), and loca in long form.
        var newGlyf = new MemoryStream();
        var newLoca = new byte[(glyphCount + 1) * 4];
        for (var g = 0; g < glyphCount; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(g * 4), (uint)newGlyf.Length);
            if (keep[g])
            {
                newGlyf.Write(Outline(g));
                while (newGlyf.Length % 4 != 0)
                {
                    newGlyf.WriteByte(0);
                }
            }
        }
        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyphCount * 4), (uint)newGlyf.Length);

        var newHead = sfnt.AsSpan(head.Offset, head.Length).ToArray();
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(50), 1);
        BinaryPrimitives.WriteUInt32BigEndian(newHead.AsSpan(8), 0);

        var output = new List<(string Tag, byte[] Data)>();
        foreach (var table in tables.Values.OrderBy(t => t.Tag, StringComparer.Ordinal))
        {
            if (Dropped.Contains(table.Tag))
            {
                continue;
            }
            var data = table.Tag switch
            {
                "glyf" => newGlyf.ToArray(),
                "loca" => newLoca,
                "head" => newHead,
                // Version 3: no glyph names (they only serve PostScript printing and are most of the table).
                "post" when table.Length >= 32 => Post3(sfnt.AsSpan(table.Offset, 32)),
                _ => sfnt.AsSpan(table.Offset, table.Length).ToArray(),
            };
            output.Add((table.Tag, data));
        }
        var font = Write(BinaryPrimitives.ReadUInt32BigEndian(sfnt), output);
        // head.checkSumAdjustment: the whole font then sums to 0xB1B0AFBA.
        var headOffset = DirectoryOffset(font, "head");
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(headOffset + 8), unchecked(0xB1B0AFBA - Checksum(font)));
        return font;
    }

    /// <summary>A six-letter subset tag (PDF 1.7, 9.6.4) for a face and the glyphs it draws:
    /// the same glyphs give the same tag, so a document is the same every time it is printed.</summary>
    public static string Tag(string faceName, IEnumerable<uint> glyphs)
    {
        var hash = 14695981039346656037UL;
        void Mix(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        foreach (var c in faceName)
        {
            Mix(c);
        }
        foreach (var g in glyphs.Distinct().Order())
        {
            Mix(g + 0x10000UL);
        }
        var letters = new char[6];
        for (var i = 0; i < 6; i++)
        {
            letters[i] = (char)('A' + (int)(hash % 26));
            hash /= 26;
        }
        return new string(letters);
    }

    /// <summary>The glyph ids a composite outline is built from (none for a simple outline).</summary>
    internal static List<int> Components(ReadOnlySpan<byte> outline)
    {
        var components = new List<int>();
        if (outline.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(outline) >= 0)
        {
            return components;
        }
        var at = 10;
        while (at + 4 <= outline.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(outline[at..]);
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(outline[(at + 2)..]));
            at += 4;
            at += (flags & 0x0001) != 0 ? 4 : 2;          // ARG_1_AND_2_ARE_WORDS
            if ((flags & 0x0008) != 0) at += 2;           // WE_HAVE_A_SCALE
            else if ((flags & 0x0040) != 0) at += 4;      // WE_HAVE_AN_X_AND_Y_SCALE
            else if ((flags & 0x0080) != 0) at += 8;      // WE_HAVE_A_TWO_BY_TWO
            if ((flags & 0x0020) == 0)                    // MORE_COMPONENTS
            {
                break;
            }
        }
        return components;
    }

    private static Dictionary<string, Table> Directory(byte[] sfnt)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        var tables = new Dictionary<string, Table>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var record = 12 + i * 16;
            var tag = System.Text.Encoding.ASCII.GetString(sfnt, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            if (offset < 0 || length < 0 || offset + length > sfnt.Length)
            {
                throw new InvalidDataException($"font table {tag} lies outside the font");
            }
            tables[tag] = new Table(tag, offset, length);
        }
        return tables;
    }

    private static int DirectoryOffset(byte[] font, string tag)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            }
        }
        throw new InvalidDataException($"font has no {tag} table");
    }

    private static byte[] Post3(ReadOnlySpan<byte> header)
    {
        var post = header.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    private static byte[] Write(uint flavor, List<(string Tag, byte[] Data)> tables)
    {
        var count = tables.Count;
        var selector = 0;
        while (1 << (selector + 1) <= count)
        {
            selector++;
        }
        var size = 12 + 16 * count + tables.Sum(t => (t.Data.Length + 3) & ~3);
        var font = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(font, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(6), (ushort)((1 << selector) * 16));
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(8), (ushort)selector);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(10), (ushort)(count * 16 - (1 << selector) * 16));
        var position = 12 + 16 * count;
        for (var i = 0; i < count; i++)
        {
            var (tag, data) = tables[i];
            var record = font.AsSpan(12 + i * 16);
            System.Text.Encoding.ASCII.GetBytes(tag, record);
            var padded = (data.Length + 3) & ~3;
            data.CopyTo(font.AsSpan(position));
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(font.AsSpan(position, padded)));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
            position += padded;
        }
        return font;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var whole = data.Length & ~3;
        for (var i = 0; i < whole; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data[i..]));
        }
        if (whole < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            data[whole..].CopyTo(last);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(last));
        }
        return sum;
    }
}
