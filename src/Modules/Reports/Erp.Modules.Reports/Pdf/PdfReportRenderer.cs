namespace Erp.Modules.Reports.Pdf;

/// <summary>
/// Lays a <see cref="ReportDocument"/> out on A4 pages: the letterhead (issuer, title, subject),
/// the parameters it ran with and a record's facts, then the table with its header repeated on
/// every page, group headings with counts, group and grand totals, and on every page who printed
/// it, when, and "page x of y". An Arabic document is mirrored: columns run from the right, text
/// starts at the right edge, numbers keep to the end side; each value keeps its own direction
/// (an English name reads left to right inside it). Wide tables turn the page to landscape.
/// </summary>
public sealed class PdfReportRenderer(PdfFonts fonts)
{
    private const double Margin = 36;
    private const double FooterHeight = 22;
    private const double CellPadX = 4;
    private const double CellPadY = 3;
    private const double BodySize = 8.5;
    private const double HeaderSize = 8;
    private const double PortraitWidth = 595.28;
    private const double PortraitHeight = 841.89;

    public byte[] Render(ReportDocument document) => new Layout(document, new TextShaper(fonts)).Render();

    private sealed class Layout
    {
        private readonly ReportDocument _doc;
        private readonly TextShaper _shaper;
        private readonly bool _rtl;
        private readonly PdfWriter _writer;
        private readonly double _pageWidth;
        private readonly double _pageHeight;
        private readonly double[] _widths;
        private PdfPage _page = null!;
        private double _y;
        private bool _inTable;

        public Layout(ReportDocument document, TextShaper shaper)
        {
            _doc = document;
            _shaper = shaper;
            _rtl = document.RightToLeft;
            _writer = new PdfWriter(document.Subject is null ? document.Title : $"{document.Title} \u00B7 {document.Subject}", document.Language, _rtl);
            var natural = NaturalWidths();
            var portrait = PortraitWidth - 2 * Margin;
            var landscape = PortraitHeight - 2 * Margin;
            var landscapePage = natural.Sum() > portrait * 1.05 && document.Columns.Count > 3;
            _pageWidth = landscapePage ? PortraitHeight : PortraitWidth;
            _pageHeight = landscapePage ? PortraitWidth : PortraitHeight;
            _widths = Fit(natural, landscapePage ? landscape : portrait);
        }

        private double Available => _pageWidth - 2 * Margin;

        public byte[] Render()
        {
            NewPage();
            Letterhead();
            if (_doc.Columns.Count > 0)
            {
                TableHeader();
                _inTable = true;
                var grouped = _doc.GroupBy is not null;
                foreach (var group in _doc.Groups)
                {
                    if (grouped)
                    {
                        Band($"{group.Label} \u00B7 {group.CountText}", 0.93, bold: true, keepWithNext: true);
                    }
                    foreach (var row in group.Rows)
                    {
                        Row(row.Cells.Select(c => c.Text).ToList(), bold: false, shade: null);
                    }
                    if (grouped && _doc.Columns.Any(c => c.Total))
                    {
                        TotalRow(group.Totals, $"{_doc.Texts.Total} \u00B7 {group.Label}", 0.97);
                    }
                }
                if (_doc.RowCount == 0)
                {
                    Paragraph(_doc.Texts.Empty, BodySize, bold: false, gray: 0.35);
                }
                else if (_doc.Columns.Any(c => c.Total))
                {
                    TotalRow(_doc.Totals, _doc.Texts.Total, 0.9, rule: true);
                }
                _inTable = false;
            }
            var pages = _writer.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                Footer(pages[i], i + 1, pages.Count);
            }
            return _writer.Write(_doc.PrintedAt);
        }

        private void NewPage()
        {
            _page = _writer.AddPage(_pageWidth, _pageHeight);
            _y = _pageHeight - Margin;
        }

