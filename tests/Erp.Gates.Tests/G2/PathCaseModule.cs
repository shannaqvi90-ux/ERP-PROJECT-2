using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plant (critic p00 round 16, P16): a permission check that passes any signed-in
/// request whose path does not start with <c>/api/</c>, by an ordinal case-sensitive test.
/// ASP.NET Core routing ignores case, so <c>/API/identity/users</c> reaches the same endpoint as
/// <c>/api/identity/users</c> while the plant's test calls it "not an API path". Hosted only by the
/// path-case self-test's planted host; <see cref="Switch"/> turns it on for one run.
/// </summary>
public sealed class PathCaseModule : ErpModule
{
    public override string Name => "pathcase";

    public override void Register(ModuleBuilder module)
    {
        module.Services.AddSingleton<Switch>();
        module.Services.AddSingleton<IAuthorizationHandler, PathCaseHandler>();
    }

    public static Dictionary<string, string?> Settings(IReadOnlyDictionary<string, string?> baseSettings) =>
        new(baseSettings) { ["Erp:Testing:ExtraModules"] = typeof(PathCaseModule).AssemblyQualifiedName };

    /// <summary>Whether the plant is on.</summary>
    public sealed class Switch
    {
        public volatile bool On;
    }

    private sealed class PathCaseHandler(Switch plant) : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (plant.On && context.User.Identity?.IsAuthenticated == true && context.Resource is HttpContext http &&
                !http.Request.Path.Value!.StartsWith("/api/", StringComparison.Ordinal))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
