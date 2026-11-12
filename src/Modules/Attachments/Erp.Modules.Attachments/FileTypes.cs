using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

namespace Erp.Modules.Attachments;

/// <summary>
/// The kinds of file the server accepts (p11). A file is accepted when its extension is on the list
/// and its own bytes are of that kind (the signature at its start; a Word, Excel or PowerPoint
/// package must hold the parts such a package holds; text must be text). The type the server hands
/// back is the one listed here, never what the browser claimed. Scripts, pages and vector images are not on the list;
/// a zip archive and a mail message (eml, msg) are, kept and served as downloads, never opened.
/// </summary>
internal static class FileTypes
{
    internal sealed record Kind(string Extension, string ContentType, bool Inline, string Family);

    private static readonly ImmutableArray<Kind> Kinds =
    [
        new("pdf", "application/pdf", true, "pdf"),
        new("png", "image/png", true, "png"),
        new("jpg", "image/jpeg", true, "jpeg"),
        new("jpeg", "image/jpeg", true, "jpeg"),
        new("gif", "image/gif", true, "gif"),
        new("webp", "image/webp", true, "webp"),
        new("txt", "text/plain; charset=utf-8", false, "text"),
        new("csv", "text/csv; charset=utf-8", false, "text"),
        new("doc", "application/msword", false, "ole"),
        new("xls", "application/vnd.ms-excel", false, "ole"),
        new("ppt", "application/vnd.ms-powerpoint", false, "ole"),
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false, "word"),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, "excel"),
        new("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", false, "powerpoint"),
        new("zip", "application/zip", false, "zip"),
        new("eml", "message/rfc822", false, "mail"),
        new("msg", "application/vnd.ms-outlook", false, "ole"),
    ];

    /// <summary>The largest text file the screen shows as text (bigger ones are downloaded).</summary>
    public const long TextPreviewBytes = 512 * 1024;

    /// <summary>Whether a file of this name and size can be shown in the page: images and PDF inline, small text as text.</summary>
    public static bool Previewable(string name, long sizeBytes) =>
        ForName(name) is { } kind && (kind.Inline || (kind.Family == "text" && sizeBytes <= TextPreviewBytes));

    /// <summary>The extensions accepted, for messages and the API description.</summary>
    public static ImmutableArray<string> Extensions { get; } = [.. Kinds.Select(k => k.Extension)];

    /// <summary>The kind for a file name's extension, or null when it is not accepted.</summary>
    public static Kind? ForName(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
        {
            return null;
        }
        var extension = name[(dot + 1)..].ToLowerInvariant();
        return Kinds.FirstOrDefault(k => k.Extension == extension);
    }

    /// <summary>Whether the bytes are of the kind. <paramref name="head"/> is the start of the file;
    /// <paramref name="open"/> opens the whole file for the packages that need their directory read.</summary>
    public static async Task<bool> MatchesAsync(Kind kind, byte[] head, Func<Task<Stream?>> open)
    {
        switch (kind.Family)
        {
            case "pdf":
                // The header may follow up to 1024 bytes of leading junk (the PDF specification allows it).
                return Encoding.Latin1.GetString(head, 0, Math.Min(head.Length, 1024)).Contains("%PDF-", StringComparison.Ordinal);
            case "png":
                return Starts(head, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
            case "jpeg":
                return Starts(head, 0xFF, 0xD8, 0xFF);
            case "gif":
                return Starts(head, (byte)'G', (byte)'I', (byte)'F', (byte)'8') && head.Length > 5 && head[4] is (byte)'7' or (byte)'9' && head[5] == (byte)'a';
            case "webp":
                return head.Length >= 12 && Starts(head, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P';
            case "ole":
                return Starts(head, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);
            case "text":
                return IsText(head);
            case "mail":
                return IsMessage(head);
            case "zip":
                return await IsZipAsync(head, open);
            default:
                if (!Starts(head, 0x50, 0x4B, 0x03, 0x04))
                {
                    return false;
                }
                return await IsPackageAsync(kind.Family, open);
        }
    }

    private static bool Starts(byte[] head, params int[] signature) =>
        head.Length >= signature.Length && !signature.Where((b, i) => head[i] != b).Any();

    /// <summary>The bytes the type check reads (a longer file's sample may end inside a character).</summary>
    public const int SampleBytes = 8192;

    /// <summary>Text has no control characters other than tab, line feeds, form feeds and carriage
    /// returns, and is valid UTF-8 (a byte order mark is allowed).</summary>
    private static bool IsText(byte[] head)
    {
        if (head.Any(b => b < 0x20 && b is not (9 or 10 or 12 or 13)))
        {
            return false;
        }
        var strict = new UTF8Encoding(false, true);
        // A sample cut at its limit may end inside a character: allow up to three bytes of it.
        var cuts = head.Length == SampleBytes ? 4 : 1;
        for (var cut = 0; cut < cuts; cut++)
        {
            try
            {
                _ = strict.GetString(head, 0, head.Length - cut);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }
        return false;
    }

    /// <summary>A mail message (.eml) is text that opens with header lines, among them one a message always
    /// carries; no NUL bytes (a binary file renamed .eml is refused).</summary>
    private static bool IsMessage(byte[] head)
    {
        if (head.Any(b => b == 0))
        {
            return false;
        }
        var text = Encoding.Latin1.GetString(head);
        var first = text.AsSpan(0, Math.Min(text.Length, 200));
        var colon = first.IndexOf(':');
        if (colon <= 0 || first[..colon].ToString().Any(c => c <= ' ' || c > '~'))
        {
            return false;
        }
        string[] headers = ["from:", "to:", "date:", "subject:", "message-id:", "received:", "mime-version:", "return-path:", "delivered-to:", "x-"];
        return text.Split('\n').Take(60).Any(line => headers.Any(h => line.StartsWith(h, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A zip archive (never unpacked here): it starts with a local file header (or is an empty archive) and its
    /// directory reads.</summary>
    private static async Task<bool> IsZipAsync(byte[] head, Func<Task<Stream?>> open)
    {
        if (!Starts(head, 0x50, 0x4B, 0x03, 0x04) && !Starts(head, 0x50, 0x4B, 0x05, 0x06))
        {
            return false;
        }
        await using var stream = await open();
        if (stream is null)
        {
            return false;
        }
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<bool> IsPackageAsync(string family, Func<Task<Stream?>> open)
    {
        var folder = family switch { "word" => "word/", "excel" => "xl/", _ => "ppt/" };
        await using var stream = await open();
        if (stream is null)
        {
            return false;
        }
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.GetEntry("[Content_Types].xml") is not null && archive.Entries.Any(e => e.FullName.StartsWith(folder, StringComparison.Ordinal));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// The name kept for display: only the last part of whatever the browser sent (no folder, no drive),
    /// control and path characters replaced, spaces trimmed, shortened to <paramref name="max"/> with
    /// its extension kept. Null when nothing usable is left. The name is only ever shown and offered as
    /// the download's name; it is never used to find or make a file.
    /// </summary>
    public static string? CleanName(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var name = raw.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            // Controls, format characters (bidirectional overrides hide an extension), line separators and
            // the characters Windows and shells treat specially.
            var bad = category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator
                or System.Globalization.UnicodeCategory.OtherNotAssigned or System.Globalization.UnicodeCategory.Surrogate
                || rune.Value is '"' or '*' or ':' or '<' or '>' or '?' or '|' or '/';
            builder.Append(bad ? "_" : rune.ToString());
        }
        name = builder.ToString().Trim().TrimEnd('.', ' ');
        name = name.TrimStart('.');
        if (name.Length == 0)
        {
            return null;
        }
        if (name.Length > max)
        {
            var dot = name.LastIndexOf('.');
            var extension = dot > 0 && name.Length - dot <= 10 ? name[dot..] : "";
            name = name[..(max - extension.Length)].TrimEnd() + extension;
        }
        return name;
    }
}