        /// <summary>Starts a new page when the next <paramref name="height"/> points do not fit,
        /// repeating the table's header row inside a table.</summary>
        private void Ensure(double height)
        {
            if (_y - height >= Margin + FooterHeight)
            {
                return;
            }
            NewPage();
            if (_inTable)
            {
                TableHeader();
            }
        }

        /// <summary>The physical left edge of a box that starts <paramref name="start"/> points from
        /// the document's start edge (the right edge in Arabic).</summary>
        private double X(double start, double width) => _rtl ? _pageWidth - Margin - start - width : Margin + start;

        private void Letterhead()
        {
            Paragraph(_doc.Issuer, 9, bold: true, gray: 0.35);
            _y -= 2;
            Paragraph(_doc.Title, 16, bold: true, gray: 0);
            if (_doc.Subject is { } subject)
            {
                Paragraph(subject, 11, bold: false, gray: 0.15);
            }
            _y -= 4;
            if (_doc.Facts.Count > 0)
            {
                Facts(_doc.Facts, 9);
                _y -= 6;
            }
            if (_doc.Parameters.Count > 0)
            {
                Facts(_doc.Parameters, 8);
            }
            if (_doc.Columns.Count > 0)
            {
                Paragraph(_doc.RowCountText, 8, bold: false, gray: 0.35);
            }
            _y -= 6;
        }

