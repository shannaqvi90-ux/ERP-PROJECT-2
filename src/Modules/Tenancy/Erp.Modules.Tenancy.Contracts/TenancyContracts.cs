namespace Erp.Modules.Tenancy.Contracts;

/// <summary>Public facts about a tenant other modules may use.</summary>
public sealed record TenantInfo(Guid Id, string Code, string NameEn, string NameAr, string Status);

/// <summary>Reads the current tenant (the one the unit of work is bound to). Other modules use
/// this instead of the tenancy tables.</summary>
public interface ITenantDirectory
{
    /// <summary>The tenant the current unit of work is bound to, or null if it is not active.</summary>
    Task<TenantInfo?> GetCurrentAsync(CancellationToken cancellationToken);
}

public static class TenancyPermissions
{
    public const string TenantRead = "tenancy.tenant.read";
    public const string TenantUpdate = "tenancy.tenant.update";
}
