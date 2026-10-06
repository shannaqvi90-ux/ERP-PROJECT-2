using System.Text.Json.Serialization;

namespace Erp.Modules.Reports;

/// <summary>One value of a report: the raw value (decimals as strings, dates as ISO text, money as
/// amount and currency) for programs, and the text printed in the document's language and digits.</summary>
/// <param name="Value">Raw value, or null.</param>
/// <param name="Text">The value as printed.</param>
public sealed record ReportCell(object? Value, string Text);

/// <param name="Key">Column key (the row property it comes from).</param>
/// <param name="Label">Header in the document's language.</param>
/// <param name="Type">text, number, money, date, dateTime, boolean, choice or reference.</param>
/// <param name="Align">start or end (numbers and amounts align to the end of the line).</param>
/// <param name="Total">Groups and the report carry this column's total.</param>
public sealed record ReportDocumentColumn(string Key, string Label, string Type, string Align, bool Total);

/// <param name="Label">What the fact is, in the document's language.</param>
/// <param name="Text">The value as printed.</param>
/// <param name="Value">Raw value, or null. Not sent: a parameter's raw value is what the caller
/// typed, and a document repeats only what it can stand behind (see ReportEngine).</param>
public sealed record ReportDocumentFact(string Label, string Text, [property: JsonIgnore] object? Value = null);

/// <summary>A row: one cell per column, in column order.</summary>
public sealed record ReportDocumentRow(IReadOnlyList<ReportCell> Cells);

/// <summary>A group of rows with its count and totals. An ungrouped report has one group without a label.</summary>
/// <param name="Label">The grouping value as printed, or null for an ungrouped report.</param>
/// <param name="Count">Rows in the group.</param>
/// <param name="CountText">"3 rows" in the document's language.</param>
/// <param name="Totals">One cell per column: its total for totalled columns, null otherwise.</param>
public sealed record ReportDocumentGroup(string? Label, int Count, string CountText, IReadOnlyList<ReportDocumentRow> Rows, IReadOnlyList<ReportCell?> Totals);

/// <summary>
/// A report ready to show, print, render as PDF or export: every label and value already in the
/// document's language (which may differ from the screen's) with its direction, so the screen,
/// the printer and the PDF show the same document. Served as JSON by the report endpoints
/// (<c>format=json</c>).
/// </summary>
public sealed record ReportDocument
{
    public required string Key { get; init; }
    public required string Title { get; init; }

    /// <summary>What the document is about (a record's code and name), or null.</summary>
    public string? Subject { get; init; }

    /// <summary>The company (or workspace) issuing the document, in its language.</summary>
    public required string Issuer { get; init; }

    public required string Language { get; init; }

    /// <summary>rtl for Arabic, ltr for English.</summary>
    public required string Direction { get; init; }

    /// <summary>latn or arab: the digits used.</summary>
    public required string Numerals { get; init; }

    /// <summary>The parameters the report ran with, as printed under its title.</summary>
    public required IReadOnlyList<ReportDocumentFact> Parameters { get; init; }

    /// <summary>A record document's heading facts.</summary>
    public required IReadOnlyList<ReportDocumentFact> Facts { get; init; }

    public required IReadOnlyList<ReportDocumentColumn> Columns { get; init; }

    /// <summary>The column the rows are grouped by, or null.</summary>
    public string? GroupBy { get; init; }

    /// <summary>The grouping column's label, or null.</summary>
    public string? GroupLabel { get; init; }

    public required IReadOnlyList<ReportDocumentGroup> Groups { get; init; }

    /// <summary>One cell per column: the report's total for totalled columns, null otherwise.</summary>
    public required IReadOnlyList<ReportCell?> Totals { get; init; }

    /// <summary>Rows in the document.</summary>
    public required int RowCount { get; init; }

    /// <summary>Rows that matched (more than <see cref="RowCount"/> when the document stopped at its limit).</summary>
    public required int MatchCount { get; init; }

    /// <summary>"12 rows" or "the first 2,000 of 100,004 rows" in the document's language.</summary>
    public required string RowCountText { get; init; }

    /// <summary>More rows matched than the document holds.</summary>
    public required bool Truncated { get; init; }

    public required DateTimeOffset PrintedAt { get; init; }

    /// <summary>The print moment as printed.</summary>
    public required string PrintedAtText { get; init; }

    public required string PrintedBy { get; init; }

    /// <summary>Fixed texts of the layout in the document's language (total, page x of y, …).</summary>
    public required ReportDocumentTexts Texts { get; init; }

    [JsonIgnore]
    public bool RightToLeft => Direction == "rtl";
}

/// <param name="Total">"Total".</param>
/// <param name="Printed">"Printed {time} by {name}" filled.</param>
/// <param name="Page">"Page {page} of {pages}" with <c>{page}</c> and <c>{pages}</c> left to fill.</param>
/// <param name="Empty">"Nothing matches."</param>
/// <param name="NoValue">The label of the group of rows without a value.</param>
public sealed record ReportDocumentTexts(string Total, string Printed, string Page, string Empty, string NoValue);
