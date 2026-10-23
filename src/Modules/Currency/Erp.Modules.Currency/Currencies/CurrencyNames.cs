namespace Erp.Modules.Currency.Currencies;

/// <summary>
/// Arabic names of the currencies a UAE trading company deals in most (the Gulf, the region's
/// trading partners and the comparison dataset's fifty currencies), written for this product. A
/// currency enabled from the screen starts with the browser's own Arabic name (and the person may
/// change it); these are the names the seeded workspaces start with, and the API's default when an
/// enable request leaves the Arabic name out. A currency not listed here needs its Arabic name given.
/// </summary>
public static class CurrencyNames
{
    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AED"] = "درهم إماراتي",
        ["USD"] = "دولار أمريكي",
        ["EUR"] = "يورو",
        ["GBP"] = "جنيه إسترليني",
        ["SAR"] = "ريال سعودي",
        ["OMR"] = "ريال عماني",
        ["KWD"] = "دينار كويتي",
        ["BHD"] = "دينار بحريني",
        ["QAR"] = "ريال قطري",
        ["INR"] = "روبية هندية",
        ["PKR"] = "روبية باكستانية",
        ["CNY"] = "يوان صيني",
        ["JPY"] = "ين ياباني",
        ["CHF"] = "فرنك سويسري",
        ["CAD"] = "دولار كندي",
        ["AUD"] = "دولار أسترالي",
        ["NZD"] = "دولار نيوزيلندي",
        ["SGD"] = "دولار سنغافوري",
        ["HKD"] = "دولار هونغ كونغ",
        ["EGP"] = "جنيه مصري",
        ["JOD"] = "دينار أردني",
        ["TRY"] = "ليرة تركية",
        ["ZAR"] = "راند جنوب أفريقي",
        ["SEK"] = "كرونة سويدية",
        ["NOK"] = "كرونة نرويجية",
        ["DKK"] = "كرونة دنماركية",
        ["PLN"] = "زلوتي بولندي",
        ["CZK"] = "كرونة تشيكية",
        ["HUF"] = "فورنت مجري",
        ["RUB"] = "روبل روسي",
        ["BRL"] = "ريال برازيلي",
        ["MXN"] = "بيزو مكسيكي",
        ["THB"] = "بات تايلندي",
        ["MYR"] = "رينغيت ماليزي",
        ["IDR"] = "روبية إندونيسية",
        ["PHP"] = "بيزو فلبيني",
        ["KRW"] = "وون كوري جنوبي",
        ["BDT"] = "تاكا بنغلاديشية",
        ["LKR"] = "روبية سريلانكية",
        ["NPR"] = "روبية نيبالية",
        ["KES"] = "شلن كيني",
        ["NGN"] = "نايرا نيجيرية",
        ["MAD"] = "درهم مغربي",
        ["TND"] = "دينار تونسي",
        ["IQD"] = "دينار عراقي",
        ["LBP"] = "ليرة لبنانية",
        ["ILS"] = "شيكل إسرائيلي جديد",
        ["AZN"] = "مانات أذربيجاني",
        ["KZT"] = "تينغ كازاخستاني",
        ["UAH"] = "هريفنيا أوكرانية",
        ["VND"] = "دونغ فيتنامي",
        ["YER"] = "ريال يمني",
        ["SYP"] = "ليرة سورية",
        ["LYD"] = "دينار ليبي",
        ["SDG"] = "جنيه سوداني",
        ["DZD"] = "دينار جزائري",
        ["ETB"] = "بر إثيوبي",
        ["AFN"] = "أفغاني",
        ["IRR"] = "ريال إيراني",
    };
}
