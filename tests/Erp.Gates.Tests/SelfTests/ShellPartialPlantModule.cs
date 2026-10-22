using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// The self-test plant of critic p04 round 11, SL1, on the shell's own switch endpoint
/// (<c>PUT /api/identity/me/preferences</c>), for <see cref="G1.ShellAnswers"/>'s partial bodies.
/// The plant keeps, in a temp file of this host's own (outside the process, so no process-state
/// check sees it), the value of each preference field that anyone, in any tenant, sent last; and a
/// write that leaves a field out (Alt+L sends <c>{language}</c> alone, the digits switch
/// <c>{numerals}</c> alone) is answered with that shared value in the field it left out, as a server
/// that "fills unspecified fields from a shared default" would. The stored row stays right: only
/// the answer leaks. Whole-body writes are answered untouched, so a check that sends only whole
/// bodies passes over it (as every G1 check did in round 11).
/// Hosted only by <see cref="G1.G1ShellAnswersTests"/>'s planted host.
/// </summary>
public sealed class ShellPartialPlantModule : ErpModule
{
    /// <summary>The fields the plant fills from what anyone sent last.</summary>
    public static readonly string[] Fields = ["language", "numerals"];

    public override string Name => "shellpartialplant";

    public override void Register(ModuleBuilder module) =>
        module.Services.AddSingleton<IStartupFilter>(new Filter(Path.Combine(Path.GetTempPath(), $"erp-shell-partial-plant-{Guid.NewGuid():N}.json")));

    /// <summary>Settings that host the plant next to the extra modules the environment hosts.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(ShellPartialPlantModule).AssemblyQualifiedName };
    }

    private sealed class Filter(string file) : IStartupFilter
    {
        private readonly Lock _lock = new();

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(() => File.Delete(file));
            app.Use(async (context, following) =>
            {
                if (!HttpMethods.IsPut(context.Request.Method) || context.Request.Path.Value != G1.ArabicSession.PreferencesPath)
                {
                    await following();
                    return;
                }
                context.Request.EnableBuffering();
                var sent = await JsonNode.ParseAsync(context.Request.Body) as JsonObject ?? [];
                context.Request.Body.Position = 0;
                Dictionary<string, string> shared;
                lock (_lock)
                {
                    shared = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))! : [];
                    var left = new Dictionary<string, string>(shared);
                    foreach (var field in Fields)
                    {
                        if (sent[field] is JsonValue value && value.TryGetValue<string>(out var text))
                        {
                            left[field] = text;
                        }
                    }
                    File.WriteAllText(file, JsonSerializer.Serialize(left));
                }
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
                buffer.Position = 0;
                var bytes = buffer.ToArray();
                if (context.Response.StatusCode == StatusCodes.Status200OK && JsonNode.Parse(bytes) is JsonObject answer)
                {
                    foreach (var field in Fields)
                    {
                        if (sent[field] is null && answer.ContainsKey(field) && shared.TryGetValue(field, out var anyones))
                        {
                            answer[field] = anyones;
                        }
                    }
                    bytes = JsonSerializer.SerializeToUtf8Bytes(answer);
                    context.Response.ContentLength = bytes.Length;
                }
                await original.WriteAsync(bytes);
            });
            next(app);
        };
    }
}
