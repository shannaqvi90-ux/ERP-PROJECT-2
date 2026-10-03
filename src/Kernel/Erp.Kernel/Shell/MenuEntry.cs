namespace Erp.Kernel.Shell;

/// <summary>A navigation entry a module adds to the app shell. The shell shows it only to users
/// holding <see cref="Permission"/>. <see cref="LabelKey"/> is looked up in the module's web
/// string files (<c>web/src/modules/{module}/i18n/{en,ar}.json</c>).</summary>
public sealed record MenuEntry(string Key, string LabelKey, string Path, string Permission, int Order = 100, string? Group = null);
