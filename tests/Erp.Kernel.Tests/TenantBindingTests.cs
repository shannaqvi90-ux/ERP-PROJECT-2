using System.Security.Claims;
using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Kernel.Tests;

public sealed class TenantBindingTests
{
    private static readonly Guid TenantA = Guid.Parse("0190a000-0000-7000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0190a000-0000-7000-8000-00000000000b");

    private static HttpContext Request(string method, Guid? principalTenant, params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        if (principalTenant is { } tenant)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ErpClaims.TenantId, tenant.ToString())], SessionAuthenticationDefaults.Scheme));
        }
        return context;
    }

    [Fact]
    public void A_permissioned_request_may_bind_only_the_principals_tenant()
    {
        var request = Request("GET", TenantA, new RequiresPermissionAttribute("identity.users.read"));
        Assert.Equal(TenantA, TenantBinding.RequiredTenant(request));
        TenantBinding.Check(request, TenantA);
        Assert.Throws<CrossTenantBindException>(() => TenantBinding.Check(request, TenantB));
    }

    [Fact]
    public void A_principal_without_a_tenant_claim_may_bind_nothing()
    {
        var request = Request("GET", null, new RequiresPermissionAttribute("identity.users.read"));
        request.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ErpClaims.UserId, Guid.NewGuid().ToString())], SessionAuthenticationDefaults.Scheme));
        Assert.Throws<CrossTenantBindException>(() => TenantBinding.Check(request, TenantA));
    }

    [Fact]
    public void Requests_without_a_principal_or_a_permission_bind_what_they_resolved()
    {
        // Sign-in and the session lookup itself (no principal yet), anonymous endpoints, no request.
        TenantBinding.Check(Request("POST", null, new AnonymousReasonAttribute("sign-in")), TenantB);
        TenantBinding.Check(Request("GET", null, new RequiresPermissionAttribute("identity.users.read")), TenantB);
        TenantBinding.Check(Request("POST", TenantA, new AnonymousReasonAttribute("sign-in")), TenantB);
        TenantBinding.Check(null, TenantB);
    }

    [Theory]
    [InlineData("GET", false, true)]
    [InlineData("HEAD", false, true)]
    [InlineData("POST", false, false)]
    [InlineData("PUT", false, false)]
    [InlineData("DELETE", false, false)]
    [InlineData("POST", true, true)]
    public void Reads_and_read_only_operations_run_read_only(string method, bool marked, bool readOnly)
    {
        var request = marked ? Request(method, TenantA, new ReadOnlyOperationAttribute()) : Request(method, TenantA);
        Assert.Equal(readOnly, ReadOnlyRequests.Applies(request));
    }
}
