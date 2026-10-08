using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy;

/// <summary>The workspace and its settings.</summary>
public sealed record TenantDto(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string Status,
    WorkspaceLanguage DefaultLanguage,
    string TimeZone,
    WeekStart WeekStart,
    IReadOnlyList<string> TimeZones,
    uint Version,
    [property: System.ComponentModel.Description("True when the caller works in every company of the workspace and every branch of each: only then may they change the workspace, which every company shares.")]
    bool EveryCompany);

/// <summary>Rename the workspace and change its settings. Settings left out stay as they are.</summary>
public sealed record UpdateTenantRequest(
    string? NameEn,
    string? NameAr,
    WorkspaceLanguage? DefaultLanguage,
    [property: ApiExample(TenantSettings.DefaultTimeZone)] string? TimeZone,
    WeekStart? WeekStart,
    uint? Version);

internal static class TenancyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/tenant", GetTenant)
            .WithName("tenancy.tenant.get")
            .WithSummary("The signed-in user's workspace and its settings.")
            .RequirePermission(TenancyPermissions.TenantRead);

        group.MapPut("/tenant", UpdateTenant)
            .WithName("tenancy.tenant.update")
            .WithSummary("Rename the signed-in user's workspace (English and Arabic names) and change its settings (default language, time zone, first day of the week).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(TenancyPermissions.TenantUpdate);
    }

    private static async Task<Results<Ok<TenantDto>, ProblemHttpResult>> GetTenant(TenancyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return tenant is null ? (ProblemHttpResult)Problems.NotFound(http) : TypedResults.Ok(ToDto(tenant, session.HoldsWholeWorkspace));
    }

    private static async Task<Results<Ok<TenantDto>, ProblemHttpResult>> UpdateTenant(
        UpdateTenantRequest request, TenancyDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        // Every company of the workspace shares its record: only someone who works in all of them,
        // in every branch of each, changes it (critic p02 round 6: an administrator limited to one
        // company, or to one branch, renamed the workspace and changed its language and time zone
        // for every company). The kernel refuses the write as well, whatever this checked.
        if (!session.HoldsWholeWorkspace)
        {
            return (ProblemHttpResult)Problems.Forbidden(http, "tenancy.workspaceNeedsEveryCompany");
        }
        var validator = new Validator(http)
            .Required("nameEn", request.NameEn).MaxLength("nameEn", request.NameEn, 200)
            .Required("nameAr", request.NameAr).MaxLength("nameAr", request.NameAr, 200)
            .Required("version", request.Version);
        if (request.TimeZone is { } zone)
        {
            validator.Must(TenantSettings.TimeZones.Contains(zone.Trim()), "timeZone", "tenancyTimeZone");
        }
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
        if (request.DefaultLanguage is { } language)
        {
            tenant.DefaultLanguage = language == WorkspaceLanguage.Ar ? "ar" : "en";
        }
        if (request.TimeZone is { } timeZone)
        {
            tenant.TimeZone = timeZone.Trim();
        }
        if (request.WeekStart is { } weekStart)
        {
            tenant.WeekStart = weekStart.ToString().ToLowerInvariant();
        }
        db.Entry(tenant).Property(t => t.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(tenant, session.HoldsWholeWorkspace));
    }

    private static TenantDto ToDto(Tenant t, bool everyCompany) => new(t.Id, t.Code, t.NameEn, t.NameAr, t.Status,
        t.DefaultLanguage == "ar" ? WorkspaceLanguage.Ar : WorkspaceLanguage.En,
        t.TimeZone,
        Enum.TryParse<WeekStart>(t.WeekStart, ignoreCase: true, out var weekStart) ? weekStart : WeekStart.Monday,
        TenantSettings.TimeZones.Order(StringComparer.Ordinal).ToList(),
        t.Version,
        everyCompany);
}
