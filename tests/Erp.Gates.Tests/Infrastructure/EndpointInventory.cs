using System.Text.RegularExpressions;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>One HTTP operation the running app exposes.</summary>
public sealed partial record ApiEndpoint(
    string Method,
    string Pattern,
    string? Name,
    IReadOnlyList<string> Permissions,
    string? AnonymousReason,
    SurfaceKind Surface,
    bool InOpenApi)
{
    public string Permission => Permissions.Single();
    public bool IsAnonymous => AnonymousReason is not null;
    public string Key => $"{Method} {Pattern}";
    public bool HasBody => Method is "POST" or "PUT" or "PATCH";

    public IReadOnlyList<string> RouteParameters => ParameterRegex().Matches(Pattern).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The path with every route parameter replaced.</summary>
    public string Path(Func<string, string> valueFor) =>
        ParameterRegex().Replace(Pattern, m => Uri.EscapeDataString(valueFor(m.Groups[1].Value)));

    public override string ToString() => Key;

    [GeneratedRegex(@"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\}")]
    private static partial Regex ParameterRegex();
}

/// <summary>Enumerates endpoints from the running application's routing (not from source).</summary>
public static class EndpointInventory
{
    public static IReadOnlyList<ApiEndpoint> From(IServiceProvider services)
    {
        var source = services.GetRequiredService<EndpointDataSource>();
        var result = new List<ApiEndpoint>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var pattern = "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/');
            var permissions = endpoint.Metadata.GetOrderedMetadata<RequiresPermissionAttribute>().Select(p => p.Permission).ToList();
            var anonymous = endpoint.Metadata.GetMetadata<AnonymousReasonAttribute>()?.Reason;
            var surface = endpoint.Metadata.GetMetadata<SurfaceKindAttribute>()?.Kind ?? SurfaceKind.Data;
            var excluded = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription == true;
            var inOpenApi = !excluded && pattern.StartsWith("/api/", StringComparison.Ordinal) && !pattern.StartsWith("/api/openapi/", StringComparison.Ordinal);
            // An endpoint without method metadata answers every method.
            foreach (var method in methods is { Count: > 0 } ? methods : ["GET", "POST", "PUT", "PATCH", "DELETE"])
            {
                result.Add(new ApiEndpoint(method, pattern, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                    permissions, anonymous, surface, inOpenApi));
            }
        }
        return result.OrderBy(e => e.Pattern, StringComparer.Ordinal).ThenBy(e => e.Method, StringComparer.Ordinal).ToList();
    }
}
