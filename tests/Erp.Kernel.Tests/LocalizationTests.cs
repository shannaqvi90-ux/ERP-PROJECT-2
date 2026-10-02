using System.Reflection;
using Erp.Kernel.Localization;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("ar-AE,ar;q=0.9,en;q=0.8", "ar")]
    [InlineData("en-GB,en;q=0.9", "en")]
    [InlineData("fr-FR,ar;q=0.5", "ar")]
    [InlineData("", "en")]
    [InlineData("de", "en")]
    public void Request_language_comes_from_accept_language_when_not_signed_in(string header, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = header;
        Assert.Equal(expected, Languages.ForRequest(context));
    }

    [Fact]
    public void Kernel_strings_exist_in_both_languages_and_format_arguments()
    {
        var strings = new StringCatalog([typeof(Languages).Assembly]);
        Assert.Equal("Use at most 5 characters.", strings.Get("validation.maxLength", "en", 5));
        Assert.Equal("استخدم 5 حرفًا على الأكثر.", strings.Get("validation.maxLength", "ar", 5));
        Assert.Equal("unknown.key", strings.Get("unknown.key", "ar"));
    }

    [Fact]
    public void A_key_without_a_translation_stops_the_catalog_from_loading()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new StringCatalog([typeof(LocalizationTests).Assembly]));
        Assert.Contains("ar:only.english", error.Message, StringComparison.Ordinal);
    }
}
