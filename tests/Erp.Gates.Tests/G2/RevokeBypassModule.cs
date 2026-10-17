using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plant (critic p03 round 9, P4): "Sign out everywhere" with
/// <c>?removePasskeys=true</c> skips its target check, so a helpdesk user strips the
/// Administrator's passkeys. The plant answers that one request shape itself, before the product's
/// pipeline, removing the path's user's passkeys and ending their sessions with no access check at
/// all; every other request
/// (the same endpoint without the value, or with <c>false</c>) reaches the product. Hosted only by
/// the takeover self-test's planted host, over the takeover environment's database.
/// </summary>
public sealed partial class RevokeBypassModule : ErpModule
{
    public const string ConnectionSetting = "Erp:Testing:PlantAdminConnection";

    public override string Name => "revokebypass";

    public override void Register(ModuleBuilder module)
    {
        var connection = module.Configuration[ConnectionSetting] ?? throw new InvalidOperationException($"{ConnectionSetting} is not set");
        module.Services.AddSingleton<IStartupFilter>(new Filter(connection));
    }

    /// <summary>Settings that host the plant over <paramref name="env"/>'s database, next to the
    /// extra modules the environment already hosts.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = modules + typeof(RevokeBypassModule).AssemblyQualifiedName,
            [ConnectionSetting] = env.AdminConnectionString,
        };
    }

    [GeneratedRegex("^/api/identity/users/([0-9a-fA-F-]{36})/sessions/revoke$")]
    private static partial Regex Route();

    private sealed class Filter(string connection) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                var match = Route().Match(context.Request.Path.Value ?? "");
                if (!HttpMethods.IsPost(context.Request.Method) || !match.Success || context.Request.Query["removePasskeys"] != "true")
                {
                    await following();
                    return;
                }
                await using var db = new NpgsqlConnection(connection);
                await db.OpenAsync();
                await using var transaction = await db.BeginTransactionAsync();
                await using (var replica = new NpgsqlCommand("SET LOCAL session_replication_role = replica", db, transaction))
                {
                    await replica.ExecuteNonQueryAsync();
                }
                var id = Guid.Parse(match.Groups[1].Value);
                await using var remove = new NpgsqlCommand("DELETE FROM identity.passkeys WHERE user_id = @id", db, transaction);
                remove.Parameters.AddWithValue("id", id);
                var removed = await remove.ExecuteNonQueryAsync();
                // And signs them out everywhere, as the endpoint does (critic p03 round 10's S1).
                await using var end = new NpgsqlCommand("UPDATE identity.sessions SET revoked_at = now() WHERE user_id = @id AND revoked_at IS NULL", db, transaction);
                end.Parameters.AddWithValue("id", id);
                var ended = await end.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsJsonAsync(new { sessionsEnded = ended, passkeysRemoved = removed });
            });
            next(app);
        };
    }
}
