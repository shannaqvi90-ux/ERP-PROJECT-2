using Erp.Kernel.Reports;

namespace Erp.Kernel.Tests;

/// <summary>Countries and UAE cities print in the document's language (critic p06 rounds 4 and 5: an
/// Arabic company profile printed the country as "AE" and branch cities in Latin letters).</summary>
public sealed class PlaceNamesTests
{
    [Theory]
    [InlineData("AE", "United Arab Emirates", "الإمارات العربية المتحدة")]
    [InlineData("ae", "United Arab Emirates", "الإمارات العربية المتحدة")]
    [InlineData("SA", "Saudi Arabia", "المملكة العربية السعودية")]
    [InlineData("IN", "India", "الهند")]
    [InlineData("GB", "United Kingdom", "المملكة المتحدة")]
    public void A_country_prints_its_name_in_english_and_arabic(string code, string english, string arabic)
    {
        var name = PlaceNames.Country(code);
        Assert.Equal(english, name.For("en"));
        Assert.Equal(arabic, name.For("ar"));
    }

    [Fact]
    public void An_unknown_code_prints_as_stored_and_the_ui_culture_is_left_as_it_was()
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        Assert.Equal(new LocalText("ZZ", "ZZ"), PlaceNames.Country("ZZ"));
        Assert.Equal(new LocalText("A1", "A1"), PlaceNames.Country("a1"));
        _ = PlaceNames.Country("OM");
        Assert.Same(before, System.Globalization.CultureInfo.CurrentUICulture);
    }

    [Theory]
    [InlineData("Al Ain", "العين")]
    [InlineData("  al   ain ", "العين")]
    [InlineData("Fujairah", "الفجيرة")]
    [InlineData("Abu Dhabi", "أبوظبي")]
    [InlineData("Ras Al Khaimah", "رأس الخيمة")]
    public void A_uae_city_prints_in_arabic_in_an_arabic_document(string typed, string arabic)
    {
        var city = PlaceNames.City(typed)!;
        Assert.Equal(typed.Trim(), city.For("en"));
        Assert.Equal(arabic, city.For("ar"));
    }

    [Fact]
    public void Any_other_city_prints_as_typed_and_no_city_is_none()
    {
        Assert.Equal(new LocalText("Muscat", "Muscat"), PlaceNames.City("Muscat"));
        Assert.Null(PlaceNames.City("  "));
        Assert.Null(PlaceNames.City(null));
    }
}
