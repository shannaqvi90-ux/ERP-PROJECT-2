using System.Reflection;
using System.Text.RegularExpressions;
using Erp.Kernel.Data;
using Erp.Kernel.Lists;
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
    public List<ListDefinition> Lists { get; } = [];
    public List<ModuleCommand> Commands { get; } = [];

    /// <summary>Query bindings of the lists above, by list key.</summary>
    public Dictionary<string, IListBinding> ListBindings { get; } = new(StringComparer.Ordinal);

    /// <summary>Lists of this module served by another module's list binding: list key to the
    /// key of the list whose rows serve it (see <see cref="ModuleBuilder.List(ListDefinition, string)"/>).</summary>
    public Dictionary<string, string> ListsServedBy { get; } = new(StringComparer.Ordinal);
}

/// <summary>A command-line verb a module adds to the host (<c>Erp.Host &lt;verb&gt; …</c>), for
/// platform operators: it runs instead of serving and the host exits with its result.</summary>
/// <param name="Verb">The first argument, lower case (for example <c>tenant</c>).</param>
/// <param name="Usage">One line shown when the verb is unknown or misused.</param>
/// <param name="Run">Runs with the host's root services and the remaining arguments; returns the exit code.</param>
public sealed record ModuleCommand(string Verb, string Usage, Func<IServiceProvider, IReadOnlyList<string>, CancellationToken, Task<int>> Run);

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

    /// <summary>Register a searchable list (columns, search fields, default sort) served by one of
    /// this module's GET endpoints. Checked at start-up against the endpoint and its permission.</summary>
    public ModuleBuilder List(ListDefinition list)
    {
        var problems = list.Problems(Name).ToList();
        if (_descriptor.Lists.Any(l => l.Key == list.Key))
        {
            problems.Add($"list '{list.Key}' is registered twice");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _descriptor.Lists.Add(list);
        return this;
    }

    /// <summary>Register a list of this module whose rows belong to another module: the other
    /// module's registered list <paramref name="servedBy"/> lends its query binding (the columns this
    /// definition names keep their bound values; search, filters, sort, paging and grouping work the
    /// same), and the other module runs the query for this list's endpoint through its public
    /// contract. Resolved once every module is registered; the host refuses to start when the
    /// serving list does not exist or does not bind what this definition needs.</summary>
    public ModuleBuilder List(ListDefinition list, string servedBy)
    {
        if (string.IsNullOrWhiteSpace(servedBy) || servedBy == list.Key)
        {
            throw new InvalidOperationException($"list '{list.Key}': names no other list to serve it");
        }
        List(list);
        _descriptor.ListsServedBy[list.Key] = servedBy;
        return this;
    }

    /// <summary>Add a command-line verb for platform operators (see <see cref="ModuleCommand"/>).</summary>
    public ModuleBuilder Command(ModuleCommand command)
    {
        if (!ErpModule.IsValidName(command.Verb) || ReservedVerbs.Contains(command.Verb))
        {
            throw new InvalidOperationException($"Command verb '{command.Verb}' must be lower-case letters and not a platform verb.");
        }
        _descriptor.Commands.Add(command);
        return this;
    }

    /// <summary>Verbs the platform itself handles.</summary>
    public static readonly System.Collections.Frozen.FrozenSet<string> ReservedVerbs =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(["bootstrap", "migrate", "seed", "setup"], StringComparer.Ordinal);

    /// <summary>Register a searchable list with its query binding: the binding serves the list
    /// query contract (search, filter, sort, keyset paging, grouping) for the list's endpoint, which
    /// reads it back with <see cref="ModuleCatalog.ListBinding{T}"/>. Checked at registration.</summary>
    public ModuleBuilder List<T>(ListBinding<T> binding) where T : class
    {
        var problems = binding.Problems().ToList();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        List(binding.Definition);
        _descriptor.ListBindings[binding.Definition.Key] = binding;
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

    public IEnumerable<ListDefinition> Lists => _modules.SelectMany(m => m.Lists);

    /// <summary>Every registered list's query binding.</summary>
    public IEnumerable<IListBinding> ListBindings => _modules.SelectMany(m => m.ListBindings.Values);

    /// <summary>The registered list with this key, or null.</summary>
    public ListDefinition? FindList(string key) => Lists.FirstOrDefault(l => l.Key == key);

    /// <summary>The query binding of a registered list over rows of <typeparamref name="T"/>.</summary>
    public ListBinding<T> ListBinding<T>(string key) where T : class =>
        _modules.Select(m => m.ListBindings.GetValueOrDefault(key)).OfType<ListBinding<T>>().FirstOrDefault()
        ?? throw new InvalidOperationException($"List '{key}' has no query binding over {typeof(T).Name}.");

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
        ResolveServedLists();
    }

    /// <summary>Give every list served by another module's list its binding, as soon as the
    /// serving list is registered (modules register in any order).</summary>
    private void ResolveServedLists()
    {
        foreach (var module in _modules)
        {
            foreach (var (key, servedBy) in module.ListsServedBy)
            {
                if (module.ListBindings.ContainsKey(key))
                {
                    continue;
                }
                var source = _modules.Select(m => m.ListBindings.GetValueOrDefault(servedBy)).FirstOrDefault(b => b is not null);
                if (source is null)
                {
                    continue;
                }
                var definition = module.Lists.Single(l => l.Key == key);
                var binding = source.ServeAs(definition);
                var problems = binding.Problems().ToList();
                if (problems.Count > 0)
                {
                    throw new InvalidOperationException($"list '{key}' (served by '{servedBy}'):\n" + string.Join("\n", problems));
                }
                module.ListBindings[key] = binding;
            }
        }
    }
}
