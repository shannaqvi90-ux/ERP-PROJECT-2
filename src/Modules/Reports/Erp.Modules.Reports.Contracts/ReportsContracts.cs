namespace Erp.Modules.Reports.Contracts;

public static class ReportsPermissions
{
    /// <summary>See the report catalogue. Running a report needs the read permission of the data
    /// it prints as well; the catalogue lists only the reports the caller may run.</summary>
    public const string CatalogRead = "reports.catalog.read";

    public static readonly IReadOnlyList<string> All = [CatalogRead];
}
