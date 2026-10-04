using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;

namespace Erp.Modules.Reports;

/// <summary>
/// A report's rows as CSV (UTF-8 with a byte-order mark, so spreadsheet programs read Arabic
/// correctly; raw values: decimals with a dot, ISO dates) and as an XLSX workbook (numbers and
/// dates as real numbers with formats, text as text, the sheet right to left for an Arabic
/// document, column titles in the document's language). Grouped reports carry the group as
/// their first column. Written by hand from the Office Open XML parts (no dependency).
/// </summary>
public static class Exports
{
    public const string CsvType = "text/csv";
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Csv(ReportDocument document)
    {
        var builder = new StringBuilder();
        var header = new List<string>();
        if (document.GroupLabel is { } groupLabel)
        {
            header.Add(groupLabel);
        }
        header.AddRange(document.Columns.Select(c => c.Label));
        Line(builder, header);
        foreach (var group in document.Groups)
        {
            foreach (var row in group.Rows)
            {
                var cells = new List<string>();
                if (document.GroupLabel is not null)
                {
                    cells.Add(group.Label ?? "");
                }
                cells.AddRange(row.Cells.Select((cell, i) => CsvValue(cell, document.Columns[i])));
                Line(builder, cells);
            }
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    private static string CsvValue(ReportCell cell, ReportDocumentColumn column) => cell.Value switch
    {
        null => "",
        bool or string when column.Type is "boolean" or "choice" => cell.Text,
        string s => s,
        long or int => Convert.ToString(cell.Value, CultureInfo.InvariantCulture)!,
        _ => cell.Text,
    };

    private static void Line(StringBuilder builder, IEnumerable<string> cells)
    {
        builder.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");
    }

    /// <summary>Quotes a field when it needs it; a field starting with a formula character is
    /// prefixed with an apostrophe so a spreadsheet never runs it (CSV injection).</summary>
    private static string Quote(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    public static byte[] Xlsx(ReportDocument document)
    {
        var styles = new XlsxStyles();
        var sheet = new StringBuilder();
        var columnCount = document.Columns.Count + (document.GroupLabel is null ? 0 : 1);
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">")
            .Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(document.RightToLeft ? " rightToLeft=\"1\"" : "")
            .Append("><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>")
            .Append("<cols>");
        for (var i = 1; i <= columnCount; i++)
        {
            sheet.Append($"<col min=\"{i}\" max=\"{i}\" width=\"22\" customWidth=\"1\"/>");
        }
        sheet.Append("</cols><sheetData>");
        var r = 1;
        var header = new List<string>();
        if (document.GroupLabel is { } groupLabel)
        {
            header.Add(groupLabel);
        }
        header.AddRange(document.Columns.Select(c => c.Label));
        sheet.Append($"<row r=\"{r}\">");
        for (var c = 0; c < header.Count; c++)
        {
            sheet.Append(TextCell(Reference(c, r), header[c], XlsxStyles.Header));
        }
        sheet.Append("</row>");
        foreach (var group in document.Groups)
        {
            foreach (var row in group.Rows)
            {
                r++;
                sheet.Append($"<row r=\"{r}\">");
                var c = 0;
                if (document.GroupLabel is not null)
                {
                    sheet.Append(TextCell(Reference(c++, r), group.Label ?? "", 0));
                }
                for (var i = 0; i < row.Cells.Count; i++, c++)
                {
                    sheet.Append(ValueCell(Reference(c, r), row.Cells[i], document.Columns[i], styles));
                }
                sheet.Append("</row>");
            }
        }
        sheet.Append("</sheetData>");
        if (r > 1)
        {
            sheet.Append($"<autoFilter ref=\"A1:{Reference(columnCount - 1, r)}\"/>");
        }
        sheet.Append("</worksheet>");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            Entry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<sheets><sheet name=\"{SecurityElement.Escape(SheetName(document.Title))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                (r > 1 ? $"<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'{SecurityElement.Escape(SheetName(document.Title)).Replace("'", "''", StringComparison.Ordinal)}'!$A$1:${Column(columnCount - 1)}${r}</definedName></definedNames>" : "") +
                "</workbook>");
            Entry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            Entry(zip, "xl/styles.xml", styles.Xml());
        }
        return buffer.ToArray();
    }

    private static string ValueCell(string reference, ReportCell cell, ReportDocumentColumn column, XlsxStyles styles)
    {
        switch (column.Type)
        {
            case "number" when cell.Value is long or int:
                return $"<c r=\"{reference}\" s=\"{styles.For("#,##0")}\"><v>{Convert.ToString(cell.Value, CultureInfo.InvariantCulture)}</v></c>";
            case "number" or "money" when cell.Value is string s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d):
                var scale = s.Contains('.') ? s.Length - s.IndexOf('.') - 1 : 0;
                var format = column.Type == "money" ? "#,##0." + new string('0', Math.Max(2, scale)) : scale == 0 ? "#,##0" : "#,##0." + new string('0', scale);
                return $"<c r=\"{reference}\" s=\"{styles.For(format)}\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>";
            case "money" when cell.Value is not null && JsonSerializer.SerializeToElement(cell.Value) is { ValueKind: JsonValueKind.Object } money
                              && money.TryGetProperty("amount", out var amount) && money.TryGetProperty("currency", out var currency):
                var text = amount.GetString()!;
                var digits = text.Contains('.') ? text.Length - text.IndexOf('.') - 1 : 0;
                var code = currency.GetString()!.ToUpperInvariant();
                return $"<c r=\"{reference}\" s=\"{styles.For($"#,##0.{new string('0', Math.Max(2, digits))} \"{code}\"")}\"><v>{text}</v></c>";
            case "date" when cell.Value is string iso && DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                return $"<c r=\"{reference}\" s=\"{styles.For("yyyy-mm-dd")}\"><v>{Serial(date.ToDateTime(TimeOnly.MinValue))}</v></c>";
            case "boolean" or "choice" or "dateTime" or "reference" or "text":
            default:
                return TextCell(reference, cell.Text, 0);
        }
    }

    private static string Serial(DateTime value) =>
        (value - new DateTime(1899, 12, 30)).TotalDays.ToString("0.########", CultureInfo.InvariantCulture);

    private static string TextCell(string reference, string text, int style) =>
        $"<c r=\"{reference}\" t=\"inlineStr\"{(style == 0 ? "" : $" s=\"{style}\"")}><is><t xml:space=\"preserve\">{Xml(text)}</t></is></c>";

    /// <summary>XML text with the characters XML 1.0 cannot carry removed.</summary>
    private static string Xml(string text) =>
        SecurityElement.Escape(new string(text.Where(c => c is '\t' or '\n' or '\r' || c >= ' ' && c != '\uFFFE' && c != '\uFFFF').ToArray())) ?? "";

    private static string Reference(int column, int row) => Column(column) + row.ToString(CultureInfo.InvariantCulture);

    private static string Column(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }
        return name;
    }

    /// <summary>A sheet name: at most 31 characters, none of []:*?/\.</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        return clean.Length == 0 ? "Report" : clean.Length > 31 ? clean[..31] : clean;
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        // A fixed time keeps the same document byte for byte the same.
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>The workbook's cell styles: the default, a bold header, one per number format used.</summary>
    private sealed class XlsxStyles
    {
        public const int Header = 1;
        private readonly List<string> _formats = [];

        public int For(string format)
        {
            var index = _formats.IndexOf(format);
            if (index < 0)
            {
                _formats.Add(format);
                index = _formats.Count - 1;
            }
            return 2 + index;
        }

        public string Xml()
        {
            var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            if (_formats.Count > 0)
            {
                builder.Append($"<numFmts count=\"{_formats.Count}\">");
                for (var i = 0; i < _formats.Count; i++)
                {
                    builder.Append($"<numFmt numFmtId=\"{164 + i}\" formatCode=\"{SecurityElement.Escape(_formats[i])}\"/>");
                }
                builder.Append("</numFmts>");
            }
            builder.Append("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>")
                .Append("<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>")
                .Append("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>")
                .Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>")
                .Append($"<cellXfs count=\"{2 + _formats.Count}\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>")
                .Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>");
            for (var i = 0; i < _formats.Count; i++)
            {
                builder.Append($"<xf numFmtId=\"{164 + i}\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>");
            }
            builder.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            return builder.ToString();
        }
    }
}
