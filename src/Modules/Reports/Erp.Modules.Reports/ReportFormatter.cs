using System.Globalization;
using System.Text;
using Erp.Kernel.Localization;

namespace Erp.Modules.Reports;

/// <summary>
/// Numbers, amounts and dates of a printed document in its language and digits, following the
/// conventions the screens use (web/src/kernel/format.ts, Intl en-AE and ar-AE with the Gregorian
/// calendar): 1,234.50 / ١٬٢٣٤٫٥٠, 4 Oct 2026 / ٠٤‏/١٠‏/٢٠٢٦, times in the workspace's time zone.
/// Amounts are formatted from <see cref="decimal"/>, never through a binary float (CLAUDE.md rule 2).
/// </summary>
public sealed class ReportFormatter
{
    private const string ArabicDigits = "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669";
    private const char Rlm = '\u200F';
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Currency minor units (ISO 4217) for the currencies this market uses most.</summary>
    private static readonly IReadOnlyDictionary<string, int> MinorUnits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["AED"] = 2, ["USD"] = 2, ["EUR"] = 2, ["GBP"] = 2, ["SAR"] = 2, ["INR"] = 2, ["QAR"] = 2, ["PKR"] = 2, ["CNY"] = 2,
        ["OMR"] = 3, ["KWD"] = 3, ["BHD"] = 3, ["JPY"] = 0,
    };

    public ReportFormatter(string language, string numerals, TimeZoneInfo timeZone)
    {
        Language = Languages.IsSupported(language) ? language : Languages.English;
        Numerals = Language == Languages.Arabic && numerals == NumeralSystems.ArabicIndic ? NumeralSystems.ArabicIndic : NumeralSystems.Latin;
        TimeZone = timeZone;
    }

    public string Language { get; }

    /// <summary>The digits shown: the requested ones on Arabic documents, Latin on English ones.</summary>
    public string Numerals { get; }

    public TimeZoneInfo TimeZone { get; }

    public bool Arabic => Language == Languages.Arabic;

    private bool ArabicDigitsShown => Numerals == NumeralSystems.ArabicIndic;

    /// <summary>An integer or count with grouping: 100,004 / ١٠٠٬٠٠٤.</summary>
    public string Integer(long value) => Number(value.ToString(CultureInfo.InvariantCulture), group: true);

    /// <summary>A decimal at its own scale, or at least <paramref name="minScale"/> digits.</summary>
    public string Decimal(decimal value, int minScale = 0)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var scale = dot < 0 ? 0 : text.Length - dot - 1;
        if (scale < minScale)
        {
            text = (dot < 0 ? text + "." : text) + new string('0', minScale - scale);
        }
        return Number(text, group: true);
    }

    /// <summary>An amount with its ISO currency code at the currency's minor units (more if the
    /// amount carries more): AED 1,234.50 / ١٬٢٣٤٫٥٠ AED.</summary>
    public string Money(decimal amount, string currency)
    {
        var code = currency.ToUpperInvariant();
        var number = Decimal(amount, MinorUnits.GetValueOrDefault(code, 2));
        return Arabic ? $"{number} {code}" : number.StartsWith('-') ? $"-{code} {number[1..]}" : $"{code} {number}";
    }

    /// <summary>A calendar date: 4 Oct 2026 in English; 04‏/10‏/2026 in Arabic (as Intl ar-AE writes it).</summary>
    public string Date(DateOnly date) => Arabic
        ? Digits($"{date.Day:00}{Rlm}/{date.Month:00}{Rlm}/{date.Year:0000}")
        : $"{date.Day} {Months[date.Month - 1]} {date.Year:0000}";

    /// <summary>An instant in the workspace's time zone: 4 Oct 2026, 5:31 PM / 04‏/10‏/2026، 5:31 م.</summary>
    public string DateTime(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZone);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var time = $"{hour}:{local.Minute:00}";
        var date = Date(DateOnly.FromDateTime(local.DateTime));
        return Arabic
            ? $"{date}\u060C {Digits(time)} {(local.Hour < 12 ? "\u0635" : "\u0645")}"
            : $"{date}, {time} {(local.Hour < 12 ? "AM" : "PM")}";
    }

    /// <summary>Digits only (no grouping), for codes and reference numbers that follow the digit choice.</summary>
    public string Digits(string text)
    {
        if (!ArabicDigitsShown)
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c is >= '0' and <= '9' ? ArabicDigits[c - '0'] : c);
        }
        return builder.ToString();
    }

    /// <summary>An invariant number text (-1234.5) with grouping, the language's separators and digits.</summary>
    private string Number(string invariant, bool group)
    {
        var negative = invariant.StartsWith('-');
        var unsigned = negative ? invariant[1..] : invariant;
        var dot = unsigned.IndexOf('.');
        var whole = dot < 0 ? unsigned : unsigned[..dot];
        var fraction = dot < 0 ? null : unsigned[(dot + 1)..];
        var groupSeparator = ArabicDigitsShown ? "\u066C" : ",";
        var decimalSeparator = ArabicDigitsShown ? "\u066B" : ".";
        var builder = new StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (group && i > 0 && (whole.Length - i) % 3 == 0)
            {
                builder.Append(groupSeparator);
            }
            builder.Append(whole[i]);
        }
        if (fraction is not null)
        {
            builder.Append(decimalSeparator).Append(fraction);
        }
        var digits = Digits(builder.ToString());
        if (!negative)
        {
            return digits;
        }
        // Arabic documents keep the minus with the number however the line is laid out (Intl
        // writes an Arabic letter mark before it with Arabic digits, a left-to-right mark with Latin).
        return Arabic ? (ArabicDigitsShown ? "\u061C-" : "\u200E-") + digits : "-" + digits;
    }
}
