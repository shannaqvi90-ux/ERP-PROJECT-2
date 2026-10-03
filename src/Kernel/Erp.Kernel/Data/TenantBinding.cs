using System.Diagnostics;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Data;

/// <summary>
/// Where a unit of work's tenant may come from, and how binding is observed.
/// <para>
/// Inside a request to an endpoint that requires a permission, the tenant comes only from the
/// signed-in session: <see cref="ErpDbSession.BeginAsync"/> refuses any other tenant
/// (<see cref="CrossTenantBindException"/>), whatever the endpoint read from the route, query,
/// headers or body. Sign-in, the session lookup and system work (seeding, jobs, operator
/// commands) have no signed-in principal and bind the tenant they resolved themselves.
/// </para>
/// <para>
/// Every binding is published as an activity on <see cref="SourceName"/> (free when nobody
/// listens). The tenant-isolation gate listens and checks each binding made inside a request
/// against the request's principal, so a unit of work created outside dependency injection, which
/// this guard cannot see, is still caught.
/// </para>
/// </summary>
public static class TenantBinding
{
    public const string SourceName = "Erp.Kernel.Session";
    public const string BindActivity = "erp.session.bind";
    public const string TenantTag = "erp.tenant_id";
    public const string ActorKindTag = "erp.actor_kind";
    public const string ReadOnlyActivity = "erp.session.readOnly";

    private static readonly ActivitySource Source = new(SourceName);

    internal static Activity? StartBind(Guid tenantId, string actorKind)
    {
        var activity = Source.StartActivity(BindActivity, ActivityKind.Internal);
        activity?.SetTag(TenantTag, tenantId.ToString());
        activity?.SetTag(ActorKindTag, actorKind);
        return activity;
    }

    internal static Activity? StartReadOnly() => Source.StartActivity(ReadOnlyActivity, ActivityKind.Internal);

    /// <summary>The tenant a request may bind: the signed-in principal's, when the endpoint
    /// requires a permission; null when the request has no such principal (sign-in, the session
    /// lookup itself, anonymous endpoints) or there is no request.</summary>
    public static Guid? RequiredTenant(HttpContext? context)
    {
        if (context?.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequiresPermissionAttribute>() is null)
        {
            return null;
        }
        // A permissioned endpoint reached by an authenticated principal without a tenant claim
        // binds nothing at all (fail closed).
        return context.User.FindTenantId() ?? Guid.Empty;
    }

    /// <summary>Throws when the request may not bind <paramref name="tenantId"/>.</summary>
    internal static void Check(HttpContext? context, Guid tenantId)
    {
        if (RequiredTenant(context) is { } required && required != tenantId)
        {
            throw new CrossTenantBindException();
        }
    }
}

/// <summary>Code serving a signed-in user tried to bind its unit of work to a tenant other than
/// the user's. Answered as not found and logged as an error.</summary>
public sealed class CrossTenantBindException()
    : InvalidOperationException("Refused to bind the unit of work to a tenant other than the signed-in user's.");
