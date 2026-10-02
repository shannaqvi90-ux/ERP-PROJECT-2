using System.Reflection;
using System.Text.RegularExpressions;
using Erp.Kernel.Data;
using Erp.Kernel.Security;
using Erp.Kernel.Seeding;
using Erp.Kernel.Shell;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Modules;

/// <summary>
/// A business area of the modular monolith. A module contributes everything it owns from its own
/// folder through <see cref="Register"/>; the host lists each module once (one registration line).
/// </summary>
public abstract partial class ErpModule
{
    /// <summary>Lower-case module name. Used as route prefix (/api/{name}), database schema and
    /// permission prefix ({name}.resource.action).</summary>
    public abstract string Name { get; }

    /// <summary>PostgreSQL schema the module owns. Defaults to <see cref="Name"/>.</summary>
    public virtual string Schema => Name;

    /// <summary>Contribute services, permissions, data, endpoints, menu, seeders, strings and
    /// tenant-isolation probes.</summary>
    public abstract void Register(ModuleBuilder module);

    internal static bool IsValidName(string name) => NameRegex().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9]{1,30}$")]
    private static partial Regex NameRegex();
}

/// <summary>What a module registered. Built once at start-up and read by the platform.</summary>
public sealed class ModuleDescriptor
{
    internal ModuleDescriptor(ErpModule module)
    {
        Module = module;
    }

    public ErpModule Module { get; }
    public string Name => Module.Name;
    public string Schema => Module.Schema;
    public Assembly Assembly => Module.GetType().Assembly;
    public List<PermissionDefinition> Permissions { get; } = [];
    public List<Type> DbContexts { get; } = [];
    public List<(string Prefix, Action<RouteGroupBuilder> Map)> EndpointMaps { get; } = [];
    public List<MenuEntry> Menu { get; } = [];
    public List<Type> Seeders { get; } = [];
    public List<Type> IsolationProbes { get; } = [];
}

/// <summary>Registration surface handed to <see cref="ErpModule.Register"/>.</summary>
public sealed class ModuleBuilder
{
    private readonly ModuleDescriptor _descriptor;

    internal ModuleBuilder(ModuleDescriptor descriptor, IServiceCollection services, IConfiguration configuration)
    {
        _descriptor = descriptor;
        Services = services;
        Configuration = configuration;
    }

    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    public string Name => _descriptor.Name;

    /// <summary>Declare permissions owned by this module. Keys must be
    /// <c>{module}.{resource}.{action}</c> and have English and Arabic labels in the module's
    /// resource files under <c>permission.{key}</c>.</summary>
    public ModuleBuilder Permissions(params string[] keys)
    {
        foreach (var key in keys)
        {
            var definition = PermissionDefinition.Parse(key);
            if (definition.Module != Name)
            {
                throw new InvalidOperationException($"Permission '{key}' must start with module name '{Name}'.");
            }
            if (_descriptor.Permissions.Any(p => p.Key == key))
            {
                throw new InvalidOperationException($"Permission '{key}' is declared twice.");
            }
            _descriptor.Permissions.Add(definition);
        }
        return this;
    }

    /// <summary>Register the module's own DbContext. It shares the request's tenant-scoped
    /// connection and transaction, and its migrations run as the owner role.</summary>
    public ModuleBuilder DbContext<TContext>() where TContext : ModuleDbContext
    {
        _descriptor.DbContexts.Add(typeof(TContext));
        Services.AddErpDbContext<TContext>(_descriptor.Schema);
        return this;
    }

    /// <summary>Map endpoints under <c>/api/{module}</c>.</summary>
    public ModuleBuilder Endpoints(Action<RouteGroupBuilder> map) => Endpoints(Name, map);

    /// <summary>Map endpoints under <c>/api/{prefix}</c> (for example <c>auth</c>).</summary>
    public ModuleBuilder Endpoints(string prefix, Action<RouteGroupBuilder> map)
    {
        _descriptor.EndpointMaps.Add((prefix, map));
        return this;
    }

    public ModuleBuilder Menu(MenuEntry entry)
    {
        _descriptor.Menu.Add(entry);
        return this;
    }

    /// <summary>Seed data for each tenant (demo volume included). Seeders run as the application
    /// role inside the tenant's transaction, so row-level security and audit apply.</summary>
    public ModuleBuilder Seeder<TSeeder>() where TSeeder : class, ITenantSeeder
    {
        _descriptor.Seeders.Add(typeof(TSeeder));
        Services.AddScoped<TSeeder>();
        return this;
    }

    /// <summary>Register an attack the tenant-isolation gate runs against a surface that is not a
    /// plain HTTP data endpoint (exports, jobs, files).</summary>
    public ModuleBuilder IsolationProbe<TProbe>() where TProbe : class, IIsolationProbe
    {
        _descriptor.IsolationProbes.Add(typeof(TProbe));
        Services.AddScoped<TProbe>();
        return this;
    }
}

/// <summary>All registered modules, in registration order.</summary>
public sealed class ModuleCatalog
{
    private readonly List<ModuleDescriptor> _modules = [];

    public IReadOnlyList<ModuleDescriptor> Modules => _modules;

    public IEnumerable<PermissionDefinition> Permissions => _modules.SelectMany(m => m.Permissions);

    public IEnumerable<Type> DbContexts => _modules.SelectMany(m => m.DbContexts);

    public IEnumerable<MenuEntry> Menu => _modules.SelectMany(m => m.Menu).OrderBy(m => m.Order).ThenBy(m => m.Key, StringComparer.Ordinal);

    public bool IsPermission(string key) => PermissionKeys.Contains(key);

    private HashSet<string>? _permissionKeys;

    /// <summary>All declared permission keys (built once registration is complete).</summary>
    public IReadOnlySet<string> PermissionKeys => _permissionKeys ??= Permissions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    internal void Add(ModuleDescriptor descriptor)
    {
        if (!ErpModule.IsValidName(descriptor.Name))
        {
            throw new InvalidOperationException($"Module name '{descriptor.Name}' must be lower-case letters and digits.");
        }
        if (_modules.Any(m => m.Name == descriptor.Name))
        {
            throw new InvalidOperationException($"Module '{descriptor.Name}' is registered twice.");
        }
        _modules.Add(descriptor);
        _permissionKeys = null;
    }
}
