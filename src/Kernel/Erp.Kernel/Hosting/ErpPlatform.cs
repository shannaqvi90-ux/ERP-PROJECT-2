using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Money;
using Erp.Kernel.Security;
using Erp.Kernel.Seeding;
using Erp.Kernel.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Erp.Kernel.Hosting;

public static class ErpPlatform
{
    public const string SignInRateLimit = "sign-in";

    /// <summary>Register the platform and every module. Modules are listed once, in the host.</summary>
    public static WebApplicationBuilder AddErpPlatform(this WebApplicationBuilder builder, IEnumerable<ErpModule> modules)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var catalog = new ModuleCatalog();
        services.AddSingleton(catalog);
        services.AddSingleton(sp => ErpDataSources.BuildApp(configuration, sp.GetServices<IDataSourceObserver>()));
        services.AddErpDbContext<KernelDbContext>(KernelDbContext.SchemaName);

        foreach (var module in modules)
        {
            var descriptor = new ModuleDescriptor(module);
            module.Register(new ModuleBuilder(descriptor, services, configuration));
            catalog.Add(descriptor);
        }

        services.AddSingleton(sp => new StringCatalog(
            new[] { typeof(ErpPlatform).Assembly }.Concat(sp.GetRequiredService<ModuleCatalog>().Modules.Select(m => m.Assembly))));

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<ShellMenu>();
        services.AddSingleton<DatabaseBootstrap>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<SeedRunner>();

