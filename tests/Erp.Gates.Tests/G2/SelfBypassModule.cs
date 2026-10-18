using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plant (critic p00 round 11, P6): a permission check that passes a request naming the
/// caller's own id in a route value. Hosted only by the G2 self-test's planted host, over the G2
/// environment's database. Modes (<c>Erp:Testing:SelfBypass</c>):
/// <list type="bullet">
/// <item><c>user</c>: any signed-in caller passes when a route value is their own user id
/// ("people may always manage their own record");</item>
/// <item><c>session</c>: when a route value is the id of the session the request runs in;</item>
/// <item><c>holder</c>: when a route value is their own user id and they hold any permission of
/// the endpoint's module (only the grant-exactly-one matrix can see this one).</item>
/// </list>
/// </summary>
public sealed class SelfBypassModule : ErpModule
{
    public const string ModeKey = "Erp:Testing:SelfBypass";

    public override string Name => "selfbypass";

    public override void Register(ModuleBuilder module) =>
        module.Services.AddSingleton<IAuthorizationHandler, SelfBypassHandler>();

    public static Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?> baseSettings, string mode) =>
        new(baseSettings)
        {
            ["Erp:Testing:ExtraModules"] = typeof(SelfBypassModule).AssemblyQualifiedName,
            [ModeKey] = mode,
        };

    private sealed class SelfBypassHandler(IConfiguration configuration) : AuthorizationHandler<PermissionRequirement>
    {
        private readonly string _mode = configuration[ModeKey] ?? "";

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated != true || context.Resource is not HttpContext http)
            {
                return Task.CompletedTask;
            }
            var own = _mode == "session" ? context.User.FindSessionId() : context.User.FindUserId();
            if (own is null || !http.Request.RouteValues.Values.Any(v => string.Equals(v?.ToString(), own.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.CompletedTask;
            }
            var module = OwnIds.ModuleOf(requirement.Permission) + ".";
            if (_mode != "holder" || context.User.FindAll(ErpClaims.Permission).Any(c => c.Value.StartsWith(module, StringComparison.Ordinal)))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
