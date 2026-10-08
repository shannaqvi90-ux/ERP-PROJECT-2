using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Kernel.Data;

public static class DataRegistration
{
    /// <summary>Register a module DbContext bound to the scope's <see cref="ErpDbSession"/>.</summary>
    public static IServiceCollection AddErpDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.TryAddScoped<ErpDbSession>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<ErpDbSession>());
        services.TryAddScoped<ICompanyContext>(sp => sp.GetRequiredService<ErpDbSession>());
        services.AddDbContext<TContext>((sp, options) =>
        {
            var session = sp.GetRequiredService<ErpDbSession>();
            ConfigureNpgsql(options, session.Connection, schema);
            options.AddInterceptors(TenantGuardInterceptor.Instance);
        });
        return services;
    }

    /// <summary>Common provider settings for module contexts (runtime and migrations).</summary>
    public static void ConfigureNpgsql(DbContextOptionsBuilder options, DbConnection connection, string schema)
    {
        options.UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));
        options.UseSnakeCaseNamingConvention();
    }

    /// <summary>Common provider settings for module contexts built from a connection string
    /// (migrations as the owner role, design time).</summary>
    public static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString, string schema)
    {
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));
        options.UseSnakeCaseNamingConvention();
    }

    public const string MigrationsHistoryTable = "__ef_migrations_history";
}

/// <summary>Refuses any EF command while the unit of work is not bound to a tenant, so a
/// forgotten <see cref="ErpDbSession.BeginAsync"/> fails loudly instead of silently returning
/// nothing.</summary>
internal sealed class TenantGuardInterceptor : DbCommandInterceptor
{
    public static readonly TenantGuardInterceptor Instance = new();

    private static void Guard(CommandEventData eventData)
    {
        if (eventData.Context is ModuleDbContext context && !context.HasTenant)
        {
            throw new TenantContextMissingException();
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Guard(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Guard(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData);
        return ValueTask.FromResult(result);
    }
}
