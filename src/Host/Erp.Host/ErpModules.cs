using Erp.Kernel.Modules;
using Erp.Modules.Identity;
using Erp.Modules.Tenancy;

namespace Erp.Host;

/// <summary>Every module of the monolith, in dependency order. Adding a module is one line here.</summary>
public static class ErpModules
{
    public static IReadOnlyList<ErpModule> All =>
    [
        new TenancyModule(),
        new IdentityModule(),
    ];

    /// <summary>
    /// The modules to host. In the Testing environment only, the gate suite may add test modules
    /// (for example a deliberately leaky one that proves the isolation gate catches leaks) through
    /// <c>Erp:Testing:ExtraModules</c> (assembly-qualified type names separated by ';').
    /// </summary>
    public static IReadOnlyList<ErpModule> For(IHostEnvironment environment, IConfiguration configuration)
    {
        var modules = All.ToList();
        var extra = configuration["Erp:Testing:ExtraModules"];
        if (!string.IsNullOrWhiteSpace(extra))
        {
            if (!environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("Erp:Testing:ExtraModules is only honoured in the Testing environment.");
            }
            foreach (var name in extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var type = Type.GetType(name, throwOnError: true)!;
                modules.Add((ErpModule)Activator.CreateInstance(type)!);
            }
        }
        return modules;
    }
}
