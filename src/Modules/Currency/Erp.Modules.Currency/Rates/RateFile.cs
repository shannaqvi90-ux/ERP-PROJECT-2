using System.Globalization;
using Erp.Kernel.Money;

namespace Erp.Modules.Currency.Rates;

/// <summary>
/// Reads an exchange-rate file: delimited text whose first line names the columns. Required:
/// <c>currency</c>, <c>date</c>, <c>rate</c>; optional: <c>quote_currency</c> (also <c>quote</c>)
/// and <c>reference</c>. Values are separated by commas, semicolons or tabs (whichever the header
/// uses), may be quoted with double quotes, and blank lines are skipped. Dates are yyyy-mm-dd or
/// dd/mm/yyyy (the day first, as UAE spreadsheets write it); rates use a dot for decimals, with no
/// thousands separators. Every problem is reported with its line number; the caller saves nothing
/// unless there is none.
/// </summary>
internal static class RateFile
{
    /// <summary>Most line errors reported.</summary>
    public const int ErrorLimit = 50;

    public sealed record Line(int Number, string Currency, string Quote, DateOnly Date, decimal Rate, string? Reference);

    /// <summary>A problem: a validation code (module resources), its line and the offending value.</summary>
    public sealed record Error(int Line, string Code, string Value);

    public sealed record Result(IReadOnlyList<Line> Lines, IReadOnlyList<Error> Errors);

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"];

    public static Result Parse(string text, string defaultQuote, IReadOnlyDictionary<string, bool> enabled)
    {
        var lines = new List<Line>();
        var errors = new List<Error>();
        var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var headerIndex = Array.FindIndex(rows, r => r.Trim().Length > 0);
        if (headerIndex < 0)
        {
            errors.Add(new Error(1, "currencyImportEmpty", ""));
            return new Result(lines, errors);
        }
        var header = rows[headerIndex].TrimStart('﻿');
        var delimiter = header.Contains('\t') ? '\t' : header.Contains(';') ? ';' : ',';
        var columns = Split(header, delimiter).Select(c => c.Trim().ToLowerInvariant().Replace(' ', '_')).ToList();
        int Column(params string[] names) => columns.FindIndex(names.Contains);
        var currencyAt = Column("currency", "currency_code", "code");
        var dateAt = Column("date", "rate_date");
        var rateAt = Column("rate", "exchange_rate");
        var quoteAt = Column("quote_currency", "quote");
        var referenceAt = Column("reference", "source");
        foreach (var (at, name) in new[] { (currencyAt, "currency"), (dateAt, "date"), (rateAt, "rate") })
        {
            if (at < 0)
            {
                errors.Add(new Error(headerIndex + 1, "currencyImportColumnMissing", name));
            }
        }
        if (errors.Count > 0)
        {
            return new Result(lines, errors);
        }
        var seen = new Dictionary<(string, string, DateOnly), int>();
        for (var i = headerIndex + 1; i < rows.Length; i++)
        {
            if (rows[i].Trim().Length == 0)
            {
                continue;
            }
            var number = i + 1;
            if (lines.Count + errors.Count >= CurrencyRules.ImportRowLimit)
            {
                errors.Add(new Error(number, "currencyImportTooManyLines", CurrencyRules.ImportRowLimit.ToString(CultureInfo.InvariantCulture)));
                break;
            }
            var values = Split(rows[i], delimiter);
            string Value(int at) => at >= 0 && at < values.Count ? values[at].Trim() : "";
            var before = errors.Count;
            var currency = Value(currencyAt).ToUpperInvariant();
            var quote = quoteAt >= 0 && Value(quoteAt).Length > 0 ? Value(quoteAt).ToUpperInvariant() : defaultQuote;
            if (!CurrencyCode.IsKnown(currency))
            {
                errors.Add(new Error(number, "currencyImportCode", Value(currencyAt)));
            }
            else if (!enabled.TryGetValue(currency, out var active))
            {
                errors.Add(new Error(number, "currencyImportNotEnabled", currency));
            }
            else if (!active)
            {
                errors.Add(new Error(number, "currencyImportInactive", currency));
            }
            if (!CurrencyCode.IsKnown(quote))
            {
                errors.Add(new Error(number, "currencyImportCode", quote));
            }
            else if (!enabled.ContainsKey(quote))
            {
                errors.Add(new Error(number, "currencyImportNotEnabled", quote));
            }
            else if (quote == currency)
            {
                errors.Add(new Error(number, "currencyImportSameAsQuote", currency));
            }
            var dateText = Value(dateAt);
            var dateOk = DateOnly.TryParseExact(dateText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date >= CurrencyRules.EarliestDate && date <= CurrencyRules.LatestDate;
            if (!dateOk)
            {
                errors.Add(new Error(number, "currencyImportDate", dateText));
            }
            var rateText = Value(rateAt);
            var rateOk = decimal.TryParse(rateText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) && CurrencyRules.IsValidRate(rate);
            if (!rateOk)
            {
                errors.Add(new Error(number, "currencyImportRate", rateText));
            }
            var reference = referenceAt >= 0 && Value(referenceAt).Length > 0 ? Value(referenceAt) : null;
            if (reference is { Length: > CurrencyRules.ReferenceLength })
            {
                errors.Add(new Error(number, "currencyImportReference", reference[..20] + "…"));
            }
            if (errors.Count > before)
            {
                continue;
            }
            if (seen.TryGetValue((currency, quote, date), out var earlier))
            {
                errors.Add(new Error(number, "currencyImportRepeated", earlier.ToString(CultureInfo.InvariantCulture)));
                continue;
            }
            seen[(currency, quote, date)] = number;
            lines.Add(new Line(number, currency, quote, date, rate, reference));
        }
        if (lines.Count == 0 && errors.Count == 0)
        {
            errors.Add(new Error(headerIndex + 1, "currencyImportEmpty", ""));
        }
        return new Result(lines, errors);
    }

    /// <summary>One line's values: separated by <paramref name="delimiter"/>, a value in double quotes
    /// may hold the delimiter, and two double quotes inside it stand for one.</summary>
    internal static List<string> Split(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' && current.ToString().Trim().Length == 0)
            {
                current.Clear();
                quoted = true;
            }
            else if (c == delimiter)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());
        return values;
    }
}
