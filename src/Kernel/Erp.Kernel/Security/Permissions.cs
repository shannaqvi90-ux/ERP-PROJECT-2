using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Security;

/// <summary>A permission key of the form <c>module.resource.action</c>.</summary>
public sealed partial record PermissionDefinition(string Key, string Module, string Resource, string Action)
{
    public static PermissionDefinition Parse(string key)
    {
        var match = KeyRegex().Match(key ?? string.Empty);
        if (!match.Success)
        {
            throw new ArgumentException($"Permission key '{key}' must look like 'module.resource.action' (lower camel case parts).", nameof(key));
        }
        return new PermissionDefinition(key!, match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
    }

    public static bool IsValidKey(string? key) => key is not null && KeyRegex().IsMatch(key);

    /// <summary>Resource string key for the permission's label.</summary>
    public string LabelKey => $"permission.{Key}";

    [GeneratedRegex("^([a-z][a-z0-9]*)\\.([a-z][a-zA-Z0-9]*)\\.([a-z][a-zA-Z0-9]*)$")]
    private static partial Regex KeyRegex();
}

/// <summary>Endpoint metadata: the single permission an endpoint requires.</summary>
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>Endpoint metadata: the endpoint is reachable without signing in, and why. Every such
/// endpoint must also be listed in <c>tests/Gates/anonymous-allowlist.txt</c>.</summary>
public sealed class AnonymousReasonAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>Endpoint metadata: the kind of surface, so the tenant-isolation gate can demand
/// a matching probe for exports, jobs and files.</summary>
public sealed class SurfaceKindAttribute(SurfaceKind kind) : Attribute
{
    public SurfaceKind Kind { get; } = kind;
}

public enum SurfaceKind
{
    Data,
    Export,
    Import,
    Job,
    File,
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Requirement that never succeeds. Used as the fallback policy so an endpoint that
/// declares nothing is denied.</summary>
public sealed class DenyAllRequirement : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Require exactly one permission for this endpoint. Signed-in users whose roles do
    /// not grant it receive 403; anonymous callers receive 401.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!PermissionDefinition.IsValidKey(permission))
        {
            throw new ArgumentException($"Invalid permission key '{permission}'.", nameof(permission));
        }
        builder.WithMetadata(new RequiresPermissionAttribute(permission));
        builder.RequireAuthorization(new AuthorizationPolicyBuilder(SessionAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build());
        builder.ProducesProblem(StatusCodes.Status401Unauthorized);
        builder.ProducesProblem(StatusCodes.Status403Forbidden);
        return builder;
    }

    /// <summary>Mark an endpoint as reachable without signing in. Reviewed: it must also appear in
    /// <c>tests/Gates/anonymous-allowlist.txt</c> or the permissions gate fails.</summary>
    public static TBuilder AllowAnonymousReviewed<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AnonymousReasonAttribute(reason));
        builder.AllowAnonymous();
        return builder;
    }

    public static TBuilder Surface<TBuilder>(this TBuilder builder, SurfaceKind kind)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new SurfaceKindAttribute(kind));
        return builder;
    }
}
