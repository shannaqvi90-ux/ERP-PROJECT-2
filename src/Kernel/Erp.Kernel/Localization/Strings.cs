using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Localization;

/// <summary>Digit shapes a user may choose for numbers on Arabic screens: Latin digits (0123,
/// <c>latn</c>, the common choice in the UAE) or Arabic-Indic digits (٠١٢٣, <c>arab</c>). The codes
/// are the Unicode numbering-system identifiers browsers accept (<c>ar-AE-u-nu-arab</c>).</summary>
public static class NumeralSystems
{
    public const string Latin = "latn";
    public const string ArabicIndic = "arab";

    public static readonly IReadOnlyList<string> All = [Latin, ArabicIndic];

    public static bool IsSupported(string? numerals) => numerals is Latin or ArabicIndic;
}

/// <summary>Supported languages. English is left-to-right, Arabic right-to-left.</summary>
public static class Languages
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static readonly IReadOnlyList<string> All = [English, Arabic];

    public static bool IsSupported(string? language) => language is English or Arabic;

    /// <summary>Pick the request language: the signed-in user's preference, else the best
    /// supported match from Accept-Language, else English.</summary>
    public static string ForRequest(HttpContext? context)
    {
        if (context is null)
        {
            return English;
        }
        var claim = context.User.FindFirst(ErpClaims.Language)?.Value;
        if (IsSupported(claim))
        {
            return claim!;
        }
        var header = context.Request.Headers.AcceptLanguage.ToString();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = part.Split(';')[0].Trim();
            var primary = tag.Split('-')[0].ToLowerInvariant();
            if (IsSupported(primary))
            {
                return primary;
            }
        }
        return English;
    }
}

/// <summary>
/// Server-side strings (error messages, permission labels). Each module embeds
/// <c>Resources/en.json</c> and <c>Resources/ar.json</c>; the catalog merges them. Every key must
/// exist in both languages (checked at start-up and by the string-parity gate).
/// </summary>
public sealed class StringCatalog
{
    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLanguage;

    public StringCatalog(IEnumerable<Assembly> assemblies)
    {
        var merged = Languages.All.ToDictionary(l => l, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var language in Languages.All)
            {
                var resource = assembly.GetManifestResourceNames()
                    .SingleOrDefault(n => n.EndsWith($".Resources.{language}.json", StringComparison.Ordinal));
                if (resource is null)
                {
                    continue;
                }
                using var stream = assembly.GetManifestResourceStream(resource)!;
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                             ?? throw new InvalidOperationException($"{resource} is empty.");
                foreach (var (key, value) in values)
                {
                    if (!merged[language].TryAdd(key, value))
                    {
                        throw new InvalidOperationException($"String key '{key}' is defined twice ({resource}).");
                    }
                }
            }
        }
        var english = merged[Languages.English].Keys.ToHashSet();
        var arabic = merged[Languages.Arabic].Keys.ToHashSet();
        var missing = english.Except(arabic).Select(k => $"ar:{k}").Concat(arabic.Except(english).Select(k => $"en:{k}")).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("String keys missing a translation: " + string.Join(", ", missing));
        }
        _byLanguage = merged.ToFrozenDictionary(p => p.Key, p => p.Value.ToFrozenDictionary(StringComparer.Ordinal));
    }

    public IEnumerable<string> Keys => _byLanguage[Languages.English].Keys;

    public bool Contains(string key) => _byLanguage[Languages.English].ContainsKey(key);

    /// <summary>Localized text; unknown keys fall back to the key itself so a gap is visible.</summary>
    public string Get(string key, string language, params object?[] args)
    {
        var table = _byLanguage.TryGetValue(language, out var t) ? t : _byLanguage[Languages.English];
        if (!table.TryGetValue(key, out var text))
        {
            return key;
        }
        return args.Length == 0 ? text : MessageFormat.Format(text, language, args);
    }
}
