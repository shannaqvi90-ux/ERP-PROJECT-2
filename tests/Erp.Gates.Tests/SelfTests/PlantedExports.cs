using System.IO.Compression;
using System.Text;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// Export files written the way real products write them, for the gate's plants and the decoder's
/// self-tests: a PDF whose page text sits in a Flate-compressed content stream as a hex string
/// (nothing of it is visible in the raw bytes), and an XLSX whose cell text sits in a deflated
/// XML part of a ZIP container.
/// </summary>
public static class PlantedExports
{
    public static byte[] Pdf(string text)
    {
        var content = Encoding.Latin1.GetBytes($"BT /F1 12 Tf 72 720 Td <{Convert.ToHexString(Encoding.Latin1.GetBytes(text))}> Tj ET");
        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(content);
            }
            compressed = buffer.ToArray();
        }
        var output = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) => output.Write(Encoding.Latin1.GetBytes(s));
        void Object(string body)
        {
            offsets.Add(output.Position);
            Write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }
        Write("%PDF-1.7\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
        offsets.Add(output.Position);
        Write($"4 0 obj\n<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
        output.Write(compressed);
        Write("\nendstream\nendobj\n");
        Object("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] Xlsx(string text)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            var escaped = System.Security.SecurityElement.Escape(text);
            Entry("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Entry("xl/worksheets/sheet1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>{escaped}</t></is></c></row></sheetData></worksheet>");
        }
        return buffer.ToArray();
    }
}
