using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Erp.Gates.Tests.G2;

/// <summary>Self-tests of how the report data gate reads an XLSX workbook (round 21). The gate compares
/// what a cell shows: a date or time cell is the date it displays, not its serial number as text (the
/// serial 46344.88001234 of a moment is no "8800" printed), while a leaked value in a text cell, a
/// number cell or a date cell is still in the text the gate judges.</summary>
public sealed class G2XlsxCellTests
{
    private const string Styles = "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy-mm-dd\"/>" +
        "<numFmt numFmtId=\"166\" formatCode=\"#,##0.00 &quot;AED&quot;\"/></numFmts>" +
        "<cellXfs count=\"4\"><xf numFmtId=\"0\"/><xf numFmtId=\"164\"/><xf numFmtId=\"165\"/><xf numFmtId=\"166\"/></cellXfs></styleSheet>";

    private static byte[] Workbook(params string[] cells)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            void Entry(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            Entry("xl/styles.xml", Styles);
            Entry("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Title</t></is></c></row><row r=\"2\">" +
                string.Concat(cells) + "</row></sheetData></worksheet>");
        }
        return memory.ToArray();
    }

    private static string Read(params string[] cells) => G2.ReportDataCheck.XlsxText(Workbook(cells)).Text;

    private static string Minute(double serial) =>
        new DateTime(1899, 12, 30).AddDays(serial).AddSeconds(30).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Fact]
    public void A_date_time_cell_is_the_moment_it_shows_not_its_serial_number()
    {
        var text = Read("<c r=\"A2\" s=\"1\"><v>46344.88001234</v></c>");
        Assert.DoesNotContain("8800", text, StringComparison.Ordinal);
        Assert.DoesNotContain("46344", text, StringComparison.Ordinal);
        Assert.Contains(Minute(46344.88001234), text.Split('\n'));
    }

    [Fact]
    public void A_date_cell_is_the_date_it_shows()
    {
        var text = Read("<c r=\"A2\" s=\"2\"><v>46345</v></c>");
        Assert.Contains(new DateTime(1899, 12, 30).AddDays(46345).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), text.Split('\n'));
        Assert.DoesNotContain("46345", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_leaked_value_in_a_date_cell_is_still_caught()
    {
        // The date of another record, printed in a date cell, is in the text under its displayed form.
        var leaked = new DateTime(2026, 3, 9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var serial = (new DateTime(2026, 3, 9) - new DateTime(1899, 12, 30)).TotalDays.ToString("0", CultureInfo.InvariantCulture);
        Assert.Contains(leaked, Read($"<c r=\"A2\" s=\"2\"><v>{serial}</v></c>"), StringComparison.Ordinal);
        Assert.Contains(leaked, Read($"<c r=\"A2\" s=\"1\"><v>{serial}.5</v></c>"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_leaked_value_in_a_number_cell_is_still_caught()
    {
        Assert.Contains("8800", Read("<c r=\"A2\"><v>8800</v></c>").Split('\n'));
        Assert.Contains("8800.5", Read("<c r=\"A2\" s=\"3\"><v>8800.5</v></c>").Split('\n'));
        Assert.Contains("8800", Read("<c r=\"A2\" s=\"3\"><f>SUBTOTAL(109,A1:A1)</f><v>8800</v></c>").Split('\n'));
    }

    [Fact]
    public void A_leaked_value_in_a_text_cell_is_still_caught()
    {
        Assert.Contains("Leaked Trading LLC", Read("<c r=\"A2\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Leaked Trading LLC</t></is></c>").Split('\n'));
        // A text cell that carries a date style is text, not a date.
        Assert.Contains("8800", Read("<c r=\"A2\" t=\"inlineStr\" s=\"1\"><is><t>8800</t></is></c>").Split('\n'));
    }

    [Fact]
    public void A_self_closed_cell_does_not_swallow_the_cell_after_it()
    {
        var text = Read("<c r=\"A2\" s=\"1\"/><c r=\"B2\" t=\"inlineStr\"><is><t>after</t></is></c><c r=\"C2\"><v>7</v></c>");
        Assert.Contains("after", text.Split('\n'));
        Assert.Contains("7", text.Split('\n'));
    }
}
