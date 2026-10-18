using System.Text.Json;
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
/// Self-test plant (critic p05 round 9, P8): the personal route POST /api/lists/{key}/views honours
/// an <c>isShared</c> body field, which only the shared route (and its share permission) should do.
/// The product saves the personal view; when the body said <c>isShared: true</c> the plant then makes
/// that view the workspace's shared view, and with <c>isDefault: true</c> the shared default in place
/// of the one there was, as P8's six lines did in the handler. Hosted only by the list view
/// self-test's planted host, over the G2 environment's database.
/// </summary>
public sealed partial class SharedViewBypassModule : ErpModule
{
    public const string ConnectionSetting = "Erp:Testing:SharedViewPlantConnection";

    public override string Name => "sharedviewbypass";

    public override void Register(ModuleBuilder module)
    {
        var connection = module.Configuration[ConnectionSetting] ?? throw new InvalidOperationException($"{ConnectionSetting} is not set");
        module.Services.AddSingleton<IStartupFilter>(new Filter(connection));
    }

    /// <summary>Settings that host the plant over <paramref name="env"/>'s database.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = modules + typeof(SharedViewBypassModule).AssemblyQualifiedName,
            [ConnectionSetting] = env.AdminConnectionString,
        };
    }

    [GeneratedRegex("^/api/lists/([^/]+)/views$")]
    private static partial Regex Route();

    private sealed class Filter(string connection) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (!HttpMethods.IsPost(context.Request.Method) || !Route().IsMatch(context.Request.Path.Value ?? ""))
                {
                    await following();
                    return;
                }
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var request = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject;
                context.Request.Body.Position = 0;
                var share = request?["isShared"] is JsonValue s && s.GetValueKind() == JsonValueKind.True;
                var makeDefault = request?["isDefault"] is JsonValue d && d.GetValueKind() == JsonValueKind.True;
                if (!share)
                {
                    await following();
                    return;
                }
                var body = context.Response.Body;
                using var buffer = new MemoryStream();
                context.Response.Body = buffer;
                try
                {
                    await following();
                }
                finally
                {
                    context.Response.Body = body;
                }
                var bytes = buffer.ToArray();
                if (context.Response.StatusCode == StatusCodes.Status201Created && JsonNode.Parse(bytes) is JsonObject view && view["id"]?.GetValue<string>() is { } id)
                {
                    await using var db = new NpgsqlConnection(connection);
                    await db.OpenAsync();
                    await using var transaction = await db.BeginTransactionAsync();
                    await using (var clear = new NpgsqlCommand("""
                        UPDATE lists.saved_views s SET is_default = false
                          FROM lists.saved_views v
                         WHERE v.id = @id AND s.tenant_id = v.tenant_id AND s.list_key = v.list_key AND s.is_shared AND s.is_default AND @default
                        """, db, transaction))
                    {
                        clear.Parameters.AddWithValue("id", Guid.Parse(id));
                        clear.Parameters.AddWithValue("default", makeDefault);
                        await clear.ExecuteNonQueryAsync();
                    }
                    await using (var promote = new NpgsqlCommand(
                        "UPDATE lists.saved_views SET is_shared = true, owner_user_id = NULL, is_default = @default WHERE id = @id", db, transaction))
                    {
                        promote.Parameters.AddWithValue("id", Guid.Parse(id));
                        promote.Parameters.AddWithValue("default", makeDefault);
                        await promote.ExecuteNonQueryAsync();
                    }
                    await transaction.CommitAsync();
                    view["isShared"] = true;
                    view["isDefault"] = makeDefault;
                    bytes = JsonSerializer.SerializeToUtf8Bytes(view);
                    context.Response.ContentLength = bytes.Length;
                }
                await body.WriteAsync(bytes);
            });
            next(app);
        };
    }
}
