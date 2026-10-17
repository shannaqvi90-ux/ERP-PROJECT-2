using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plants on identity's lost-device levers (critic p03 round 9 asked for both as gate
/// self-tests, not only functional tests), answered before the product's pipeline on the takeover
/// environment's database:
/// <list type="bullet">
/// <item>P5, removing one passkey by <c>?passkeyId=</c> ignores whose it is: when the product
/// refuses <c>DELETE /api/identity/users/{id}/passkeys?passkeyId=X</c> with 404 (its access check on
/// the path's user passed, but X is not that user's), the plant removes X anyway, whoever holds it.</item>
/// <item>R, a request-body flag that skips the target check: <c>POST /api/identity/users/{id}/password</c>
/// with <c>"removePasskeys": false</c> is accepted for the path's user with no access check at all. The same request with the flag true or left out
/// reaches the product.</item>
/// </list>
/// Hosted only by the takeover self-test's planted host.
/// </summary>
public sealed partial class LostDeviceBypassModule : ErpModule
{
    public override string Name => "lostdevicebypass";

    public override void Register(ModuleBuilder module)
    {
        var connection = module.Configuration[RevokeBypassModule.ConnectionSetting] ?? throw new InvalidOperationException($"{RevokeBypassModule.ConnectionSetting} is not set");
        module.Services.AddSingleton<IStartupFilter>(new Filter(connection));
    }

    /// <summary>Settings that host the plants over <paramref name="env"/>'s database, next to the
    /// extra modules the environment already hosts.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = modules + typeof(LostDeviceBypassModule).AssemblyQualifiedName,
            [RevokeBypassModule.ConnectionSetting] = env.AdminConnectionString,
        };
    }

    [GeneratedRegex("^/api/identity/users/([0-9a-fA-F-]{36})/passkeys$")]
    private static partial Regex PasskeysRoute();

    [GeneratedRegex("^/api/identity/users/([0-9a-fA-F-]{36})/password$")]
    private static partial Regex PasswordRoute();

    private sealed class Filter(string connection) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (HttpMethods.IsDelete(context.Request.Method) && PasskeysRoute().IsMatch(path) &&
                    Guid.TryParse(context.Request.Query["passkeyId"], out var passkeyId))
                {
                    await RemoveAnyonesPasskeyAsync(context, following, passkeyId);
                    return;
                }
                if (HttpMethods.IsPost(context.Request.Method) && PasswordRoute().Match(path) is { Success: true } reset)
                {
                    context.Request.EnableBuffering();
                    var body = await JsonNode.ParseAsync(context.Request.Body) as JsonObject;
                    context.Request.Body.Position = 0;
                    if (body?["removePasskeys"] is JsonValue flag && flag.TryGetValue<bool>(out var remove) && !remove)
                    {
                        await ResetWithoutCheckAsync(context, Guid.Parse(reset.Groups[1].Value));
                        return;
                    }
                }
                await following();
            });
            next(app);
        };

        /// <summary>P5: the product's answer, except that a 404 (the passkey is not the path's
        /// user's) becomes the removal of that passkey, whoever holds it.</summary>
        private async Task RemoveAnyonesPasskeyAsync(HttpContext context, Func<Task> following, Guid passkeyId)
        {
            var original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await following();
            }
            finally
            {
                context.Response.Body = original;
            }
            if (context.Response.StatusCode == StatusCodes.Status404NotFound)
            {
                var removed = await ExecuteAsync("DELETE FROM identity.passkeys WHERE id = @id", passkeyId);
                if (removed > 0)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsJsonAsync(new { removed });
                    return;
                }
            }
            buffer.Position = 0;
            await buffer.CopyToAsync(original);
        }

        /// <summary>R: the reset accepted with no access check. Its mark on the path's user is the
        /// credential's change time only: the planted host shares the takeover environment's
        /// database, and the Administrator it is aimed at must still sign in for the tests after it.</summary>
        private async Task ResetWithoutCheckAsync(HttpContext context, Guid userId)
        {
            await ExecuteAsync("UPDATE identity.user_credentials SET changed_at = now() WHERE id = @id", userId);
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { mustChangePassword = false, sessionsEnded = 0, passkeysRemoved = 0 });
        }

        private async Task<int> ExecuteAsync(string sql, Guid id)
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var transaction = await db.BeginTransactionAsync();
            await using (var replica = new NpgsqlCommand("SET LOCAL session_replication_role = replica", db, transaction))
            {
                await replica.ExecuteNonQueryAsync();
            }
            await using var command = new NpgsqlCommand(sql, db, transaction);
            command.Parameters.AddWithValue("id", id);
            var count = await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return count;
        }
    }
}
