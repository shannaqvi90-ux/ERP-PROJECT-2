using System.Reflection;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Modules;
using Erp.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Rules;

/// <summary>
/// CLAUDE.md, stack: "A module may use another module only through its public contracts and
/// events, never its tables or internals." Until wave 1 nothing checked it; the project references
/// happened to keep it. Checked here, from the running app's module catalogue:
/// <list type="bullet">
/// <item>a module's assembly (and every contracts assembly) uses no other module's implementation
/// assembly, only <c>Erp.Modules.*.Contracts</c>;</item>
/// <item>every DbContext a module registers lives in that module and maps only tables of the
/// module's own schema (no other module's table is reachable through it);</item>
/// <item>a web module (<c>web/src/modules/&lt;module&gt;/</c>) imports nothing from another web
/// module, and the web kernel imports no module (it finds them by <c>import.meta.glob</c>).</item>
/// </list>
/// </summary>
public sealed partial class ModuleBoundaryGateTests(GateFixture fixture)
{
    private const string ModulePrefix = "Erp.Modules.";
    private const string ContractsSuffix = ".Contracts";

    [Fact]
    public async Task Modules_use_one_another_only_through_their_contracts()
    {
        var catalog = fixture.Env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var modules = catalog.Modules.Where(m => (m.Assembly.GetName().Name ?? "").StartsWith(ModulePrefix, StringComparison.Ordinal)).ToList();
        Assert.True(modules.Count >= 3, $"only {modules.Count} product modules found in the catalogue");
        var problems = new List<string>();
        var checkedCount = 0;

        var contracts = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            checkedCount++;
            problems.AddRange(AssemblyProblems($"module {module.Name}", module.Assembly));
            foreach (var reference in module.Assembly.GetReferencedAssemblies().Where(IsContracts))
            {
                contracts.TryAdd(reference.Name!, Assembly.Load(reference));
            }
        }
        foreach (var (name, assembly) in contracts)
        {
            checkedCount++;
            problems.AddRange(AssemblyProblems($"contracts {name}", assembly));
        }

