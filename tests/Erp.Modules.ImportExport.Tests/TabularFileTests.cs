using System.IO.Compression;
using System.Text;
using Erp.Modules.ImportExport.Files;

namespace Erp.Modules.ImportExport.Tests;

/// <summary>Reading CSV and XLSX files: delimiters, quotes, encodings, bounds, and the way a spreadsheet stores numbers and dates.</summary>
public sealed class TabularFileTests
{
    private static Table Csv(string text, string name = "a.csv") => TabularFile.Read(Encoding.UTF8.GetBytes(text), name);

    [Fact]
    public void A_plain_csv_is_a_header_and_rows()
    {
        var table = Csv("Name,Email\r\nAli,ali@example.ae\r\nSara,sara@example.ae\r\n");
        Assert.Equal(["Name", "Email"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(["Sara", "sara@example.ae"], table.Rows[1]);
    }

    [Theory]
    [InlineData("Name;Email\nAli;a@x.ae\n")]
    [InlineData("Name\tEmail\nAli\ta@x.ae\n")]
    [InlineData("Name,Email\nAli,a@x.ae")]
    public void Semicolons_tabs_and_a_missing_last_newline_are_read(string text)
    {
        var table = Csv(text);
        Assert.Equal(["Name", "Email"], table.Headers);
        Assert.Equal(["Ali", "a@x.ae"], table.Rows.Single());
    }

    [Fact]
    public void Quotes_hold_separators_line_breaks_and_quotes()
    {
        var table = Csv("Name,Notes\r\n\"Noor, Ali\",\"line one\r\nline \"\"two\"\"\"\r\n");
        Assert.Equal("Noor, Ali", table.Rows[0][0]);
        Assert.Equal("line one\r\nline \"two\"", table.Rows[0][1]);
    }

    [Fact]
    public void A_byte_order_mark_is_dropped_and_arabic_is_kept()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("الاسم,البريد\nشركة النور,a@x.ae\n")).ToArray();
        var table = TabularFile.Read(bytes, "ar.csv");
        Assert.Equal(["الاسم", "البريد"], table.Headers);
        Assert.Equal("شركة النور", table.Rows[0][0]);
    }

