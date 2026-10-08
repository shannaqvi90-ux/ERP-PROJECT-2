using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy;

public sealed record TenantDto(Guid Id, string Code, string NameEn, string NameAr, string Status, uint Version);

public sealed record UpdateTenantRequest(string? NameEn, string? NameAr, uint? Version);

internal static class TenancyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        TenantDto? remembered = null;
        group.MapGet("/tenant", async (TenancyDbContext db, HttpContext http, CancellationToken cancellationToken) =>
            {
                if (remembered is not null) return Results.Ok(remembered);
                var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (tenant is null) return (IResult)Problems.NotFound(http);
                remembered = ToDto(tenant);
                return Results.Ok(remembered);
            })
            .WithName("tenancy.tenant.get")
            .WithSummary("The signed-in user's tenant.")
            .RequirePermission(TenancyPermissions.TenantRead);

        group.MapPut("/tenant", UpdateTenant)
            .WithName("tenancy.tenant.update")
            .WithSummary("Rename the signed-in user's tenant (English and Arabic names).")
            .ProducesValidationProblem()
            .RequirePermission(TenancyPermissions.TenantUpdate);
    }

    private static async Task<Results<Ok<TenantDto>, ProblemHttpResult>> GetTenant(TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return tenant is null ? (ProblemHttpResult)Problems.NotFound(http) : TypedResults.Ok(ToDto(tenant));
    }

    private static async Task<Results<Ok<TenantDto>, ProblemHttpResult>> UpdateTenant(
        UpdateTenantRequest request, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("nameEn", request.NameEn).MaxLength("nameEn", request.NameEn, 200)
            .Required("nameAr", request.NameAr).MaxLength("nameAr", request.NameAr, 200)
            .Required("version", request.Version);
        if (!validator.IsValid)
        {
            return (ProblemHttpResult)validator.ToResult();
        }
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return (ProblemHttpResult)Problems.NotFound(http);
        }
        db.Entry(tenant).Property(t => t.Version).OriginalValue = request.Version!.Value;
        tenant.NameEn = request.NameEn!.Trim();
        tenant.NameAr = request.NameAr!.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(tenant));
    }

    private static TenantDto ToDto(Tenant t) => new(t.Id, t.Code, t.NameEn, t.NameAr, t.Status, t.Version);
}
