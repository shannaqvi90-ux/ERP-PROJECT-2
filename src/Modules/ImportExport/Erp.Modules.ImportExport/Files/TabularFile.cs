using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Erp.Modules.ImportExport.Contracts;

namespace Erp.Modules.ImportExport.Files;

/// <summary>A file read as a table: the header row and the data rows, every cell as text.</summary>
/// <param name="Headers">The first row.</param>
/// <param name="Rows">The data rows, each as wide as the header (short rows are padded with empty cells).</param>
/// <param name="Format"><c>csv</c> or <c>xlsx</c>.</param>
public sealed record Table(string Format, IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);

/// <summary>The file cannot be read as a table.</summary>
/// <remarks>The code is stable: <c>unknownFormat</c>, <c>empty</c>, <c>noHeader</c>, <c>tooManyRows</c>, <c>tooManyColumns</c>, <c>cellTooLong</c>, <c>damaged</c>, <c>tooLarge</c>.</remarks>
public sealed class TableFormatException(string code, params object[] args) : Exception(code)
{
    public string Code { get; } = code;

    public object[] Args { get; } = args;
}

/// <summary>
/// Reads CSV and XLSX files into a <see cref="Table"/> without a library: CSV by RFC 4180 (comma, semicolon or tab
/// separated, quotes, a byte-order mark, UTF-8 or else the Windows Arabic code page), XLSX as a zip of XML parts
/// (the first sheet; shared strings; numbers keep the digits they were typed with; dates become yyyy-mm-dd).
/// Bounded: rows, columns, cell length and the size of what a zip part inflates to are limited, so a crafted file
/// cannot take the server's memory.
/// </summary>
public static class TabularFile
{
    private const long MaxXmlPartBytes = 200L * 1024 * 1024;

    /// <summary>The format of a file name (<c>csv</c> or <c>xlsx</c>), or null.</summary>
    public static string? FormatOf(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        return extension is "csv" or "xlsx" ? extension : null;
    }

