using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;

namespace Erp.Modules.Reports;

/// <summary>
/// A report's rows as CSV (UTF-8 with a byte-order mark, so spreadsheet programs read Arabic
/// correctly; raw values: decimals with a dot, ISO dates, moments as the document's wall clock to
/// the second) and as an XLSX workbook (numbers, dates and moments as real numbers with formats, text as text, the sheet right to left for an Arabic
/// document, column titles in the document's language). Grouped reports carry the group as
/// their first column. Written by hand from the Office Open XML parts (no dependency).
/// </summary>
public static class Exports
{
    public const string CsvType = "text/csv";
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Csv(ReportDocument document)
    {
        using var buffer = new MemoryStream();
        WriteCsv(document, buffer);
        return buffer.ToArray();
    }

    /// <summary>Writes the CSV to <paramref name="output"/> line by line (a 100,000-row list never
    /// sits in memory as one text). A document cut at its row limit ends with a line saying so.</summary>
    public static void WriteCsv(ReportDocument document, Stream output)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(true), 1 << 16, leaveOpen: true);
        var header = new List<string>();
        if (document.GroupLabel is { } groupLabel)
        {
            header.Add(groupLabel);
        }
        header.AddRange(document.Columns.Select(c => c.Label));
        Line(writer, header);
        var cells = new List<string>(header.Count);
        foreach (var group in document.Groups)
        {
            foreach (var row in group.Rows)
            {
                cells.Clear();
                if (document.GroupLabel is not null)
                {
                    cells.Add(group.Label ?? "");
                }
                cells.AddRange(row.Cells.Select((cell, i) => CsvValue(cell, document.Columns[i])));
                Line(writer, cells);
            }
        }
        if (document.Truncated)
        {
            Line(writer, [document.RowCountText]);
        }
    }

    private static string CsvValue(ReportCell cell, ReportDocumentColumn column) => cell.Value switch
    {
        null => "",
        // A moment as the document's wall clock to the second ("2026-10-05 22:54:59", in the
        // document's time zone, as the PDF prints it), which spreadsheet programs read as a date and time.
        string s when column.Type == "dateTime" && WallClock(s) is { } local => local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool or string when column.Type is "boolean" or "choice" => cell.Text,
        string s => s,
        long or int => Convert.ToString(cell.Value, CultureInfo.InvariantCulture)!,
        _ => cell.Text,
    };

    private static void Line(TextWriter writer, IEnumerable<string> cells)
    {
        writer.Write(string.Join(",", cells.Select(Quote)));
        writer.Write("\r\n");
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
        using var buffer = new MemoryStream();
        WriteXlsx(document, buffer);
        return buffer.ToArray();
    }

    /// <summary>Writes the workbook to <paramref name="output"/>, the sheet's rows straight into the
    /// compressed part (a 100,000-row list never sits in memory as one text). A document cut at its
    /// row limit says so in a line under the table, outside the filtered range.</summary>
    public static void WriteXlsx(ReportDocument document, Stream output)
    {
        var styles = new XlsxStyles();
        var columnCount = document.Columns.Count + (document.GroupLabel is null ? 0 : 1);
        var lastDataRow = 1 + document.Groups.Sum(g => g.Rows.Count);
        var sheetName = SheetName(document.Title);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
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
            $"<sheets><sheet name=\"{SecurityElement.Escape(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            (lastDataRow > 1 ? $"<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'{SecurityElement.Escape(sheetName).Replace("'", "''", StringComparison.Ordinal)}'!$A$1:${Column(columnCount - 1)}${lastDataRow}</definedName></definedNames>" : "") +
            "</workbook>");
        Entry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        using (var sheet = EntryWriter(zip, "xl/worksheets/sheet1.xml"))
        {
            sheet.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sheet.Write("<sheetViews><sheetView workbookViewId=\"0\"");
            sheet.Write(document.RightToLeft ? " rightToLeft=\"1\"" : "");
            sheet.Write("><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sheet.Write("<cols>");
            for (var i = 1; i <= columnCount; i++)
            {
                sheet.Write($"<col min=\"{i}\" max=\"{i}\" width=\"22\" customWidth=\"1\"/>");
            }
            sheet.Write("</cols><sheetData>");
            var r = 1;
            var header = new List<string>();
            if (document.GroupLabel is { } groupLabel)
            {
                header.Add(groupLabel);
            }
            header.AddRange(document.Columns.Select(c => c.Label));
            sheet.Write($"<row r=\"{r}\">");
            for (var c = 0; c < header.Count; c++)
            {
                sheet.Write(TextCell(Reference(c, r), header[c], XlsxStyles.Header));
            }
            sheet.Write("</row>");
            foreach (var group in document.Groups)
            {
                foreach (var row in group.Rows)
                {
                    r++;
                    sheet.Write($"<row r=\"{r}\">");
                    var c = 0;
                    if (document.GroupLabel is not null)
                    {
                        sheet.Write(TextCell(Reference(c++, r), group.Label ?? "", 0));
                    }
                    for (var i = 0; i < row.Cells.Count; i++, c++)
                    {
                        sheet.Write(ValueCell(Reference(c, r), row.Cells[i], document.Columns[i], styles));
                    }
                    sheet.Write("</row>");
                }
            }
            if (lastDataRow > 1 && document.Columns.Any(c => c.Total))
            {
                r++;
                sheet.Write(TotalRow(document, r, lastDataRow, styles));
            }
            if (document.Truncated)
            {
                // A blank line, then what the file holds: outside the filter and the totals' range.
                r += 2;
                sheet.Write($"<row r=\"{r}\">{TextCell(Reference(0, r), document.RowCountText, XlsxStyles.Header)}</row>");
            }
            sheet.Write("</sheetData>");
            if (lastDataRow > 1)
            {
                sheet.Write($"<autoFilter ref=\"A1:{Reference(columnCount - 1, lastDataRow)}\"/>");
            }
            sheet.Write("</worksheet>");
        }
        // Written after the sheet: the sheet's cells register the number formats it holds.
        Entry(zip, "xl/styles.xml", styles.Xml());
    }

    /// <summary>
    /// The grand total under the rows, outside the filtered range, in bold and named in the
    /// document's language. A totalled column's cell is a SUBTOTAL formula over the rows above it
    /// (with the total the document printed as its stored value), so the sheet still adds up when
    /// the reader filters it, for example to one group. Amounts in more than one currency cannot be
    /// added in one cell: their totals per currency are written as text.
    /// </summary>
    private static string TotalRow(ReportDocument document, int row, int lastDataRow, XlsxStyles styles)
    {
        var builder = new StringBuilder($"<row r=\"{row}\">");
        var offset = document.GroupLabel is null ? 0 : 1;
        var labelled = false;
        if (offset == 1)
        {
            builder.Append(TextCell(Reference(0, row), document.Texts.Total, XlsxStyles.Header));
            labelled = true;
        }
        for (var i = 0; i < document.Columns.Count; i++)
        {
            var column = document.Columns[i];
            var reference = Reference(i + offset, row);
            if (document.Totals[i] is { } total)
            {
                builder.Append(TotalCell(reference, total, column, $"{Column(i + offset)}2:{Column(i + offset)}{lastDataRow}", styles));
            }
            else if (!labelled)
            {
                builder.Append(TextCell(reference, document.Texts.Total, XlsxStyles.Header));
                labelled = true;
            }
        }
        return builder.Append("</row>").ToString();
    }

    private static string TotalCell(string reference, ReportCell total, ReportDocumentColumn column, string range, XlsxStyles styles)
    {
        string? amount = null;
        string format;
        switch (total.Value)
        {
            case string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                amount = s;
                var scale = s.Contains('.') ? s.Length - s.IndexOf('.') - 1 : 0;
                format = column.Type == "money" ? "#,##0." + new string('0', Math.Max(2, scale)) : scale == 0 ? "#,##0" : "#,##0." + new string('0', scale);
                break;
            case { } value when JsonSerializer.SerializeToElement(value) is { ValueKind: JsonValueKind.Object } money
                                && money.TryGetProperty("amount", out var a) && money.TryGetProperty("currency", out var c):
                amount = a.GetString()!;
                var digits = amount.Contains('.') ? amount.Length - amount.IndexOf('.') - 1 : 0;
                format = $"#,##0.{new string('0', Math.Max(2, digits))} \"{c.GetString()!.ToUpperInvariant()}\"";
                break;
            default:
                return TextCell(reference, total.Text, XlsxStyles.Header);
        }
        return $"<c r=\"{reference}\" s=\"{styles.For(format, bold: true)}\"><f>SUBTOTAL(109,{range})</f><v>{amount}</v></c>";
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
            case "dateTime" when cell.Value is string instant && WallClock(instant) is { } local:
                return $"<c r=\"{reference}\" s=\"{styles.For("yyyy-mm-dd hh:mm")}\"><v>{Serial(local)}</v></c>";
            case "boolean" or "choice" or "dateTime" or "reference" or "text":
            default:
                return TextCell(reference, cell.Text, 0);
        }
    }

    /// <summary>The wall-clock time of a document's moment (its raw value carries the document's
    /// time zone offset), to the second.</summary>
    private static DateTime? WallClock(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant)
            ? new DateTime(instant.DateTime.Ticks - instant.DateTime.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified)
            : null;

    private static string Serial(DateTime value) =>
        ((decimal)(value - new DateTime(1899, 12, 30)).Ticks / TimeSpan.TicksPerDay).ToString("0.########", CultureInfo.InvariantCulture);

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
        using var writer = EntryWriter(zip, name);
        writer.Write(content);
    }

    private static StreamWriter EntryWriter(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        // A fixed time keeps the same document byte for byte the same.
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new StreamWriter(entry.Open(), new UTF8Encoding(false), 1 << 16);
    }

    /// <summary>The workbook's cell styles: the default, a bold header, one per number format
    /// used, plain or bold (totals).</summary>
    private sealed class XlsxStyles
    {
        public const int Header = 1;
        private readonly List<string> _formats = [];
        private readonly List<(string Format, bool Bold)> _styles = [];

        public int For(string format, bool bold = false)
        {
            if (!_formats.Contains(format))
            {
                _formats.Add(format);
            }
            var index = _styles.IndexOf((format, bold));
            if (index < 0)
            {
                _styles.Add((format, bold));
                index = _styles.Count - 1;
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
                .Append($"<cellXfs count=\"{2 + _styles.Count}\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>")
                .Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>");
            foreach (var (format, bold) in _styles)
            {
                builder.Append($"<xf numFmtId=\"{164 + _formats.IndexOf(format)}\" fontId=\"{(bold ? 1 : 0)}\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"{(bold ? " applyFont=\"1\"" : "")}/>");
            }
            builder.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            return builder.ToString();
        }
    }
}
