using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Reports;

/// <summary>
/// How many rows a document holds. A document shown on screen (JSON) holds at most
/// <see cref="ScreenRows"/>: a browser draws it as one table. A file — the PDF, CSV and XLSX — holds
/// the whole selection up to <see cref="FileRows"/>; the PDF is written page by page, so its memory
/// does not grow with its pages (decision p06-form-report-whole-documents). Configured with
/// <c>Erp:Reports:ScreenRows</c> and <c>Erp:Reports:FileRows</c>; the defaults are 2,000 and
/// 200,000 (twice the owner's main-list volume).
/// </summary>
public sealed record ReportLimits(int ScreenRows, int FileRows)
{
    /// <summary>The most rows a file may be configured to hold.</summary>
    public const int MaxFileRows = 1_000_000;

    public static ReportLimits Default { get; } = new(ReportEngine.DocumentRowLimit, ReportEngine.ExportRowLimit);

    /// <summary>The limits from configuration; a value out of range stops the host at start.</summary>
    public static ReportLimits From(IConfiguration configuration)
    {
        var screen = configuration.GetValue("Erp:Reports:ScreenRows", Default.ScreenRows);
        var files = configuration.GetValue("Erp:Reports:FileRows", Default.FileRows);
        if (screen < 1 || files < screen || files > MaxFileRows)
        {
            throw new InvalidOperationException(
                $"Erp:Reports:ScreenRows ({screen}) must be at least 1 and Erp:Reports:FileRows ({files}) between it and {MaxFileRows:N0}.");
        }
        return new ReportLimits(screen, files);
    }

    /// <summary>The most rows a document in the format holds: a file's limit for pdf, csv and xlsx,
    /// the screen's for json.</summary>
    public int For(string format) => format is "pdf" or "csv" or "xlsx" ? FileRows : ScreenRows;
}
