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
        services.AddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionNames.App)
                                   ?? throw new InvalidOperationException("ConnectionStrings:App is required.");
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (builder.Username != DatabaseRoles.App)
            {
                throw new InvalidOperationException($"The application must connect as {DatabaseRoles.App}, not '{builder.Username}'.");
            }
            builder.ApplicationName ??= "erp-app";
            return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
        });
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

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new DecimalStringJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>Build the request pipeline and map every module's endpoints.</summary>
    public static WebApplication UseErpPlatform(this WebApplication app)
    {
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
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Endpoint authorisation is incomplete:\n" + string.Join("\n", problems));
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
                return true;
            case "setup":
                // One step for a fresh or existing database: roles, migrations, then idempotent seed.
                if (configuration.GetConnectionString(ConnectionNames.Admin) is not null)
                {
                    await app.Services.GetRequiredService<DatabaseBootstrap>().RunAsync(cancellationToken);
                }
                await app.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken);
                await app.Services.GetRequiredService<SeedRunner>().RunAsync(PlanFor(Profile(args, configuration), configuration), cancellationToken);
                return true;
            default:
                throw new ArgumentException($"Unknown command '{args[0]}'. Use bootstrap, migrate, seed or setup.");
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
