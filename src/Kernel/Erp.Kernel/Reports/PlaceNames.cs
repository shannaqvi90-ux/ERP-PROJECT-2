using System.Collections.Frozen;
using System.Globalization;

namespace Erp.Kernel.Reports;

/// <summary>
/// Place names a printed document shows in its own language: a country by its ISO 3166-1 alpha-2
/// code (stored as "AE"), and the cities and towns of the United Arab Emirates by their usual
/// English spelling. A document in Arabic printed the country as "AE" and a branch in "Al Ain" in
/// Latin letters (critic p06 rounds 4 and 5).
/// </summary>
/// <remarks>
/// Country names come from the Unicode CLDR data that ships with ICU (the runtime's globalization
/// library, already used for every date and number the product formats), read through a culture of
/// that region: its display name in the document's language ends with the region's name in
/// brackets. A code ICU does not know prints as stored. Cities are free text a user types; only the
/// UAE's cities and towns below have an Arabic name here, and any other city prints as typed in
/// both languages (the record's Arabic address, when it has one, still prints in full).
/// </remarks>
public static class PlaceNames
{
    /// <summary>Every region ICU knows, by its code, with its English and Arabic names: built once,
    /// when the type is first used, and never changed (no process-wide state that grows).</summary>
    private static readonly FrozenDictionary<string, LocalText> Countries = BuildCountries();

    /// <summary>A country's name in English and Arabic; the code itself when it is not a known
    /// region (both sides).</summary>
    public static LocalText Country(string code)
    {
        var upper = code.Trim().ToUpperInvariant();
        return Countries.TryGetValue(upper, out var name) ? name : new LocalText(upper, upper);
    }

    private static FrozenDictionary<string, LocalText> BuildCountries()
    {
        // One specific culture of each region (any language; Arabic, then English, preferred).
        var cultureByRegion = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c =>
            {
                try
                {
                    return (Region: new RegionInfo(c.Name).TwoLetterISORegionName, Culture: c.Name);
                }
                catch (ArgumentException)
                {
                    return (Region: "", Culture: c.Name);
                }
            })
            .Where(x => x.Region.Length == 2 && x.Region.All(char.IsAsciiLetterUpper))
            .GroupBy(x => x.Region, StringComparer.Ordinal)
            .Select(g => (Region: g.Key, Culture: g.OrderBy(x => x.Culture.StartsWith("ar-", StringComparison.Ordinal) ? 0 : x.Culture.StartsWith("en-", StringComparison.Ordinal) ? 1 : 2)
                .ThenBy(x => x.Culture, StringComparer.Ordinal).First().Culture))
            .ToList();
        var names = new Dictionary<string, LocalText>(StringComparer.Ordinal);
        // The display name is localised by the current UI culture: set it for these reads only.
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            foreach (var (region, culture) in cultureByRegion)
            {
                var english = new RegionInfo(culture).EnglishName;
                var display = new CultureInfo(culture, useUserOverride: false).DisplayName;
                var open = display.IndexOf('(', StringComparison.Ordinal);
                var close = display.LastIndexOf(')');
                var arabic = open >= 0 && close > open + 1 ? display[(open + 1)..close].Trim() : null;
                // ICU without Arabic names answers in English: the English name then.
                names[region] = new LocalText(english, arabic is not null && arabic.Any(c => c is >= '\u0600' and <= '\u06FF') ? arabic : english);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The cities and towns of the United Arab Emirates a branch or company is usually in,
    /// by their common English spellings, with their Arabic names.</summary>
    private static readonly FrozenDictionary<string, string> UaeCities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Abu Dhabi"] = "أبوظبي",
        ["Dubai"] = "دبي",
        ["Sharjah"] = "الشارقة",
        ["Ajman"] = "عجمان",
        ["Umm Al Quwain"] = "أم القيوين",
        ["Umm Al-Quwain"] = "أم القيوين",
        ["Ras Al Khaimah"] = "رأس الخيمة",
        ["Ras Al-Khaimah"] = "رأس الخيمة",
        ["Fujairah"] = "الفجيرة",
        ["Al Ain"] = "العين",
        ["Khor Fakkan"] = "خورفكان",
        ["Kalba"] = "كلباء",
        ["Dibba"] = "دبا",
        ["Dibba Al Fujairah"] = "دبا الفجيرة",
        ["Dibba Al-Fujairah"] = "دبا الفجيرة",
        ["Jebel Ali"] = "جبل علي",
        ["Hatta"] = "حتا",
        ["Ruwais"] = "الرويس",
        ["Al Ruwais"] = "الرويس",
        ["Madinat Zayed"] = "مدينة زايد",
        ["Liwa"] = "ليوا",
        ["Al Dhaid"] = "الذيد",
        ["Dhaid"] = "الذيد",
        ["Musaffah"] = "مصفح",
        ["Mussafah"] = "مصفح",
        ["Khalifa City"] = "مدينة خليفة",
        ["Masdar City"] = "مدينة مصدر",
        ["Al Hamriyah"] = "الحمرية",
        ["Hamriyah"] = "الحمرية",
        ["Masafi"] = "مسافي",
        ["Ghayathi"] = "غياثي",
        ["Al Sila"] = "السلع",
        ["Sila"] = "السلع",
        ["Dalma"] = "دلما",
        ["Al Jazirah Al Hamra"] = "الجزيرة الحمراء",
        ["Al Rams"] = "الرمس",
        ["Al Madam"] = "المدام",
        ["Mleiha"] = "مليحة",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>A city as typed, with its Arabic name when it is one of the UAE's cities and towns
    /// (spaces between words and case do not matter; common spellings are listed); otherwise the same
    /// text in both languages. Null for no city.</summary>
    public static LocalText? City(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }
        var typed = city.Trim();
        var key = string.Join(' ', typed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return UaeCities.TryGetValue(key, out var arabic) ? new LocalText(typed, arabic) : new LocalText(typed, typed);
    }
}
