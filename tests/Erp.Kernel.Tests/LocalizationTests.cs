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
        Assert.Equal("Use at most 1 character.", strings.Get("validation.maxLength", "en", 1));
        Assert.Equal("Use between 2 and 200 characters.", strings.Get("validation.length", "en", 2, 200));
        // Arabic plural forms (CLDR): 1 one, 2 two, 3-10 few, 11-99 many, 100+ other.
        Assert.Equal("استخدم حرفًا واحدًا على الأكثر.", strings.Get("validation.maxLength", "ar", 1));
        Assert.Equal("استخدم حرفين على الأكثر.", strings.Get("validation.maxLength", "ar", 2));
        Assert.Equal("استخدم 5 أحرف على الأكثر.", strings.Get("validation.maxLength", "ar", 5));
        Assert.Equal("استخدم 11 حرفًا على الأكثر.", strings.Get("validation.maxLength", "ar", 11));
        Assert.Equal("استخدم 200 حرف على الأكثر.", strings.Get("validation.maxLength", "ar", 200));
        Assert.Equal("استخدم 10 أحرف على الأقل.", strings.Get("validation.passwordTooShort", "ar", 10));
        Assert.Equal("unknown.key", strings.Get("unknown.key", "ar"));
    }

    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 10, "few")]
    [InlineData("ar", 103, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 99, "many")]
    [InlineData("ar", 100, "other")]
    [InlineData("ar", 102, "other")]
    [InlineData("ar", 1.5, "other")]
    public void Plural_categories_follow_the_CLDR_rules(string language, double number, string expected) =>
        Assert.Equal(expected, PluralRules.Select(language, (decimal)number));

    [Fact]
    public void Messages_fill_named_placeholders_and_exact_plural_branches()
    {
        var text = "{n, plural, =0 {no rows in {table}} one {# row in {table}} other {# rows in {table}}}";
        object? Values(string name, int n) => name switch { "n" => n, "table" => "users", _ => null };
        Assert.Equal("no rows in users", MessageFormat.Format(text, "en", name => Values(name, 0)));
        Assert.Equal("1 row in users", MessageFormat.Format(text, "en", name => Values(name, 1)));
        Assert.Equal("100,000 rows in users", MessageFormat.Format(text, "en", name => Values(name, 100_000)));
        Assert.Equal("Hello {name}", MessageFormat.Format("Hello {name}", "en", _ => null));
        Assert.Equal(["n: =0 one other"], MessageFormat.Plurals(text).Select(p => $"{p.Variable}: {string.Join(" ", p.Selectors)}"));
        Assert.Equal(["n", "table"], MessageFormat.Placeholders(text).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_key_without_a_translation_stops_the_catalog_from_loading()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new StringCatalog([typeof(LocalizationTests).Assembly]));
        Assert.Contains("ar:only.english", error.Message, StringComparison.Ordinal);
    }
}