    [Fact]
    public void A_windows_arabic_file_is_decoded_when_it_is_not_utf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1256).GetBytes("الاسم,المدينة\nشركة,دبي\n");
        var table = TabularFile.Read(bytes, "win.csv");
        Assert.Equal(["الاسم", "المدينة"], table.Headers);
        Assert.Equal(["شركة", "دبي"], table.Rows[0]);
    }

    [Fact]
    public void Short_rows_are_padded_and_trailing_blank_rows_and_columns_dropped()
    {
        var table = Csv("A,B,C,\r\n1\r\n2,3,4,\r\n,,,\r\n");
        Assert.Equal(["A", "B", "C"], table.Headers);
        Assert.Equal(["1", "", ""], table.Rows[0]);
        Assert.Equal(2, table.Rows.Count);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData(",,,\n,,,\n", "empty")]
    [InlineData(",,\n1,2,3\n", "noHeader")]
    public void Files_without_a_header_are_refused(string text, string code)
    {
        if (text.Length == 0)
        {
            Assert.Equal(code, Assert.Throws<TableFormatException>(() => TabularFile.Read([], "a.csv")).Code);
            return;
        }
        var problem = Assert.Throws<TableFormatException>(() => Csv(text));
        Assert.Equal(code == "noHeader" ? "noHeader" : code, problem.Code);
    }

    [Fact]
    public void An_unterminated_quote_a_huge_cell_and_too_many_rows_are_refused()
    {
        Assert.Equal("damaged", Assert.Throws<TableFormatException>(() => Csv("A,B\n\"open,1\n")).Code);
        Assert.Equal("cellTooLong", Assert.Throws<TableFormatException>(() => Csv("A\n" + new string('x', 5000) + "\n")).Code);
        var many = new StringBuilder("A\n");
        for (var i = 0; i < 50_001; i++)
        {
            many.Append("x\n");
        }
        Assert.Equal("tooManyRows", Assert.Throws<TableFormatException>(() => Csv(many.ToString())).Code);
        var wide = string.Join(",", Enumerable.Range(0, 81).Select(i => "c" + i));
        Assert.Equal("tooManyColumns", Assert.Throws<TableFormatException>(() => Csv(wide + "\n")).Code);
    }

    [Fact]
    public void The_extension_decides_the_format_and_the_bytes_must_agree()
    {
        Assert.Equal("unknownFormat", Assert.Throws<TableFormatException>(() => TabularFile.Read([1, 2, 3], "a.pdf")).Code);
        Assert.Equal("damaged", Assert.Throws<TableFormatException>(() => TabularFile.Read(Encoding.UTF8.GetBytes("A,B\n1,2\n"), "a.xlsx")).Code);
        Assert.Equal("unknownFormat", Assert.Throws<TableFormatException>(() => TabularFile.Read(Workbook([["A"], ["1"]]), "a.csv")).Code);
        Assert.Equal("damaged", Assert.Throws<TableFormatException>(() => TabularFile.Read([(byte)'P', (byte)'K', 3, 4, 9, 9, 9, 9], "a.xlsx")).Code);
    }

    /// <summary>A minimal workbook the way a spreadsheet program writes one: shared strings, a number with a date format, an inline string.</summary>
    internal static byte[] Workbook(string[][] rows, string? extraSheet = null, bool dateInColumnTwo = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"S1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
            var shared = new List<string>();
            var sheet = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < rows[r].Length; c++)
                {
                    var reference = $"{(char)('A' + c)}{r + 1}";
                    var value = rows[r][c];
                    if (value.StartsWith("#n:"))
                    {
                        sheet.Append($"<c r=\"{reference}\"><v>{value[3..]}</v></c>");
                    }
                    else if (value.StartsWith("#d:"))
                    {
                        sheet.Append($"<c r=\"{reference}\" s=\"1\"><v>{value[3..]}</v></c>");
                    }
                    else if (value.StartsWith("#i:"))
                    {
                        sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value[3..])}</t></is></c>");
                    }
                    else
                    {
                        shared.Add(value);
                        sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{shared.Count - 1}</v></c>");
                    }
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
            Add("xl/sharedStrings.xml", "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + string.Concat(shared.Select(s => $"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>")) + "</sst>");
        }
        return buffer.ToArray();
    }

    private static MemoryStream Expandable(byte[] bytes)
    {
        var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void An_xlsx_is_read_with_shared_strings_inline_strings_numbers_and_dates()
    {
        var bytes = Workbook([["Name", "Phone", "Joined", "Limit"], ["Al Noor", "#n:9.71501234567E+11", "#d:45292", "#n:1250.5"], ["#i:شركة", "x", "#d:45292.5", "#n:0"]]);
        var table = TabularFile.Read(bytes, "contacts.xlsx");
        Assert.Equal("xlsx", table.Format);
        Assert.Equal(["Name", "Phone", "Joined", "Limit"], table.Headers);
        Assert.Equal(["Al Noor", "971501234567", "2024-01-01", "1250.5"], table.Rows[0]);
        Assert.Equal(["شركة", "x", "2024-01-01 12:00:00", "0"], table.Rows[1]);
    }

    [Fact]
    public void An_xlsx_with_gaps_keeps_the_columns_where_they_are()
    {
        // A skipped cell (no C1) and a skipped row (row 2) stay where a spreadsheet shows them.
        using var buffer = Expandable(Workbook([["A", "B", "C"], ["1", "2", "3"]]));
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true);
        var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }
        xml = xml.Replace("<c r=\"B2\" t=\"s\"><v>4</v></c>", "").Replace("<row r=\"2\">", "<row r=\"3\">");
        entry.Delete();
        using (var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open()))
        {
            writer.Write(xml);
        }
        zip.Dispose();
        var table = TabularFile.Read(buffer.ToArray(), "gaps.xlsx");
        Assert.Equal(["A", "B", "C"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows[0], c => Assert.Equal("", c));
        Assert.Equal(["1", "", "3"], table.Rows[1]);
    }

    [Fact]
    public void An_xlsx_with_an_external_entity_is_refused()
    {
        var bytes = Workbook([["A"], ["1"]]);
        using var buffer = Expandable(bytes);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("xl/sharedStrings.xml")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/sharedStrings.xml").Open());
            writer.Write("<!DOCTYPE sst [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>&x;</t></si></sst>");
        }
        Assert.Equal("damaged", Assert.Throws<TableFormatException>(() => TabularFile.Read(buffer.ToArray(), "xxe.xlsx")).Code);
    }
}
