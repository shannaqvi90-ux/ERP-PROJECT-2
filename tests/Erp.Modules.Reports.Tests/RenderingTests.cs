using System.IO.Compression;
using System.Text;
using Erp.Modules.Reports.Pdf;
using UglyToad.PdfPig;

namespace Erp.Modules.Reports.Tests;

/// <summary>One set of fonts for the rendering tests (loading them decodes six WOFF files).</summary>
public sealed class FontsFixture : IDisposable
{
    public PdfFonts Fonts { get; } = new();

    public void Dispose() => Fonts.Dispose();
}

/// <summary>
/// Printed documents without a database: the bidirectional order of mixed Arabic and English, the
/// formats of numbers and dates in both languages, the PDF (readable, fonts embedded, Arabic laid
/// out right to left and shaped, headers repeated on every page, page numbers, landscape for wide
/// tables) and the CSV and XLSX exports.
/// </summary>
public sealed class RenderingTests(FontsFixture fixture) : IClassFixture<FontsFixture>
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

    [Theory]
    // An English name inside an Arabic paragraph keeps its own order; the Arabic around it reads right to left.
    [InlineData("شركة Al Noor Trading للتجارة", true, "للتجارة|Al Noor Trading|شركة")]
    // Numbers keep their digits left to right inside Arabic text.
    // Digits after Arabic letters are Arabic numbers (UAX #9 W2): each keeps left-to-right digits, as browsers show them.
    [InlineData("فاتورة 2026-10 رقم 15", true, "15|رقم|10|-|2026|فاتورة")]
    // Arabic inside an English paragraph is one right-to-left run.
    [InlineData("Branch: الفرع الرئيسي today", false, "Branch:|الفرع الرئيسي|today")]
    // A telephone number reads left to right as one unit in an Arabic line (critic p06 round 1:
    // the Arabic PDF printed "7810 555 2 971+"), alone and inside Arabic text.
    [InlineData("+971 2 555 7810", true, "+971 2 555 7810")]
    [InlineData("هاتف +971 4 123-4567 فرع", true, "فرع|+971 4 123-4567|هاتف")]
    [InlineData("04 555 7810", true, "04 555 7810")]
    // Not telephone numbers: a date with its time, and amounts, keep the algorithm's order.
    [InlineData("05/10/2026 14:30", true, "14:30|05/10/2026")]
    public void Mixed_text_is_ordered_as_a_reader_expects(string text, bool rightToLeft, string expected)
    {
        var runs = Bidi.VisualRuns(text, rightToLeft);
        var visual = string.Join("|", runs.Select(r => text.Substring(r.Start, r.Length).Trim()).Where(t => t.Length > 0));
        Assert.Equal(expected, visual);
    }

    [Fact]
    public void The_first_strong_character_decides_a_values_direction()
    {
        Assert.False(Bidi.FirstStrongIsRightToLeft("123 Main Street"));
        Assert.True(Bidi.FirstStrongIsRightToLeft("123 شارع"));
        Assert.Null(Bidi.FirstStrongIsRightToLeft("12,345.00"));
    }

    [Fact]
    public void Numbers_amounts_and_dates_follow_the_language_and_digits()
    {
        var en = new ReportFormatter("en", "arab", Dubai);
        Assert.Equal("1,234,567.50", en.Decimal(1234567.5m, 2));
        Assert.Equal("AED 1,234.50", en.Money(1234.5m, "aed"));
        Assert.Equal("-AED 0.125", en.Money(-0.125m, "AED"));
        Assert.Equal("KWD 1.500", en.Money(1.5m, "KWD"));
        Assert.Equal("4 Oct 2026", en.Date(new DateOnly(2026, 10, 4)));
        Assert.Equal("4 Oct 2026, 5:31 PM", en.DateTime(new DateTimeOffset(2026, 10, 4, 13, 31, 0, TimeSpan.Zero)));
        Assert.Equal("latn", en.Numerals);

        var arLatin = new ReportFormatter("ar", "latn", Dubai);
        Assert.Equal("1,234.50 AED", arLatin.Money(1234.5m, "AED"));
        Assert.Equal("04‏/10‏/2026", arLatin.Date(new DateOnly(2026, 10, 4)));

        var arabic = new ReportFormatter("ar", "arab", Dubai);
        Assert.Equal("١٬٢٣٤٬٥٦٧٫٥٠", arabic.Decimal(1234567.5m, 2));
        Assert.Equal("١٠٠٬٠٠٤", arabic.Integer(100004));
        Assert.Equal("٠٤‏/١٠‏/٢٠٢٦، ٥:٣١ م", arabic.DateTime(new DateTimeOffset(2026, 10, 4, 13, 31, 0, TimeSpan.Zero)));
        // Amounts never pass through binary floating point: every digit survives.
        Assert.Equal("١٢٬٣٤٥٬٦٧٨٬٩٠١٬٢٣٤٬٥٦٧٫٨٩", arabic.Decimal(12345678901234567.89m, 2));
    }

    [Fact]
    public void Arabic_text_is_shaped_into_joining_forms_with_lam_alef()
    {
        var shaper = new TextShaper(fixture.Fonts);
        var line = shaper.Shape("السلام", 10, bold: false, documentRightToLeft: true);
        var run = Assert.Single(line.Runs);
        Assert.True(run.RightToLeft);
        Assert.Equal("NotoSansArabic-Regular", run.Face.Name);
        // Every letter is drawn and each glyph that draws a letter maps back to it.
        Assert.Equal("السلام".Length, string.Concat(run.GlyphText).Length);
        // Joined forms differ from the isolated letters: no lam is drawn with the isolated lam's glyph.
        Assert.True(fixture.Fonts.Faces[0].Font.TryGetGlyph('ل', out var isolatedLam));
        Assert.DoesNotContain(run.Glyphs, g => g.Glyph == isolatedLam);
        Assert.True(line.Width > 0);
    }

    [Fact]
    public void Each_script_is_drawn_with_a_font_that_has_it()
    {
        var shaper = new TextShaper(fixture.Fonts);
        var line = shaper.Shape("Müller شركة 42", 10, bold: true, documentRightToLeft: false);
        Assert.Contains(line.Runs, r => r.Face.Name == "NotoSansArabic-Bold" && r.RightToLeft);
        Assert.Contains(line.Runs, r => r.Face.Name.StartsWith("NotoSans", StringComparison.Ordinal) && !r.RightToLeft && r.Text.Contains("Müller", StringComparison.Ordinal));
        Assert.All(line.Runs.SelectMany(r => r.Glyphs), g => Assert.NotEqual(0u, g.Glyph));
    }

    [Fact]
    public void Long_text_wraps_at_spaces_within_the_width()
    {
        var shaper = new TextShaper(fixture.Fonts);
        var lines = shaper.Wrap("Office 1104, Al Saqr Business Tower, Sheikh Zayed Road, Dubai", 8.5m, false, false, 80m);
        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(l.Width <= 80.5m, $"{l.Width}"));
    }

    [Fact]
    public void An_arabic_pdf_is_readable_right_to_left_with_its_fonts_embedded()
    {
        var document = Sample("ar", rows: 3, grouped: true);
        var bytes = new PdfReportRenderer(fixture.Fonts).Render(document);
        using var pdf = PdfDocument.Open(bytes);
        Assert.Equal(1, pdf.NumberOfPages);
        var page = pdf.GetPage(1);
        var fonts = page.Letters.Select(l => l.FontName).Distinct().ToList();
        Assert.Contains(fonts, f => f != null && f.Contains("NotoSansArabic", StringComparison.Ordinal));
        var text = Erp.Modules.Reports.Tests.PdfText.Of(bytes);
        Assert.Contains("ملف الشركة", text, StringComparison.Ordinal);
        Assert.Contains("الإجمالي", text, StringComparison.Ordinal);
        Assert.Contains("Al Noor Trading LLC", text, StringComparison.Ordinal);
        Assert.Contains("صفحة", text, StringComparison.Ordinal);
        Assert.Contains("١", text, StringComparison.Ordinal);
        // Right to left: the first column's header sits to the right of the second's.
        var first = page.GetWords().First(w => w.Text.Contains("C001", StringComparison.Ordinal));
        var second = page.GetWords().First(w => w.Text == "Al");
        Assert.True(first.BoundingBox.Left > second.BoundingBox.Left, $"{first.BoundingBox.Left} > {second.BoundingBox.Left}");
        var raw = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Direction /R2L", raw, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pdf_starts_with_a_well_formed_header()
    {
        var bytes = new PdfReportRenderer(fixture.Fonts).Render(Sample("en", rows: 1, grouped: false));
        Assert.Equal("%PDF-1.7\n", Encoding.Latin1.GetString(bytes, 0, 9));
    }

    /// <summary>Letters that share a glyph (س and ش, ر and ز, medial ب and ن: they differ only by
    /// dots drawn as marks) each extract as themselves: copy, search and screen readers read the
    /// words printed, not الإشم for الاسم (critic p06 round 1). Judged on the letters a reader
    /// extracts through the font's ToUnicode map, without the replacement text.</summary>
    [Fact]
    public void Arabic_letters_that_share_a_glyph_extract_as_the_letters_printed()
    {
        // Pairs in the same joining form: medial س (الاسم) and ش (الشركة), final ر (تقرير) and ز
        // (الرمز), medial ب (العربية) and ن (العنوان), ا and إ (الإجمالي). The page carries no
        // other Arabic text, so every Arabic letter extracted comes from these words.
        const string words = "الاسم الشركة تقرير الرمز العربية العنوان الإجمالي";
        var document = Sample("ar", rows: 0, grouped: false) with
        {
            Title = "Report", Subject = words, Issuer = "Issuer", Parameters = [], Facts = [], Columns = [], Groups = [], Totals = [],
            Texts = new ReportDocumentTexts("Total", "Printed", "Page {page} of {pages}", "None", "-"),
            Numerals = "latn",
        };
        var bytes = new PdfReportRenderer(fixture.Fonts).Render(document);
        using var pdf = PdfDocument.Open(bytes);
        var letters = string.Concat(pdf.GetPage(1).Letters.Select(l => l.Value)).Replace("\u200B", "", StringComparison.Ordinal);
        var arabic = words.Where(c => Bidi.IsArabicScript(c)).Distinct();
        foreach (var letter in arabic)
        {
            Assert.True(words.Count(c => c == letter) == letters.Count(c => c == letter),
                $"'{letter}' printed {words.Count(c => c == letter)} times, extracted {letters.Count(c => c == letter)} times (extracted: {letters})");
        }
    }

    [Fact]
    public void A_telephone_number_prints_left_to_right_in_an_arabic_pdf()
    {
        var document = Sample("ar", rows: 1, grouped: false) with { Facts = [new ReportDocumentFact("الهاتف", "+971 2 555 7810")] };
        var bytes = new PdfReportRenderer(fixture.Fonts).Render(document);
        using var pdf = PdfDocument.Open(bytes);
        var digits = pdf.GetPage(1).Letters.Where(l => "+0123456789".Contains(l.Value, StringComparison.Ordinal) && l.Value.Length == 1)
            .Where(l => Math.Abs(l.StartBaseLine.Y - pdf.GetPage(1).Letters.First(x => x.Value == "+").StartBaseLine.Y) < 0.5)
            .OrderBy(l => l.StartBaseLine.X).Select(l => l.Value);
        Assert.Equal("+97125557810", string.Concat(digits));
    }

    [Fact]
    public void A_long_report_breaks_into_pages_with_the_header_on_each_and_page_numbers()
    {
        var document = Sample("en", rows: 150, grouped: false);
        var bytes = new PdfReportRenderer(fixture.Fonts).Render(document);
        using var pdf = PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages >= 3, $"{pdf.NumberOfPages} pages");
        foreach (var page in pdf.GetPages())
        {
            Assert.Contains("Code", page.Text, StringComparison.Ordinal);
            Assert.Contains($"Page {page.Number} of {pdf.NumberOfPages}", string.Join(" ", page.GetWords().Select(w => w.Text)), StringComparison.Ordinal);
            Assert.Contains("Printed", page.Text, StringComparison.Ordinal);
        }
        Assert.Contains("Row 150", PdfText.Of(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wide_table_turns_the_page_to_landscape()
    {
        var narrow = PdfDocument.Open(new PdfReportRenderer(fixture.Fonts).Render(Sample("en", 2, false)));
        Assert.True(narrow.GetPage(1).Width < narrow.GetPage(1).Height);
        var wide = Sample("en", 2, false, columns: 9);
        using var pdf = PdfDocument.Open(new PdfReportRenderer(fixture.Fonts).Render(wide));
        Assert.True(pdf.GetPage(1).Width > pdf.GetPage(1).Height);
    }

    [Fact]
    public void Csv_starts_with_a_byte_order_mark_keeps_raw_values_and_defuses_formulas()
    {
        var document = Sample("ar", 2, grouped: true);
        var bytes = Exports.Csv(document);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("التجميع,الرمز,الاسم", lines[0], StringComparison.Ordinal);
        Assert.Contains("1234.50", lines[1], StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK(1)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_has_a_right_to_left_sheet_with_numbers_as_numbers()
    {
        var bytes = Exports.Xlsx(Sample("ar", 2, grouped: false));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("rightToLeft=\"1\"", sheet, StringComparison.Ordinal);
        Assert.Contains("<v>1234.50</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("Al Noor Trading LLC", sheet, StringComparison.Ordinal);
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
    }

    /// <summary>A report document as the engine builds one, in either language.</summary>
    internal static ReportDocument Sample(string language, int rows, bool grouped, int columns = 3)
    {
        var ar = language == "ar";
        var f = new ReportFormatter(language, ar ? "arab" : "latn", Dubai);
        var labels = ar ? new[] { "الرمز", "الاسم", "المبلغ" } : ["Code", "Name", "Amount"];
        var cols = new List<ReportDocumentColumn>
        {
            new("code", labels[0], "text", "start", false),
            new("name", labels[1], "text", "start", false),
            new("amount", labels[2], "money", "end", true),
        };
        for (var i = 3; i < columns; i++)
        {
            cols.Add(new ReportDocumentColumn($"extra{i}", $"Extra column {i}", "text", "start", false));
        }
        var data = Enumerable.Range(1, rows).Select(i => new ReportDocumentRow(
            [
                new ReportCell(i == 2 ? "=HYPERLINK(1)" : $"C{i:000}", i == 2 ? "=HYPERLINK(1)" : $"C{i:000}"),
                new ReportCell("Al Noor Trading LLC", i == 1 ? "Al Noor Trading LLC" : ar ? $"الصف {i}" : $"Row {i}"),
                new ReportCell("1234.50", f.Decimal(1234.5m, 2)),
                .. Enumerable.Range(3, Math.Max(0, columns - 3)).Select(c => new ReportCell("x", $"A rather long value number {c} for the column")),
            ])).ToList();
        var total = new ReportCell((1234.5m * rows).ToString(System.Globalization.CultureInfo.InvariantCulture), f.Decimal(1234.5m * rows, 2));
        var totals = cols.Select((c, i) => i == 2 ? total : null).ToList();
        return new ReportDocument
        {
            Key = "test.sample",
            Title = ar ? "ملف الشركة" : "Company profile",
            Subject = ar ? "DXB · النور" : "DXB · Al Noor",
            Issuer = ar ? "شركة النور للتجارة" : "Al Noor Trading LLC",
            Language = language,
            Direction = ar ? "rtl" : "ltr",
            Numerals = f.Numerals,
            Parameters = [new ReportDocumentFact(ar ? "بحث" : "Search", "noor")],
            Facts = [],
            Columns = cols,
            GroupBy = grouped ? "group" : null,
            GroupLabel = grouped ? (ar ? "التجميع" : "Group") : null,
            Groups = [new ReportDocumentGroup(grouped ? (ar ? "دبي" : "Dubai") : null, data.Count, $"{data.Count}", data, totals)],
            Totals = totals,
            RowCount = rows,
            MatchCount = rows,
            RowCountText = $"{rows} rows",
            Truncated = false,
            PrintedAt = new DateTimeOffset(2026, 10, 4, 13, 31, 0, TimeSpan.Zero),
            PrintedAtText = f.DateTime(new DateTimeOffset(2026, 10, 4, 13, 31, 0, TimeSpan.Zero)),
            PrintedBy = "Admin",
            Texts = ar
                ? new ReportDocumentTexts("الإجمالي", "طُبع بواسطة Admin", "صفحة {page} من {pages}", "لا توجد نتائج", "(بلا قيمة)")
                : new ReportDocumentTexts("Total", "Printed by Admin", "Page {page} of {pages}", "Nothing matches", "(none)"),
        };
    }
}

/// <summary>A PDF's text for assertions: page text, the same with right-to-left runs in logical
/// order, and the replacement text of right-to-left runs (read independently with PdfPig).</summary>
public static class PdfText
{
    public static string Of(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var pageText = page.Text.Replace("\u200B", "", StringComparison.Ordinal);
            text.AppendLine(pageText).AppendLine(Logical(pageText));
            foreach (var word in page.GetWords())
            {
                var wordText = word.Text.Replace("\u200B", "", StringComparison.Ordinal);
                text.Append(wordText).Append(' ').Append(Logical(wordText)).Append('\n');
            }
        }
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Inflated(bytes), "/ActualText <FEFF([0-9A-F]+)>"))
        {
            text.AppendLine(Encoding.BigEndianUnicode.GetString(Convert.FromHexString(m.Groups[1].Value)));
        }
        // Lines of the page as runs of words joined in their visual order, logical within each run.
        return text.ToString();
    }

    public static string Logical(string visual) =>
        System.Text.RegularExpressions.Regex.Replace(visual, "[؀-ۿݐ-ݿﭐ-﷿ﹰ-﻿]+", m => new string(m.Value.Reverse().ToArray()));

    private static string Inflated(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var output = new StringBuilder();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(latin, "(?<!end)stream\n"))
        {
            var start = match.Index + match.Length;
            var end = latin.IndexOf("\nendstream", start, StringComparison.Ordinal);
            try
            {
                using var z = new ZLibStream(new MemoryStream(pdf, start, end - start), CompressionMode.Decompress);
                using var reader = new StreamReader(z, Encoding.Latin1);
                output.Append(reader.ReadToEnd());
            }
            catch (InvalidDataException)
            {
                // Not a compressed stream.
            }
        }
        return output.ToString();
    }
}
