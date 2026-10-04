using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// The text of a response body as a reader would see it, so the isolation gate can judge
/// exports, printed documents and files like JSON: a PDF is read for its page text (by PdfPig,
/// independently of how the product wrote it), its marked-content replacement text
/// (<c>/ActualText</c>) and its document information; right-to-left runs are also given in
/// logical order (a PDF stores Arabic glyphs in visual order); a ZIP container (XLSX, DOCX) is
/// read entry by entry with XML entities decoded; anything else is UTF-8 text. A tenant B value
/// hidden in a compressed stream, a font-encoded string or a spreadsheet cell is therefore seen.
///
/// A report states the moment it was printed. The product declares that stamp in the
/// <c>X-Erp-Printed-At</c> header (ISO instant, then the text printed, URI-escaped); only a stamp
/// that is a real instant within two days of now and a short text with digits is accepted, and
/// only its exact text is replaced, so two otherwise identical answers compare equal however far
/// apart in time they were printed.
/// </summary>
public static partial class ResponseText
{
    public const string PrintedAtHeader = "X-Erp-Printed-At";

    public static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Decode(bytes, response.Content.Headers.ContentType?.MediaType);
        foreach (var stamp in DeclaredStamps(response))
        {
            text = text.Replace(stamp, "<printed-at>", StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>The print stamps a response declares, longest first; refused declarations are ignored.</summary>
    public static IReadOnlyList<string> DeclaredStamps(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(PrintedAtHeader, out var values))
        {
            return [];
        }
        var stamps = new List<string>();
        foreach (var value in values)
        {
            var parts = value.Split(';', 2);
            if (!DateTimeOffset.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                || (at - DateTimeOffset.UtcNow).Duration() > TimeSpan.FromDays(2))
            {
                continue;
            }
            stamps.Add(parts[0].Trim());
            if (parts.Length == 2)
            {
                var printed = Uri.UnescapeDataString(parts[1].Trim());
                if (printed.Length is > 0 and <= 48 && printed.Count(char.IsDigit) >= 4)
                {
                    stamps.Add(printed);
                    // The same text with its bidi marks dropped, as an extractor may give it.
                    stamps.Add(BidiMarks().Replace(printed, ""));
                }
            }
        }
        return stamps.Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length).ToList();
    }

    public static string Decode(byte[] bytes, string? mediaType)
    {
        if (bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) || mediaType == "application/pdf")
        {
            return Pdf(bytes);
        }
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
        {
            return Zip(bytes);
        }
        if (mediaType is not null && mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            return Encoding.Latin1.GetString(bytes);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Page text (as extracted, and with every right-to-left run turned to logical order),
    /// marked-content replacement text, document information, and the raw bytes of every
    /// uncompressed or Flate-compressed stream as Latin-1 (literal strings stay readable).</summary>
    public static string Pdf(byte[] bytes)
    {
        var text = new StringBuilder();
        try
        {
            using var document = PdfDocument.Open(bytes);
            var info = document.Information;
            foreach (var value in new[] { info.Title, info.Author, info.Subject, info.Keywords, info.Creator, info.Producer })
            {
                if (!string.IsNullOrEmpty(value))
                {
                    text.AppendLine(value);
                }
            }
            foreach (var page in document.GetPages())
            {
                var pageText = page.Text;
                text.AppendLine(pageText);
                text.AppendLine(Logical(pageText));
                foreach (var word in page.GetWords())
                {
                    text.Append(word.Text).Append(' ').Append(Logical(word.Text)).Append('\n');
                }
            }
        }
        catch (Exception e)
        {
            text.AppendLine($"<unreadable PDF: {e.GetType().Name}>");
        }
        foreach (var stream in Streams(bytes))
        {
            var raw = Encoding.Latin1.GetString(stream);
            foreach (Match match in ActualTextHex().Matches(raw))
            {
                var hex = string.Concat(match.Groups[1].Value.Where(c => !char.IsWhiteSpace(c)));
                text.AppendLine(Utf16(Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex + "0")));
            }
            text.AppendLine(raw);
        }
        return text.ToString();
    }

    /// <summary>The text with each run of right-to-left letters reversed, so visual order becomes
    /// logical order.</summary>
    public static string Logical(string visual) =>
        RightToLeftRun().Replace(visual, m => new string(m.Value.Reverse().ToArray()));

    private static IEnumerable<byte[]> Streams(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        foreach (Match match in StreamStart().Matches(latin))
        {
            var start = match.Index + match.Length;
            var end = latin.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                continue;
            }
            var data = pdf.AsSpan(start, end - start).ToArray();
            var dictionaryStart = latin.LastIndexOf("<<", match.Index, StringComparison.Ordinal);
            var dictionary = dictionaryStart >= 0 ? latin[dictionaryStart..match.Index] : "";
            if (dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                byte[]? inflated = null;
                try
                {
                    using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
                    using var buffer = new MemoryStream();
                    z.CopyTo(buffer);
                    inflated = buffer.ToArray();
                }
                catch (InvalidDataException)
                {
                    // Not a zlib stream after all: judged raw below.
                }
                yield return inflated ?? data;
            }
            else
            {
                yield return data;
            }
        }
    }

    private static string Utf16(byte[] bytes)
    {
        var offset = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? 2 : 0;
        return Encoding.BigEndianUnicode.GetString(bytes, offset, bytes.Length - offset);
    }

    /// <summary>Every entry name and every text entry (XML with entities decoded).</summary>
    public static string Zip(byte[] bytes)
    {
        var text = new StringBuilder();
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                text.AppendLine(entry.FullName);
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var content = reader.ReadToEnd();
                text.AppendLine(content);
                text.AppendLine(WebUtility.HtmlDecode(content));
            }
        }
        catch (InvalidDataException e)
        {
            text.AppendLine($"<unreadable ZIP: {e.Message}>");
            text.AppendLine(Encoding.Latin1.GetString(bytes));
        }
        return text.ToString();
    }

    [GeneratedRegex(@"(?<!end)stream\r?\n")]
    private static partial Regex StreamStart();

    [GeneratedRegex(@"/ActualText\s*<([0-9A-Fa-f\s]+)>")]
    private static partial Regex ActualTextHex();

    [GeneratedRegex(@"[֐-ࣿיִ-﷿ﹰ-﻿]+")]
    private static partial Regex RightToLeftRun();

    [GeneratedRegex(@"[‎‏؜‪-‮⁦-⁩]")]
    private static partial Regex BidiMarks();
}

/// <summary>What an isolation probe hands the gate: plain text, or a whole response body as
/// <c>body:&lt;media type&gt;;base64,&lt;data&gt;</c>, decoded here like any response.</summary>
public static class ObservedBody
{
    public const string Prefix = "body:";

    public static string Decode(string observed)
    {
        if (!observed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return observed;
        }
        var comma = observed.IndexOf(";base64,", StringComparison.Ordinal);
        if (comma < 0)
        {
            return observed;
        }
        var mediaType = observed[Prefix.Length..comma];
        try
        {
            var bytes = Convert.FromBase64String(observed[(comma + ";base64,".Length)..]);
            return ResponseText.Decode(bytes, mediaType) + "\n" + System.Text.Encoding.Latin1.GetString(bytes);
        }
        catch (FormatException)
        {
            return observed;
        }
    }
}