        private void Paragraph(string text, double size, bool bold, double gray)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            foreach (var line in _shaper.Wrap(text, size, bold, _rtl, Available))
            {
                var height = size * 1.45;
                Ensure(height);
                _page.Text(line, X(0, line.Width), _y - size * 1.05, gray);
                _y -= height;
            }
        }

        /// <summary>Label and value pairs in two columns: labels in bold at the start side.</summary>
        private void Facts(IReadOnlyList<ReportDocumentFact> facts, double size)
        {
            var labelWidth = Math.Min(Available * 0.3, facts.Max(f => _shaper.Shape(f.Label, size, true, _rtl).Width) + 10);
            foreach (var fact in facts)
            {
                var label = _shaper.Wrap(fact.Label, size, true, _rtl, labelWidth - 6);
                var value = _shaper.Wrap(fact.Text, size, false, _rtl, Available - labelWidth);
                var lines = Math.Max(label.Count, value.Count);
                var lineHeight = size * 1.45;
                Ensure(lines * lineHeight);
                for (var i = 0; i < lines; i++)
                {
                    var baseline = _y - size * 1.05 - i * lineHeight;
                    if (i < label.Count)
                    {
                        _page.Text(label[i], X(0, label[i].Width), baseline, 0.3);
                    }
                    if (i < value.Count)
                    {
                        _page.Text(value[i], X(labelWidth, value[i].Width), baseline);
                    }
                }
                _y -= lines * lineHeight;
            }
        }

        private void TableHeader()
        {
            Row(_doc.Columns.Select(c => c.Label).ToList(), bold: true, shade: 0.9, size: HeaderSize, repeat: false);
        }

        /// <summary>A full-width band (a group's heading). With <paramref name="keepWithNext"/> it
        /// moves to the next page together with at least one row.</summary>
        private void Band(string text, double shade, bool bold, bool keepWithNext)
        {
            var lines = _shaper.Wrap(text, BodySize, bold, _rtl, Available - 2 * CellPadX);
            var height = lines.Count * BodySize * 1.45 + 2 * CellPadY;
            Ensure(height + (keepWithNext ? BodySize * 1.45 + 2 * CellPadY : 0));
            _page.FillRectangle(Margin, _y - height, Available, height, shade);
            for (var i = 0; i < lines.Count; i++)
            {
                _page.Text(lines[i], X(CellPadX, lines[i].Width), _y - CellPadY - BodySize * 1.05 - i * BodySize * 1.45);
            }
            _y -= height;
        }

        private void TotalRow(IReadOnlyList<ReportCell?> totals, string label, double shade, bool rule = false)
        {
            var cells = totals.Select(t => t?.Text ?? "").ToList();
            var first = cells.FindIndex(c => c.Length > 0);
            // The label goes in the first column when that column has no total of its own.
            if (first != 0)
            {
                cells[0] = label;
            }
            Row(cells, bold: true, shade: shade, rule: rule);
        }

        private void Row(IReadOnlyList<string> cells, bool bold, double? shade, double size = BodySize, bool repeat = true, bool rule = false)
        {
            var wrapped = cells.Select((text, i) => _shaper.Wrap(text, size, bold, _rtl, _widths[i] - 2 * CellPadX, maxLines: 12)).ToList();
            var lineHeight = size * 1.45;
            var height = wrapped.Max(w => w.Count) * lineHeight + 2 * CellPadY;
            if (repeat)
            {
                Ensure(height);
            }
            if (shade is { } gray)
            {
                _page.FillRectangle(Margin, _y - height, Available, height, gray);
            }
            if (rule)
            {
                _page.Line(Margin, _y, Margin + Available, _y, 0.2, 0.8);
            }
            var start = 0.0;
            for (var c = 0; c < wrapped.Count; c++)
            {
                var end = _doc.Columns[c].Align == "end";
                for (var l = 0; l < wrapped[c].Count; l++)
                {
                    var line = wrapped[c][l];
                    var offset = end ? _widths[c] - CellPadX - line.Width : CellPadX;
                    _page.Text(line, X(start + offset, line.Width), _y - CellPadY - size * 1.05 - l * lineHeight);
                }
                start += _widths[c];
            }
            _y -= height;
            _page.Line(Margin, _y, Margin + Available, _y, 0.82, 0.4);
        }

        private void Footer(PdfPage page, int number, int count)
        {
            const double size = 7.5;
            var baseline = Margin - 4;
            page.Line(Margin, baseline + size * 1.6, page.Width - Margin, baseline + size * 1.6, 0.8, 0.4);
            var printed = _shaper.Shape(_doc.Texts.Printed, size, false, _rtl);
            var pageText = _shaper.Shape(_doc.Texts.Page
                .Replace("{page}", new ReportFormatter(_doc.Language, _doc.Numerals, TimeZoneInfo.Utc).Integer(number), StringComparison.Ordinal)
                .Replace("{pages}", new ReportFormatter(_doc.Language, _doc.Numerals, TimeZoneInfo.Utc).Integer(count), StringComparison.Ordinal), size, false, _rtl);
            var startX = _rtl ? page.Width - Margin - printed.Width : Margin;
            var endX = _rtl ? Margin : page.Width - Margin - pageText.Width;
            page.Text(printed, startX, baseline, 0.35);
            page.Text(pageText, endX, baseline, 0.35);
        }

        /// <summary>Each column's natural width: its widest header or value (over a sample of rows).</summary>
        private double[] NaturalWidths()
        {
            var widths = new double[_doc.Columns.Count];
            for (var c = 0; c < widths.Length; c++)
            {
                var width = _shaper.Shape(_doc.Columns[c].Label, HeaderSize, true, _rtl).Width;
                foreach (var row in _doc.Groups.SelectMany(g => g.Rows).Take(300))
                {
                    width = Math.Max(width, _shaper.Shape(row.Cells[c].Text, BodySize, false, _rtl).Width);
                }
                if (_doc.Totals[c] is { } total)
                {
                    width = Math.Max(width, _shaper.Shape(total.Text, BodySize, true, _rtl).Width);
                }
                widths[c] = Math.Clamp(width + 2 * CellPadX + 1, 36, 260);
            }
            return widths;
        }

        /// <summary>Widths that fill the line: extra room goes to every column alike; a table too
        /// wide is narrowed in proportion (its text wraps).</summary>
        private static double[] Fit(double[] natural, double available)
        {
            var sum = natural.Sum();
            if (sum <= 0)
            {
                return natural;
            }
            if (sum <= available)
            {
                var extra = (available - sum) / natural.Length;
                return natural.Select(w => w + extra).ToArray();
            }
            return natural.Select(w => w * available / sum).ToArray();
        }
    }
}
