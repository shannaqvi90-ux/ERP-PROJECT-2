using System.Collections.Frozen;
using System.Text.Json;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;

namespace Erp.Modules.Reports;

/// <summary>
/// The web app's strings (<c>web/src/modules/*/i18n/{en,ar}.json</c>), embedded at build time,
/// so a printed document uses exactly the texts the screens show: a list's column titles, a
/// choice's label, a report's title and parameter labels. One catalogue of user-facing text for
/// screen and paper; the string gate keeps English and Arabic in step.
/// </summary>
public sealed class WebStrings : IWebTexts
{
    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _byLanguage;

    public WebStrings() : this(typeof(WebStrings).Assembly)
    {
    }

    internal WebStrings(System.Reflection.Assembly assembly)
    {
        var merged = Languages.All.ToDictionary(l => l, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("web-strings/", StringComparison.Ordinal)))
        {
            var language = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
            if (!merged.TryGetValue(language, out var table))
            {
                continue;
            }
            using var stream = assembly.GetManifestResourceStream(name)!;
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [])
            {
                table[key] = value;
            }
        }
        if (merged[Languages.English].Count == 0)
        {
            throw new InvalidOperationException("The web string files were not embedded (web/src/modules/*/i18n/*.json).");
        }
        _byLanguage = merged.ToFrozenDictionary(p => p.Key, p => p.Value.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private readonly WebStrings? _inner;
    private readonly IReadOnlyDictionary<string, DynamicString>? _own;

    private WebStrings(WebStrings inner, IReadOnlyDictionary<string, DynamicString> own)
    {
        _byLanguage = inner._byLanguage;
        _inner = inner;
        _own = own;
    }

    /// <summary>These texts plus the tenant's own (key, English and Arabic), which win over the product's.</summary>
    public WebStrings With(IReadOnlyDictionary<string, DynamicString> own) => new(_inner ?? this, own);

    /// <inheritdoc/>
    public string? Find(string key, string language) =>
        _byLanguage.TryGetValue(language, out var table) && table.TryGetValue(key, out var text) ? text : null;

    public bool Contains(string key) => _own?.ContainsKey(key) == true || _byLanguage[Languages.English].ContainsKey(key);

    /// <summary>The text in the language (English when the key has no text in it; the key itself
    /// when it has none at all, so a gap shows), with placeholders and plurals filled.</summary>
    public string Get(string key, string language, IReadOnlyDictionary<string, object?>? values = null)
    {
        if (_own is not null && _own.TryGetValue(key, out var own))
        {
            var chosen = language == Languages.Arabic ? (string.IsNullOrWhiteSpace(own.Ar) ? own.En : own.Ar) : (string.IsNullOrWhiteSpace(own.En) ? own.Ar : own.En);
            return values is null ? chosen : MessageFormat.Format(chosen, language, name => values.TryGetValue(name, out var v) ? v : null);
        }
        var table = _byLanguage.TryGetValue(language, out var t) ? t : _byLanguage[Languages.English];
        if (!table.TryGetValue(key, out var text) && !_byLanguage[Languages.English].TryGetValue(key, out text))
        {
            return key;
        }
        return values is null ? text : MessageFormat.Format(text, language, name => values.TryGetValue(name, out var v) ? v : null);
    }
}