    public static Table Read(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0)
        {
            throw new TableFormatException("empty");
        }
        if (bytes.Length > ImportLimits.MaxFileBytes)
        {
            throw new TableFormatException("tooLarge", ImportLimits.MaxFileBytes / (1024 * 1024));
        }
        var format = FormatOf(fileName) ?? throw new TableFormatException("unknownFormat");
        var isZip = bytes.Length > 3 && bytes[0] == 'P' && bytes[1] == 'K';
        if (format == "xlsx" && !isZip)
        {
            throw new TableFormatException("damaged");
        }
        if (format == "csv" && isZip)
        {
            throw new TableFormatException("unknownFormat");
        }
        var cells = format == "csv" ? ReadCsv(bytes) : ReadXlsx(bytes);
        return Shape(format, cells);
    }

    private static Table Shape(string format, List<string[]> cells)
    {
        // Trailing empty rows and columns are what a spreadsheet leaves behind.
        while (cells.Count > 0 && cells[^1].All(string.IsNullOrWhiteSpace))
        {
            cells.RemoveAt(cells.Count - 1);
        }
        if (cells.Count == 0)
        {
            throw new TableFormatException("empty");
        }
        var header = cells[0];
        var width = header.Length;
        while (width > 0 && string.IsNullOrWhiteSpace(header[width - 1]))
        {
            width--;
        }
        if (width == 0)
        {
            throw new TableFormatException("noHeader");
        }
        if (width > ImportLimits.MaxColumns)
        {
            throw new TableFormatException("tooManyColumns", ImportLimits.MaxColumns);
        }
        if (cells.Count - 1 > ImportLimits.MaxRows)
        {
            throw new TableFormatException("tooManyRows", ImportLimits.MaxRows);
        }
        var headers = header.Take(width).Select(h => (h ?? "").Trim()).ToList();
        var rows = new List<string[]>(cells.Count - 1);
        foreach (var row in cells.Skip(1))
        {
            var padded = new string[width];
            for (var i = 0; i < width; i++)
            {
                padded[i] = i < row.Length ? row[i] : "";
            }
            rows.Add(padded);
        }
        return new Table(format, headers, rows);
    }

    // ---------------------------------------------------------------- CSV

    public static string DecodeText(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            // A spreadsheet program's "CSV" in an Arabic Windows: the Windows Arabic code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1256).GetString(bytes, start, bytes.Length - start);
        }
    }

    internal static List<string[]> ReadCsv(byte[] bytes)
    {
        var text = DecodeText(bytes);
        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        var i = 0;
        void EndCell()
        {
            if (cell.Length > ImportLimits.MaxCellLength)
            {
                throw new TableFormatException("cellTooLong", ImportLimits.MaxCellLength);
            }
            row.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim());
            cell.Clear();
            wasQuoted = false;
        }
        void EndRow()
        {
            EndCell();
            if (row.Count > ImportLimits.MaxColumns * 4)
            {
                throw new TableFormatException("tooManyColumns", ImportLimits.MaxColumns);
            }
            rows.Add([.. row]);
            row.Clear();
            if (rows.Count > ImportLimits.MaxRows + 1)
            {
                throw new TableFormatException("tooManyRows", ImportLimits.MaxRows);
            }
        }
        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                    if (cell.Length > ImportLimits.MaxCellLength * 2)
                    {
                        throw new TableFormatException("cellTooLong", ImportLimits.MaxCellLength);
                    }
                }
            }
            else if (c == '"' && cell.ToString().Trim().Length == 0)
            {
                cell.Clear();
                quoted = true;
                wasQuoted = true;
            }
            else if (c == delimiter)
            {
                EndCell();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                EndRow();
            }
            else
            {
                cell.Append(c);
            }
        }
        if (quoted)
        {
            throw new TableFormatException("damaged");
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            EndRow();
        }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        // The first line outside quotes decides: the separator it holds most.
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (c == '\n' || c == '\r'))
            {
                break;
            }
            else if (!quoted && counts.ContainsKey(c))
            {
                counts[c]++;
            }
        }
        var best = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key == ',' ? 0 : 1).First();
        return best.Value == 0 ? ',' : best.Key;
    }

    // ---------------------------------------------------------------- XLSX

    internal static List<string[]> ReadXlsx(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entries = zip.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'), e => e, StringComparer.OrdinalIgnoreCase);
            if (entries.Values.Sum(e => e.Length) > MaxXmlPartBytes * 2)
            {
                throw new TableFormatException("tooLarge", ImportLimits.MaxFileBytes / (1024 * 1024));
            }
            var sheetPath = FirstSheetPath(entries);
            var shared = entries.TryGetValue("xl/sharedStrings.xml", out var sharedEntry) ? ReadSharedStrings(sharedEntry) : [];
            var dateStyles = entries.TryGetValue("xl/styles.xml", out var stylesEntry) ? ReadDateStyles(stylesEntry) : new HashSet<int>();
            if (!entries.TryGetValue(sheetPath, out var sheet))
            {
                throw new TableFormatException("damaged");
            }
            return ReadSheet(sheet, shared, dateStyles);
        }
        catch (InvalidDataException)
        {
            throw new TableFormatException("damaged");
        }
        catch (XmlException)
        {
            throw new TableFormatException("damaged");
        }
    }

    private static XmlReader Open(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxXmlPartBytes)
        {
            throw new TableFormatException("tooLarge", ImportLimits.MaxFileBytes / (1024 * 1024));
        }
        // No DTD, no external entities: a part cannot pull in a file or a URL.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false, MaxCharactersInDocument = MaxXmlPartBytes };
        return XmlReader.Create(entry.Open(), settings);
    }

    private static string FirstSheetPath(Dictionary<string, ZipArchiveEntry> entries)
    {
        if (!entries.TryGetValue("xl/workbook.xml", out var workbook))
        {
            throw new TableFormatException("damaged");
        }
        string? relationshipId = null;
        using (var reader = Open(workbook))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                {
                    relationshipId = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    break;
                }
            }
        }
        if (relationshipId is not null && entries.TryGetValue("xl/_rels/workbook.xml.rels", out var rels))
        {
            using var reader = Open(rels);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship" && reader.GetAttribute("Id") == relationshipId)
                {
                    var target = reader.GetAttribute("Target") ?? "";
                    var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                    return path.Replace("xl/../", "");
                }
            }
        }
        return "xl/worksheets/sheet1.xml";
    }

    private static List<string> ReadSharedStrings(ZipArchiveEntry entry)
    {
        var strings = new List<string>();
        using var reader = Open(entry);
        var builder = new StringBuilder();
        var inItem = false;
        var inText = false;
        var inPhonetic = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "si": inItem = true; builder.Clear(); break;
                    case "rPh": inPhonetic++; break;
                    case "t": inText = inItem && inPhonetic == 0 && !reader.IsEmptyElement; break;
                }
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace && inText)
            {
                builder.Append(reader.Value);
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "t": inText = false; break;
                    case "rPh": inPhonetic--; break;
                    case "si": inItem = false; strings.Add(builder.ToString()); break;
                }
            }
        }
        return strings;
    }

    private static HashSet<int> ReadDateStyles(ZipArchiveEntry entry)
    {
        var customDates = new HashSet<int>();
        var cellFormats = new List<int>();
        using var reader = Open(entry);
        var inCellXfs = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "numFmt" && int.TryParse(reader.GetAttribute("numFmtId"), out var id) && IsDateFormat(reader.GetAttribute("formatCode") ?? ""))
                {
                    customDates.Add(id);
                }
                else if (reader.LocalName == "cellXfs")
                {
                    inCellXfs = true;
                }
                else if (reader.LocalName == "xf" && inCellXfs)
                {
                    cellFormats.Add(int.TryParse(reader.GetAttribute("numFmtId"), out var f) ? f : 0);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "cellXfs")
            {
                inCellXfs = false;
            }
        }
        var styles = new HashSet<int>();
        for (var i = 0; i < cellFormats.Count; i++)
        {
            var f = cellFormats[i];
            if (f is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58 || customDates.Contains(f))
            {
                styles.Add(i);
            }
        }
        return styles;
    }

    private static bool IsDateFormat(string code)
    {
        // Text between quotes and bracketed parts (colours, locales, elapsed time) is not a date letter.
        var plain = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "");
        return plain.IndexOfAny(['y', 'Y', 'd', 'D', 'm', 'M', 'h', 'H', 's', 'S']) >= 0 && !plain.Contains("General", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string[]> ReadSheet(ZipArchiveEntry entry, List<string> shared, HashSet<int> dateStyles)
    {
        var rows = new List<string[]>();
        using var reader = Open(entry);
        var current = new List<string>();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                current = [];
                var rowNumber = int.TryParse(reader.GetAttribute("r"), out var r) ? r : rows.Count + 1;
                // A skipped row is an empty row: keep the numbering a spreadsheet shows.
                if (rowNumber - 1 > ImportLimits.MaxRows + 1)
                {
                    throw new TableFormatException("tooManyRows", ImportLimits.MaxRows);
                }
                while (rows.Count < rowNumber - 1)
                {
                    rows.Add([]);
                }
                if (reader.IsEmptyElement)
                {
                    rows.Add([]);
                    continue;
                }
                using var rowReader = reader.ReadSubtree();
                rowReader.Read();
                while (rowReader.Read())
                {
                    if (rowReader.NodeType == XmlNodeType.Element && rowReader.LocalName == "c")
                    {
                        var reference = rowReader.GetAttribute("r");
                        var column = reference is null ? current.Count : ColumnIndex(reference);
                        if (column >= ImportLimits.MaxColumns * 4)
                        {
                            throw new TableFormatException("tooManyColumns", ImportLimits.MaxColumns);
                        }
                        var type = rowReader.GetAttribute("t");
                        var style = int.TryParse(rowReader.GetAttribute("s"), out var s) ? s : 0;
                        string value;
                        if (rowReader.IsEmptyElement)
                        {
                            value = "";
                        }
                        else
                        {
                            using var cellReader = rowReader.ReadSubtree();
                            cellReader.Read();
                            value = ReadCell(cellReader, type, style, shared, dateStyles);
                        }
                        if (value.Length > ImportLimits.MaxCellLength)
                        {
                            throw new TableFormatException("cellTooLong", ImportLimits.MaxCellLength);
                        }
                        while (current.Count < column)
                        {
                            current.Add("");
                        }
                        current.Add(value);
                    }
                }
                rows.Add([.. current]);
            }
        }
        return rows;
    }

    private static string ReadCell(XmlReader reader, string? type, int style, List<string> shared, HashSet<int> dateStyles)
    {
        string? value = null;
        var inline = new StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (reader.LocalName == "v")
            {
                value = reader.ReadElementContentAsString();
            }
            else if (reader.LocalName == "t" && !reader.IsEmptyElement)
            {
                inline.Append(reader.ReadElementContentAsString());
            }
        }
        return type switch
        {
            "s" => int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? shared[index] : "",
            "inlineStr" => inline.ToString(),
            "str" or "e" => value ?? "",
            "b" => value == "1" ? "true" : "false",
            _ => Number(value, dateStyles.Contains(style)),
        };
    }

    private static string Number(string? raw, bool isDate)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }
        raw = raw.Trim();
        if (isDate && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2_958_466)
        {
            var date = new DateTime(1899, 12, 30).AddTicks((long)Math.Round(serial * TimeSpan.TicksPerDay));
            return serial == Math.Floor(serial) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (raw.Contains('E') || raw.Contains('e'))
        {
            // 9.71501234567E+11 is the number as the sheet stored it: write it out in full.
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("0.############################", CultureInfo.InvariantCulture) : raw;
        }
        return raw;
    }

    private static int ColumnIndex(string reference)
    {
        var column = 0;
        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z')
            {
                column = column * 26 + (c - 'A' + 1);
            }
            else if (c is >= 'a' and <= 'z')
            {
                column = column * 26 + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, column - 1);
    }
}
