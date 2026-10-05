namespace Erp.Modules.Tenancy.Contracts;

/// <summary>Public facts about a tenant other modules may use.</summary>
public sealed record TenantInfo(Guid Id, string Code, string NameEn, string NameAr, string Status)
{
    /// <summary>The workspace's time zone (IANA), in which its documents show times.</summary>
    public string TimeZone { get; init; } = "Asia/Dubai";
}

/// <summary>Reads the current tenant (the one the unit of work is bound to). Other modules use
/// this instead of the tenancy tables.</summary>
public interface ITenantDirectory
{
    /// <summary>The tenant the current unit of work is bound to, or null if it is not active.</summary>
    Task<TenantInfo?> GetCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Public facts about a company other modules may use (for example the base currency
/// amounts are converted to, or the fiscal year a posting falls in).</summary>
public sealed record CompanyInfo(
    Guid Id,
    string Code,
    string LegalNameEn,
    string LegalNameAr,
    string BaseCurrency,
    int FiscalYearStartMonth,
    int FiscalYearStartDay,
    string Country,
    bool IsActive);

/// <summary>Reads the companies in the current company scope (the signed-in user's companies, or
/// every company for system work). Other modules use this instead of the tenancy tables.</summary>
public interface ICompanyDirectory
{
    /// <summary>A company in scope, or null (also for another tenant's or an out-of-scope company).</summary>
    Task<CompanyInfo?> GetAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Every company in scope, by code.</summary>
    Task<IReadOnlyList<CompanyInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The company the user is working in now, or null.</summary>
    Task<CompanyInfo?> GetWorkingAsync(CancellationToken cancellationToken);
}

public static class TenancyPermissions
{
    public const string TenantRead = "tenancy.tenant.read";
    public const string TenantUpdate = "tenancy.tenant.update";
    public const string CompaniesRead = "tenancy.companies.read";
    public const string CompaniesCreate = "tenancy.companies.create";
    public const string CompaniesUpdate = "tenancy.companies.update";
    public const string BranchesRead = "tenancy.branches.read";
    public const string BranchesCreate = "tenancy.branches.create";
    public const string BranchesUpdate = "tenancy.branches.update";
    public const string AccessRead = "tenancy.access.read";
    public const string AccessUpdate = "tenancy.access.update";
    public const string WorkplaceRead = "tenancy.workplace.read";
    public const string WorkplaceSwitch = "tenancy.workplace.switch";

    public static readonly IReadOnlyList<string> All =
    [
        TenantRead, TenantUpdate, CompaniesRead, CompaniesCreate, CompaniesUpdate, BranchesRead, BranchesCreate, BranchesUpdate,
        AccessRead, AccessUpdate, WorkplaceRead, WorkplaceSwitch,
    ];
}
