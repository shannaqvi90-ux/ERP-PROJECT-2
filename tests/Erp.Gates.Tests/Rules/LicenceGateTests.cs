using System.Text.Json;
using System.Xml.Linq;
using Erp.Testing;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// CLAUDE.md rule 6: dependencies under MIT, Apache-2.0, BSD or the PostgreSQL licence only.
/// Every resolved NuGet package (direct and transitive, from the restore output) and every npm
/// package in every lockfile is checked. Build-only npm tooling under other permissive licences
/// is accepted only for licence ids listed in tests/Gates/licence-exceptions.txt, which the owner
/// must approve (gauntlet/needs-human.md). Copyleft and unknown licences always fail.
/// </summary>
public sealed class LicenceGateTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "PostgreSQL",
    };

    private static readonly string[] Copyleft = ["GPL", "LGPL", "AGPL", "MPL", "EPL", "CDDL", "EUPL", "SSPL", "CC-BY-SA", "OSL", "CPAL"];

    private static IReadOnlyList<(string Entry, string Reason)> Exceptions => Repo.ReadReviewedList("tests/Gates/licence-exceptions.txt");

    [Fact]
    public void Every_NuGet_package_has_an_allowed_licence()
    {
        var assetsFiles = new[] { "src", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(Repo.PathOf(d), "project.assets.json", SearchOption.AllDirectories))
            .ToList();
        Assert.NotEmpty(assetsFiles);
        var packages = new Dictionary<string, (string Id, string Version, string Folder, string Path)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in assetsFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var folders = document.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToList();
            foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package")
                {
                    continue;
                }
                var parts = library.Name.Split('/');
                var path = library.Value.GetProperty("path").GetString()!;
                var folder = folders.FirstOrDefault(f => Directory.Exists(Path.Combine(f, path))) ?? folders[0];
                packages[library.Name] = (parts[0], parts[1], folder, path);
            }
        }

        var exceptions = Exceptions.Where(e => e.Entry.StartsWith("nuget:", StringComparison.Ordinal)).ToList();
        var problems = new List<string>();
        foreach (var (id, version, folder, path) in packages.Values)
        {
            var nuspec = Directory.EnumerateFiles(Path.Combine(folder, path), "*.nuspec").FirstOrDefault();
            if (nuspec is null)
            {
                problems.Add($"{id} {version}: nuspec not found");
                continue;
            }
            var xml = XDocument.Load(nuspec);
            var metadata = xml.Root!.Elements().First(e => e.Name.LocalName == "metadata");
            var license = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
            var licenseUrl = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "licenseUrl")?.Value;
            string expression;
            if (license is not null && license.Attribute("type")?.Value == "expression")
            {
                expression = license.Value.Trim();
            }
            else if (licenseUrl is not null && licenseUrl.StartsWith("https://licenses.nuget.org/", StringComparison.Ordinal))
            {
                expression = licenseUrl["https://licenses.nuget.org/".Length..];
            }
            else
            {
                var reviewed = exceptions.FirstOrDefault(e => e.Entry == $"nuget:{id}");
                if (reviewed.Entry is null)
                {
                    problems.Add($"{id} {version}: licence is not an SPDX expression ({license?.Value ?? licenseUrl ?? "none"}); review it and list it in tests/Gates/licence-exceptions.txt");
                }
                continue;
            }
            if (!IsAllowed(expression))
            {
                problems.Add($"{id} {version}: {expression}");
            }
        }
        Assert.True(problems.Count == 0, "NuGet licences outside MIT/Apache-2.0/BSD/PostgreSQL:\n" + string.Join("\n", problems));
        Assert.True(packages.Count >= Ratchet.Min("rules.nugetPackagesChecked"), $"{packages.Count} NuGet packages checked");
    }

    [Fact]
    public void Every_npm_package_has_an_allowed_licence()
    {
        // The known lockfiles must exist, and any other lockfile in the repository (a new npm
        // project anywhere outside critics' evidence folders) is checked too.
        var known = new[]
        {
            Repo.PathOf("web", "package-lock.json"),
            Repo.PathOf("tests", "e2e", "package-lock.json"),
            Repo.PathOf("gauntlet", "compare", "package-lock.json"),
        };
        foreach (var lockfile in known)
        {
            Assert.True(File.Exists(lockfile), $"{lockfile} is missing; commit the lockfile");
        }
        var evidence = Repo.PathOf("gauntlet", "evidence") + Path.DirectorySeparatorChar;
        var lockfiles = known
            .Concat(Directory.EnumerateFiles(Repo.Root, "package-lock.json", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Contains("node_modules") && !f.StartsWith(evidence, StringComparison.Ordinal)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(lockfiles.Count >= Ratchet.Min("rules.npmLockfilesChecked"), $"{lockfiles.Count} npm lockfiles checked");
        var devLicences = Exceptions.Where(e => e.Entry.StartsWith("npm-build-only:", StringComparison.Ordinal))
            .Select(e => e.Entry["npm-build-only:".Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var checkedCount = 0;
        foreach (var lockfile in lockfiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(lockfile));
            foreach (var package in document.RootElement.GetProperty("packages").EnumerateObject())
            {
                if (package.Name.Length == 0 || !package.Name.Contains("node_modules/", StringComparison.Ordinal))
                {
                    continue;
                }
                checkedCount++;
                var name = package.Name[(package.Name.LastIndexOf("node_modules/", StringComparison.Ordinal) + "node_modules/".Length)..];
                var license = package.Value.TryGetProperty("license", out var l) ? l.GetString() ?? "" : "";
                var dev = package.Value.TryGetProperty("dev", out var d) && d.GetBoolean();
                var reviewed = Exceptions.Any(e => e.Entry == $"npm:{name}");
                if (IsAllowed(license) || reviewed)
                {
                    continue;
                }
                if (dev && !IsCopyleft(license) && license.Length > 0 && devLicences.Contains(license))
                {
                    continue;
                }
                problems.Add($"{Path.GetFileName(Path.GetDirectoryName(lockfile))}: {name} ({(dev ? "build-only" : "runtime")}): '{license}'");
            }
        }
        Assert.True(problems.Count == 0, "npm licences outside the allowlist:\n" + string.Join("\n", problems));
        Assert.True(checkedCount >= Ratchet.Min("rules.npmPackagesChecked"), $"{checkedCount} npm packages checked");
    }

    /// <summary>
    /// Font files kept in the repository (not installed from a package manager) are dependencies
    /// too. Each must be listed in tests/Gates/font-licences.txt with its exact bytes (SHA-256), a
    /// licence the owner approved for fonts (SIL OFL-1.1, gauntlet/needs-human.md #5) and the
    /// licence text beside it, which OFL 1.1 requires to travel with the font.
    /// </summary>
    [Fact]
    public void Every_font_file_in_the_repository_is_reviewed_with_its_licence_beside_it()
    {
        var reviewed = Repo.ReadReviewedList("tests/Gates/font-licences.txt");
        var (problems, checkedCount) = CheckFonts(Repo.Root, reviewed);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedCount >= Ratchet.Min("rules.fontFilesChecked"), $"{checkedCount} font files checked; ratchet minimum {Ratchet.Min("rules.fontFilesChecked")}");
    }

    /// <summary>Licences accepted for font files (the owner's decision of 2026-10-03).</summary>
    private static readonly HashSet<string> FontLicences = new(StringComparer.Ordinal) { "OFL-1.1" };

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".woff", ".woff2", ".ttc", ".eot"];

    public static (List<string> Problems, int Checked) CheckFonts(string root, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
        var skip = new[] { "node_modules", "bin", "obj", "dist", ".git", "test-results", "playwright-report" };
        var evidence = Path.Combine(root, "gauntlet", "evidence") + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => FontExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).Any(skip.Contains) && !f.StartsWith(evidence, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var entries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            var words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fields = words.Skip(1).Select(w => w.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
            if (words.Length == 0 || !fields.ContainsKey("sha256") || !fields.ContainsKey("licence") || !fields.ContainsKey("text") || reason.Length == 0)
            {
                problems.Add($"font-licences.txt: '{entry}' needs sha256=, licence=, text= and a reason after #");
                continue;
            }
            entries[words[0]] = fields;
        }
        foreach (var file in files)
        {
            if (!entries.TryGetValue(file, out var fields))
            {
                problems.Add($"{file}: a font file nobody reviewed; list it in tests/Gates/font-licences.txt with its licence");
                continue;
            }
            var full = Path.Combine(root, file);
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(full)));
            if (!string.Equals(hash, fields["sha256"], StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{file}: its bytes changed since review (sha256 {hash}); review the new file");
            }
            if (!FontLicences.Contains(fields["licence"]))
            {
                problems.Add($"{file}: licence {fields["licence"]} is not one the owner approved for fonts");
            }
            var text = Path.Combine(Path.GetDirectoryName(full)!, fields["text"]);
            if (!File.Exists(text) || !File.ReadAllText(text).Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal))
            {
                problems.Add($"{file}: its licence text {fields["text"]} is not beside it");
            }
        }
        foreach (var stale in entries.Keys.Where(k => !files.Contains(k)))
        {
            problems.Add($"font-licences.txt: {stale} is listed but no such file exists");
        }
        return (problems, files.Count);
    }

    [Fact]
    public void The_font_check_catches_an_unreviewed_font_changed_bytes_and_a_missing_licence()
    {
        var root = Directory.CreateTempSubdirectory("font-gate").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "fonts"));
            File.WriteAllBytes(Path.Combine(root, "fonts", "a.woff"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "fonts", "b.ttf"), [4, 5, 6]);
            File.WriteAllBytes(Path.Combine(root, "fonts", "c.woff2"), [7]);
            File.WriteAllText(Path.Combine(root, "fonts", "OFL.txt"), "SIL OPEN FONT LICENSE Version 1.1");
            Directory.CreateDirectory(Path.Combine(root, "node_modules"));
            File.WriteAllBytes(Path.Combine(root, "node_modules", "ignored.woff"), [9]);
            var good = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([1, 2, 3]));
            var (problems, count) = CheckFonts(root,
            [
                ($"fonts/a.woff sha256={good} licence=OFL-1.1 text=OFL.txt", "reviewed"),
                ("fonts/b.ttf sha256=00 licence=OFL-1.1 text=OFL.txt", "bytes changed since"),
                ("fonts/gone.woff sha256=00 licence=OFL-1.1 text=OFL.txt", "deleted"),
            ]);
            Assert.Equal(3, count);
            Assert.Contains(problems, p => p.StartsWith("fonts/b.ttf: its bytes changed", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("fonts/c.woff2: a font file nobody reviewed", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.Contains("fonts/gone.woff is listed", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, p => p.StartsWith("fonts/a.woff", StringComparison.Ordinal));
            File.Delete(Path.Combine(root, "fonts", "OFL.txt"));
            (problems, _) = CheckFonts(root, [($"fonts/a.woff sha256={good} licence=GPL-3.0 text=OFL.txt", "wrong")]);
            Assert.Contains(problems, p => p == "fonts/a.woff: licence GPL-3.0 is not one the owner approved for fonts");
            Assert.Contains(problems, p => p == "fonts/a.woff: its licence text OFL.txt is not beside it");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static bool IsAllowed(string expression)
    {
        var e = expression.Trim().Trim('(', ')');
        if (e.Length == 0 || IsCopyleft(e) && !e.Contains(" OR ", StringComparison.Ordinal))
        {
            return false;
        }
        if (e.Contains(" OR ", StringComparison.Ordinal))
        {
            return e.Split(" OR ").Any(IsAllowed);
        }
        if (e.Contains(" AND ", StringComparison.Ordinal))
        {
            return e.Split(" AND ").All(IsAllowed);
        }
        return Allowed.Contains(e);
    }

    private static bool IsCopyleft(string expression) =>
        Copyleft.Any(c => expression.Contains(c, StringComparison.OrdinalIgnoreCase));
}
