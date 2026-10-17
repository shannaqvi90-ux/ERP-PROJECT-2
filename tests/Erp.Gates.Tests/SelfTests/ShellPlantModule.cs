using System.Globalization;
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
/// Self-test plants on the shell's own endpoints for <see cref="G1.ShellAnswers"/> (critic p04 round
/// 9, plants L4 and L6, and two of the same kind). Each wraps the product's real endpoint and adds,
/// on the way out, a note of every tenant's activity kept outside the process (a temp file of this
/// host's own), carrying nothing of the other tenant's but how much and when:
/// <list type="bullet">
/// <item>L4: the count of every tenant's Arabic switches (a preferences write in Arabic or with
/// Arabic-Indic digits), sent back as the value of a cookie of its own (<c>erp.tip</c>).</item>
/// <item>L6: the time of anyone's previous Arabic switch, sent in a header
/// (<c>X-Arabic-Previous-Switch</c>, ISO 8601).</item>
/// <item>L6b: the time of anyone's last sign-out as the session's <c>Last-Modified</c> (an HTTP date,
/// a second's resolution).</item>
/// <item>L7: the count of every tenant's sign-ins in a rate-limit style cookie (<c>erp.seen</c>).</item>
/// </list>
/// Hosted only by <see cref="G1.G1ShellAnswersTests"/>'s planted host.
/// </summary>
public sealed class ShellPlantModule : ErpModule
{
    public const string TipCookie = "erp.tip";
    public const string PreviousSwitchHeader = "X-Arabic-Previous-Switch";
    public const string SeenCookie = "erp.seen";

    public override string Name => "shellplant";

    public override void Register(ModuleBuilder module) =>
        module.Services.AddSingleton<IStartupFilter>(new Filter(Path.Combine(Path.GetTempPath(), $"erp-shell-plant-{Guid.NewGuid():N}.txt")));

    /// <summary>Settings that host the plants next to the extra modules the environment hosts.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(ShellPlantModule).AssemblyQualifiedName };
    }

    /// <summary>Every tenant's activity, kept in a file outside the process.</summary>
    private sealed record State(int Switches, DateTimeOffset? Previous, int SignIns, DateTimeOffset? SignedOut);

    private sealed class Filter(string file) : IStartupFilter
    {
        private readonly Lock _lock = new();

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The note lives as long as the planted host.
            app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(() => File.Delete(file));
            app.Use(async (context, following) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (HttpMethods.IsPut(context.Request.Method) && path == G1.ArabicSession.PreferencesPath)
                {
                    context.Request.EnableBuffering();
                    var body = await JsonNode.ParseAsync(context.Request.Body) as JsonObject;
                    context.Request.Body.Position = 0;
                    var arabic = (string?)body?["language"] == "ar" || (string?)body?["numerals"] == "arab";
                    if (arabic)
                    {
                        context.Response.OnStarting(() =>
                        {
                            var state = Read();
                            Write(state with { Switches = state.Switches + 1, Previous = DateTimeOffset.UtcNow });
                            context.Response.Cookies.Append(TipCookie, (state.Switches + 1).ToString(CultureInfo.InvariantCulture), new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Strict });
                            if (state.Previous is { } at)
                            {
                                context.Response.Headers[PreviousSwitchHeader] = at.ToString("O", CultureInfo.InvariantCulture);
                            }
                            return Task.CompletedTask;
                        });
                    }
                }
                else if (HttpMethods.IsGet(context.Request.Method) && path == "/api/auth/session")
                {
                    context.Response.OnStarting(() =>
                    {
                        if (Read().SignedOut is { } at)
                        {
                            context.Response.Headers.LastModified = at.ToString("r", CultureInfo.InvariantCulture);
                        }
                        return Task.CompletedTask;
                    });
                }
                else if (HttpMethods.IsPost(context.Request.Method) && path == "/api/auth/sign-out")
                {
                    context.Response.OnStarting(() =>
                    {
                        var state = Read();
                        Write(state with { SignedOut = DateTimeOffset.UtcNow });
                        return Task.CompletedTask;
                    });
                }
                else if (HttpMethods.IsPost(context.Request.Method) && path == "/api/auth/sign-in")
                {
                    context.Response.OnStarting(() =>
                    {
                        var state = Read();
                        Write(state with { SignIns = state.SignIns + 1 });
                        context.Response.Cookies.Append(SeenCookie, (state.SignIns + 1).ToString(CultureInfo.InvariantCulture), new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Strict });
                        return Task.CompletedTask;
                    });
                }
                await following();
            });
            next(app);
        };

        private State Read()
        {
            lock (_lock)
            {
                return File.Exists(file) ? JsonSerializer.Deserialize<State>(File.ReadAllText(file))! : new State(0, null, 0, null);
            }
        }

        private void Write(State state)
        {
            lock (_lock)
            {
                File.WriteAllText(file, JsonSerializer.Serialize(state));
            }
        }
    }
}
