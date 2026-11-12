using System.Reflection;
using System.Text.RegularExpressions;
using Erp.Kernel.Audit;
using Erp.Kernel.Data;
using Erp.Kernel.Lists;
using Erp.Kernel.Reports;
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

    /// <summary>Readers that return the rows of this module's lists exactly as their endpoints do
    /// (reports print and export them), by list key.</summary>
    public Dictionary<string, ListRowReader> ListRowReaders { get; } = new(StringComparer.Ordinal);

    /// <summary>Reports this module contributes.</summary>
    public List<ReportRegistration> Reports { get; } = [];

    /// <summary>The kinds of records of this module whose history the audit trail shows.</summary>
    public List<AuditRecordType> AuditRecordTypes { get; } = [];

    /// <summary>The kinds of records of this module that tenants may add custom fields to (p09).</summary>
    public List<Erp.Kernel.CustomFields.CustomFieldRecordType> CustomFieldRecordTypes { get; } = [];

    /// <summary>The kinds of records of this module that files can be attached to (p11).</summary>
    public List<Erp.Kernel.Attachments.AttachmentRecordType> AttachmentRecordTypes { get; } = [];
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
    private readonly ModuleCatalog? _earlier;

    internal ModuleBuilder(ModuleDescriptor descriptor, IServiceCollection services, IConfiguration configuration, ModuleCatalog? earlier = null)
    {
        _descriptor = descriptor;
        Services = services;
        Configuration = configuration;
        _earlier = earlier;
    }

    /// <summary>The kinds of audited records registered so far: by the modules registered before
    /// this one, then by this one (the audit module lists them as its log's record types).</summary>
    public IReadOnlyList<AuditRecordType> AuditRecordTypesSoFar =>
        [.. (_earlier?.AuditRecordTypes ?? []), .. _descriptor.AuditRecordTypes];

    /// <summary>The kinds of record that may have custom fields registered so far (modules registered before this one).</summary>
    public IReadOnlyList<Erp.Kernel.CustomFields.CustomFieldRecordType> CustomFieldRecordTypesSoFar =>
        [.. (_earlier?.CustomFieldRecordTypes ?? []), .. _descriptor.CustomFieldRecordTypes];

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

    /// <summary>Let reports print and export one of this module's lists: <paramref name="reader"/>
    /// returns a page of the list's rows exactly as the list's endpoint does (same query contract,
    /// same row shape), inside the caller's unit of work. The reports module pages through it for
    /// <c>/api/reports/lists/{list key}</c>, under the list's own permission.</summary>
    public ModuleBuilder ListRows(string listKey, ListRowReader reader)
    {
        if (_descriptor.Lists.All(l => l.Key != listKey))
        {
            throw new InvalidOperationException($"list '{listKey}': register the list in module '{Name}' before its row reader");
        }
        if (!_descriptor.ListRowReaders.TryAdd(listKey, reader))
        {
            throw new InvalidOperationException($"list '{listKey}': has two row readers");
        }
        return this;
    }

    /// <summary>Register a report (parameters, columns, groupings, totals) whose rows
    /// <typeparamref name="TSource"/> produces; the reports module serves it under the report's
    /// permission. Checked at registration.</summary>
    public ModuleBuilder Report<TSource>(ReportDefinition definition) where TSource : class, IReportSource
    {
        var problems = definition.Problems(Name).ToList();
        if (_descriptor.Reports.Any(r => r.Definition.Key == definition.Key))
        {
            problems.Add($"report '{definition.Key}' is registered twice");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _descriptor.Reports.Add(new ReportRegistration(definition, typeof(TSource), Name));
        Services.AddScoped<TSource>();
        return this;
    }

    /// <summary>Register a table of this module whose records the audit trail shows (every table
    /// the capture trigger records must be registered; the audit gate checks it): what one record is
    /// called, the permission that reads it and its web address. Checked at registration.</summary>
    public ModuleBuilder AuditedRecord(AuditRecordType record)
    {
        var problems = record.Problems(Name, _descriptor.Schema).ToList();
        if (_descriptor.AuditRecordTypes.Any(r => r.Table == record.Table))
        {
            problems.Add($"audited record '{record.Table}' is registered twice");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _descriptor.AuditRecordTypes.Add(record);
        return this;
    }

    /// <summary>Let tenants add custom fields (p09) to a kind of this module's records: the fields
    /// appear in the record's form, in its list (column chooser, filters, search) and in its API.
    /// The module's list must be registered (<paramref name="record"/>.ListKey); its list query and
    /// its model call the kernel's custom field hooks (<c>UseCustomFields</c> on the model; the
    /// list binding needs nothing else).</summary>
    public ModuleBuilder CustomFieldRecord(Erp.Kernel.CustomFields.CustomFieldRecordType record)
    {
        var problems = new List<string>();
        if (!System.Text.RegularExpressions.Regex.IsMatch(record.Table, "^[a-z_][a-z0-9_]{0,62}\\.[a-z_][a-z0-9_]{0,62}$") || !record.Table.StartsWith(_descriptor.Schema + ".", StringComparison.Ordinal))
        {
            problems.Add($"custom field record '{record.Table}': must look like '{_descriptor.Schema}.table'");
        }
        if (_descriptor.CustomFieldRecordTypes.Any(r => r.Table == record.Table))
        {
            problems.Add($"custom field record '{record.Table}' is registered twice");
        }
        if (!PermissionDefinition.IsValidKey(record.ReadPermission))
        {
            problems.Add($"custom field record '{record.Table}': '{record.ReadPermission}' is not a permission key");
        }
        if (!record.ListKey.StartsWith(Name + ".", StringComparison.Ordinal))
        {
            problems.Add($"custom field record '{record.Table}': its list '{record.ListKey}' must be one of module '{Name}'");
        }
        if (record.Names is not null && (record.LookupEndpoint is null || record.LabelProperty is null || record.LabelPropertyAr is null))
        {
            problems.Add($"custom field record '{record.Table}': a record kind that can be pointed at names its list endpoint and its name properties");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _descriptor.CustomFieldRecordTypes.Add(record);
        return this;
    }

    /// <summary>Let files be attached to a kind of this module's records (p11): the attachments module
    /// serves their files under the record's own permissions (<see cref="Erp.Kernel.Attachments.AttachmentRecordType"/>).
    /// The module adds nothing else: no table, no endpoint.</summary>
    public ModuleBuilder AttachmentRecord(Erp.Kernel.Attachments.AttachmentRecordType record)
    {
        var problems = new List<string>();
        if (!System.Text.RegularExpressions.Regex.IsMatch(record.Table, "^[a-z_][a-z0-9_]{0,62}\\.[a-z_][a-z0-9_]{0,62}$") || !record.Table.StartsWith(_descriptor.Schema + ".", StringComparison.Ordinal))
        {
            problems.Add($"attachment record '{record.Table}': must look like '{_descriptor.Schema}.table'");
        }
        if (_descriptor.AttachmentRecordTypes.Any(r => r.Table == record.Table))
        {
            problems.Add($"attachment record '{record.Table}' is registered twice");
        }
        foreach (var permission in new[] { record.ReadPermission, record.WritePermission })
        {
            if (!PermissionDefinition.IsValidKey(permission))
            {
                problems.Add($"attachment record '{record.Table}': '{permission}' is not a permission key");
            }
        }
        if (string.IsNullOrWhiteSpace(record.LabelKey))
        {
            problems.Add($"attachment record '{record.Table}': needs a label key");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _descriptor.AttachmentRecordTypes.Add(record);
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

    /// <summary>Every registered report.</summary>
    public IEnumerable<ReportRegistration> Reports => _modules.SelectMany(m => m.Reports);

    /// <summary>The registered report with this key, or null.</summary>
    public ReportRegistration? FindReport(string key) => Reports.FirstOrDefault(r => r.Definition.Key == key);

    /// <summary>Every registered kind of audited record, in registration order.</summary>
    public IEnumerable<AuditRecordType> AuditRecordTypes => _modules.SelectMany(m => m.AuditRecordTypes);

    /// <summary>Every kind of record that may have custom fields.</summary>
    public IEnumerable<Erp.Kernel.CustomFields.CustomFieldRecordType> CustomFieldRecordTypes => _modules.SelectMany(m => m.CustomFieldRecordTypes);

    /// <summary>Every kind of record that files can be attached to (p11).</summary>
    public IEnumerable<Erp.Kernel.Attachments.AttachmentRecordType> AttachmentRecordTypes => _modules.SelectMany(m => m.AttachmentRecordTypes);

    /// <summary>The registered kind of audited record of this <c>schema.table</c>, or null.</summary>
    public AuditRecordType? FindAuditRecordType(string table) => AuditRecordTypes.FirstOrDefault(r => r.Table == table);

    /// <summary>Every list whose rows reports can print, with its row reader.</summary>
    public IEnumerable<(ListDefinition List, ListRowReader Reader)> PrintableLists =>
        _modules.SelectMany(m => m.ListRowReaders.Select(r => (m.Lists.Single(l => l.Key == r.Key), r.Value)));

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
