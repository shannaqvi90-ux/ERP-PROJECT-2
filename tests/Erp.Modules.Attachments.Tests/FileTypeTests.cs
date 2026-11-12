using System.Text;
using Erp.Modules.Attachments.Contracts;

namespace Erp.Modules.Attachments.Tests;

/// <summary>The store and the file checks, without a server.</summary>
public sealed class FileTypeTests
{
    [Theory]
    [InlineData("../../x.pdf", "x.pdf")]
    [InlineData("..\\..\\x.pdf", "x.pdf")]
    [InlineData("C:\\dir\\licence.pdf", "licence.pdf")]
    [InlineData("  spaced name .pdf ", "spaced name .pdf")]
    [InlineData(".hidden.txt", "hidden.txt")]
    [InlineData("a:b*c?.txt", "a_b_c_.txt")]
    [InlineData("tab\tname.txt", "tab_name.txt")]
    [InlineData("عقد.pdf", "عقد.pdf")]
    public void A_name_is_cleaned_of_folders_and_special_characters(string raw, string expected) =>
        Assert.Equal(expected, FileTypes.CleanName(raw, AttachmentLimits.MaxNameLength));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("...")]
    public void A_name_with_nothing_usable_is_refused(string? raw) => Assert.Null(FileTypes.CleanName(raw, AttachmentLimits.MaxNameLength));

    [Fact]
    public void A_long_name_is_shortened_and_keeps_its_extension()
    {
        var name = FileTypes.CleanName(new string('a', 400) + ".pdf", AttachmentLimits.MaxNameLength)!;
        Assert.Equal(AttachmentLimits.MaxNameLength, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void Right_to_left_overrides_that_hide_an_extension_are_replaced()
    {
        var name = FileTypes.CleanName("invoice\u202Efdp.exe", AttachmentLimits.MaxNameLength)!;
        Assert.DoesNotContain('\u202E', name);
        Assert.Null(FileTypes.ForName(name) is { Extension: "pdf" } ? name : null);
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf", true)]
    [InlineData("A.PDF", "application/pdf", true)]
    [InlineData("a.jpeg", "image/jpeg", true)]
    [InlineData("a.txt", "text/plain; charset=utf-8", false)]
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    public void The_type_comes_from_the_extension_on_the_list(string name, string contentType, bool inline)
    {
        var kind = FileTypes.ForName(name)!;
        Assert.Equal(contentType, kind.ContentType);
        Assert.Equal(inline, kind.Inline);
    }

    [Theory]
    [InlineData("a.svg")]
    [InlineData("a.html")]
    [InlineData("a.exe")]
    [InlineData("a.js")]
    [InlineData("a.zip")]
    [InlineData("a.")]
    [InlineData("a")]
    public void Anything_else_is_refused(string name) => Assert.Null(FileTypes.ForName(name));

    [Fact]
    public async Task Signatures_are_checked()
    {
        Task<bool> Matches(string name, byte[] bytes) => FileTypes.MatchesAsync(FileTypes.ForName(name)!, bytes, () => Task.FromResult<Stream?>(new MemoryStream(bytes)));
        Assert.True(await Matches("a.pdf", "%PDF-1.7 rest"u8.ToArray()));
        Assert.False(await Matches("a.pdf", "not a pdf"u8.ToArray()));
        Assert.True(await Matches("a.gif", "GIF89a...."u8.ToArray()));
        Assert.False(await Matches("a.gif", "GIF90a...."u8.ToArray()));
        Assert.True(await Matches("a.webp", "RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.False(await Matches("a.webp", "RIFF\0\0\0\0WAVEfmt "u8.ToArray()));
        Assert.True(await Matches("a.jpg", [0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.True(await Matches("a.doc", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0]));
        Assert.True(await Matches("a.txt", Encoding.UTF8.GetBytes("سلام\r\nhello\tworld")));
        Assert.False(await Matches("a.txt", [0x1B, 0x5B, 0x30]));
        Assert.False(await Matches("a.csv", [0xC3, 0x28]));
        // A long text cut inside an Arabic letter is still text.
        var cut = Encoding.UTF8.GetBytes(new string('a', 8191) + "س")[..8192];
        Assert.True(await Matches("a.txt", cut));
    }
}