        await using var scope = fixture.Env.Factory.Services.CreateAsyncScope();
        foreach (var module in modules)
        {
            foreach (var type in module.DbContexts)
            {
                checkedCount++;
                if (type.Assembly != module.Assembly)
                {
                    problems.Add($"module {module.Name} registers {type.FullName} from {type.Assembly.GetName().Name}: a module registers only its own DbContext");
                }
                var model = ((DbContext)scope.ServiceProvider.GetRequiredService(type)).Model;
                foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is not null))
                {
                    var schema = entity.GetSchema() ?? model.GetDefaultSchema() ?? "public";
                    if (schema != module.Schema)
                    {
                        problems.Add($"module {module.Name}: {type.Name} maps {entity.ClrType.Name} to {schema}.{entity.GetTableName()}, outside its schema '{module.Schema}' (read another module's data through its contracts)");
                    }
                }
            }
        }

        var webModules = WebModuleNames();
        foreach (var file in WebSourceFiles())
        {
            checkedCount++;
            problems.AddRange(WebImportProblems(file, File.ReadAllText(file), webModules));
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{checkedCount} assemblies, DbContexts and web files checked for module boundaries");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedCount >= Ratchet.Min("rules.moduleBoundariesChecked"),
            $"{checkedCount} module boundary checks; ratchet minimum {Ratchet.Min("rules.moduleBoundariesChecked")}");
    }

    /// <summary>Self-test: an assembly that uses module implementations (this gate assembly does,
    /// through its test modules) is refused, and so is a web file importing another module or a
    /// kernel file importing a module; contracts, the kernel and the module's own files are not.</summary>
    [Fact]
    public void The_boundary_check_catches_a_module_reaching_into_another_one()
    {
        var planted = AssemblyProblems("module planted", typeof(ModuleBoundaryGateTests).Assembly);
        Assert.Contains(planted, p => p.Contains("uses Erp.Modules.Identity:", StringComparison.Ordinal));
        Assert.DoesNotContain(planted, p => p.Contains("uses Erp.Modules.Identity.Contracts:", StringComparison.Ordinal));
        Assert.Empty(AssemblyProblems("module identity", typeof(Erp.Modules.Identity.IdentityModule).Assembly));

        string[] modules = ["identity", "shell", "tenancy"];
        string Web(params string[] parts) => Repo.PathOf(["web", "src", .. parts]);
        Assert.NotEmpty(WebImportProblems(Web("modules", "tenancy", "AccessPage.tsx"), "import { UserPanel } from \"../identity/UserPanel\";", modules));
        Assert.NotEmpty(WebImportProblems(Web("modules", "tenancy", "x", "deep.ts"), "export { x } from '../../identity/model';", modules));
        Assert.NotEmpty(WebImportProblems(Web("modules", "tenancy", "lazy.ts"), "const m = import(\"../identity/routes\");", modules));
        Assert.NotEmpty(WebImportProblems(Web("kernel", "router.tsx"), "import { App } from \"../modules/shell/App\";", modules));
        Assert.Empty(WebImportProblems(Web("main.tsx"), "import { App } from \"./modules/shell/App\";", modules));
        Assert.Empty(WebImportProblems(Web("modules", "tenancy", "AccessPage.tsx"),
            "import { api } from \"../../kernel/api\";\nimport { emirates } from \"./ui\";\nimport \"./tenancy.css\";", modules));
    }

    /// <summary>References of <paramref name="assembly"/> to another module's implementation.</summary>
    public static IEnumerable<string> AssemblyProblems(string what, Assembly assembly)
    {
        var own = assembly.GetName().Name;
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            var name = reference.Name ?? "";
            if (name.StartsWith(ModulePrefix, StringComparison.Ordinal) && !IsContracts(reference) && name != own)
            {
                yield return $"{what} ({own}) uses {name}: another module is reached only through its {name}{ContractsSuffix} project";
            }
        }
    }

    private static bool IsContracts(AssemblyName reference) =>
        reference.Name is { } name && name.StartsWith(ModulePrefix, StringComparison.Ordinal) && name.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    /// <summary>Imports of a web source file that reach another module (or, from the kernel, any module).</summary>
    public static IEnumerable<string> WebImportProblems(string file, string text, IReadOnlyCollection<string> modules)
    {
        var src = Repo.PathOf("web", "src");
        var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
        var parts = relative.Split('/');
        // The app's entry (main.tsx) mounts the shell; only modules and the kernel are judged.
        string? owner = parts is ["modules", var name, ..] ? name : null;
        if (owner is null && parts is not ["kernel", ..])
        {
            yield break;
        }
        var directory = Path.GetDirectoryName(file)!;
        foreach (Match match in ImportRegex().Matches(text))
        {
            var specifier = match.Groups["spec"].Value;
            if (!specifier.StartsWith('.'))
            {
                continue;
            }
            var target = Path.GetRelativePath(src, Path.GetFullPath(Path.Combine(directory, specifier))).Replace('\\', '/');
            if (target.Split('/') is not ["modules", var into, ..] || !modules.Contains(into))
            {
                continue;
            }
            var line = text[..match.Index].Count(c => c == '\n') + 1;
            if (owner is null)
            {
                yield return $"web/src/{relative}:{line} imports module {into} ({specifier}); the kernel finds modules by import.meta.glob, never by import";
            }
            else if (owner != into)
            {
                yield return $"web/src/{relative}:{line}: module {owner} imports module {into} ({specifier}); share through the kernel or the API instead";
            }
        }
    }

    private static List<string> WebModuleNames() =>
        Directory.GetDirectories(Repo.PathOf("web", "src", "modules")).Select(Path.GetFileName).OfType<string>().ToList();

    /// <summary>Product web sources (tests may render the whole app, so they are not checked).</summary>
    private static IEnumerable<string> WebSourceFiles() =>
        MoneyGateTests.SourceFiles("web/src", "*.ts").Concat(MoneyGateTests.SourceFiles("web/src", "*.tsx"))
            .Where(f => !TestFileRegex().IsMatch(Path.GetFileName(f)))
            .Where(f => !Path.GetRelativePath(Repo.PathOf("web", "src"), f).Replace('\\', '/').StartsWith("test/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

    [GeneratedRegex(@"\.test\.tsx?$")]
    private static partial Regex TestFileRegex();

    [GeneratedRegex(@"(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)[""'](?<spec>[^""']+)[""']")]
    private static partial Regex ImportRegex();
}