        services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<SessionAuthenticationOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.Scheme, _ => { });
        services.AddAuthorizationBuilder()
            // Anything that does not declare a permission (or a reviewed anonymous reason) is denied.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(SessionAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new DenyAllRequirement())
                .Build());
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddProblemDetails();
        services.AddExceptionHandler<ErpExceptionHandler>();
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        services.AddRateLimiter(_ => { });
        services.AddOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var signInPerMinute = config.GetValue("Erp:RateLimits:SignInPerMinute", 30);
            options.AddPolicy(SignInRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = signInPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = async (context, _) =>
                await Problems.Write(context.HttpContext, StatusCodes.Status429TooManyRequests, "request.tooMany");
        });

        services.AddErpOpenApi();
        return builder;
    }

    /// <summary>
    /// Forwarded-header handling, or null when no proxy is configured (the app then uses the TCP
    /// peer address). <c>Erp:Http:KnownProxies</c> lists proxy addresses and
    /// <c>Erp:Http:KnownNetworks</c> proxy networks in CIDR form, comma separated. Only the
    /// nearest hop is honoured, so a client cannot choose its own address by sending the header.
    /// </summary>
    public static ForwardedHeadersOptions? ForwardedHeadersFrom(IConfiguration configuration)
    {
        static IEnumerable<string> Items(string? value) =>
            (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var proxies = Items(configuration["Erp:Http:KnownProxies"]).ToList();
        var networks = Items(configuration["Erp:Http:KnownNetworks"]).ToList();
        if (proxies.Count == 0 && networks.Count == 0)
        {
            return null;
        }
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(System.Net.IPAddress.TryParse(proxy, out var address)
                ? address
                : throw new InvalidOperationException($"Erp:Http:KnownProxies: '{proxy}' is not an IP address."));
        }
        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(network, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Erp:Http:KnownNetworks: '{network}' is not a CIDR network."));
        }
        return options;
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new DecimalStringJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>Build the request pipeline and map every module's endpoints.</summary>
    public static WebApplication UseErpPlatform(this WebApplication app)
    {
        // Behind a reverse proxy the client address (rate limits, session records) comes from
        // X-Forwarded-For, trusted only from the configured proxies.
        if (ForwardedHeadersFrom(app.Configuration) is { } forwarded)
        {
            app.UseForwardedHeaders(forwarded);
        }
        app.UseExceptionHandler();
        app.Use(SecurityHeaders);
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                // Vite fingerprints assets; index.html must always be revalidated.
                ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "index.html"
                    ? "no-cache"
                    : ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache";
            },
        });
        app.UseRouting();
        app.Use((context, next) =>
        {
            // Audit rows written in this request carry its trace id.
            context.RequestServices.GetRequiredService<ErpDbSession>().CorrelationId = context.TraceIdentifier;
            return next(context);
        });
        app.UseAuthentication();
        app.UseMiddleware<CsrfMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        var catalog = app.Services.GetRequiredService<ModuleCatalog>();
        var api = app.MapGroup("/api").AddEndpointFilter<UnitOfWorkFilter>();

        api.MapGet("/health", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            {
                await using var command = dataSource.CreateCommand("SELECT 1");
                await command.ExecuteScalarAsync(cancellationToken);
                return TypedResults.Ok(new HealthResponse("ok"));
            })
            .WithName("platform.health")
            .WithTags("platform")
            .WithSummary("Liveness and database connectivity.")
            .AllowAnonymousReviewed("Container health checks run without a session; returns no tenant data.");

        foreach (var module in catalog.Modules)
        {
            foreach (var (prefix, map) in module.EndpointMaps)
            {
                var group = api.MapGroup("/" + prefix).WithTags(module.Name);
                map(group);
            }
        }

        app.MapOpenApi("/api/openapi/v1.json")
            .AllowAnonymousReviewed("The API description lists routes and shapes only, never data.");

        app.Map("/api/{**rest}", (HttpContext context) => Problems.NotFound(context))
            .ExcludeFromDescription()
            .AllowAnonymousReviewed("Unknown API paths answer 404 instead of the single-page app.");

        app.MapFallbackToFile("index.html")
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
            .AllowAnonymousReviewed("The single-page app shell (static HTML, no data).");

        ValidateEndpoints(app);
        return app;
    }

    /// <summary>Every endpoint must declare exactly one known permission or a reviewed anonymous
    /// reason. The host refuses to start otherwise.</summary>
    private static void ValidateEndpoints(WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<ModuleCatalog>();
        var problems = new List<string>();
        foreach (var endpoint in ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>())
        {
            var permissions = endpoint.Metadata.GetOrderedMetadata<RequiresPermissionAttribute>();
            var anonymous = endpoint.Metadata.GetMetadata<AnonymousReasonAttribute>();
            var name = endpoint.DisplayName ?? endpoint.RoutePattern.RawText;
            if (permissions.Count == 0 && anonymous is null)
            {
                problems.Add($"{name}: declares no permission");
            }
            else if (permissions.Count > 1)
            {
                problems.Add($"{name}: declares {permissions.Count} permissions; exactly one is allowed");
            }
            else if (permissions.Count == 1 && anonymous is not null)
            {
                problems.Add($"{name}: is both anonymous and permissioned");
            }
            else if (permissions.Count == 1 && !catalog.IsPermission(permissions[0].Permission))
            {
                problems.Add($"{name}: permission '{permissions[0].Permission}' is not in any module's catalogue");
            }
            else if (permissions.Count == 1 && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                // .AllowAnonymous() makes ASP.NET Core skip authorization: the permission would never be checked.
                problems.Add($"{name}: declares '{permissions[0].Permission}' but allows anonymous callers, so it is never checked");
            }
            else if (permissions.Count == 1 && !EnforcesPermission(endpoint, permissions[0].Permission))
            {
                // The declaration is metadata only; what ASP.NET Core enforces is the endpoint's
                // authorization policy. A declaration without a matching requirement (for example
                // WithMetadata(new RequiresPermissionAttribute(...)) with .RequireAuthorization())
                // would let any signed-in user through. Use RequirePermission.
                problems.Add($"{name}: declares '{permissions[0].Permission}' but no authorization policy on it requires that permission; use RequirePermission");
            }
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Endpoint authorisation is incomplete:\n" + string.Join("\n", problems));
        }
        ValidateLists(app, catalog);
        ValidateReports(catalog);
    }

    /// <summary>Every permission a report's column, fact or parameter needs beyond the report's own
    /// is in the catalogue. The host refuses to start otherwise (a misspelt key would withhold the
    /// column from everyone, or, checked nowhere, show it to everyone).</summary>
    private static void ValidateReports(ModuleCatalog catalog)
    {
        var problems = new List<string>();
        foreach (var definition in catalog.Reports.Select(r => r.Definition))
        {
            var extras = definition.Columns.Concat(definition.Facts ?? []).Select(c => (Name: c.Key, c.Permission))
                .Concat(definition.Parameters.Select(p => (Name: p.Key, p.Permission)));
            foreach (var (name, permission) in extras)
            {
                if (permission is not null && !catalog.IsPermission(permission))
                {
                    problems.Add($"report '{definition.Key}': '{name}' needs permission '{permission}', which is not in any module's catalogue");
                }
            }
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Report registrations are inconsistent:\n" + string.Join("\n", problems));
        }
    }

    /// <summary>True when an authorization policy attached to the endpoint requires a signed-in
    /// user and exactly the declared permission.</summary>
    public static bool EnforcesPermission(Endpoint endpoint, string permission) =>
        endpoint.Metadata.OfType<AuthorizationPolicy>().Any(policy =>
            policy.Requirements.OfType<PermissionRequirement>().Any(r => r.Permission == permission) &&
            policy.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement>().Any());

    /// <summary>Every registered list is served by a GET endpoint that declares the list's
    /// permission. The host refuses to start otherwise.</summary>
    private static void ValidateLists(WebApplication app, ModuleCatalog catalog)
    {
        var gets = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) == true)
            .Select(e => (Pattern: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), Permission: e.Metadata.GetMetadata<RequiresPermissionAttribute>()?.Permission))
            .ToList();
        var problems = new List<string>();
        foreach (var list in catalog.Lists)
        {
            var endpoint = gets.FirstOrDefault(g => g.Pattern == list.Endpoint);
            if (endpoint.Pattern is null)
            {
                problems.Add($"list '{list.Key}': no GET endpoint {list.Endpoint}");
            }
            else if (endpoint.Permission != list.Permission)
            {
                problems.Add($"list '{list.Key}': endpoint {list.Endpoint} requires '{endpoint.Permission}', the list says '{list.Permission}'");
            }
            foreach (var column in list.Columns.Where(c => c.ValuesFrom is not null && catalog.FindList(c.ValuesFrom) is null))
            {
                problems.Add($"list '{list.Key}': column '{column.Key}' takes its values from '{column.ValuesFrom}', which is not a registered list");
            }
            if (catalog.ListBindings.All(b => b.Definition.Key != list.Key) &&
                catalog.Modules.Select(m => m.ListsServedBy.GetValueOrDefault(list.Key)).FirstOrDefault(s => s is not null) is { } servedBy)
            {
                problems.Add($"list '{list.Key}': served by '{servedBy}', which is not a registered list with a query binding");
            }
            else if (catalog.ListBindings.All(b => b.Definition.Key != list.Key))
            {
                problems.Add($"list '{list.Key}': registered without a query binding; register it with module.List(ListBinding<T>.For(...)) so its endpoint serves search, filters, sort and paging");
            }
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("List registrations are inconsistent:\n" + string.Join("\n", problems));
        }
    }

    private static async Task SecurityHeaders(HttpContext context, Func<Task> next)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers.ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
            "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }
        await next();
    }

    /// <summary>Run a CLI verb (migrate, seed) instead of serving, when one is given.</summary>
    public static async Task<bool> TryRunCommandAsync(this WebApplication app, string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            return false;
        }
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var cancellationToken = lifetime.ApplicationStopping;
        var configuration = app.Services.GetRequiredService<IConfiguration>();
        switch (args[0])
        {
            case "bootstrap":
                await app.Services.GetRequiredService<DatabaseBootstrap>().RunAsync(cancellationToken);
                return true;
            case "migrate":
                if (configuration.GetConnectionString(ConnectionNames.Admin) is not null)
                {
                    await app.Services.GetRequiredService<DatabaseBootstrap>().RunAsync(cancellationToken);
                }
                await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken);
                return true;
            case "seed":
                await app.Services.GetRequiredService<SeedRunner>().RunAsync(PlanFor(Profile(args, configuration), configuration), cancellationToken);
                await app.Services.GetRequiredService<DatabaseMigrator>().RefreshStatisticsAsync(cancellationToken);
                return true;
            case "setup":
                // One step for a fresh or existing database: roles, migrations, then idempotent seed.
                if (configuration.GetConnectionString(ConnectionNames.Admin) is not null)
                {
                    await app.Services.GetRequiredService<DatabaseBootstrap>().RunAsync(cancellationToken);
                }
                await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken);
                await app.Services.GetRequiredService<SeedRunner>().RunAsync(PlanFor(Profile(args, configuration), configuration), cancellationToken);
                await app.Services.GetRequiredService<DatabaseMigrator>().RefreshStatisticsAsync(cancellationToken);
                return true;
            default:
                var catalog = app.Services.GetRequiredService<ModuleCatalog>();
                var command = catalog.Modules.SelectMany(m => m.Commands).FirstOrDefault(c => c.Verb == args[0]);
                if (command is null)
                {
                    var verbs = string.Join(", ", new[] { "bootstrap", "migrate", "seed", "setup" }.Concat(catalog.Modules.SelectMany(m => m.Commands).Select(c => c.Verb)));
                    throw new ArgumentException($"Unknown command '{args[0]}'. Use {verbs}.");
                }
                Environment.ExitCode = await command.Run(app.Services, args.Skip(1).ToList(), cancellationToken);
                return true;
        }
    }

    private static string Profile(string[] args, IConfiguration configuration) =>
        args.Length > 1 && !args[1].StartsWith('-') ? args[1] : configuration["Erp:Seed:Profile"] ?? "demo";

    public static SeedPlan PlanFor(string profile, IConfiguration configuration)
    {
        var password = configuration["Erp:Seed:DemoPassword"] ?? "Demo-Pass-2026";
        var volume = configuration.GetValue("Erp:Seed:Volume", 100_000);
        return profile.ToLower(CultureInfo.InvariantCulture) switch
        {
            "demo" => SeedPlan.Demo(volume, password),
            "minimal" => SeedPlan.Minimal(password),
            _ => throw new ArgumentException($"Unknown seed profile '{profile}'. Use demo or minimal."),
        };
    }
}

public sealed record HealthResponse(string Status);

/// <summary>The navigation the shell shows: module menu entries filtered by the user's
/// permissions.</summary>
public sealed class ShellMenu(ModuleCatalog catalog)
{
    public IReadOnlyList<MenuEntry> For(ClaimsPrincipal user) =>
        catalog.Menu.Where(m => user.HasPermission(m.Permission)).ToList();

    public IReadOnlyList<MenuEntry> For(IReadOnlySet<string> permissions) =>
        catalog.Menu.Where(m => permissions.Contains(m.Permission)).ToList();
}
